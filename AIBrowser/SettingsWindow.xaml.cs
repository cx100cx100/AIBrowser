using AIBrowser.Models;
using AIBrowser.Services;
using System;
using System.Linq;
using System.Windows;

namespace AIBrowser
{
    public partial class SettingsWindow : Window
    {
        private AppConfig _editing = new();

        public SettingsWindow()
        {
            InitializeComponent();
            LoadFromConfig();
            Loaded += (s, e) => AIBrowser.Services.ThemeService.ApplyTitleBarTheme(this, AIBrowser.Services.ThemeService.CurrentEffectiveTheme);
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            // 创建并显示关于窗口，设置为模态对话框（必须关掉才能操作后面的）
            var aboutWin = new AboutWindow { Owner = this };
            aboutWin.ShowDialog();
        }

        private void LoadFromConfig()
        {
            var src = App.Config.Current;

            _editing = new AppConfig
            {
                StartOnBoot = src.StartOnBoot,
                Theme = string.IsNullOrWhiteSpace(src.Theme) ? "Dark" : src.Theme,
                Tabs = src.Tabs.Select(t => new TabConfig
                {
                    Id = t.Id,
                    Name = t.Name,
                    Url = t.Url,
                    Enabled = t.Enabled,
                    // 【修改】核心修复：如果是空字符串，强制转为 null
                    IconPath = string.IsNullOrWhiteSpace(t.IconPath) ? null : t.IconPath
                }).ToList()
            };

            TabsList.ItemsSource = _editing.Tabs;

            // 主题下拉
            string themeConfig = _editing.Theme; // 这里可能是 "Dark", "Light", 或 "System"

            bool found = false;
            foreach (var item in ThemeCombo.Items.OfType<System.Windows.Controls.ComboBoxItem>())
            {
                if ((item.Tag?.ToString() ?? "") == themeConfig)
                {
                    ThemeCombo.SelectedItem = item;
                    found = true;
                    break;
                }
            }

            if (!found) ThemeCombo.SelectedIndex = 0;


            // 开机启动：以系统实际为准
            StartOnBootCheck.IsChecked = StartupService.IsRunOnStartup("AIBrowser");
            _editing.StartOnBoot = StartOnBootCheck.IsChecked == true;
        }

        // SettingsWindow.xaml.cs

        // ... 

        private void ResetDefaults_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "确定要重置所有网站列表为默认模板吗？\n当前未保存的修改将丢失。",
                "确认重置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                // 1. 获取默认列表
                var defaults = App.Config.GetDefaultTabs();

                // 2. 清空当前编辑列表
                _editing.Tabs.Clear();

                // 3. 将默认项加入当前编辑列表
                foreach (var item in defaults)
                {
                    _editing.Tabs.Add(item);
                }

                // 4. 刷新 UI
                TabsList.Items.Refresh();
            }
        }




        private void AddTab_Click(object sender, RoutedEventArgs e)
        {
            _editing.Tabs.Add(new TabConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = $"网站 {_editing.Tabs.Count + 1}",
                Url = "",
                Enabled = true
            });

            TabsList.Items.Refresh();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            // 【修改】同上
            if (sender is FrameworkElement btn && btn.DataContext is TabConfig tab)
            {
                _editing.Tabs.Remove(tab);
                TabsList.Items.Refresh();
            }
        }
        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            // 【修改】不从 ListBox 拿选中项，而是从按钮的 DataContext 拿数据
            if (sender is FrameworkElement btn && btn.DataContext is TabConfig tab)
            {
                var idx = _editing.Tabs.IndexOf(tab);
                if (idx <= 0) return;

                _editing.Tabs.RemoveAt(idx);
                _editing.Tabs.Insert(idx - 1, tab);

                TabsList.Items.Refresh();

                // 可选：为了视觉反馈，顺便让它被选中
                TabsList.SelectedItem = tab;
            }
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            // 【修改】同上
            if (sender is FrameworkElement btn && btn.DataContext is TabConfig tab)
            {
                var idx = _editing.Tabs.IndexOf(tab);
                if (idx < 0 || idx >= _editing.Tabs.Count - 1) return;

                _editing.Tabs.RemoveAt(idx);
                _editing.Tabs.Insert(idx + 1, tab);

                TabsList.Items.Refresh();
                TabsList.SelectedItem = tab;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // 主题
            if (ThemeCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item)
                _editing.Theme = item.Tag?.ToString() ?? "Dark";
            else
                _editing.Theme = "Dark";

            // 开机启动
            _editing.StartOnBoot = StartOnBootCheck.IsChecked == true;

            var exe = Environment.ProcessPath ?? "";
            try
            {
                StartupService.SetRunOnStartup("AIBrowser", exe, _editing.StartOnBoot);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("设置开机启动失败：" + ex.Message);
            }

            // 保存配置并立即生效
            App.Config.Save(_editing, raiseEvent: true);

            // 立即切主题（不等重启）
            ThemeService.ApplyTheme(_editing.Theme);

            Close();

            if (Owner is MainWindow mw)
                mw.ShowAndActivate();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
            if (Owner is MainWindow mw) mw.ShowAndActivate();
        }

        private void ClearCacheBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Owner is MainWindow mw)
                mw.ClearBrowserData();
            else
                System.Windows.MessageBox.Show("找不到主窗口，无法清缓存。");
        }
    }
}
