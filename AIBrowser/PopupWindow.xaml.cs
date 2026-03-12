using Microsoft.Web.WebView2.Core;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace AIBrowser
{
    public partial class PopupWindow : Window
    {
        public PopupWindow()
        {
            InitializeComponent();
            // 应用主题，保持弹窗标题栏颜色一致
            AIBrowser.Services.ThemeService.ApplyTitleBarTheme(this, AIBrowser.Services.ThemeService.CurrentEffectiveTheme);
        }

        public async Task InitializeAsync(CoreWebView2Environment env)
        {
            // 此时窗口已经 Show()，直接注入环境，瞬间完成，绝生死锁！
            await PopupWebView.EnsureCoreWebView2Async(env);

            // 读取配置并应用主题
            string currentTheme = App.Config.Current.Theme;
            PopupWebView.CoreWebView2.Profile.PreferredColorScheme =
                currentTheme.Equals("Light", StringComparison.OrdinalIgnoreCase)
                    ? CoreWebView2PreferredColorScheme.Light
                    : CoreWebView2PreferredColorScheme.Dark;

            // 监听窗口标题改变
            PopupWebView.CoreWebView2.DocumentTitleChanged += (s, e) =>
            {
                // 让弹窗的标题栏显示正确的网页名称
                Title = PopupWebView.CoreWebView2.DocumentTitle;
            };

            // 拦截弹窗内部的再次弹窗（防止自带的丑陋窗口再次跑出来）
            PopupWebView.CoreWebView2.NewWindowRequested += async (s, e) =>
            {
                var deferral = e.GetDeferral();
                e.Handled = true;
                try
                {
                    var pop = new PopupWindow { Owner = this };

                    // 【同样必须先 Show】
                    pop.Show();

                    await pop.InitializeAsync(env);
                    e.NewWindow = pop.PopupWebView.CoreWebView2;
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("打开深层新窗口失败：" + ex.Message);
                }
                finally
                {
                    deferral.Complete();
                }
            };

            // 【新增】自定义右键菜单逻辑
            // 自定义右键菜单逻辑
            PopupWebView.CoreWebView2.ContextMenuRequested += (s, e) =>
            {
                var currentEnv = PopupWebView.CoreWebView2.Environment;
                var currentUrl = PopupWebView.CoreWebView2.Source;

                // 创建“复制当前网址”菜单项
                var copyUrlItem = currentEnv.CreateContextMenuItem("复制当前网址", null, CoreWebView2ContextMenuItemKind.Command);
                copyUrlItem.CustomItemSelected += (sender, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(currentUrl))
                    {
                        try
                        {
                            System.Windows.Clipboard.SetText(currentUrl);
                        }
                        catch (Exception ex)
                        {
                            System.Windows.MessageBox.Show("复制网址失败：" + ex.Message);
                        }
                    }
                };

                // 创建“在默认浏览器中打开”菜单项
                var openInDefaultBrowserItem = currentEnv.CreateContextMenuItem("在默认浏览器中打开", null, CoreWebView2ContextMenuItemKind.Command);
                openInDefaultBrowserItem.CustomItemSelected += (sender, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(currentUrl))
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(currentUrl) { UseShellExecute = true });
                        }
                        catch (Exception ex)
                        {
                            System.Windows.MessageBox.Show("调用默认浏览器失败：" + ex.Message);
                        }
                    }
                };

                // 严格按照顺序添加
                e.MenuItems.Add(copyUrlItem);
                e.MenuItems.Add(openInDefaultBrowserItem);
            };
        }


        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // 新增：重写 OnClosed 方法，窗口关闭时彻底销毁底层浏览器进程
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            if (PopupWebView != null)
            {
                PopupWebView.Dispose();
            }
        }
    }
}