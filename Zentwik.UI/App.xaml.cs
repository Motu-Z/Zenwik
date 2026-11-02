using System;
using System.Windows;
using Zentwik.Core.Helpers;
using Zentwik.Core.Services;

// UseWPF=true 与 UseWindowsForms=true 并存时，SDK 会把 System.Windows 与
// System.Windows.Forms 都放进全局 using。两个命名空间里都有 MessageBox / Application，
// 不消歧义就是 CS0104「引用不明确」。这里按本轮实际用到的两个类型给别名。
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace Zentwik.UI
{
    /// <summary>
    /// 关闭窗口时的行为。对应事实卡 §7.3 组二（B1 / B2 / B3）。
    ///
    /// 本轮不做设置界面，改行为靠改 <see cref="App.CurrentCloseBehavior"/> 的初始值再编译，
    /// 供手工验收切换三种模式。
    /// </summary>
    public enum CloseBehavior
    {
        /// <summary>B1：完全退出。UI 退出，并让本次 UI 拉起的 Worker 一起退出。</summary>
        Exit,

        /// <summary>B2：收托盘。销毁 MainWindow，UI 进程存活，托盘图标由 UI 持有。</summary>
        Tray,

        /// <summary>B3：只留 Worker。UI 进程退出，Worker 常驻（无论由谁拉起）。</summary>
        WorkerOnly
    }

    public partial class App : Application
    {
        /// <summary>
        /// 当前关闭行为。默认 <see cref="CloseBehavior.Exit"/>，与事实卡 §7.3 第 1 行一致
        /// （A1 + B1：点关闭 = UI 退出 + 通知 Worker 退出）。
        /// </summary>
        public static CloseBehavior CurrentCloseBehavior { get; set; } = CloseBehavior.Exit;

        /// <summary>
        /// UI 进程自己的日志。前缀必须是 <c>zentwik-</c>，不是 <c>worker-</c>：
        /// 两个进程写同一个 Logs 目录，靠前缀分文件，否则排障时互相覆盖。
        ///
        /// 【Directory 为什么显式给】
        /// <see cref="LogOptions.Normalize"/> 在 Directory 为空时会回落到 <c>Path.GetTempPath()</c>。
        /// 只写 FilePrefix 的话，UI 日志会落到 %TEMP% 而不是 %AppData%\Zentwik\Logs，
        /// 而验收要求两个进程的日志同目录并存。取 <see cref="AppPaths.Logs"/> 是为了
        /// 让「配置目录在哪」仍然只有 Core 一个出口（含 ZenForge→Zenwik→Zentwik 迁移链）。
        /// </summary>
        public static LogService Log { get; } = new(new LogOptions
        {
            FilePrefix = "zentwik-",
            Directory = new AppPaths().Logs
        });

        /// <summary>
        /// 界面设置（ui.json）。本轮只用到 <c>AutoStart</c> 一个字段。
        ///
        /// 【为什么是静态字段而不是走 DI】
        /// 本工程还没有容器（批次四的壳子刻意不建），窗口与 App 都按静态入口取用。
        /// 与 <see cref="Log"/> 同理：静态字段初始化早于任何容器构建，只能自己 new。
        /// </summary>
        public static UiSettingsStore Settings { get; } = new(new AppPaths());

        private TrayIcon? _trayIcon;

        /// <summary>
        /// 启动流程。async void 是 WPF 事件签名的硬约束，因此**整体**必须包在 try/catch 里：
        /// async void 里逃出去的异常不会回到调用方，直接走 Dispatcher 未处理异常 → 进程崩溃。
        /// 本轮会抛的地方至少有两处：XAML 加载失败（new MainWindow()）与 progress 回调。
        /// </summary>
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                // ---------- 0. 读配置 ----------
                // 必须排在 new MainWindow() 之前：窗口构造函数要用 Settings 初始化「开机自启」开关，
                // 没 Load 就是一份默认值（AutoStart=false），界面会显示错误的状态。
                Settings.Load();

                // ---------- 1. 托盘先建 ----------
                // 只在 Tray 模式建，且必须排在窗口之前：如果在连接完成之后才建托盘，
                // 用户在等待连接期间点关闭，就没有托盘可以「收」，进程会变成一个
                // 没有窗口也没有图标的孤儿。Exit / WorkerOnly 模式不建托盘 ——
                // 前者关窗口就退，后者 UI 进程直接退出，托盘随进程消失。
                if (CurrentCloseBehavior == CloseBehavior.Tray)
                {
                    _trayIcon = new TrayIcon();
                    _trayIcon.Show();
                }

                // ---------- 2. 窗口 ----------
                // 用局部变量持有引用：Application.MainWindow 的静态类型是 Window，
                // 没有 StatusText 属性，写 MainWindow.StatusText 编译不过。
                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                mainWindow.Show();

                mainWindow.StatusText = "正在连接 Worker…";

                // ---------- 3. 拉起 / 连接 Worker ----------
                // 等待上限 90 秒，覆盖「无 PawnIO 时 ApplyFromStore 的 WaitForAvailability(60)」
                // 把管道监听推迟的最坏情况（UFIX 第 9 条）。
                bool connected = await WorkerLauncher.EnsureRunningAsync(
                    progress: s => mainWindow.StatusText = s);

                mainWindow.StatusText = connected ? "Worker: 已连接" : "Worker: 未连接";

                if (connected)
                    Log.Info("Worker 已就绪");
                else
                    Log.Warn("Worker 未就绪");
            }
            catch (Exception ex)
            {
                Log.Error($"UI 启动失败：{ex}");

                // 托盘先于窗口创建，失败路径上必须回收：否则用户会看到一个
                // 没有窗口、点了也没反应的托盘图标，且进程退不掉。
                if (_trayIcon is not null)
                {
                    _trayIcon.Dispose();
                    _trayIcon = null;
                }

                MessageBox.Show(
                    $"启动失败：{ex.Message}",
                    "Zentwik",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 退出路径。三种关闭行为在这里的差别只有一件事：要不要通知 Worker 退出。
        ///
        /// <list type="bullet">
        /// <item><see cref="CloseBehavior.Exit"/>：通知（B1 语义，本轮简化为「只关本次 UI 拉起的」）。</item>
        /// <item><see cref="CloseBehavior.Tray"/>：通知 —— 托盘菜单的「退出」就是彻底退出，
        /// 与关闭窗口（B2）不是同一件事。</item>
        /// <item><see cref="CloseBehavior.WorkerOnly"/>：**不通知**。B3 语义要求 Worker 常驻，
        /// 即使它是本次 UI 拉起的；在 OnExit 里调 ShutdownIfOwned 会让「只留 Worker」变成
        /// 「两个都退」，而用户以为自己只是关了界面。</item>
        /// </list>
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                if (CurrentCloseBehavior != CloseBehavior.WorkerOnly)
                    WorkerLauncher.ShutdownIfOwned();

                _trayIcon?.Dispose();
                _trayIcon = null;

                Log.Flush();
                Log.Dispose();
            }
            catch (Exception ex)
            {
                // 退出路径不允许再抛：此时没有窗口可以承载错误，
                // 抛出去只会把「正常退出」变成「崩溃退出」。
                System.Diagnostics.Debug.WriteLine($"OnExit 异常：{ex}");
            }

            base.OnExit(e);
        }
    }
}
