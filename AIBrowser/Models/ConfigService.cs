using AIBrowser.Models;
using System.Collections.Generic;
using System.IO;
using System.Text.Json; // 确保引用了 System.Text.Json 或 Newtonsoft.Json

namespace AIBrowser.Services
{
    internal class ConfigService
    {
        // ... 原有的字段 ...
        private readonly string _appName;
        public string ConfigDir { get; }
        public string ConfigPath { get; }
        public AppConfig Current { get; private set; } = new();

        public event Action<AppConfig>? ConfigChanged;

        public ConfigService(string appName)
        {
            _appName = appName;
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            ConfigDir = Path.Combine(appData, appName);
            ConfigPath = Path.Combine(ConfigDir, "config.json");
            Directory.CreateDirectory(ConfigDir);
        }

        // 【新增】定义默认模板数据
        // Services/ConfigService.cs

        public List<TabConfig> GetDefaultTabs()
        {
            // 获取程序运行时的根目录 (即 bin/Debug/net...)
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var assetsDir = Path.Combine(baseDir, "Assets");

            return new List<TabConfig>
    {
        new TabConfig {
            Id = Guid.NewGuid().ToString("N"),
            Name = "ChatGPT",
            Url = "https://chat.openai.com/chat",
            Enabled = true,
            // 指向本地资源
            IconPath = Path.Combine(assetsDir, "chatgpt.png")
        },
        new TabConfig {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Kimi",
            Url = "https://www.kimi.com/",
            Enabled = true,
            IconPath = Path.Combine(assetsDir, "kimi.png")
        },
        new TabConfig {
            Id = Guid.NewGuid().ToString("N"),
            Name = "腾讯元宝",
            Url = "https://yuanbao.tencent.com/",
            Enabled = true,
            IconPath = Path.Combine(assetsDir, "yuanbao.png")
        },
        new TabConfig {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Bohrium",
            Url = "https://sdu.bohrium.com/",
            Enabled = true,
            IconPath = Path.Combine(assetsDir, "bohrium.png")
        },
        new TabConfig {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Gemini",
            Url = "https://gemini.google.com/",
            Enabled = true,
            IconPath = Path.Combine(assetsDir, "gemini.png")
        }
    };
        }

        public void LoadOrCreateDefault()
        {
            if (File.Exists(ConfigPath))
            {
                try
                {
                    var json = File.ReadAllText(ConfigPath);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                    if (cfg != null)
                    {
                        Current = cfg;
                        // 如果读取回来是空的（比如旧版本文件），也补上默认值
                        if (Current.Tabs.Count == 0)
                        {
                            Current.Tabs = GetDefaultTabs();
                        }
                        return;
                    }
                }
                catch { }
            }

            // 文件不存在，或者读取失败，使用默认配置
            Current = new AppConfig
            {
                Tabs = GetDefaultTabs(), // 【修改】这里直接使用默认模板
                StartOnBoot = false,
                Theme = "Dark"
            };
            Save(Current, raiseEvent: false);
        }

        public void Save(AppConfig config, bool raiseEvent = true)
        {
            Current = config;
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(config, options);
                File.WriteAllText(ConfigPath, json);

                if (raiseEvent) ConfigChanged?.Invoke(Current);
            }
            catch { }
        }
    }
}