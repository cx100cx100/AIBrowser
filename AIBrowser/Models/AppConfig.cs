namespace AIBrowser.Models
{
    internal sealed class AppConfig
    {
        public List<TabConfig> Tabs { get; set; } = new();
        public bool StartOnBoot { get; set; } = false;

        // 新增：主题
        public string Theme { get; set; } = "Dark"; // "Dark" or "Light"
    }

    internal sealed class TabConfig
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public bool Enabled { get; set; } = true;

        // 【修改】默认值改为 null，允许为空
        public string? IconPath { get; set; } = null;
    }
}
