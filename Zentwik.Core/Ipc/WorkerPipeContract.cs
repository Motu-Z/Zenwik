using System.Text;

namespace Zentwik.Core.Ipc
{
    /// <summary>
    /// UI 进程与 Worker 进程之间的管道契约。管道名、互斥体名、协议版本、消息上限
    /// 全部集中在这里 —— 两端各写一份字面量，改名时必然漏掉一处，
    /// 表现为「UI 一直显示未连接」这种没有任何报错的故障。
    ///
    /// 【协议形态】
    /// UTF-8 无 BOM 的单行 JSON：一条消息 = 一行，以 <c>\n</c> 结尾。
    /// 单行而不是定长帧：管道是字节流，没有消息边界，靠换行切分是最省事的边界定义，
    /// 代价是消息体里不能出现裸换行 —— JSON 序列化会把换行转义成 <c>\n</c>，天然满足。
    /// </summary>
    public static class WorkerPipeContract
    {
        /// <summary>
        /// 协议版本。任一端收到不匹配的版本必须拒绝而不是猜测兼容性：
        /// UI 与 Worker 是两个独立部署的 exe，用户目录里出现新旧混搭是常态。
        ///
        /// 【版本协商规则只有两条，没有第三条】
        ///   1. <c>received == ProtocolVersion</c>：正常处理该请求 / 响应。
        ///   2. <c>received != ProtocolVersion</c>（含字段缺失被反序列化成 0、含任意非法值）：
        ///      一律返回 <see cref="PipeResponse"/> 且 <c>Success = false</c>、
        ///      <c>Message = </c><see cref="VersionMismatchMessage"/>，
        ///      **不做任何兼容降级**。
        ///
        /// 【为什么不降级】
        /// 版本号的作用是检测「文件混用 / 被替换 / 篡改」，不是向后兼容。
        /// 本项目没有远程服务端，UI 与 Worker 由同一次发布产出、装进同一个目录；
        /// 出现版本不一致只可能是用户手工替换了其中一个 exe。
        /// 此时继续按旧字段猜测语义，等于让一个不确定的进程去写 SMU —— 宁可直接失败。
        ///
        /// 两端都必须调用 <see cref="ValidateVersion"/> 判断，不得自己比较整数：
        /// 文案与失败响应形状集中在这里，才不会出现「UI 提示重装、Worker 只说 invalid」。
        /// </summary>
        public const int ProtocolVersion = 1;

        /// <summary>
        /// 版本不一致时返回给对端的固定文案。用户看到的第一句话，不随上下文变化。
        /// </summary>
        public const string VersionMismatchMessage = "UI 与 Worker 版本不一致，请重新安装";

        /// <summary>
        /// 单条消息上限（64 KB）。超过即视为协议错误而非「大消息」：
        /// 本协议只承载控制指令，没有一条合法请求接近这个量级，
        /// 无上限读取等于把对端的内存安全交给对端的实现质量。
        /// </summary>
        public const int MaxMessageBytes = 64 * 1024;

        /// <summary>
        /// 管道名（不含 <c>\\.\pipe\</c> 前缀，该前缀由 NamedPipe 类型自己拼）。
        /// </summary>
        public const string PipeName = "Zentwik.Worker";

        /// <summary>
        /// Worker 单实例互斥体名。带 <c>Global\</c> 前缀：跨会话可见，
        /// 远程桌面另开一个会话时不会拉起第二个 Worker 去抢同一块 SMU。
        ///
        /// 命名规则与管道名保持一致的前缀，两者在代码里总是成对出现。
        /// </summary>
        public const string WorkerMutexName = @"Global\Zentwik.Worker.SingleInstance";

        /// <summary>连通性探测。不产生副作用，可被 UI 高频调用。</summary>
        public const string CommandPing = "ping";

        /// <summary>请求 Worker 优雅退出（刷日志、停监听、释放互斥体）。</summary>
        public const string CommandShutdown = "shutdown";

        /// <summary>
        /// 注册开机自启（创建计划任务 ZentwikAutoStart，参数 -silent）。
        ///
        /// 参数：<c>Payload = null</c>。任务名与启动参数取自 Worker 自身的常量，
        /// 不让调用方指定 —— 一旦可以由 UI 传入，任务名就成了第二份契约。
        /// 返回：<c>Success = true</c> 表示任务已创建或本就存在（幂等）；<c>false</c> 时
        /// <c>Message</c> 说明原因（权限不足 / 被组策略锁定 / schtasks 返回非 0）。
        /// 该命令写系统状态，Worker 非管理员权限时必然失败。
        /// </summary>
        public const string CommandRegisterAutoStart = "register-autostart";

