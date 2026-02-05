using Microsoft.Win32;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AIBrowser.Services
{
    public static class ThemeService
    {
        // 暴露一个事件，当“实际主题”发生变化时触发（用于通知 WebView2 变色）
        public static event Action<string>? EffectiveThemeChanged;

        // 获取当前实际生效的主题 ("Dark" 或 "Light")
        public static string CurrentEffectiveTheme { get; private set; } = "Dark";

        public static void ApplyTheme(string themeSetting)
        {
            // 1. 解析目标主题（System -> Dark/Light）
            string targetTheme = themeSetting;
            if (string.Equals(themeSetting, "System", StringComparison.OrdinalIgnoreCase))
            {
                targetTheme = GetSystemTheme();
                // 开启系统监听
                StartSystemThemeListener();
            }
            else
            {
                // 关闭系统监听（如果是强制指定）
                StopSystemThemeListener();
            }

            CurrentEffectiveTheme = targetTheme;

            // 2. 切换 WPF 资源字典
            UpdateResourceDictionary(targetTheme);

            // 3. 设置所有窗口的标题栏颜色 (DWM)
            // 【修复】显式指定 System.Windows.Application
            foreach (Window window in System.Windows.Application.Current.Windows)
            {
                ApplyTitleBarTheme(window, targetTheme);
            }

            // 4. 通知外部（如 WebView2）
            EffectiveThemeChanged?.Invoke(targetTheme);
        }

        public static void ApplyTitleBarTheme(Window window, string theme)
        {
            if (window == null) return;

            // 确保窗口句柄已创建
            var helper = new WindowInteropHelper(window);
            if (helper.Handle == IntPtr.Zero)
            {
                // 如果窗口还没显示，等它加载完再设
                window.SourceInitialized += (s, e) => ApplyTitleBarTheme(window, theme);
                return;
            }

            bool isDark = theme.Equals("Dark", StringComparison.OrdinalIgnoreCase);

            // 调用 DWM API 设置沉浸式深色模式
            // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 2004+ / Windows 11)
            int attribute = 20;
            int useImmersiveDarkMode = isDark ? 1 : 0;

            DwmSetWindowAttribute(helper.Handle, attribute, ref useImmersiveDarkMode, sizeof(int));
        }

        private static void UpdateResourceDictionary(string theme)
        {
            var src = theme.Equals("Light", StringComparison.OrdinalIgnoreCase)
                ? "Themes/Light.xaml"
                : "Themes/Dark.xaml";

            // 【修复】显式指定 System.Windows.Application
            var app = System.Windows.Application.Current;
            if (app == null) return;

            var dict = new ResourceDictionary { Source = new Uri(src, UriKind.Relative) };
            var merged = app.Resources.MergedDictionaries;

            var old = merged.FirstOrDefault(d =>
                d.Source != null &&
                (d.Source.OriginalString.Contains("Themes/Light.xaml") ||
                 d.Source.OriginalString.Contains("Themes/Dark.xaml")));

            if (old != null) merged.Remove(old);
            merged.Add(dict);
        }

        #region 系统主题监听逻辑

        private static bool _isListening = false;

        private static void StartSystemThemeListener()
        {
            if (_isListening) return;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _isListening = true;
        }

        private static void StopSystemThemeListener()
        {
            if (!_isListening) return;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _isListening = false;
        }

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // 只有当颜色设置改变时才处理
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color)
            {
                // 重新读取并应用
                // 【修复】显式指定 System.Windows.Application
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    // 只有当前配置确实是 System 时才自动切换
                    if (App.Config.Current.Theme.Equals("System", StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyTheme("System");
                    }
                });
            }
        }

        private static string GetSystemTheme()
        {
            try
            {
                const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
                using var key = Registry.CurrentUser.OpenSubKey(keyPath);
                if (key != null)
                {
                    var val = key.GetValue("AppsUseLightTheme");
                    if (val is int i && i == 1)
                        return "Light";
                }
            }
            catch { }
            return "Dark"; // 默认回退到深色
        }

        #endregion

        #region Native Methods
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        #endregion
    }
}