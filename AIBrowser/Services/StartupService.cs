using Microsoft.Win32;
using System;

namespace AIBrowser.Services
{
    internal static class StartupService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static void SetRunOnStartup(string appName, string exePath, bool enable)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                           ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (key == null) return;

            if (enable)
            {
                // 加引号避免路径有空格
                key.SetValue(appName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(appName, throwOnMissingValue: false);
            }
        }

        public static bool IsRunOnStartup(string appName)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var val = key?.GetValue(appName) as string;
            return !string.IsNullOrWhiteSpace(val);
        }
    }
}
