using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Zentwik.Core.Ipc;

namespace Zentwik.UI
{
    /// <summary>
    /// Worker 进程的拉起与连通性判定。
    ///
    /// 【判定顺序为什么是「先 ping，再查互斥体」】
    /// 管道能 ping 通是唯一可信的「Worker 已就绪」信号：进程存在不代表管道已经建好
    /// （BackendHostService 先跑，PipeServerService 后跑），而互斥体存在只说明进程活着。
    /// 反过来，管道连不上不代表 Worker 不在 —— 它可能正卡在启动流程里。
    /// 两个信号合起来才分得清「没启动」「在启动」「已就绪」三种状态。
    ///
    /// 【等待上限为什么是 90 秒】
    /// UFIX 第 9 条：无 PawnIO 时 Worker 启动会走 ApplyFromStore →
    /// WaitForAvailability(60) 轮询，最长阻塞 60 秒，而管道监听排在它之后。
    /// 60 秒是同步阻塞在 StartAsync 里的，UI 这侧看到的只是「连不上」。
    /// 90 秒 = 60 秒最坏阻塞 + 30 秒余量（SMU 初始化本身也要时间）。
    /// 少于 90 秒的等待在没装 PawnIO 的机器上会稳定误报「未连接」。
    /// </summary>
    internal static class WorkerLauncher
    {
        private const string WorkerExeName = "Zentwik.Worker.exe";

        /// <summary>首次探测的超时。够本机管道建立，又不至于让「Worker 不在」等太久。</summary>
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

        /// <summary>重试期间单次 ping 的超时。</summary>
        private static readonly TimeSpan RetryPingTimeout = TimeSpan.FromSeconds(1);

        /// <summary>重试间隔，同时也是进度文案的刷新间隔。</summary>
        private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);

        /// <summary>等待总时长，覆盖无 PawnIO 时 60 秒阻塞（UFIX 第 9 条）。</summary>
        private static readonly TimeSpan RetryTotal = TimeSpan.FromSeconds(90);

        /// <summary>本次 Worker 是否由本进程拉起。只影响 ShutdownIfOwned 是否发命令。</summary>
        private static bool _startedByUs;

        /// <summary>
        /// 保证 Worker 在跑并已就绪。**任何失败都返回 false，绝不向调用方抛异常** ——
        /// 调用方是 async void 的 OnStartup，抛出去就是整个应用崩掉。
        /// </summary>
        /// <param name="progress">状态文案回调。调用方在 UI 线程上调用本方法，且本方法内部
        /// 所有 await 都不使用 ConfigureAwait(false)，因此回调始终在 UI 线程执行，
        /// 可以直接改控件属性。</param>
        public static async Task<bool> EnsureRunningAsync(
            Action<string>? progress = null,
            CancellationToken ct = default)
        {
            try
            {
                // ---------- 1. 已经在跑？ ----------
                if (await PingAsync(ProbeTimeout).ConfigureAwait(true))
                {
                    _startedByUs = false;
                    App.Log.Info("Worker 已在运行，直接复用");
                    return true;
                }

                // ---------- 2. 进程在，但管道还没起来 ----------
                if (WorkerProcessExists())
                {
                    App.Log.Info("Worker 进程已存在，等待管道就绪");
                    return await WaitUntilReadyAsync(progress, ct).ConfigureAwait(true);
                }

                // ---------- 3. 进程不在，找 exe 并拉起 ----------
                string? exePath = ResolveWorkerPath();
                if (exePath is null)
                {
                    progress?.Invoke("找不到 Zentwik.Worker.exe");
                    App.Log.Warn($"找不到 {WorkerExeName}。已搜索：{string.Join(" | ", EnumerateWorkerPaths())}");
                    return false;
                }

                progress?.Invoke("正在启动 Worker（会请求管理员权限）…");

                // 【UseShellExecute 必须是 true + Verb=runas】
                // Worker 清单是 requireAdministrator。非提权进程用 UseShellExecute=false
                // 调 CreateProcess 会直接失败，实测 Win32Exception 740（ERROR_ELEVATION_REQUIRED，
                // 「请求的操作需要提升」）—— 不弹 UAC、进程也起不来。
                // 要拿到事实卡 §5.2 第 4 行要求的「弹一次 UAC」，只能走 ShellExecute 的 runas 动词。
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas"
                });

                _startedByUs = true;
                App.Log.Info($"已拉起 Worker：{exePath}");

