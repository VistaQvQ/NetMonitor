using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace NetMonitor.Core
{
    /// <summary>
    /// 全屏检测：判断当前前台窗口是否为全屏应用。
    /// 纯静态逻辑，无窗体依赖，可独立单元测试。
    /// </summary>
    internal static class FullScreenDetector
    {
        // 刚启动进程（< 3s）或主窗口未就绪时统一忽略，防止 splash / launcher 误触发
        private const double IgnoreNewProcessSeconds = 3.0;

        /// <summary>
        /// 判断当前前台窗口是否为全屏应用。
        /// 依次排除：本进程 → DWM cloaked → 不可见 → 有 owner → ToolWindow/Layered → 刚启动进程。
        /// 通过后比较窗口矩形与屏幕边界（含任务栏覆盖/自动隐藏逻辑）。
        /// </summary>
        public static bool IsFullScreen()
        {
            const int tolerance = 8;
            try
            {
                IntPtr fg = NativeApi.GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;

                // 前台窗口属于本进程 → 不是"其他全屏"
                // （用进程 ID 而非句柄比较，兼容 WS_EX_TOOLWINDOW 窗口）
                try
                {
                    uint fgPid;
                    NativeApi.GetWindowThreadProcessId(fg, out fgPid);
                    if (fgPid == (uint)Process.GetCurrentProcess().Id) return false;
                }
                catch { }

                // 排除 DWM cloaked（UWP / 虚拟桌面不在当前桌面的窗口）
                try
                {
                    int cloaked = 0;
                    if (NativeApi.DwmGetWindowAttribute(fg, NativeApi.DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0)
                        return false;
                }
                catch { }

                if (!NativeApi.IsWindowVisible(fg)) return false;

                // 排除有 owner 的窗口（splash、临时子窗口）
                try { if (NativeApi.GetWindow(fg, NativeApi.GW_OWNER) != IntPtr.Zero) return false; }
                catch { }

                // 排除 ToolWindow / Layered（HUD、透明叠加层等非典型全屏应用）
                try
                {
                    int exStyle = NativeApi.GetWindowLong(fg, NativeApi.GWL_EXSTYLE);
                    if ((exStyle & NativeApi.WS_EX_TOOLWINDOW) != 0) return false;
                    if ((exStyle & NativeApi.WS_EX_LAYERED)    != 0) return false;
                }
                catch { }

                // 获取窗口矩形
                NativeApi.RECT rect;
                if (!NativeApi.GetWindowRect(fg, out rect)) return false;
                int width  = rect.Right  - rect.Left;
                int height = rect.Bottom - rect.Top;
                if (width <= 0 || height <= 0) return false;

                // 排除刚启动进程（< IgnoreNewProcessSeconds）及主窗口未就绪的进程
                try
                {
                    uint pid;
                    NativeApi.GetWindowThreadProcessId(fg, out pid);
                    if (pid != 0)
                    {
                        try
                        {
                            Process process = Process.GetProcessById((int)pid);
                            if ((DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalSeconds < IgnoreNewProcessSeconds)
                                return false;
                            if (process.MainWindowHandle == IntPtr.Zero && string.IsNullOrEmpty(process.MainWindowTitle))
                                return false;
                        }
                        catch { }
                    }
                }
                catch { }

                // 取窗口所在屏幕
                Rectangle winRect = new Rectangle(rect.Left, rect.Top, Math.Max(1, width), Math.Max(1, height));
                Screen screen;
                try   { screen = Screen.FromRectangle(winRect); }
                catch { screen = Screen.PrimaryScreen; }
                Rectangle screenBounds = screen.Bounds;

                bool coversScreen =
                    Math.Abs(rect.Left   - screenBounds.Left)   <= tolerance &&
                    Math.Abs(rect.Top    - screenBounds.Top)     <= tolerance &&
                    Math.Abs(rect.Right  - screenBounds.Right)   <= tolerance &&
                    Math.Abs(rect.Bottom - screenBounds.Bottom)  <= tolerance;

                // 任务栏状态
                IntPtr taskbarHwnd = NativeApi.FindWindow("Shell_TrayWnd", null);
                NativeApi.APPBARDATA appBarData = new NativeApi.APPBARDATA { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeApi.APPBARDATA)) };
                int abmState = 0;
                try { abmState = NativeApi.SHAppBarMessage(NativeApi.ABM_GETSTATE, ref appBarData).ToInt32(); } catch { }
                bool taskbarAutoHide = (abmState & NativeApi.ABS_AUTOHIDE) == NativeApi.ABS_AUTOHIDE;

                bool coversTaskbar = false;
                if (taskbarHwnd != IntPtr.Zero)
                {
                    NativeApi.RECT taskRect;
                    if (NativeApi.GetWindowRect(taskbarHwnd, out taskRect))
                    {
                        coversTaskbar =
                            rect.Left   <= taskRect.Left   + tolerance &&
                            rect.Top    <= taskRect.Top    + tolerance &&
                            rect.Right  >= taskRect.Right  - tolerance &&
                            rect.Bottom >= taskRect.Bottom - tolerance;
                    }
                }

                if (coversScreen && (coversTaskbar || taskbarAutoHide))
                {
                    //Debug.WriteLine("前台窗口被判定为全屏（覆盖任务栏或任务栏自动隐藏）");
                    return true;
                }

                // 面积覆盖率 ≥ 99.5% 时也认为是全屏
                if ((double)(width * height) / (screenBounds.Width * screenBounds.Height) >= 0.995)
                {
                    //Debug.WriteLine("前台窗口面积占比接近屏幕，判定为全屏");
                    return true;
                }

                //Debug.WriteLine("前台窗口未判定为全屏");
                return false;
            }
            catch
            {
                //Debug.WriteLine("isFullScreen 异常");
                return false;
            }
        }
    }
}
