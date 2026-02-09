using AIBrowser.Services;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows; // 核心引用

namespace AIBrowser
{
    public partial class App : System.Windows.Application
    {
        // ==========================================
        // 核心服务与变量
        // ==========================================

        private SingleInstanceService? _singleInstance;
        private TrayService? _trayService;

        // 全局配置服务
        internal static ConfigService Config { get; private set; } = null!;

        // 退出标志
        public static bool IsExiting { get; set; } = false;

        // ==========================================
        // 启动逻辑
        // ==========================================
        protected override void OnStartup(StartupEventArgs e)
        {
            // 1. 初始化单例服务
            _singleInstance = new SingleInstanceService("AIBrowser_App_ID");
            _singleInstance.Start();

            if (!_singleInstance.IsFirstInstance)
            {
                _singleInstance.SignalFirstInstanceToShow();
                Shutdown();
                return;
            }

            _singleInstance.ShowRequested += () => Dispatcher.Invoke(ShowMainWindow);

            base.OnStartup(e);

            // 2. 初始化配置
            Config = new ConfigService("AIBrowser");
            Config.LoadOrCreateDefault();

            // 3. 应用主题
            ThemeService.ApplyTheme(Config.Current.Theme);

            // 4. 初始化托盘图标
            _trayService = new TrayService(
                onShow: ShowMainWindow,
                onExit: QuitApp,
                onRestart: RestartApp
            );

            // 5. 显示主窗口
            ShowMainWindow();
        }

        // ==========================================
        // 窗口控制逻辑
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
                if (MainWindow.WindowState == WindowState.Minimized)
                {
                    MainWindow.WindowState = WindowState.Normal;
                }

                MainWindow.Show();
                MainWindow.Activate();
                MainWindow.Topmost = true;
                MainWindow.Topmost = false;
                MainWindow.Focus();
            }
        }

        public void QuitApp()
        {
            IsExiting = true;
            _trayService?.Dispose();

            if (MainWindow != null)
            {
                MainWindow.Close();
            }

            Shutdown();
        }

        private void RestartApp()
        {
            try
            {
                var path = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                QuitApp();
            }
            catch (Exception ex)
            {
                // 【修复点】这里显式指定使用 WPF 的 MessageBox，避免冲突
                System.Windows.MessageBox.Show("重启失败：" + ex.Message);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayService?.Dispose();
            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }
}