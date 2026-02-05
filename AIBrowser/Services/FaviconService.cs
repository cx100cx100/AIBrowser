using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;

namespace AIBrowser.Services
{
    internal static class FaviconService
    {
        // 设置 3 秒超时，防止卡死
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        public static string IconsDir =>
            Path.Combine(App.Config.ConfigDir, "icons");

        public static string GetIconPath(string tabId) =>
            Path.Combine(IconsDir, $"{tabId}.png");

        public static async Task<string?> TryDownloadFaviconAsync(string pageUrl, string tabId, Func<Task<string?>> getIconUrlFromDom)
        {
            try
            {
                Directory.CreateDirectory(IconsDir);
                var finalPath = GetIconPath(tabId);

                // 1. 本地缓存检查：如果文件存在且大小 > 0，直接用（极速）
                if (File.Exists(finalPath))
                {
                    try
                    {
                        if (new FileInfo(finalPath).Length > 0) return finalPath;
                    }
                    catch { /* 忽略文件访问冲突，继续下载 */ }
                }

                byte[]? bytes = null;
                string? iconUrl = null;

                // 2. 方案 A：尝试从网页 DOM 获取 (最准确，但 SPA 网页容易抓空)
                try
                {
                    iconUrl = await getIconUrlFromDom();
                }
                catch { }

                if (!string.IsNullOrWhiteSpace(iconUrl))
                {
                    // 处理相对路径
                    if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var baseUri) &&
                        Uri.TryCreate(baseUri, iconUrl, out var abs))
                    {
                        iconUrl = abs.ToString();
                    }
                    bytes = await DownloadBytesAsync(iconUrl);
                }

                // 3. 方案 B：如果 DOM 失败，尝试标准的 /favicon.ico (传统方法)
                if ((bytes == null || bytes.Length < 10) && Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri))
                {
                    var fallbackUrl = $"{pageUri.Scheme}://{pageUri.Host}/favicon.ico";
                    bytes = await DownloadBytesAsync(fallbackUrl);
                }

                // 4. 方案 C (最强保底)：如果上面都挂了，使用 Google Favicon API
                // 专门解决 Gemini、Bilibili 等反爬或动态加载的网站
                if ((bytes == null || bytes.Length < 10) && Uri.TryCreate(pageUrl, UriKind.Absolute, out var uriC))
                {
                    // sz=64 表示请求 64x64 的高清图标
                    var googleApiUrl = $"https://www.google.com/s2/favicons?domain={uriC.Host}&sz=64";
                    bytes = await DownloadBytesAsync(googleApiUrl);
                }

                // 5. 还是没有？放弃。
                if (bytes == null || bytes.Length < 10) return null;

                // 6. 验证并保存图片
                try
                {
                    using var ms = new MemoryStream(bytes);
                    // 这一步非常关键：它能检测下载的是不是真的图片（而不是 404 页面的 HTML）
                    using var img = System.Drawing.Image.FromStream(ms);

                    // 如果旧文件存在，先删除，防止占用报错
                    if (File.Exists(finalPath)) File.Delete(finalPath);

                    img.Save(finalPath, ImageFormat.Png);
                    return finalPath;
                }
                catch
                {
                    // 下载的不是图片（比如是文本），忽略
                    return null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"图标下载总异常: {ex.Message}");
                return null;
            }
        }

        private static async Task<byte[]?> DownloadBytesAsync(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            try
            {
                var resp = await _http.GetAsync(url);
                if (resp.IsSuccessStatusCode)
                {
                    return await resp.Content.ReadAsByteArrayAsync();
                }
            }
            catch
            {
                // 超时或 404 忽略
            }
            return null;
        }
    }
}