using Microsoft.Web.WebView2.Core;
using System;
using System.Windows;

namespace AIBrowser
{
    public partial class PopupWindow : Window
    {
        private readonly Uri _target;

        public PopupWindow(Uri target)
        {
            InitializeComponent();
            _target = target;

            Loaded += PopupWindow_Loaded;
        }

        private async void PopupWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await PopupWebView.EnsureCoreWebView2Async();
                // 读取配置并应用
                string currentTheme = App.Config.Current.Theme;
                PopupWebView.CoreWebView2.Profile.PreferredColorScheme =
                    currentTheme.Equals("Light", StringComparison.OrdinalIgnoreCase)
                        ? CoreWebView2PreferredColorScheme.Light
                        : CoreWebView2PreferredColorScheme.Dark;

                PopupWebView.CoreWebView2.Navigate(_target.ToString());
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("打开新窗口失败：" + ex.Message);
                Close();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
