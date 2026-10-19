using System;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NetMonitor.Core
{
    /// <summary>
    /// 开机自启管理（注册表 HKCU\...\Run）。纯逻辑，UI 提示由调用方处理。
    /// </summary>
    internal static class AutoStartManager
    {
        private const string RunKeyPath = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string ValueName  = "NetMonitor";

        /// <summary>设置/取消开机自启。返回是否成功；失败时 <paramref name="error"/> 携带异常信息。</summary>
        public static bool SetAutoRun(bool enable, out string error)
        {
            error = null;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (enable)
                        key.SetValue(ValueName, Application.ExecutablePath);
                    else
                        key.DeleteValue(ValueName, false);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>是否已开机自启（注册表值与当前 exe 路径一致）。</summary>
        public static bool GetAutoRun()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    object value = key?.GetValue(ValueName);
                    return value != null && value.ToString() == Application.ExecutablePath;
                }
            }
            catch
            {
                // 忽略异常，默认为未启用
            }
            return false;
        }

        /// <summary>注册表自启值是否与当前 exe 路径不一致（程序路径已被修改）。</summary>
        public static bool IsPathChanged()
        {
            try
            {
                string expectedPath = Application.ExecutablePath;
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    object value = key?.GetValue(ValueName);
                    return value != null && value.ToString() != expectedPath;
                }
            }
            catch
            {
                // 忽略异常，继续执行
            }
            return false;
        }

        /// <summary>进程名是否非 NetMonitor（程序名称已被修改）。</summary>
        public static bool IsNameChanged()
        {
            return Process.GetCurrentProcess().ProcessName != "NetMonitor";
        }
    }
}
