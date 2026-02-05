using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;

namespace AIBrowser.Services
{
    internal sealed class TrayService : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly Action _onExit;
        private readonly Action _onRestart;
        private readonly Action _onShow;

        public TrayService(Action onShow, Action onExit, Action onRestart)
        {
            _onShow = onShow;
            _onExit = onExit;
            _onRestart = onRestart;

            _notifyIcon = new NotifyIcon
            {
                Text = "AIbrowser",
                Icon = SystemIcons.Application, // 先用默认图标，后面再换成你的 icon
                Visible = true,
                ContextMenuStrip = BuildMenu()
            };

            // 双击托盘图标显示窗口（虽然你没强制，但这个很实用；不想要我下一步教你删掉）
            _notifyIcon.DoubleClick += (_, __) => _onShow();
        }

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();

            var restart = new ToolStripMenuItem("重启");
            restart.Click += (_, __) => _onRestart();

            var exit = new ToolStripMenuItem("退出");
            exit.Click += (_, __) => _onExit();

            menu.Items.Add(restart);
            menu.Items.Add(exit);

            return menu;
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}
