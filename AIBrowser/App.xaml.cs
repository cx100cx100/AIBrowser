using System;
using System.Diagnostics;
using System.Threading;
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
                // 【修复4】显式指定使用 WPF 的 MessageBox
                System.Windows.MessageBox.Show("AIBrowser 已经在运行中！\n请查看系统托盘区域。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                Environment.Exit(0);
                return;
            }

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
                MainWindow.Show();
                MainWindow.WindowState = WindowState.Normal;
                MainWindow.Activate();
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

            base.OnExit(e);
        }
    }
}