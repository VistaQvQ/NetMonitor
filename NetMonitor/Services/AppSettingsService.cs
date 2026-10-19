using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using NetMonitor.Properties;

namespace NetMonitor.Services
{
    /// <summary>用户设置快照（POCO），在持久化与窗体状态之间传递。</summary>
    internal sealed class AppSettings
    {
        public Point WindowLocation;
        public bool ShowInFullScreen;
        public bool MultiNicMode;
        public HashSet<string> MultiNicSelectedIds;
    }

    /// <summary>设置持久化：封装 Properties.Settings 的读写，读取失败时回退到默认值。</summary>
    internal static class AppSettingsService
    {
        public static AppSettings Load()
        {
            AppSettings settings = new AppSettings();
            try
            {
                settings.WindowLocation    = Settings.Default.WinowLocation;
                settings.ShowInFullScreen  = Settings.Default.ShowInFullScreen;
                settings.MultiNicMode      = Settings.Default.MultNicMode;

                // 读取多网卡筛选网卡集合；空串 = 未自定义（全选）
                string savedIds = Settings.Default.MultiNicSelectedIds;
                if (!string.IsNullOrEmpty(savedIds))
                    settings.MultiNicSelectedIds = new HashSet<string>(
                        savedIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else
                    settings.MultiNicSelectedIds = null;
            }
            catch (Exception ex)
            {
                settings.WindowLocation   = new Point(710, 10);
                settings.ShowInFullScreen = false;
                settings.MultiNicMode     = false;
                settings.MultiNicSelectedIds = null;
                Debug.WriteLine("读取用户设置失败: " + ex.Message);
            }
            return settings;
        }

        public static void Save(AppSettings s)
        {
            Settings.Default.WinowLocation    = s.WindowLocation;
            Settings.Default.ShowInFullScreen = s.ShowInFullScreen;
            Settings.Default.MultNicMode      = s.MultiNicMode;
            Settings.Default.MultiNicSelectedIds =
                s.MultiNicSelectedIds == null ? "" : string.Join(",", s.MultiNicSelectedIds);
            Settings.Default.Save();
        }
    }
}
