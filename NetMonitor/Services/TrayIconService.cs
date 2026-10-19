using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace NetMonitor.Services
{
    /// <summary>
    /// 系统托盘图标封装。NotifyIcon 由传入的容器（窗体 components）托管生命周期，
    /// 与窗体右键菜单共用同一 ContextMenuStrip 实例。
    /// </summary>
    internal sealed class TrayIconService
    {
        private readonly NotifyIcon icon;

        /// <summary>双击托盘图标触发（用于切换窗体显示/隐藏）。</summary>
        public event Action ToggleRequested;

        public TrayIconService(IContainer components, ContextMenuStrip menu, Icon icon, string text)
        {
            this.icon = new NotifyIcon(components)
            {
                Icon             = icon,
                Text             = text,
                Visible          = true,
                ContextMenuStrip = menu
            };
            this.icon.DoubleClick += (s, e) => { ToggleRequested?.Invoke(); };
        }

        /// <summary>隐藏托盘图标（窗体关闭时调用，防止幽灵图标残留）。</summary>
        public void Hide()
        {
            icon.Visible = false;
        }
    }
}
