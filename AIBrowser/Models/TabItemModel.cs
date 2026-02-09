using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIBrowser.Models
{
    // 修改：实现 INotifyPropertyChanged 接口，支持界面自动更新
    public class TabItemModel : INotifyPropertyChanged
    {
        public string Id { get; set; } = "";

        // URL 变化通常不需要通知 UI，除非你在 UI 上显示了 URL 栏
        public string Url { get; set; } = "";

        // 自定义标题
        private string _customTitle = "";
        public string CustomTitle
        {
            get => _customTitle;
            set { _customTitle = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayTitle)); }
        }

        // 自动获取的网页标题
        private string _autoTitle = "";
        public string AutoTitle
        {
            get => _autoTitle;
            set { _autoTitle = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayTitle)); }
        }

        // 图标路径
        private string? _iconPath;
        public string? IconPath
        {
            get => _iconPath;
            set { _iconPath = value; OnPropertyChanged(); }
        }

        // 【新增】加载状态：true=正在转圈，false=加载完毕
        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        // 界面显示的标题（优先显示自定义）
        public string DisplayTitle => !string.IsNullOrWhiteSpace(CustomTitle) ? CustomTitle : AutoTitle;

        // 标准的属性变更通知实现
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}