namespace AIBrowser.Models
{
    public class TabItemModel // 或者 internal class
    {
        public string Id { get; set; } = "";
        public string Url { get; set; } = "";
        public string CustomTitle { get; set; } = "";
        public string AutoTitle { get; set; } = "";

        // 【修改前】可能是 public string IconPath { get; set; } = "";
        // 【修改后】加个问号，允许为空
        public string? IconPath { get; set; }

        // 这是一个辅助属性，用来在界面上显示“自定义标题”或“自动标题”
        public string DisplayTitle => !string.IsNullOrWhiteSpace(CustomTitle) ? CustomTitle : AutoTitle;
    }
}
