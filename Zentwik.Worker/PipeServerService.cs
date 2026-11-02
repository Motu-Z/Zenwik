using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Zentwik.Core.Ipc;
using Zentwik.Core.Serialization;
using Zentwik.Core.Services;

namespace Zentwik.Worker
{
    /// <summary>
    /// 命名管道服务端：请求-响应模式，一行 JSON 一条消息。
    ///
    /// 【为什么一个连接只处理一条消息】
    /// 协议按行切分，但服务端不做长连接复用：读完一行、写回一行、关掉本次实例。
    /// 单实例上限 1 个，因此同一时刻只有一个客户端在处理，不存在两条响应交错的风险。
    /// 代价是每次请求多一次连接建立（本机管道，微秒级）。
    ///
    /// 【管道的访问控制：显式 DACL，不用 PipeOptions.CurrentUserOnly】
    /// 实测（批次三收尾）：Worker 以管理员身份运行时，CurrentUserOnly 创建的管道
    /// 拒绝中完整性（非提权）客户端连接，而这正是 UI 进程的形态 —— 用 CurrentUserOnly
    /// 会导致「UI 永远连不上 Worker」。改为显式 DACL：只授予当前用户 SID 的 ReadWrite。
    /// 语义与 CurrentUserOnly 相同（仍然只有同一个 Windows 用户能连），
    /// 但不会带上那层导致跨完整性级别拒绝的附加限制。
    ///
    /// 【为什么不是 BuiltinUsers】
    /// 那是「所有本地用户」组。多用户机器上等于允许别的登录用户向提权 Worker
    /// 发 shutdown —— 与 CurrentUserOnly 的初衷相反。当前用户 SID 才是等价物。
    ///
    /// 【本轮实现 ping / shutdown / register-autostart / unregister-autostart】
    /// 其余 4 个命令常量（open-ui / apply-tuning / unbind-all / snapshot-logs）需要额外服务依赖，
    /// 统一返回「未知命令」，留给后续批次接。
    /// </summary>
    internal sealed class PipeServerService : IHostedService
    {
        private readonly ILogService _log;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly AutoStartService _autoStart;

        private CancellationTokenSource? _stopToken;
        private Task? _acceptLoop;

        public PipeServerService(
            ILogService log,
            IHostApplicationLifetime lifetime,
            AutoStartService autoStart)
        {
            ArgumentNullException.ThrowIfNull(log);
            _log = log;

            ArgumentNullException.ThrowIfNull(lifetime);
            _lifetime = lifetime;

            // 任务计划注册需要管理员令牌 —— 这是本服务（提权进程）存在的意义之一。
            // UI 侧无提权，只能发 IPC 请求过来。
            ArgumentNullException.ThrowIfNull(autoStart);
            _autoStart = autoStart;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _stopToken = new CancellationTokenSource();
            CancellationToken token = _stopToken.Token;

            _acceptLoop = Task.Run(() => AcceptLoopAsync(token), CancellationToken.None);

            return Task.CompletedTask;
        }

        /// <summary>
        /// 接受循环。每次迭代 = 建一个服务端实例 → 等连接 → 读一行 → 回一行 → 关掉。
        ///
        /// 不用 CancellationTokenRegistration 关管道：`using` 在每轮末尾释放实例，
        /// 而等待阶段由 WaitForConnectionAsync(token) 自己响应取消。
        /// </summary>
        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                // 【为什么用 NamedPipeServerStreamAcl.Create 而不是创建后再 SetAccessControl】
                // PipesAclExtensions.SetAccessControl 作用在「已经创建好的管道句柄」上，
                // 实测抛异常（句柄没有 WRITE_DAC），且异常发生在 try 之外，会把整个
                // accept 循环打挂、管道直接消失。Create 在创建时就带上安全描述符，
                // 既没有这个权限问题，也没有「先建后改」的竞态窗口。
                using NamedPipeServerStream server = NamedPipeServerStreamAcl.Create(
                    WorkerPipeContract.PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 0,
                    outBufferSize: 0,
                    pipeSecurity: CreatePipeSecurity(),
                    inheritability: HandleInheritability.None,
                    additionalAccessRights: default);

