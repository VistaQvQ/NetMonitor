using System;
using System.Runtime.InteropServices;

namespace NetMonitor
{
    /// <summary>
    /// 集中管理所有 Win32 P/Invoke 声明与相关常量。
    /// 业务类通过调用此处的静态方法/常量访问原生 API，不再直接持有 DllImport。
    /// </summary>
    internal static class NativeApi
    {
        // ══════════════════════════════════════════════════════════════════════════
        // 结构体
        // ══════════════════════════════════════════════════════════════════════════

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct APPBARDATA
        {
            public int    cbSize;
            public IntPtr hWnd;
            public uint   uCallbackMessage;
            public uint   uEdge;
            public RECT   rc;
            public int    lParam;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 常量
        // ══════════════════════════════════════════════════════════════════════════

        // 窗口拖动消息
        internal const int  WM_SYSCOMMAND  = 0x0112;
        internal const int  SC_MOVE        = 0xF010;
        internal const int  HTCAPTION      = 0x0002;

        // 扩展样式
        internal const int  GWL_EXSTYLE    = -20;
        internal const int  WS_EX_TOOLWINDOW = 0x00000080;
        internal const int  WS_EX_LAYERED    = 0x00080000;

        // GetWindow 关系
        internal const uint GW_OWNER       = 4;

        // AppBar 状态
        internal const int  ABM_GETSTATE   = 0x00000004;
        internal const int  ABS_AUTOHIDE   = 0x1;

        // DWM 属性
        internal const int  DWMWA_CLOAKED  = 14;

        // ══════════════════════════════════════════════════════════════════════════
        // user32.dll
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>释放鼠标捕获，配合 SendMessage 实现无边框窗体拖动。</summary>
        [DllImport("user32.dll")]
        internal static extern bool ReleaseCapture();

        /// <summary>向目标窗口发送消息（同步）。</summary>
        [DllImport("user32.dll")]
        internal static extern bool SendMessage(IntPtr hwnd, int wMsg, int wParam, int lParam);

        /// <summary>返回当前前台（焦点）窗口句柄。</summary>
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        /// <summary>判断窗口是否可见。</summary>
        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        /// <summary>获取窗口的屏幕矩形。</summary>
        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        /// <summary>获取窗口所属线程 ID 和进程 ID。</summary>
        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        /// <summary>按关系（GW_*）获取相关窗口句柄。</summary>
        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        /// <summary>读取窗口整型属性（如扩展样式 GWL_EXSTYLE）。</summary>
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        /// <summary>枚举所有顶级窗口。</summary>
        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        /// <summary>按类名/窗口名查找窗口（用于定位任务栏）。</summary>
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        internal static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        // ══════════════════════════════════════════════════════════════════════════
        // shell32.dll
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>发送 AppBar 消息（用于查询任务栏自动隐藏状态）。</summary>
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        internal static extern IntPtr SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

        // ══════════════════════════════════════════════════════════════════════════
        // dwmapi.dll
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>读取 DWM 窗口属性（用于判断 UWP / 虚拟桌面 cloaked 状态）。</summary>
        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(
            IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        // ══════════════════════════════════════════════════════════════════════════
        // 分层窗口（Per-Pixel Alpha 透明，替代 TransparencyKey 色键）
        // ══════════════════════════════════════════════════════════════════════════

        // UpdateLayeredWindow 标志
        internal const int  ULW_ALPHA      = 0x00000002;
        internal const byte AC_SRC_OVER    = 0x00;
        internal const byte AC_SRC_ALPHA   = 0x01;   // 位图带 Alpha 通道，按像素混合

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SIZE
        {
            public int cx, cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        /// <summary>以每像素 Alpha 通道呈现分层窗口内容（真透明、无白边、支持半透明）。</summary>
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UpdateLayeredWindow(
            IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        /// <summary>获取窗口或屏幕设备上下文（hwnd 为 0 表示整个屏幕）。</summary>
        [DllImport("user32.dll")]
        internal static extern IntPtr GetDC(IntPtr hWnd);

        /// <summary>释放设备上下文。</summary>
        [DllImport("user32.dll")]
        internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        // ══════════════════════════════════════════════════════════════════════════
        // gdi32.dll（位图 → 兼容 DC，供 UpdateLayeredWindow 使用）
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>创建与指定 DC 兼容的内存 DC。</summary>
        [DllImport("gdi32.dll")]
        internal static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        /// <summary>将对象选入 DC，返回被替换的旧对象。</summary>
        [DllImport("gdi32.dll")]
        internal static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

        /// <summary>删除 GDI 对象（位图等）。</summary>
        [DllImport("gdi32.dll")]
        internal static extern bool DeleteObject(IntPtr hObject);

        /// <summary>删除设备上下文。</summary>
        [DllImport("gdi32.dll")]
        internal static extern bool DeleteDC(IntPtr hdc);

        // ══════════════════════════════════════════════════════════════════════════
        // winmm.dll（多媒体定时器：提升系统定时器分辨率，供高帧率 GIF 动画使用）
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>将系统定时器分辨率提升到指定毫秒（如 1ms），是 80/100fps 精确渲染的前提。</summary>
        [DllImport("winmm.dll")]
        internal static extern uint timeBeginPeriod(uint uMilliseconds);

        /// <summary>还原系统定时器分辨率（与 timeBeginPeriod 成对调用）。</summary>
        [DllImport("winmm.dll")]
        internal static extern uint timeEndPeriod(uint uMilliseconds);
    }
}
