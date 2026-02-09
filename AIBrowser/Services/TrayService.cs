using System;
using System.Drawing; // 用于 Icon
using System.Windows; // 用于 WPF 的 Application

namespace AIBrowser.Services
{
    internal sealed class TrayService : IDisposable
    {
        // 显式使用 System.Windows.Forms 前缀，不引用整个命名空间
        private readonly System.Windows.Forms.NotifyIcon _notifyIcon;

        private readonly Action _onShow;
        private readonly Action _onExit;
        private readonly Action _onRestart;

        public TrayService(Action onShow, Action onExit, Action onRestart)
        {
            _onShow = onShow;
            _onExit = onExit;
            _onRestart = onRestart;

            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Text = "AIBrowser AI 聚合浏览器",
                Visible = true,
                ContextMenuStrip = BuildMenu()
            };

            try
            {
                var iconUri = new Uri("pack://application:,,,/AIBrowser.ico");
                var streamInfo = System.Windows.Application.GetResourceStream(iconUri);

                if (streamInfo != null)
                {
                    _notifyIcon.Icon = new Icon(streamInfo.Stream);
                }
                else
                {
                    _notifyIcon.Icon = SystemIcons.Application;
                }
            }
            catch
            {
                _notifyIcon.Icon = SystemIcons.Application;
            }

            _notifyIcon.DoubleClick += (_, __) => _onShow();
        }

        private System.Windows.Forms.ContextMenuStrip BuildMenu()
        {
            var menu = new System.Windows.Forms.ContextMenuStrip();

            var showItem = new System.Windows.Forms.ToolStripMenuItem("显示主界面");
            showItem.Click += (_, __) => _onShow();

            var restartItem = new System.Windows.Forms.ToolStripMenuItem("重启");
            restartItem.Click += (_, __) => _onRestart();

            var exitItem = new System.Windows.Forms.ToolStripMenuItem("退出");
            exitItem.Click += (_, __) => _onExit();

            menu.Items.Add(showItem);
            menu.Items.Add(restartItem);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(exitItem);

            return menu;
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}