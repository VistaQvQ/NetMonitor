using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NetMonitor
{
    static class Program
    {
        // ══════════════════════════════════════════════════════════════════════
        // 【后台进程方案 v3 - 正确实现】
        //
        // 任务管理器将进程归为"应用"的条件：
        //   进程拥有可见的、带 WS_APPWINDOW 样式（或无 owner）的顶级窗口。
        //
        // 正确做法：保持窗口为顶级窗口（不 SetParent），
        //   但移除 WS_EX_APPWINDOW + 添加 WS_EX_TOOLWINDOW，
        //   使窗口不出现在任务栏和任务管理器"应用"列表中。
        //
        // 错误做法（已验证失败）：
        //   SetParent → HWND_MESSAGE 子窗口。子窗口的客户区由父窗口裁剪，
        //   HWND_MESSAGE 没有客户区，子窗口永远不渲染。
        // ══════════════════════════════════════════════════════════════════════

        // 获取/设置窗口扩展样式
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        private const int  GWL_EXSTYLE       = -20;
        private const int  WS_EX_TOOLWINDOW  = 0x00000080;  // 从任务栏隐藏
        private const int  WS_EX_APPWINDOW   = 0x00040000;  // 强制出现在任务栏

        private const uint SWP_NOMOVE        = 0x0002;
        private const uint SWP_NOSIZE        = 0x0001;
        private const uint SWP_NOZORDER      = 0x0004;
        private const uint SWP_NOACTIVATE    = 0x0010;
        private const uint SWP_FRAMECHANGED  = 0x0020;  // 强制重新读取样式

        [STAThread]
        static void Main()
        {
            bool createNew;
            using (System.Threading.Mutex m = new System.Threading.Mutex(true, Application.ProductName, out createNew))
            {
                if (createNew)
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);

                    // ── Step 1：创建窗体（默认不可见）──────────────────────────────
                    var form = new NetMonitor();

                    // ── Step 2：强制创建窗口句柄 ────────────────────────────────────
                    // 访问 Handle 属性触发 WinForms 内部的 CreateHandle()
                    var handle = form.Handle;
                    Debug.WriteLine($"[Program] form.Handle = {handle}");

                    // ── Step 3：修改扩展样式，隐藏任务栏按钮 ────────────────────────
                    // 读取当前扩展样式
                    int exStyle = GetWindowLong(handle, GWL_EXSTYLE);
                    Debug.WriteLine($"[Program] 原始 ExStyle = 0x{exStyle:X8}");

                    // 移除 WS_EX_APPWINDOW（强制显示在任务栏的标志）
                    // 添加 WS_EX_TOOLWINDOW（工具窗口，不出现在任务栏/任务管理器应用列表）
                    int newExStyle = (exStyle & ~WS_EX_APPWINDOW) | WS_EX_TOOLWINDOW;
                    SetWindowLong(handle, GWL_EXSTYLE, newExStyle);

                    int appliedStyle = GetWindowLong(handle, GWL_EXSTYLE);
                    Debug.WriteLine($"[Program] 应用后 ExStyle = 0x{appliedStyle:X8}");
                    Debug.WriteLine($"[Program] WS_EX_TOOLWINDOW 已设置: {(appliedStyle & WS_EX_TOOLWINDOW) != 0}");
                    Debug.WriteLine($"[Program] WS_EX_APPWINDOW 已清除: {(appliedStyle & WS_EX_APPWINDOW) == 0}");

                    // ── Step 4：通知系统重新读取样式 ────────────────────────────────
                    // SWP_FRAMECHANGED 强制系统处理 WM_NCCALCSIZE，使样式变化生效
                    SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

                    // ── Step 5：显示窗体，触发 OnShown 初始化链 ─────────────────────
                    // OnShown → InitNetworkInterface + InitializeTimer + readUserSettings
                    // 必须调用 Show() 才能触发 OnShown；
                    // WS_EX_TOOLWINDOW 已设置，窗口不会出现在任务栏。
                    Debug.WriteLine("[Program] 调用 form.Show()...");
                    form.Show();
                    Debug.WriteLine($"[Program] form.Show() 完成，form.Visible = {form.Visible}");

                    // ── Step 6：用空 ApplicationContext 启动 ─────────────────────────
                    // 传入 form 的 Application.Run 会把窗口注册为"主窗口"，
                    // 改用空 ApplicationContext 避免这一行为，窗口生命周期自管理。
                    Debug.WriteLine("[Program] 启动 ApplicationContext（空）");
                    Application.Run(new ApplicationContext());
                }
                else
                {
                    MessageBox.Show("Only One Instance Of This Application Is Allowed!");
                }
            }
        }
    }
}
