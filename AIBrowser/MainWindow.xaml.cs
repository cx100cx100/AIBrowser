using AIBrowser.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace AIBrowser
{
    public partial class MainWindow : Window
    {
        private readonly Dictionary<string, WebView2> _webviews = new();
        private readonly List<TabItemModel> _tabs = new();
        private static SettingsWindow? _settingsWindow;
        private readonly Task<CoreWebView2Environment> _envTask = CreateEnvAsync();
        private const int MaxAliveWebViews = 5;
        private readonly LinkedList<string> _lru = new();
        private readonly Dictionary<string, LinkedListNode<string>> _lruNodes = new();

        public MainWindow()
        {
            InitializeComponent();

            SourceInitialized += (_, __) => FixMaximizeToWorkArea();

            // 监听主题变化
            AIBrowser.Services.ThemeService.EffectiveThemeChanged += newTheme =>
            {
                Dispatcher.Invoke(() =>
                {
                    foreach (var wv in _webviews.Values)
                    {
                        UpdateWebViewTheme(wv);
                    }
                    AIBrowser.Services.ThemeService.ApplyTitleBarTheme(this, newTheme);
                });
            };

            // 监听配置变化
            App.Config.ConfigChanged += _ =>
            {
                Dispatcher.Invoke(() =>
                {
                    BuildTabsFromConfig();
                    AIBrowser.Services.ThemeService.ApplyTheme(App.Config.Current.Theme);
                });
            };

            BuildTabsFromConfig();
            AIBrowser.Services.ThemeService.ApplyTheme(App.Config.Current.Theme);
        }

        private static void UpdateWebViewTheme(WebView2 wv)
        {
            if (wv == null || wv.CoreWebView2 == null) return;

            string effectiveTheme = AIBrowser.Services.ThemeService.CurrentEffectiveTheme;
            wv.CoreWebView2.Profile.PreferredColorScheme =
                effectiveTheme.Equals("Light", StringComparison.OrdinalIgnoreCase)
                    ? CoreWebView2PreferredColorScheme.Light
                    : CoreWebView2PreferredColorScheme.Dark;
        }

        private void FixMaximizeToWorkArea()
        {
            MaxHeight = SystemParameters.WorkArea.Height + 16;
            MaxWidth = SystemParameters.WorkArea.Width + 16;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }
            DragMove();
        }

        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void MinBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaxBtn_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void MarkWebViewUsed(string tabId)
        {
            if (_lruNodes.TryGetValue(tabId, out var node))
            {
                _lru.Remove(node);
                _lru.AddLast(node);
            }
            else
            {
                var newNode = _lru.AddLast(tabId);
                _lruNodes[tabId] = newNode;
            }
        }

        private void EvictIfNeeded()
        {
            while (_webviews.Count > MaxAliveWebViews)
            {
                var oldest = _lru.First;
                if (oldest == null) break;

                var idToEvict = oldest.Value;

                if (TabList.SelectedItem is TabItemModel selected && selected.Id == idToEvict)
                {
                    _lru.RemoveFirst();
                    _lru.AddLast(idToEvict);
                    continue;
                }

                _lru.RemoveFirst();
                _lruNodes.Remove(idToEvict);

                if (_webviews.TryGetValue(idToEvict, out var wv))
                {
                    ContentHost.Children.Remove(wv);
                    wv.Dispose();
                    _webviews.Remove(idToEvict);
                }
            }
        }

        private static Task<CoreWebView2Environment> CreateEnvAsync()
        {
            var folder = System.IO.Path.Combine(App.Config.ConfigDir, "WebView2UserData");
            System.IO.Directory.CreateDirectory(folder);
            return CoreWebView2Environment.CreateAsync(null, folder);
        }

        private void ShowOnly(WebView2 target)
        {
            foreach (var child in ContentHost.Children)
            {
                if (child is WebView2 wv)
                    wv.Visibility = wv == target ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // ==========================================================
        // 【核心修改】增加加载状态监听
        // ==========================================================
        private async void EnsureWebViewForSelectedTab()
        {
            if (TabList.SelectedItem is not TabItemModel tab)
                return;

            var navUrl = NormalizeUrl(tab.Url);
            if (navUrl is null)
            {
                System.Windows.MessageBox.Show("该标签页的 URL 不合法，请在设置里填写完整网址，例如：https://example.com");
                return;
            }

            if (_webviews.TryGetValue(tab.Id, out var existing))
            {
                MarkWebViewUsed(tab.Id);
                ShowOnly(existing);
                EvictIfNeeded();
                return;
            }

            var wv = new WebView2 { Visibility = Visibility.Collapsed };
            // 【优化】设置默认背景色，防止深色模式下闪白屏
            if (AIBrowser.Services.ThemeService.CurrentEffectiveTheme == "Dark")
            {
                wv.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 30, 30, 30); // 和你的 PageBg 颜色接近
            }
            else
            {
                wv.DefaultBackgroundColor = System.Drawing.Color.White;
            }
            ContentHost.Children.Add(wv);
            _webviews[tab.Id] = wv;

            try
            {
                var env = await _envTask;
                await wv.EnsureCoreWebView2Async(env);
                UpdateWebViewTheme(wv);
            }
            catch (Exception ex)
            {
                ContentHost.Children.Remove(wv);
                wv.Dispose();
                _webviews.Remove(tab.Id);
                System.Windows.MessageBox.Show($"初始化 WebView2 失败：{ex.Message}");
                return;
            }

            try
            {
                // 1. 新窗口请求
                wv.CoreWebView2.NewWindowRequested += (s, e) =>
                {
                    e.Handled = true;
                    try
                    {
                        var uri = new Uri(e.Uri);
                        Dispatcher.Invoke(() =>
                        {
                            var pop = new PopupWindow(uri) { Owner = this };
                            pop.Show();
                        });
                    }
                    catch { }
                };

                // 2. 标题变化
                wv.CoreWebView2.DocumentTitleChanged += (s, e) =>
                {
                    Dispatcher.Invoke(() => tab.AutoTitle = wv.CoreWebView2.DocumentTitle);
                };

                // 3. 【新增】开始导航 -> 设置加载中 (转圈圈)
                wv.CoreWebView2.NavigationStarting += (s, e) =>
                {
                    Dispatcher.Invoke(() => tab.IsLoading = true);
                };

                // 4. 【修改】导航完成 -> 取消加载中 + 获取图标
                bool iconFetched = false;
                wv.CoreWebView2.NavigationCompleted += async (s, e) =>
                {
                    // 停止转圈
                    Dispatcher.Invoke(() => tab.IsLoading = false);

                    if (!e.IsSuccess) return; // 【优化】如果加载失败，就不去下图标了

                    if (iconFetched) return;
                    iconFetched = true;

                    // 检查是否已经是本地资源图标
                    bool isDefaultAsset = !string.IsNullOrWhiteSpace(tab.IconPath)
                                          && tab.IconPath.Contains("Assets", StringComparison.OrdinalIgnoreCase);

                    if (!string.IsNullOrWhiteSpace(tab.IconPath)
                        && System.IO.File.Exists(tab.IconPath)
                        && !isDefaultAsset)
                    {
                        return;
                    }

                    // DOM 抓取图标函数
                    async Task<string?> getIconUrlFromDom()
                    {
                        try
                        {
                            var script = @"(function(){ var el = document.querySelector('link[rel~=""icon""]'); return el ? el.href : ''; })();";
                            var result = await wv.CoreWebView2.ExecuteScriptAsync(script);
                            if (string.IsNullOrWhiteSpace(result)) return null;
                            result = result.Trim();
                            if (result.StartsWith('"') && result.EndsWith('"'))
                                result = result[1..^1];
                            return string.IsNullOrWhiteSpace(result) ? null : result;
                        }
                        catch { return null; }
                    }

                    var savedPath = await AIBrowser.Services.FaviconService.TryDownloadFaviconAsync(navUrl, tab.Id, getIconUrlFromDom);
                    if (string.IsNullOrWhiteSpace(savedPath)) return;

                    Dispatcher.Invoke(() =>
                    {
                        tab.IconPath = savedPath;
                        // 更新配置缓存
                        var cfg = App.Config.Current;
                        var cfgTab = cfg.Tabs.FirstOrDefault(x => x.Id == tab.Id);
                        if (cfgTab != null)
                        {
                            cfgTab.IconPath = savedPath;
                            App.Config.Save(cfg, raiseEvent: false);
                        }
                    });
                };

                // 开始导航
                wv.CoreWebView2.Navigate(navUrl);

                MarkWebViewUsed(tab.Id);
                ShowOnly(wv);
                EvictIfNeeded();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"导航失败：{ex.Message}");
            }
        }

        private void BuildTabsFromConfig()
        {
            var enabledIds = new HashSet<string>(App.Config.Current.Tabs.Where(x => x.Enabled).Select(x => x.Id));

            var toRemove = _webviews.Keys.Where(id => !enabledIds.Contains(id)).ToList();
            foreach (var id in toRemove)
            {
                if (_webviews.TryGetValue(id, out var wv))
                {
                    ContentHost.Children.Remove(wv);
                    wv.Dispose();
                    _webviews.Remove(id);
                }
                if (_lruNodes.TryGetValue(id, out var node))
                {
                    _lru.Remove(node);
                    _lruNodes.Remove(id);
                }
            }

            _tabs.Clear();
            foreach (var cfgTab in App.Config.Current.Tabs)
            {
                if (!cfgTab.Enabled) continue;

                _tabs.Add(new TabItemModel
                {
                    Id = cfgTab.Id,
                    Url = NormalizeUrl(cfgTab.Url) ?? "",
                    CustomTitle = cfgTab.Name ?? "",
                    AutoTitle = "未加载",
                    IconPath = string.IsNullOrWhiteSpace(cfgTab.IconPath) ? null : cfgTab.IconPath,
                    IsLoading = false // 初始状态
                });
            }

            TabList.ItemsSource = null;
            TabList.ItemsSource = _tabs;

            if (_tabs.Count > 0) TabList.SelectedIndex = 0;

            EnsureWebViewForSelectedTab();
        }

        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsWindow is null)
            {
                _settingsWindow = new SettingsWindow { Owner = this };
                _settingsWindow.Closed += (_, __) => _settingsWindow = null;
                _settingsWindow.Show();
            }
            else
            {
                _settingsWindow.Activate();
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!App.IsExiting)
            {
                e.Cancel = true;
                this.Hide();
            }
            else
            {
                base.OnClosing(e);
            }
        }

        public void ShowAndActivate()
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        private void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            EnsureWebViewForSelectedTab();
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            if (TabList.SelectedItem is not TabItemModel tab) return;
            var originalUrl = NormalizeUrl(tab.Url);
            if (originalUrl == null) return;

            if (_webviews.TryGetValue(tab.Id, out var wv) && wv.CoreWebView2 != null)
            {
                // Navigate 会自动触发 NavigationStarting，所以进度条会自动出现
                wv.CoreWebView2.Navigate(originalUrl);
            }
            else
            {
                EnsureWebViewForSelectedTab();
            }
        }

        private static string? NormalizeUrl(string raw)
        {
            raw = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw)) return null;

            // 简单的正则判断是否像个网址 (包含点，且没有空格)
            bool isLikelyUrl = raw.Contains('.') && !raw.Contains(' ');

            if (isLikelyUrl)
            {
                if (!raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return "https://" + raw;
                }
                if (Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return uri.ToString();
            }

            // 【优化】如果不是网址，默认使用 Google/Bing 搜索
            // return $"https://www.google.com/search?q={System.Net.WebUtility.UrlEncode(raw)}";

            // 目前保持你原有的逻辑返回 null 也可以，看你是否需要这个“搜索框”特性
            return null;
        }

        public async void ClearBrowserData()
        {
            try
            {
                foreach (var wv in _webviews.Values)
                {
                    if (wv.CoreWebView2 != null)
                    {
                        await wv.CoreWebView2.Profile.ClearBrowsingDataAsync();
                    }
                }
                System.Windows.MessageBox.Show("已清理缓存与 Cookie。");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("清缓存失败：" + ex.Message);
            }
        }
    }
}