                try
                {
                    await server.WaitForConnectionAsync(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    string line = await ReadLineAsync(server, token);

                    PipeRequest? request = JsonSerializer.Deserialize(
                        line,
                        IpcJsonContext.Default.PipeRequest);

                    PipeResponse response;

                    if (request is null)
                    {
                        // 反序列化出 null（字面量 "null"）：协议上不可能出现，
                        // 按失败回一句，不猜语义。
                        response = new PipeResponse
                        {
                            Version = WorkerPipeContract.ProtocolVersion,
                            Success = false,
                            Message = "请求为空",
                            Payload = null
                        };
                    }
                    else if (!WorkerPipeContract.ValidateVersion(request.Version, out _))
                    {
                        // 版本不一致：回固定文案，不做任何兼容降级。
                        // VersionMismatchResponse 里填的是本端已知版本，不透传对端版本。
                        response = WorkerPipeContract.VersionMismatchResponse();
                    }
                    else
                    {
                        response = await HandleAsync(request, token);
                    }

                    await WriteLineAsync(server, response, token);

                    // shutdown：先把 bye 写出去，再延迟 100 ms 停宿主。
                    // 100 ms 是经验值，不保证 WriteLineAsync 的对端已读完；
                    // shutdown 是幂等操作，客户端没收到 bye 但进程确实退出了也算成功。
                    //
                    // 绝不用 Environment.Exit：那会跳过 StopAsync，
                    // 导致日志最后一段（恰好是崩溃现场）丢失、WMI 订阅不释放。
                    if (request is not null && request.Command == WorkerPipeContract.CommandShutdown)
                    {
                        _ = Task.Run(
                            async () =>
                            {
                                await Task.Delay(100, CancellationToken.None);
                                _lifetime.StopApplication();
                            },
                            CancellationToken.None);
                    }
                }
                catch (OperationCanceledException)
                {
                    // 停机时正在读 / 写：正常收尾，不记异常。
                    break;
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    // 对端中途断开（IOException）或发来非法 JSON（JsonException）：
                    // 只 Warn，循环继续等下一条连接。
                    _log.Warn($"管道处理异常：{ex.Message}");
                }
            }
        }

        /// <summary>
        /// 管道的 DACL：只允许当前用户 SID 读写。
        ///
        /// 【为什么是 ReadWrite 而不是 FullControl】
        /// 实测四种权限位里 ReadWrite 已经足够客户端完成「写一条请求 + 读一条响应」；
        /// 授 FullControl 会把 WRITE_DAC / WRITE_OWNER 也交给客户端，
        /// 等于让 UI 能改这条管道的安全描述符 —— 没有理由给。
        ///
        /// 【为什么不写 SACL】
        /// 显式 DACL + 不设 SACL 时，管道不带强制完整性标签，
        /// 中完整性客户端因此可以连上；这正是显式 DACL 与 CurrentUserOnly 的差别所在。
        /// </summary>
        private static PipeSecurity CreatePipeSecurity()
        {
            PipeSecurity security = new();
            security.AddAccessRule(new PipeAccessRule(
                WindowsIdentity.GetCurrent().User!,
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));
            return security;
        }

        /// <summary>
        /// 命令分派。本轮实现 4 个命令，其余一律「未知命令」。
        ///
        /// 【为什么是 async 而不是同步】
        /// 注册 / 注销开机自启会同步调用 schtasks.exe（内部 15 秒超时，且要回查一次），
        /// 最长可能占用几十秒。放在接受循环线程上同步执行会把整条管道卡住，
        /// 连 ping 都进不来；丢到线程池后至少接受循环本身不会被业务阻塞。
        /// </summary>
        private async Task<PipeResponse> HandleAsync(PipeRequest request, CancellationToken token)
        {
            switch (request.Command)
            {
                case WorkerPipeContract.CommandPing:
                    return new PipeResponse
                    {
                        Version = WorkerPipeContract.ProtocolVersion,
                        Success = true,
                        Message = "pong",
                        Payload = null
                    };

                case WorkerPipeContract.CommandShutdown:
                    return new PipeResponse
                    {
                        Version = WorkerPipeContract.ProtocolVersion,
                        Success = true,
                        Message = "bye",
                        Payload = null
                    };

                case WorkerPipeContract.CommandRegisterAutoStart:
                    return await RunAutoStartAsync(enable: true, token);

                case WorkerPipeContract.CommandUnregisterAutoStart:
                    return await RunAutoStartAsync(enable: false, token);

                default:
                    return new PipeResponse
                    {
                        Version = WorkerPipeContract.ProtocolVersion,
                        Success = false,
                        Message = $"未知命令：{request.Command}",
                        Payload = null
                    };
            }
        }

