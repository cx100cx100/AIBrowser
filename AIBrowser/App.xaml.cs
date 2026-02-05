using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks; // 【新增】必须加这个，用于后台监听信号
using System.Windows; // 只保留 WPF 的命名空间，去掉 Forms
using System.IO;

namespace AIBrowser
{
    // 【修复1】显式指定继承自 WPF 的 Application，防止混淆
    public partial class App : System.Windows.Application
    {
        // ==========================================
        // 核心变量定义
        // ==========================================

        private static Mutex? _mutex;

        // 【新增】用于进程间通信的事件句柄（"自动唤醒"功能的关键）
        private static EventWaitHandle? _eventWaitHandle;
        private const string UniqueEventName = "AIBrowser_BringToFront_Event";

        // 【修复2】使用全名引用 WinForms 的 NotifyIcon，不引用整个命名空间
        private System.Windows.Forms.NotifyIcon? _notifyIcon;

        // 【修复3】将 public 改为 internal，匹配 ConfigService 的访问权限
        internal static AIBrowser.Services.ConfigService Config { get; private set; } = null!;

        public static bool IsExiting { get; set; } = false;

        // ==========================================
        // 启动逻辑
        // ==========================================
        protected override void OnStartup(StartupEventArgs e)
        {
            // 1. 单例检查
            const string appName = "AIBrowser_SingleInstance_Mutex";
            bool createdNew;
            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                // ==========================================================
                // 【核心修改】这里不再弹窗，而是唤醒已有实例并退出自己
                // ==========================================================
                try
                {
                    // 尝试找到已经存在的那个“信号接收器”
                    using (var eventHandle = EventWaitHandle.OpenExisting(UniqueEventName))
                    {
                        eventHandle.Set(); // 发送“唤醒”信号！
                    }
                }
                catch
                {
                    // 忽略异常（比如极罕见情况老程序刚好崩溃了），确保自己静默退出
                }

                Environment.Exit(0);
                return;
            }

            // 【新增】如果是第一个实例，启动后台监听，等待被唤醒
            InitSignalListener();

            base.OnStartup(e);

            // 2. 初始化配置服务
            Config = new AIBrowser.Services.ConfigService("AIBrowser");
            Config.LoadOrCreateDefault();

            // 3. 应用主题
            AIBrowser.Services.ThemeService.ApplyTheme(Config.Current.Theme);

            // 4. 初始化托盘图标
            InitTrayIcon();

            // 5. 显示主窗口
            ShowMainWindow();
        }

        // ==========================================
        // 【新增】后台监听唤醒信号的方法
        // ==========================================
        private void InitSignalListener()
        {
            // 创建一个全局事件，"false"表示初始无信号，"AutoReset"表示收到信号后自动重置
            _eventWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, UniqueEventName);

            // 开启一个长期运行的后台任务一直等信号
            Task.Factory.StartNew(() =>
            {
                while (true)
                {
                    // 程序会卡在这一行，直到有新实例运行并执行了 .Set()
                    _eventWaitHandle.WaitOne();

                    // 收到信号后，必须回到 UI 线程操作窗口
                    Dispatcher.Invoke(() =>
                    {
                        ShowMainWindow();
                    });
                }
            }, TaskCreationOptions.LongRunning);
        }

        // ==========================================
        // 托盘图标逻辑
        // ==========================================
        private void InitTrayIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            _notifyIcon.Text = "AIBrowser AI 聚合浏览器";
            _notifyIcon.Visible = true;

            try
            {
                var iconUri = new Uri("pack://application:,,,/AIBrowser.ico");
                // 使用 WPF 的 GetResourceStream
                var streamInfo = GetResourceStream(iconUri);

                if (streamInfo != null)
                {
                    // 使用 System.Drawing.Icon (WinForms 专用)
                    _notifyIcon.Icon = new System.Drawing.Icon(streamInfo.Stream);
                }
            }
            catch
            {
                // 使用 System.Drawing 下的系统默认图标
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }

            _notifyIcon.DoubleClick += (s, args) => ShowMainWindow();

            // 右键菜单 (使用全名)
            var contextMenu = new System.Windows.Forms.ContextMenuStrip();

            contextMenu.Items.Add("显示主界面", null, (s, args) => ShowMainWindow());
            contextMenu.Items.Add("重启", null, (s, args) => RestartApp());
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            contextMenu.Items.Add("退出", null, (s, args) => QuitApp());

            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        // ==========================================
        // 窗口控制辅助方法
        // ==========================================

        public void ShowMainWindow()
        {
            if (MainWindow == null)
            {
                MainWindow = new MainWindow();
                MainWindow.Show();
            }
            else
            {
                // 【增强】如果窗口被最小化了，先还原
                if (MainWindow.WindowState == WindowState.Minimized)
                {
                    MainWindow.WindowState = WindowState.Normal;
                }

                MainWindow.Show();

                // 【增强】强制激活并置顶（防止被其他窗口遮挡）
                MainWindow.Activate();
                MainWindow.Topmost = true;
                MainWindow.Topmost = false;
                MainWindow.Focus();
            }
        }

        private void RestartApp()
        {
            IsExiting = true;
            try
            {
                var path = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
            }
            catch { }

            QuitApp();
        }

        public void QuitApp()
        {
            IsExiting = true;

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            if (MainWindow != null)
            {
                MainWindow.Close();
            }

            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }

            if (_mutex != null)
            {
                try { _mutex.ReleaseMutex(); } catch { }
                _mutex.Close();
            }

            // 【新增】退出时关闭事件句柄
            _eventWaitHandle?.Close();

            base.OnExit(e);
        }
    }
}