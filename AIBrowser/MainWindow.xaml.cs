using AIBrowser.Models;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Linq;



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

        // 放在 MainWindow 类里
        private static void UpdateWebViewTheme(WebView2 wv)
        {
            // 只有当 CoreWebView2 初始化完成后才能设置
            if (wv == null || wv.CoreWebView2 == null) return;

            // 读取当前配置的主题
            string effectiveTheme = AIBrowser.Services.ThemeService.CurrentEffectiveTheme;

            // 转换为 WebView2 的枚举
            // 注意：需要引用 Microsoft.Web.WebView2.Core 命名空间
            wv.CoreWebView2.Profile.PreferredColorScheme =
                effectiveTheme.Equals("Light", StringComparison.OrdinalIgnoreCase)
                    ? CoreWebView2PreferredColorScheme.Light
                    : CoreWebView2PreferredColorScheme.Dark;
        }

        private void FixMaximizeToWorkArea()
        {
            // 简化版：最大化时不覆盖任务栏
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

        // 如果你在 Window 上也加了 MouseLeftButtonDown，就用这个（可选）
        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // 空白区域也允许拖动
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void MinBtn_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaxBtn_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            // 仍然走你已有的 OnClosing 逻辑：隐藏到托盘
            Close();
        }

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

                // 理论上不会淘汰当前正在用的（因为刚 MarkUsed），但保险起见可跳过当前
                if (TabList.SelectedItem is AIBrowser.Models.TabItemModel selected && selected.Id == idToEvict)
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

        private async void EnsureWebViewForSelectedTab()
        {
            if (TabList.SelectedItem is not AIBrowser.Models.TabItemModel tab)
                return;

            // URL 为空就不加载
            var navUrl = NormalizeUrl(tab.Url);
            if (navUrl is null)
            {
                System.Windows.MessageBox.Show("该标签页的 URL 不合法，请在设置里填写完整网址，例如：https://example.com");
                return;
            }

            // 已存在：直接显示 + 更新 LRU + 必要时淘汰
            if (_webviews.TryGetValue(tab.Id, out var existing))
            {
                MarkWebViewUsed(tab.Id);
                ShowOnly(existing);
                EvictIfNeeded();
                return;
            }

            var wv = new WebView2
            {
                Visibility = Visibility.Collapsed
            };

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
                // 初始化失败：把刚加入的 webview 清理掉，避免残留
                ContentHost.Children.Remove(wv);
                wv.Dispose();
                _webviews.Remove(tab.Id);

                System.Windows.MessageBox.Show($"初始化 WebView2 失败：{ex.Message}\nHResult=0x{ex.HResult:X8}");
                return;
            }

            try
            {
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

                wv.CoreWebView2.DocumentTitleChanged += (s, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        tab.AutoTitle = wv.CoreWebView2.DocumentTitle;
                        TabList.Items.Refresh();

                    });
                };

                wv.CoreWebView2.Navigate(navUrl);

                bool iconFetched = false;

                wv.CoreWebView2.NavigationCompleted += async (s, e) =>
                {
                    if (iconFetched) return;
                    iconFetched = true;

                    bool isDefaultAsset = !string.IsNullOrWhiteSpace(tab.IconPath)
                                          && tab.IconPath.Contains("Assets", StringComparison.OrdinalIgnoreCase);

                    if (!string.IsNullOrWhiteSpace(tab.IconPath)
                        && System.IO.File.Exists(tab.IconPath)
                        && !isDefaultAsset) // 如果是默认图标，不要 return，继续往下走去尝试下载
                    {
                        return;
                    }

                    // 从 DOM 取 icon 链接（可能为空）
                    async Task<string?> getIconUrlFromDom()
                    {
                        try
                        {
                            // 取 rel=icon 或 shortcut icon
                            var script = @"
                (function(){
                    var el = document.querySelector('link[rel~=""icon""]');
                    return el ? el.href : '';
                })();";
                            var result = await wv.CoreWebView2.ExecuteScriptAsync(script);
                            // result 是 JSON 字符串，比如 ""https://..."" 或 """"，需要去掉引号
                            if (string.IsNullOrWhiteSpace(result)) return null;
                            result = result.Trim();
                            if (result.StartsWith('"') && result.EndsWith('"'))
                            {
                                // 【优化】IDE0057: 使用切片语法，更简洁（C# 8.0+）
                                result = result[1..^1];
                            }
                            return string.IsNullOrWhiteSpace(result) ? null : result;
                        }
                        catch { return null; }
                    }

                    var savedPath = await AIBrowser.Services.FaviconService.TryDownloadFaviconAsync(navUrl, tab.Id, getIconUrlFromDom);
                    if (string.IsNullOrWhiteSpace(savedPath)) return; // 这里已经防住了，如果是空直接return了，所以这里不用改。

                    Dispatcher.Invoke(() =>
                    {
                        // 更新内存模型
                        tab.IconPath = savedPath;
                        TabList.Items.Refresh();

                        // 更新配置并保存（写回 IconPath）
                        var cfg = App.Config.Current;
                        var cfgTab = cfg.Tabs.FirstOrDefault(x => x.Id == tab.Id);
                        if (cfgTab != null)
                        {
                            cfgTab.IconPath = savedPath;
                            App.Config.Save(cfg, raiseEvent: false); // 不触发重建，避免循环
                        }
                    });
                };


                // 新创建：显示 + 更新 LRU + 必要时淘汰
                MarkWebViewUsed(tab.Id);
                ShowOnly(wv);
                EvictIfNeeded();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"导航失败：{ex.Message}\nHResult=0x{ex.HResult:X8}\nURL={navUrl}");
            }
        }




        public MainWindow()
        {
            InitializeComponent();

            SourceInitialized += (_, __) => FixMaximizeToWorkArea();

            // 【新增】监听实际主题变化（处理 系统自动切换 或 手动切换）
            AIBrowser.Services.ThemeService.EffectiveThemeChanged += newTheme =>
            {
                Dispatcher.Invoke(() =>
                {
                    // 更新所有 WebView
                    foreach (var wv in _webviews.Values)
                    {
                        UpdateWebViewTheme(wv);
                    }

                    // 确保主窗口自己的标题栏（如果用了原生边框）也更新
                    AIBrowser.Services.ThemeService.ApplyTitleBarTheme(this, newTheme);
                });
            };

            App.Config.ConfigChanged += _ =>
            {
                Dispatcher.Invoke(() =>
                {
                    BuildTabsFromConfig();
                    // 主题应用现在交给 ThemeService 在 Save 时处理，或者在这里调用也可以
                    // 但建议统一由 ThemeService.ApplyTheme(App.Config.Current.Theme) 驱动
                    AIBrowser.Services.ThemeService.ApplyTheme(App.Config.Current.Theme);
                });
            };

            BuildTabsFromConfig();
            AIBrowser.Services.ThemeService.ApplyTheme(App.Config.Current.Theme);
        }

        private void BuildTabsFromConfig()
        {
            // 1) 清理被禁用/删除的 WebView2（并同步清 LRU）
            var enabledIds = new HashSet<string>(
                App.Config.Current.Tabs.Where(x => x.Enabled).Select(x => x.Id));

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

            // 2) 重建 Tab 列表（顺序按配置来）
            _tabs.Clear();

            foreach (var cfgTab in App.Config.Current.Tabs)
            {
                if (!cfgTab.Enabled) continue;

                _tabs.Add(new AIBrowser.Models.TabItemModel
                {
                    Id = cfgTab.Id,
                    Url = NormalizeUrl(cfgTab.Url) ?? "",
                    CustomTitle = cfgTab.Name ?? "",
                    AutoTitle = "未加载",

                    // 【修改】如果是空字符串，强制转为 null。WPF 对 null 不会报错。
                    IconPath = string.IsNullOrWhiteSpace(cfgTab.IconPath) ? null : cfgTab.IconPath
                });
            }

            TabList.ItemsSource = null;
            TabList.ItemsSource = _tabs;

            if (_tabs.Count > 0)
                TabList.SelectedIndex = 0;

            // 3) 主动触发当前标签加载/显示
            EnsureWebViewForSelectedTab();
        }




        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsWindow is null)
            {
                _settingsWindow = new SettingsWindow
                {
                    Owner = this
                };
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
    // 如果不是通过托盘右键“退出”点击的，则取消关闭，改为隐藏
    if (!App.IsExiting)
    {
        e.Cancel = true; // 阻止关闭
        this.Hide();     // 隐藏窗口
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

        // 在 MainWindow.xaml.cs 中

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            // 1. 获取当前选中的标签页数据
            if (TabList.SelectedItem is not AIBrowser.Models.TabItemModel tab) return;

            // 2. 获取该标签页设定的原始 URL
            var originalUrl = NormalizeUrl(tab.Url);
            if (originalUrl == null) return; // 如果配置为空，忽略

            // 3. 如果 WebView 已经存在且初始化完成
            if (_webviews.TryGetValue(tab.Id, out var wv) && wv.CoreWebView2 != null)
            {
                // 【修改】不再是 Reload()，而是强制导航回原始 URL
                // 这样就实现了你想要的“重置/回到首页”的效果
                wv.CoreWebView2.Navigate(originalUrl);
            }
            else
            {
                // 如果 WebView 还没加载（或者被 LRU 淘汰了），这个方法本身就会加载原始 URL
                EnsureWebViewForSelectedTab();
            }
        }

        private static string? NormalizeUrl(string raw)
        {
            raw = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw)) return null;

            // 用户可能填 "www.xxx.com" 或 "xxx.com"
            if (!raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                raw = "https://" + raw;
            }

            // 再验证一下是不是合法绝对 URI
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
                return null;

            return uri.ToString();
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