                return await WaitUntilReadyAsync(progress, ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                App.Log.Warn("等待 Worker 就绪被取消");
                return false;
            }
            catch (Exception ex)
            {
                // 用户取消 UAC 走 Win32Exception 1223；找不到文件走 FileNotFoundException；
                // 互斥体权限走 UnauthorizedAccessException。全部按「没连上」处理。
                App.Log.Warn($"EnsureRunningAsync 失败：{ex.GetType().Name}: {ex.Message}");
                progress?.Invoke("Worker 启动失败");
                return false;
            }
        }

        /// <summary>
        /// 单次连通性探测。<c>Version</c> 必须显式赋值（批次红线清单 §1.2）：
        /// <see cref="PipeRequest.Version"/> 没有初始化器，默认 0 会被对端
        /// <c>ValidateVersion</c> 拒绝，而对端只会回「UI 与 Worker 版本不一致，请重新安装」——
        /// 排查时会先去怀疑安装包，最后才想到是构造代码少写了一行。
        ///
        /// 【为什么每次新建 client】
        /// Worker 的管道服务端是「一个连接只处理一条消息」：读完一行、写回一行、
        /// 立刻释放该服务端实例。长连接在第二条消息上必然抛 IpcTransportException。
        /// </summary>
        public static async Task<bool> PingAsync(TimeSpan timeout)
        {
            try
            {
                await using var client = new WorkerPipeClient();

                await client.ConnectAsync(timeout).ConfigureAwait(false);

                PipeResponse response = await client.SendAsync(
                    new PipeRequest
                    {
                        Version = WorkerPipeContract.ProtocolVersion,
                        Command = WorkerPipeContract.CommandPing
                    },
                    timeout).ConfigureAwait(false);

                return response.Success;
            }
            catch (Exception ex)
            {
                // 探测失败是常态（Worker 没起来时每 2 秒一次），只进文件不刷界面。
                App.Log.Debug($"ping 失败：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 让本次 UI 拉起的 Worker 退出。fire-and-forget：不阻塞 UI 退出路径。
        ///
        /// 【语义边界（本轮简化，见交付说明）】
        /// 只关「本次 UI 拉起的」Worker。事实卡 §7.3 的 B1 完整语义是
        /// 「不管 Worker 由谁拉起，UI 退出时都发 shutdown」，本轮不实现，留批次五。
        ///
        /// 【为什么 lambda 内部还要 try/catch】
        /// 这是 fire-and-forget 任务，异常没有任何人观察：不捕获的话，
        /// 失败现场只剩一条 UnobservedTaskException，且发生在进程退出瞬间，基本查不到。
        /// </summary>
        public static void ShutdownIfOwned()
        {
            if (!_startedByUs)
            {
                App.Log.Info("Worker 非本次 UI 拉起，不发送 shutdown");
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await using var client = new WorkerPipeClient();

                    await client.ConnectAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

                    await client.SendAsync(
                        new PipeRequest
                        {
                            Version = WorkerPipeContract.ProtocolVersion,
                            Command = WorkerPipeContract.CommandShutdown
                        },
                        TimeSpan.FromSeconds(2)).ConfigureAwait(false);

                    App.Log.Info("已请求 Worker 退出");
                }
                catch (Exception ex)
                {
                    App.Log.Warn($"ShutdownIfOwned 失败：{ex.GetType().Name}: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 轮询等待管道就绪。每 <see cref="RetryInterval"/> 秒 ping 一次，
        /// 总时长 <see cref="RetryTotal"/>，期间按秒回传进度。
        /// </summary>
        private static async Task<bool> WaitUntilReadyAsync(Action<string>? progress, CancellationToken ct)
        {
            Stopwatch sw = Stopwatch.StartNew();
            int waitedSeconds = 0;

            while (sw.Elapsed < RetryTotal)
            {
                ct.ThrowIfCancellationRequested();

                if (await PingAsync(RetryPingTimeout).ConfigureAwait(true))
                {
                    App.Log.Info($"Worker 就绪，等待 {sw.Elapsed.TotalSeconds:F1} 秒");
                    return true;
                }

                await Task.Delay(RetryInterval, ct).ConfigureAwait(true);
                waitedSeconds += (int)RetryInterval.TotalSeconds;

                if (sw.Elapsed < RetryTotal)
                    progress?.Invoke($"Worker 正在初始化…（已等待 {waitedSeconds} 秒）");
            }

            App.Log.Warn($"等待 Worker 就绪超时（{RetryTotal.TotalSeconds:F0} 秒）");
            return false;
        }

        /// <summary>
        /// Worker 进程是否活着 —— 用单实例互斥体判断，不用进程名。
        ///
        /// 【为什么要单独捕获 UnauthorizedAccessException】
        /// 互斥体名带 <c>Global\</c> 前缀，由提权进程创建。非提权 UI 打开它时，
        /// TryOpenExisting 不保证返回 false，实测可能直接抛 UnauthorizedAccessException。
        /// 那个异常的含义恰恰是「对象存在但你没权限」—— 也就是 Worker 在跑。
        /// 把它当成「不存在」会导致 UI 去拉起第二个 Worker，第二个实例抢不到互斥体、
        /// 静默退出，而 UI 白等 90 秒。
        /// </summary>
        private static bool WorkerProcessExists()
        {
            try
            {
                if (Mutex.TryOpenExisting(WorkerPipeContract.WorkerMutexName, out Mutex? mutex))
                {
                    mutex?.Dispose();
                    return true;
                }

                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch (Exception ex)
            {
                App.Log.Warn($"检测 Worker 互斥体失败：{ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static string? ResolveWorkerPath()
        {
            foreach (string candidate in EnumerateWorkerPaths())
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        /// <summary>
        /// 搜索顺序：先发布/输出目录（产品形态：两个 exe 同目录），
        /// 再开发构建目录（从 Zentwik.UI/bin/&lt;Config&gt;/net10.0-windows 往上四级到仓库根）。
        /// </summary>
        private static IEnumerable<string> EnumerateWorkerPaths()
        {
            yield return Path.Combine(AppContext.BaseDirectory, WorkerExeName);

            yield return Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                @"..\..\..\..\Zentwik.Worker\bin\Release\net10.0-windows",
                WorkerExeName));
        }
    }
}
