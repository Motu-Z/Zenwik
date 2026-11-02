using System;
using System.Windows;
using Zentwik.Core.Ipc;

using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace Zentwik.UI
{
    public partial class MainWindow : Window
    {
        /// <summary>回滚 IsChecked 时置位，避免回滚动作反过来触发相反方向的处理器。</summary>
        private bool _suppressAutoStartEvent;

        /// <summary>防重入：SendAsync 最长 10 秒，用户在等待期间再点一次会并发执行。</summary>
        private bool _isProcessingAutoStart;

        public MainWindow()
        {
            InitializeComponent();

            // 标题里带上当前关闭行为：手工验收要在三种模式间切换，
            // 窗口上能直接看出跑的是哪一档，不用回头翻源码。
            Title = $"Zentwik — 关闭行为：{App.CurrentCloseBehavior}";

            // 【顺序不能反】先设初值，再订阅事件。
            // 反过来会让这次赋值触发 Checked/Unchecked → 直接发一次 IPC，
            // 表现为「程序一启动就去注册/注销计划任务」，而且用户没点过任何东西。
            AutoStartCheckBox.IsChecked = App.Settings.Settings.AutoStart;
            AutoStartCheckBox.Checked += OnAutoStartChanged;
            AutoStartCheckBox.Unchecked += OnAutoStartChanged;
        }

        /// <summary>
        /// 状态文本。x:Name 生成的是 internal 字段（同程序集可访问），
        /// 但 <see cref="Application.MainWindow"/> 的静态类型是 <see cref="Window"/>，
        /// 没有这个属性，所以必须由窗口自己暴露一个 string 属性给 App 赋值。
        /// 属性名与 x:Name 刻意不同（StatusText vs StatusTextBlock），避免同名冲突。
        /// </summary>
        public string StatusText
        {
            get => StatusTextBlock.Text;
            set => StatusTextBlock.Text = value;
        }

        private void OnOpenTuningPageClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                this,
                "调校页待后续批次实现。",
                "Zentwik",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        /// <summary>
        /// 勾选 / 取消「开机自启」。
        ///
        /// 【三处状态必须一致】内存里的 <c>Settings.AutoStart</c>、ui.json、复选框。
        /// 任何一步失败都要把复选框和内存一起回滚到操作前的值，
        /// 否则用户看到勾着但任务计划不存在（或反过来），而界面上没有任何异常。
        ///
        /// 【为什么是 async void】Checked/Unchecked 是 WPF 的 void 事件签名，
        /// 这里不用 .Wait()/.Result 去同步等待——那会在 UI 线程上死锁。
        /// </summary>
        private async void OnAutoStartChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressAutoStartEvent || _isProcessingAutoStart)
                return;

            _isProcessingAutoStart = true;

            // 从源头阻止第二次点击：早退回滚能挡住重复逻辑，但挡不住用户连点两下，
            // 而两次并发 IPC 会让任务计划与 ui.json 的状态互相覆盖。
            AutoStartCheckBox.IsEnabled = false;

            try
            {
                bool desired = AutoStartCheckBox.IsChecked == true;
                bool original = !desired;

                try
                {
                    await using var client = new WorkerPipeClient();
                    await client.ConnectAsync(TimeSpan.FromSeconds(2));

                    PipeResponse response = await client.SendAsync(
                        new PipeRequest
                        {
                            // 红线 1.2：Version 没有初始化器，默认 0 会被对端判为版本不符，
                            // 而报错文案是「UI 与 Worker 版本不一致，请重新安装」—— 归因会跑偏。
                            Version = WorkerPipeContract.ProtocolVersion,
                            Command = desired
                                ? WorkerPipeContract.CommandRegisterAutoStart
                                : WorkerPipeContract.CommandUnregisterAutoStart
                        },
                        TimeSpan.FromSeconds(10));

                    if (response.Success)
                    {
                        App.Settings.Settings.AutoStart = desired;

                        try
                        {
                            App.Settings.Save();
                        }
                        catch (Exception saveEx)
                        {
                            // Save 失败：回滚内存，保证三处状态一致。
                            // 注意此时任务计划已经真实改变，这里只能保证界面与配置一致，
                            // 任务计划与配置的短暂不一致由 MessageBox 告知用户。
                            App.Settings.Settings.AutoStart = original;

                            _suppressAutoStartEvent = true;
                            AutoStartCheckBox.IsChecked = original;
                            _suppressAutoStartEvent = false;

                            App.Log.Warn($"保存 ui.json 失败：{saveEx.GetType().Name}: {saveEx.Message}");

                            MessageBox.Show(
                                this,
                                $"任务计划已注册但配置保存失败：{saveEx.Message}\n下次启动时开关状态可能不准确。",
                                "Zentwik",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }

                        return;
                    }

                    // IPC 返回失败：任务计划没有改变，界面必须回到原状。
                    _suppressAutoStartEvent = true;
                    AutoStartCheckBox.IsChecked = original;
                    _suppressAutoStartEvent = false;

                    MessageBox.Show(
                        this,
                        $"设置开机自启失败：{response.Message}",
                        "Zentwik",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                catch (Exception ex)
                {
                    // 连接失败 / 超时 / 版本不符：同样回滚。
                    _suppressAutoStartEvent = true;
                    AutoStartCheckBox.IsChecked = original;
                    _suppressAutoStartEvent = false;

                    App.Log.Warn($"设置开机自启异常：{ex.GetType().Name}: {ex.Message}");

                    MessageBox.Show(
                        this,
                        $"设置开机自启异常：{ex.Message}",
                        "Zentwik",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            finally
            {
                AutoStartCheckBox.IsEnabled = true;
                _isProcessingAutoStart = false;
            }
        }

        /// <summary>
        /// 关闭窗口时的动作（事实卡 §7.3 组二）。
        ///
        /// 【为什么这里不调 this.Close()】
        /// Close() 会再次进入 OnClosed，形成「关闭 → 再关闭」的递归；
        /// 而且窗口已经在关闭流程里，再调一次不会产生任何新效果，只会让栈变深。
        ///
        /// 【Tray 模式为什么什么都不做】
        /// ShutdownMode = OnExplicitShutdown（App.xaml），窗口关闭只是销毁这个窗口对象，
        /// 应用本身继续跑 —— 这正是「收托盘」与「退出」的区别。这里把 MainWindow 置 null，
        /// 是为了让托盘菜单的「打开」走「新建窗口」分支，而不是去 Show() 一个已销毁的窗口
        /// （后者抛 InvalidOperationException: 无法在已关闭的 Window 上设置 Visibility）。
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            switch (App.CurrentCloseBehavior)
            {
                case CloseBehavior.Exit:
                case CloseBehavior.WorkerOnly:
                    // Exit：UI 退出，OnExit 里再决定要不要关 Worker。
                    // WorkerOnly：UI 退出，OnExit 明确不关 Worker（B3）。
                    Application.Current.Shutdown();
                    break;

                case CloseBehavior.Tray:
                    if (ReferenceEquals(Application.Current.MainWindow, this))
                        Application.Current.MainWindow = null;
                    break;
            }
        }
    }
}
