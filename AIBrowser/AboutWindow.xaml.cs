using System.Windows;

namespace AIBrowser
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            // 应用主题，确保弹窗颜色和主程序一致
            AIBrowser.Services.ThemeService.ApplyTitleBarTheme(this, AIBrowser.Services.ThemeService.CurrentEffectiveTheme);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}