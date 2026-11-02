using System;
using System.Windows;

using Application = System.Windows.Application;

namespace Zentwik.UI
{
    /// <summary>
    /// 托盘图标。只在 <see cref="CloseBehavior.Tray"/> 模式下创建
    /// （事实卡 §7.2：托盘由 UI 进程用 WinForms NotifyIcon 持有，Worker 不持有任何托盘）。
    ///
    /// 【为什么图标先用 SystemIcons.Application】
    /// 本轮是壳子，没有随包资源。图标换成 Assets\Zentwik.ico 是纯资源改动，
    /// 不影响这里的任何逻辑。
    ///
    /// 【本轮不做确认弹窗】
    /// 事实卡 §7.4 第 4 条要求「托盘右键退出 ≠ 关闭窗口，且给不同的确认文案」。
    /// 本轮只落地前半条（菜单项分离），确认文案留批次五。
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
        private bool _disposed;

        public TrayIcon()
        {
            var menu = new System.Windows.Forms.ContextMenuStrip();

            var openItem = new System.Windows.Forms.ToolStripMenuItem("打开");
            openItem.Click += (_, _) => OpenMainWindow();

            // 「退出」= 彻底退出（等价 B1），与「关闭窗口」（B2）不是同一件事。
            // 本轮两者的差别体现在：关闭窗口只销毁 MainWindow、进程与托盘都还在；
            // 点这里走 Application.Shutdown → OnExit → 按当前模式决定要不要关 Worker。
            var exitItem = new System.Windows.Forms.ToolStripMenuItem("退出");
            exitItem.Click += (_, _) => Application.Current.Shutdown();

            menu.Items.Add(openItem);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(exitItem);

            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Text = "Zentwik",
                ContextMenuStrip = menu,
                Visible = false
            };

            // 左键单击等同「打开」：托盘图标的常规预期，不给用户第二个心智模型。
            _notifyIcon.MouseClick += (_, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                    OpenMainWindow();
            };
        }

        public void Show() => _notifyIcon.Visible = true;

        /// <summary>
        /// 恢复主窗口。
        ///
        /// 【为什么 MainWindow 为 null 时要 new 一个】
        /// Tray 模式下关闭窗口是**销毁**（事实卡 §7.4 第 1 条），不是 Hide()。
        /// 已关闭的 Window 对象不能再 Show() —— 会抛
        /// InvalidOperationException: 无法在已关闭的 Window 上设置 Visibility。
        /// 所以「重新打开」只有一条路：建一个新窗口。
        /// 代价是窗口状态（位置 / 尺寸 / 当前页）随旧窗口一起丢了，
        /// 这正是 §7.4 第 3 条要求落盘 ui.json 的原因，本轮不实现（见交付说明）。
        /// </summary>
        private static void OpenMainWindow()
        {
            Application app = Application.Current;

            Window? existing = app.MainWindow;

            if (existing is null)
            {
                var window = new MainWindow();
                app.MainWindow = window;
                window.Show();
                return;
            }

            if (!existing.IsVisible)
                existing.Show();

            if (existing.WindowState == WindowState.Minimized)
                existing.WindowState = WindowState.Normal;

            existing.Activate();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // 先藏再 Dispose：直接 Dispose 在某些 shell 状态下会留下一个「幽灵图标」，
            // 直到用户把鼠标划过去才消失。
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
        }
    }
}