        /// <summary>
        /// 注销开机自启（删除同名计划任务）。
        ///
        /// 参数：<c>Payload = null</c>。
        /// 返回：<c>Success = true</c> 表示任务已删除或本就不存在（幂等）；<c>false</c> 时
        /// <c>Message</c> 说明原因。注销失败会让「关掉自启后仍随机启动」，因此失败必须
        /// 原样回传，不得吞掉。
        /// </summary>
        public const string CommandUnregisterAutoStart = "unregister-autostart";

        /// <summary>
        /// 请求 Worker 主动拉起 UI 进程。
        ///
        /// 参数：<c>Payload = null</c>。
        /// 返回：<c>Success = true</c> 表示已发起拉起（**不表示** UI 已完成启动）；
        /// <c>false</c> 表示当前组合下 Worker 不负责拉 UI，或 UI 可执行文件缺失。
        ///
        /// 【只在两种组合下由 Worker 主动拉 UI】
        ///   · 开机自启 = 启动 Worker + 启动 UI 窗口；
        ///   · 开机自启 = 启动 Worker + 托盘常驻（托盘交互负责再唤起窗口）。
        /// 其余组合（自启关闭、自启仅启动 UI、用户手动启动）Worker 一律不主动拉 UI。
        ///
        /// 【为什么要把这条写死】
        /// Worker 的职责是常驻并写 SMU，不是第二个启动器。在不该拉的组合里拉了，
        /// 用户看到的现象是「已经关掉的程序自己又弹出来」—— 这类故障无法从日志归因，
        /// 只能靠契约提前排除。
        /// </summary>
        public const string CommandOpenUi = "open-ui";

        /// <summary>
        /// 应用一套调校方案。
        ///
        /// 参数：<c>Payload</c> = 方案 Id（<c>profiles.json</c> 中方案的 Id，非方案名）。
        /// 传 Id 而不是把整套值放进请求体：方案由 Worker 自己从盘上读，
        /// 避免 UI 与 Worker 各持一份可能不同的配置。传方案名则会在改名后失效。
        /// 返回：<c>Success = true</c> 表示 SMU 写入已全部完成并回写 AppliedAt；
        /// <c>false</c> 时 <c>Message</c> 说明失败的核或 SMU 返回码。
        /// </summary>
        public const string CommandApplyTuning = "apply-tuning";

        /// <summary>
        /// 解除 CPU Set 绑定，把目标进程恢复成由系统默认调度。
        ///
        /// 参数：<c>Payload</c> = 目标进程 Id（十进制字符串）；<c>Payload = null</c>
        /// 表示解除 Worker 当前掌握的全部绑定（即进程页里所有已绑定进程）。
        /// 返回：<c>Success = true</c> 表示所有目标的默认 CPU Set 已恢复；
        /// <c>false</c> 时 <c>Message</c> 说明是哪个进程失败（进程已退出也算失败，
        /// 因为调用方需要知道它没有生效）。
        /// </summary>
        public const string CommandUnbindAll = "unbind-all";

        /// <summary>
        /// 取回 Worker 侧日志快照。
        ///
        /// 参数：<c>Payload = null</c>。
        /// 返回：<c>Payload</c> 为一行一条的纯文本，字段以 <c>|</c> 分隔：
        /// <c>yyyy-MM-ddTHH:mm:ss.fff|级别|消息</c>，级别取 DEBUG / INFO / WARN / ERROR / FATAL。
        /// 用文本而不是嵌套 JSON：协议按行切分消息，嵌套数组一旦带换行就必须再套一层转义，
        /// 而这份数据只给人看。消息内的 <c>\r\n</c> 由 Worker 转义成字面量 <c>\n</c> 后回传。
        /// </summary>
        public const string CommandSnapshotLogs = "snapshot-logs";

        /// <summary>
        /// 两端共用的编码。显式无 BOM：管道另一头如果有 BOM，
        /// 第一行 JSON 的开头会多出三个字节，解析直接失败。
        /// </summary>
        public static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

        /// <summary>
        /// 版本协商的唯一入口。匹配返回 true 且 <paramref name="error"/> 为空；
        /// 其余一切情况（不一致 / 缺失被反序列化成 0 / 任意非法值）返回 false 并给出固定文案。
        /// </summary>
        /// <param name="received">从请求或响应里读到的 <c>Version</c>。</param>
        /// <param name="error">失败时的固定文案，成功时为空串。</param>
        public static bool ValidateVersion(int received, out string error)
        {
            if (received == ProtocolVersion)
            {
                error = string.Empty;
                return true;
            }

            error = VersionMismatchMessage;
            return false;
        }

        /// <summary>
        /// 构造「版本不一致」这一条失败响应。两端都走这里，
        /// 保证 <c>Version</c> 填的是本端已知版本、<c>Message</c> 是同一句话。
        /// </summary>
        public static PipeResponse VersionMismatchResponse() => new()
        {
            Version = ProtocolVersion,
            Success = false,
            Message = VersionMismatchMessage,
            Payload = null
        };
    }
}
