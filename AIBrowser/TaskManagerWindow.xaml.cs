using AIBrowser.Models;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace AIBrowser
{
    public partial class TaskManagerWindow : Window
    {
        private MainWindow _mainWindow;
        private DispatcherTimer _memoryTimer;

        public TaskManagerWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;

            // 应用标题栏主题
            Loaded += (s, e) => AIBrowser.Services.ThemeService.ApplyTitleBarTheme(this, AIBrowser.Services.ThemeService.CurrentEffectiveTheme);

            // 初始化最大保活数输入框
            MaxAliveInput.Text = App.Config.Current.MaxAliveTabs.ToString();

            // 1. 先解除可能存在的旧绑定
            ProcessList.ItemsSource = null;
            // 2. 强制清空 XAML 里可能潜伏的隐形字符或占位项
            ProcessList.Items.Clear();
            // 3. 重新安全地绑定主窗口的数据源
            ProcessList.ItemsSource = _mainWindow.TabList.ItemsSource;

            // 启动定时器实时刷新内存占用
            _memoryTimer = new DispatcherTimer();
            _memoryTimer.Interval = TimeSpan.FromSeconds(2);
            _memoryTimer.Tick += (s, e) => UpdateMemoryUsage();
            _memoryTimer.Start();

            // 首次立即获取一次
            UpdateMemoryUsage();
        }

        private void UpdateMemoryUsage()
        {
            MemoryText.Text = $"当前浏览器总内存占用：{_mainWindow.GetTotalMemoryUsage()}";
        }

        private void KillProcess_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.DataContext is TabItemModel tab)
            {
                // 调用主窗口的公开方法彻底销毁该网页进程
                _mainWindow.KillTab(tab.Id);

                // 由于 TabItemModel 实现了属性通知，此时界面会自动将该卡片变为半透明且隐藏结束按钮
            }
        }

        private void SaveMaxAlive_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(MaxAliveInput.Text, out int newValue) && newValue >= 1)
            {
                App.Config.Current.MaxAliveTabs = newValue;
                App.Config.Save(App.Config.Current, raiseEvent: false);
                System.Windows.MessageBox.Show("保存成功！当打开新网页超出该数量时，最早休眠的网页将被自动回收。");
            }
            else
            {
                System.Windows.MessageBox.Show("请输入大于等于 1 的有效数字。");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _memoryTimer?.Stop();
        }
    }
}