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
        [System.Runtime.InteropServices.DllImport("psapi.dll")]
        private static extern int EmptyWorkingSet(IntPtr hwProc);

        private readonly Dictionary<string, WebView2> _webviews = new();
        private readonly List<TabItemModel> _tabs = new();
        private static SettingsWindow? _settingsWindow;
        private readonly Task<CoreWebView2Environment> _envTask = CreateEnvAsync();
        private readonly LinkedList<string> _lru = new();
        private readonly Dictionary<string, LinkedListNode<string>> _lruNodes = new();

        public MainWindow()
        {
            InitializeComponent();

            AdaptWindowSizeToScreen();

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

        // ==========================================================
        // 【新增】处理最大化状态下的窗口拖拽脱离 (修复命名空间冲突)
        // ==========================================================
        protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseMove(e);

            // 核心条件：鼠标左键按住拖拽，且当前窗口处于最大化状态
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && WindowState == WindowState.Maximized)
            {
                // 1. 获取鼠标当前在窗口内的相对坐标 (显式使用 System.Windows.Point)
                System.Windows.Point mousePosInWindow = e.GetPosition(this);

                // 2. 将相对坐标转换为屏幕物理绝对坐标 (必须在改变窗口状态之前获取)
                System.Windows.Point screenPoint = PointToScreen(mousePosInWindow);

                // 3. 处理高 DPI 缩放：将物理绝对坐标转换回 WPF 逻辑坐标
                var source = PresentationSource.FromVisual(this);
                if (source != null)
                {
                    screenPoint = source.CompositionTarget.TransformFromDevice.Transform(screenPoint);
                }

                // 4. 计算鼠标在当前最大化窗口宽度的百分比（用来保证恢复后鼠标依旧在标题栏的相对位置）
                double ratio = mousePosInWindow.X / ActualWidth;

                // 5. 将窗口恢复正常大小
                WindowState = WindowState.Normal;

                // 6. 获取恢复后的窗口宽度 (如果有缓存则用 RestoreBounds，否则用默认)
                double restoreWidth = RestoreBounds.Width;
                if (double.IsNaN(restoreWidth) || restoreWidth <= 0)
                {
                    restoreWidth = Width;
                    if (double.IsNaN(restoreWidth) || restoreWidth <= 0)
                    {
                        restoreWidth = 1000; // 最终保底宽度
                    }
                }

                // 7. 计算新位置：保持横向相对比例，纵向位置不变
                Left = screenPoint.X - (restoreWidth * ratio);
                Top = screenPoint.Y - mousePosInWindow.Y;

                // 8. 位置调整好后，立即调用原生拖拽接管鼠标
                try
                {
                    DragMove();
                }
                catch
                {
                    // 忽略可能产生的拖拽异常
                }
            }
        }

        // ==========================================================
        // 【新增】根据当前屏幕分辨率自适应窗口大小
        // ==========================================================
        private void AdaptWindowSizeToScreen()
        {
            // 获取当前主屏幕的工作区尺寸（工作区会自动排除底部的任务栏）
            double workAreaWidth = SystemParameters.WorkArea.Width;
            double workAreaHeight = SystemParameters.WorkArea.Height;

            // 桌面浏览器常用黄金比例：宽度的 80%，高度的 80%
            Width = workAreaWidth * 0.9;
            Height = workAreaHeight * 0.9;

            // 设置保底的最小尺寸，防止用户把窗口缩得过小导致界面崩溃或错位
            // 通常 800x600 或 1024x768 是比较安全的下限
            MinWidth = 1024;
            MinHeight = 700;

            // 让窗口自动在屏幕正中央启动
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
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

        private void TaskManagerBtn_Click(object sender, RoutedEventArgs e)
        {
            var taskManager = new TaskManagerWindow(this) { Owner = this };
            taskManager.ShowDialog();
        }
        private void EvictIfNeeded()
        {
            int maxAlive = App.Config.Current.MaxAliveTabs;
            if (maxAlive < 1) maxAlive = 1;

            while (_webviews.Count > maxAlive)
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

                KillTab(idToEvict);
            }
        }

        public void KillTab(string tabId)
        {
            if (_lruNodes.TryGetValue(tabId, out var node))
            {
                _lru.Remove(node);
                _lruNodes.Remove(tabId);
            }

            if (_webviews.TryGetValue(tabId, out var wv))
            {
                ContentHost.Children.Remove(wv);
                wv.Dispose();
                _webviews.Remove(tabId);
            }

            var tab = TabList.Items.OfType<TabItemModel>().FirstOrDefault(t => t.Id == tabId);
            if (tab != null)
            {
                tab.IsAlive = false;
            }

            // 【新增】深度清理内存
            // 1. 强制 WPF 进行垃圾回收
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // 2. 强制操作系统回收当前程序的物理内存
            try
            {
                EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle);
            }
            catch { }
        }

        public string GetTotalMemoryUsage()
        {
            // 1. 获取主程序 (WPF) 的物理内存占用
            long totalMemory = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;

            try
            {
                // 2. 精准获取我们自己创建的 WebView2 的主进程 PID
                var processedPids = new HashSet<uint>();
                foreach (var wv in _webviews.Values)
                {
                    if (wv.CoreWebView2 != null)
                    {
                        uint pid = wv.CoreWebView2.BrowserProcessId;
                        if (processedPids.Add(pid)) // 避免重复计算同一个共享内核
                        {
                            var p = System.Diagnostics.Process.GetProcessById((int)pid);
                            totalMemory += p.WorkingSet64;
                        }
                    }
                }
            }
            catch { }

            double memoryInMB = totalMemory / 1024.0 / 1024.0;
            return $"{memoryInMB:F1} MB";
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
            tab.IsAlive = true;
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
                // 1. 新窗口请求（完美继承 Cookie、Session，修复死锁）
                wv.CoreWebView2.NewWindowRequested += async (s, e) =>
                {
                    // 告诉内核：“先等一下，我去建个新窗口”
                    var deferral = e.GetDeferral();
                    e.Handled = true;

                    try
                    {
                        var env = await _envTask;
                        var pop = new PopupWindow { Owner = this };

                        // 【核心修复】：必须先 Show()！
                        // 必须先让窗口在屏幕上显示出来，生成物理句柄，WebView2 才能成功初始化
                        pop.Show();

                        // 注入主窗口的环境（共享登录状态和 Cookie）
                        await pop.InitializeAsync(env);

                        // 把准备好的内核交还给原生请求
                        e.NewWindow = pop.PopupWebView.CoreWebView2;
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show("拦截并打开新窗口失败：" + ex.Message);
                    }
                    finally
                    {
                        deferral.Complete();
                    }
                };

                // 2. 标题变化
                wv.CoreWebView2.DocumentTitleChanged += (s, e) =>
                {
                    Dispatcher.Invoke(() => tab.AutoTitle = wv.CoreWebView2.DocumentTitle);
                };

                // 3. 开始导航 -> 设置加载中 (转圈圈)
                wv.CoreWebView2.NavigationStarting += (s, e) =>
                {
                    Dispatcher.Invoke(() => tab.IsLoading = true);
                };

                // 4. 导航完成 -> 取消加载中 + 获取图标
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

                // 5. 【新增】自定义右键菜单
                wv.CoreWebView2.ContextMenuRequested += (s, e) =>
                {
                    var env = wv.CoreWebView2.Environment;
                    var currentUrl = wv.CoreWebView2.Source;

                    // 创建“复制当前网址”菜单项
                    var copyUrlItem = env.CreateContextMenuItem("复制当前网址", null, CoreWebView2ContextMenuItemKind.Command);
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
                    var openInDefaultBrowserItem = env.CreateContextMenuItem("在默认浏览器中打开", null, CoreWebView2ContextMenuItemKind.Command);
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

                    // 严格按照顺序添加，先添加复制网址，再添加默认浏览器打开
                    e.MenuItems.Add(copyUrlItem);
                    e.MenuItems.Add(openInDefaultBrowserItem);
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