        /// <summary>
        /// 注册 / 注销开机自启。
        ///
        /// 【失败语义】一律 <c>Success = false</c> + <c>Message</c>，**不向调用方抛异常**：
        /// 管道协议没有「异常」这种消息形状，抛出去只会让对端收到一条断连，
        /// UI 侧看到的是「通信中断」，而不是「权限不足」或「被组策略锁定」。
        /// 真实原因同时写进 Worker 日志（AutoStartService 内部已经记了 Error）。
        /// </summary>
        private async Task<PipeResponse> RunAutoStartAsync(bool enable, CancellationToken token)
        {
            string action = enable ? "注册" : "注销";

            try
            {
                bool ok = await Task.Run(
                    () => enable ? _autoStart.Enable() : _autoStart.Disable(),
                    token).ConfigureAwait(false);

                return new PipeResponse
                {
                    Version = WorkerPipeContract.ProtocolVersion,
                    Success = ok,
                    Message = ok
                        ? $"开机自启已{action}"
                        : $"{action}开机自启失败，详见 Worker 日志",
                    Payload = null
                };
            }
            catch (Exception ex)
            {
                _log.Warn($"{action}开机自启异常：{ex.GetType().Name}: {ex.Message}");

                return new PipeResponse
                {
                    Version = WorkerPipeContract.ProtocolVersion,
                    Success = false,
                    Message = $"{action}开机自启异常：{ex.Message}",
                    Payload = null
                };
            }
        }

        /// <summary>
        /// 读一行，硬上限 <see cref="WorkerPipeContract.MaxMessageBytes"/>（64 KB）。
        ///
        /// 【为什么不用 StreamReader.ReadLineAsync】
        /// 它没有长度上限：对端持续发送而不换行，内存会一直涨。
        /// 本协议只承载控制指令，超限即协议错误，不该当成「大消息」去容忍。
        /// </summary>
        private static async Task<string> ReadLineAsync(NamedPipeServerStream server, CancellationToken token)
        {
            byte[] buffer = new byte[4096];
            using MemoryStream accumulated = new(WorkerPipeContract.MaxMessageBytes);

            while (true)
            {
                int read = await server.ReadAsync(buffer, token);

                if (read == 0)
                    throw new IOException("对端在消息结束前关闭了管道");

                int newline = Array.IndexOf(buffer, (byte)'\n', 0, read);
                int take = newline >= 0 ? newline : read;

                if (accumulated.Length + take > WorkerPipeContract.MaxMessageBytes)
                {
                    throw new IOException(
                        $"消息超过 {WorkerPipeContract.MaxMessageBytes} 字节上限");
                }

                accumulated.Write(buffer, 0, take);

                if (newline >= 0)
                {
                    // 一行 = 一条消息，换行之后的字节属于下一条消息；
                    // 本协议一个连接只处理一条，直接丢弃。
                    return WorkerPipeContract.Utf8.GetString(accumulated.ToArray());
                }
            }
        }

        /// <summary>
        /// 写一行并刷出。UTF-8 无 BOM（<see cref="WorkerPipeContract.Utf8"/>）：
        /// 带 BOM 会让对端解析第一行时多出三个字节，直接失败。
        /// </summary>
        private static async Task WriteLineAsync(
            NamedPipeServerStream server,
            PipeResponse response,
            CancellationToken token)
        {
            string json = JsonSerializer.Serialize(response, IpcJsonContext.Default.PipeResponse);
            byte[] payload = WorkerPipeContract.Utf8.GetBytes(json + "\n");

            await server.WriteAsync(payload, token);
            await server.FlushAsync(token);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _stopToken?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 已释放：没有需要取消的循环。
            }

            Task? loop = _acceptLoop;
            if (loop is null)
                return;

            // 5 秒上限：接受循环最坏情况下正卡在 WaitForConnectionAsync，
            // 取消会立刻把它踢出来。真的超时也只记 Warn，不阻塞进程退出。
            Task finished = await Task.WhenAny(
                loop,
                Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None));

            if (!ReferenceEquals(finished, loop))
            {
                _log.Warn("acceptLoop 未在 5 秒内退出");
                return;
            }

            try
            {
                await loop;
            }
            catch (OperationCanceledException)
            {
                // 正常取消路径。
            }
            catch (Exception ex)
            {
                _log.Warn($"acceptLoop 退出时异常：{ex.Message}");
            }
        }
    }
}
