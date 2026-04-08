using NetMonitor.Properties;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Timers;
using System.Windows.Forms;

namespace NetMonitor
{
    public partial class NetMonitor : Form
    {
        private int InterfaceSelect = 0;
        private int ZreoTimes = 0;
        private bool SpeedCalcStart = false;
        private NetworkInterface[] nicArr;      //网卡集合
        private NetworkInterface[] availableNic; //可用网卡集合
        private System.Timers.Timer timers;

        //计时器
        private System.Windows.Forms.Timer giftimer;
        private Image[] gifFrames;
        private int gifFrameIndex = 0;
        private int gifFrameCount = 0;
        // 可调整：1 = 原始帧率（更慢），2 = 中速，4 = 较快（默认）
        private int gifStep = 1;

        // 新增字段（类级别）
        private long prevBytesSent = 0;
        private long prevBytesRecv = 0;
        // 记录上次采样时间，用于按真实时间计算速度（秒）
        private DateTime prevSampleTime = DateTime.MinValue;

        // 新增字段：去抖计数
        private int fullScreenConsecutiveCount = 0;
        private const int FullScreenConfirmThreshold = 2; // 需要连续两次检测为全屏才认为是真正全屏（约2秒）

        // 初始化完成标志（幂等保护）
        private bool formInitialized = false;

        /// <summary>
        /// 系统托盘图标。
        /// 在 OnShown 中创建，与窗体右键菜单（Menu）共用同一个 ContextMenuStrip 实例，
        /// 保证菜单项状态自动同步，无需额外逻辑。
        /// 在 FormClosing 中设置 Visible = false 以避免"幽灵图标"（程序退出后图标滞留）。
        /// </summary>
        private NotifyIcon trayIcon;

        public NetMonitor()
        {
            InitializeComponent();
            // 不在构造函数中初始化网卡，改到 OnShown 中以避免阻塞启动
        }

        // 将初始化放到 OnShown 的简要说明：
        // 1) 确保窗体已可见且句柄就绪，避免在启动早期对 UI 或句柄的误操作。
        // 2) 避免在 Load/构造阶段立即触发耗时/弹窗（例如注册表访问或 MessageBox），提升首屏渲染体验。 
        // 3) OnShown 在 UI 线程执行，便于安全地做 UI 相关初始化；配合 `initialized` 标志确保幂等。
        // 4) 若需更早收集数据，应改为异步或延迟首次采样，避免阻塞 UI。
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Debug.WriteLine($"[OnShown] 触发，formInitialized={formInitialized}, this.Visible={this.Visible}, Handle={this.Handle}");
            // 只在首次显示时初始化，避免重复执行
            if (!formInitialized)
            {
                InitNetworkInterface();
                InitializeTimer();
                readUserSettings();
#if !DEBUG
                InitAutoRunMenuItem();
#endif
                formInitialized = true;
                Debug.WriteLine("[OnShown] 核心初始化完成");

                // ── 初始化系统托盘图标 ────────────────────────────────────────────
                // 使用 this.components 托管生命周期，窗体 Dispose 时自动清理。
                // Icon：提取可执行文件关联图标，与资源管理器/任务管理器显示一致。
                // ContextMenuStrip：直接复用窗体已有的 Menu 实例，
                //   菜单项（选项勾选状态、网卡列表等）自动同步，无需额外代码。
                trayIcon = new NotifyIcon(this.components)
                {
                    Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
                    Text = "NetMonitor - 网络速度监控",
                    Visible = true,
                    ContextMenuStrip = this.Menu   // 与窗体右键菜单共用同一实例
                };

                // 双击托盘图标：切换悬浮窗显示/隐藏（方便在全屏应用下临时唤出）
                trayIcon.DoubleClick += (s, ev) =>
                {
                    this.Visible = !this.Visible;
                    Debug.WriteLine($"[TrayIcon] 双击，窗体可见性切换为 {this.Visible}");
                };

                Debug.WriteLine("[OnShown] 托盘图标初始化完成");
            }
        }

        /// <summary>
        /// 从 user32.dll 导入 ReleaseCapture 函数。
        /// 说明：释放当前窗口对鼠标的捕获。常用于在自定义窗体标题栏或无边框窗体中实现拖动功能时，
        /// 在开始发送移动消息之前释放系统对鼠标的捕获，以便后续通过 SendMessage 模拟系统移动窗口的行为。
        /// </summary>
        /// <returns>如果成功返回 true，否则返回 false。</returns>
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        /// <summary>
        /// 从 user32.dll 导入 SendMessage 函数（简化签名）。
        /// 说明：向指定窗口发送一个消息。本程序中用于向窗体发送系统命令（如移动窗口）
        /// 以模拟拖动无边框窗体的行为。
        /// </summary>
        /// <param name="hwnd">目标窗口句柄（窗口的 IntPtr）。</param>
        /// <param name="wMsg">消息编号（例如 WM_SYSCOMMAND）。</param>
        /// <param name="wParam">消息的第一个参数（例如系统命令和子参数）。</param>
        /// <param name="lParam">消息的第二个参数（通常为坐标或额外信息）。</param>
        /// <returns>通常返回消息处理结果，布尔值或依据具体消息而定。</returns>
        [DllImport("user32.dll")]
        public static extern bool SendMessage(IntPtr hwnd, int wMsg, int wParam, int lParam);

        /// <summary>
        /// 在 Windows 消息中表示"系统命令"消息（消息编号 0x0112）。
        /// 与 SendMessage 配合使用以发送系统级命令（如最小化、最大化或移动）。
        /// </summary>
        public const int WM_SYSCOMMAND = 0x0112;

        /// <summary>
        /// 系统命令的子项，表示移动窗口命令（0xF010）。
        /// 通常与 WM_SYSCOMMAND 一起使用，配合 HTCAPTION 可以模拟拖动标题栏。
        /// </summary>
        public const int SC_MOVE = 0xF010;

        /// <summary>
        /// 表示标题栏（caption）的命中测试值（0x0002）。
        /// 与 SC_MOVE 一起使用时表示对标题栏的移动操作，从而让窗口开始移动。
        /// </summary>
        public const int HTCAPTION = 0x0002;

        private void InitializeTimer()
        {
            timers = new System.Timers.Timer();
            timers.Interval = 1000;
            timers.Elapsed += timer_Elapsed;
            timers.Start();
        }

        void timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            // 非阻塞地将更新请求排入 UI 消息队列，避免在计时器线程上同步等待 UI
            if (this.IsHandleCreated && !this.IsDisposed)
            {
                try
                {
                    this.BeginInvoke((Action)UpdateNetworkInterface);
                }
                catch (InvalidOperationException)
                {
                    // 窗体已被关闭或句柄不可用，忽略
                }

            }
        }

        private void SetGifBackground()
        {
            // 保护：如果资源为空则直接返回
            Image gif = Properties.Resources.Cadogt;
            if (gif == null) return;

            // 释放旧资源（如果存在）
            try
            {
                if (giftimer != null)
                {
                    giftimer.Stop();
                    giftimer.Tick -= Giftimer_Tick;
                    giftimer.Dispose();
                    giftimer = null;
                }
            }
            catch { }

            try
            {
                // 提取帧
                var fd = new System.Drawing.Imaging.FrameDimension(gif.FrameDimensionsList[0]);
                gifFrameCount = gif.GetFrameCount(fd);
                if (gifFrameCount <= 0) return;

                // 释放旧帧数组（如果存在）
                if (gifFrames != null)
                {
                    foreach (var img in gifFrames)
                    {
                        try { img?.Dispose(); } catch { }
                    }
                }

                gifFrames = new Image[gifFrameCount];

                // 复制每一帧为独立的 Bitmap（保持透明通道）
                for (int i = 0; i < gifFrameCount; i++)
                {
                    gif.SelectActiveFrame(fd, i);

                    // 创建支持 alpha 的位图并清空为透明色
                    var bmp = new Bitmap(gif.Width, gif.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);

                        // 保证正确的 alpha 合成并使用较高质量的绘制参数
                        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                        // 绘制当前帧到目标位图，保留透明信息
                        g.DrawImage(gif, 0, 0, gif.Width, gif.Height);
                    }

                    gifFrames[i] = bmp;
                }

                // 配置计时器：使用 WinForms Timer 保证在 UI 线程执行
                giftimer = new System.Windows.Forms.Timer();
                // 使用较小间隔（约 60 FPS）并通过 gifStep 控制有效速度。
                giftimer.Interval = 16; // ~60FPS
                gifFrameIndex = 0;
                if (gifStep < 1) gifStep = 1;

                // 绑定单独方法，便于移除事件
                giftimer.Tick += Giftimer_Tick;
                giftimer.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine("SetGifBackground 错误: " + ex.Message);
            }
        }

        private void Giftimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (gifFrames == null || gifFrameCount == 0) return;

                gifFrameIndex = (gifFrameIndex + gifStep) % gifFrameCount;
                var frame = gifFrames[gifFrameIndex];

                // giftimer 是 WinForms Timer，在 UI 线程触发，直接更新即可。
                // 保留异常保护，防止单帧导致整体停止。
                this.pictureBox.BackgroundImage = frame;
            }
            catch (Exception ex)
            {
                try
                {
                    giftimer?.Stop();
                }
                catch { }
                Console.WriteLine("GIF 播放错误: " + ex.Message);
            }
        }

        public void UpdateNetworkInterface()
        {
            // 确保在 UI 线程执行
            if (this.InvokeRequired)
            {
                // 使用 BeginInvoke 以避免阻塞调用线程（和计时器线程）
                this.BeginInvoke(new Action(UpdateNetworkInterface));
                return;
            }
            bool fullScreen = isFullScreen();
            bool shouldShow = !fullScreen || this.ShowInFullScreen_ToolStripMenuItem.Checked;
            Debug.WriteLine($"[UpdateNetworkInterface] isFullScreen={fullScreen}, ShowInFullScreen={this.ShowInFullScreen_ToolStripMenuItem.Checked}, shouldShow={shouldShow}, currentVisible={this.Visible}");
            this.Visible = shouldShow;

            if (nicArr == null || nicArr.Length == 0)
            {
                prevBytesSent = 0;
                prevBytesRecv = 0;
                Lable_SpeedUP.Text = "  0B/S";
                Lable_SpeedDown.Text = "  0B/S";
                InitNetworkInterface();
                return;
            }

            if (ComboBox.SelectedIndex >= 0 && ComboBox.SelectedIndex < nicArr.Length)//防止越界
            {
                var nic = nicArr[ComboBox.SelectedIndex];
                var stats = nic.GetIPv4Statistics();
                long bytesSent = stats.BytesSent;
                long bytesRecv = stats.BytesReceived;

                long deltaSent = 0;
                long deltaRecv = 0;
                var now = DateTime.UtcNow;
                double deltaSeconds = 1.0; // 默认 1s，适用于首次采样或回退情况

                if (prevSampleTime != DateTime.MinValue)
                {
                    deltaSeconds = (now - prevSampleTime).TotalSeconds;
                    if (deltaSeconds <= 0) deltaSeconds = 1.0;
                }
                prevSampleTime = now;
                if (SpeedCalcStart && InterfaceSelect == ComboBox.SelectedIndex)
                {
                    deltaSent = bytesSent - prevBytesSent;
                    deltaRecv = bytesRecv - prevBytesRecv;
                }
                else
                {
                    // 切换网卡或首次启动：初始化，上次值设为当前值，避免突跳
                    SpeedCalcStart = true;
                    InterfaceSelect = ComboBox.SelectedIndex;
                    deltaSent = 0;
                    deltaRecv = 0;
                }

                // 更新"前一次"值（用于下一次差值计算）
                prevBytesSent = bytesSent;
                prevBytesRecv = bytesRecv;

                // 将 delta 转为 字节/秒，按真实时间间隔计算，避免 BeginInvoke / UI 延迟导致误差
                long netSendPerSec = (long)(deltaSent / deltaSeconds);
                long netRecvPerSec = (long)(deltaRecv / deltaSeconds);

                // 根据当前下载速度（bytes/s）调整 gifStep
                const long OneMB = 1024 * 1024;
                try
                {
                    if (netRecvPerSec < OneMB)
                    {
                        gifStep = 1;
                    }
                    else if (netRecvPerSec < 10 * OneMB)
                    {
                        gifStep = 2;
                    }
                    else
                    {
                        gifStep = 4;
                    }

                    if (gifStep < 1) gifStep = 1;
                }
                catch
                {
                    // 避免任何异常影响主流程，保留当前 gifStep
                }

                // 更新 UI：使用按秒速率显示
                Lable_SpeedUP.Text = FormatSpeed(netSendPerSec);
                Lable_SpeedDown.Text = FormatSpeed(netRecvPerSec);

                if (netRecvPerSec == 0 && netSendPerSec == 0)
                {
                    ZreoTimes++;
                    if (ZreoTimes >= 3)
                    {
                        if (ZreoTimes < nicArr.Length * 3)
                        {
                            ComboBox.SelectedIndex = (ComboBox.SelectedIndex + 1) % nicArr.Length;
                            ZreoTimes++;
                        }
                        else if (IsNetworkInterfaceListChanged())
                        {
                            ZreoTimes = 0;
                            InitNetworkInterface();
                        }
                        else
                        {
                            ComboBox.SelectedIndex = (ComboBox.SelectedIndex + 1) % nicArr.Length;
                            ZreoTimes = nicArr.Length * 3; //防止溢出
                        }
                    }
                }
                else
                {
                    ZreoTimes = 0;
                }

            }
        }


        private string FormatSpeed(long bytes)
        {
            if (bytes <= 0)
                return "  0B/S";
            else if (bytes < 1000)
            {
                return $"{bytes,3}B/S";
            }
            else if (bytes < 1024)
            {
                return "0.9K/S";
            }
            else if (bytes < 1024 * 1000)
            {
                return $"{(bytes / 1024.0),3:F0}K/S";
            }
            else if (bytes < 1024 * 1024)
            {
                return "0.9M/S";
            }
            else
            {
                return $"{(bytes / (1024.0 * 1024.0)),3:F0}M/S";
            }
        }

        private bool IsVirtualNetworkInterface(NetworkInterface nic)
        {
            if (nic == null) return true;

            // 先排除明显的类型
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Unknown)
            {
                return true;
            }

            string desc = (nic.Description ?? string.Empty).ToLowerInvariant();
            string name = (nic.Name ?? string.Empty).ToLowerInvariant();
            string id = (nic.Id ?? string.Empty).ToLowerInvariant();

            // 常见虚拟网卡关键字列表（可按需扩展）
            string[] virtualKeywords = new[]
            {
                "virtual", "vmware", "hyper-v", "hyperv", "virtualbox", "vbox",
                "tap", "tun", "hamachi", "teredo", "miniport", "pseudo", "loopback",
                "vpn", "wireguard", "gpd", "vmnet", "ndiswan", "azure", "tunnel", "openvpn"
            };

            foreach (var kw in virtualKeywords)
            {
                if (desc.Contains(kw) || name.Contains(kw) || id.Contains(kw))
                {
                    return true;
                }
            }

            try
            {
                var phys = nic.GetPhysicalAddress()?.GetAddressBytes();
                if (phys == null || phys.Length == 0) return true;
                bool allZero = true;
                foreach (var b in phys)
                {
                    if (b != 0)
                    {
                        allZero = false;
                        break;
                    }
                }
                if (allZero) return true;
            }
            catch
            {
                // 读取物理地址时出错，保守地认为可能是虚拟网卡
                return true;
            }

            return false;
        }

        public void InitNetworkInterface()
        {
            SpeedCalcStart = false;
            try
            {
                ComboBox.Text = "";
                ComboBox.Items.Clear();

                var all = NetworkInterface.GetAllNetworkInterfaces();

                // 先根据状态粗筛
                var candidates = all.Where(nic =>
                    nic.OperationalStatus == OperationalStatus.Up
                );

                // 进一步过滤掉虚拟网卡
                var filtered = candidates.Where(nic => !IsVirtualNetworkInterface(nic)).ToArray();

                nicArr = filtered;

                for (int i = 0; i < nicArr.Length; i++)
                {
                    // 使用显示名称或描述（根据需要选择），这里保留原来使用的 Name
                    ComboBox.Items.Add(nicArr[i].Name);
                }

                if (nicArr.Length > 0)
                {
                    ComboBox.SelectedIndex = 0;
                    //availableNic = DeepCopy(nicArr);
                }
                else
                {
                    // 如果没有找到任何物理/非虚拟网卡，保证界面不会因 SelectedIndex 异常崩溃
                    ComboBox.Text = "无可用网络接口";
                }
                Debug.WriteLine("可用网卡数量: " + nicArr.Length);
            }
            catch (Exception ex)
            {
                // 最小化处理，避免应用崩溃；可根据需要记录日志
                Console.WriteLine("InitNetworkInterface 错误: " + ex.Message);
                nicArr = new NetworkInterface[0];
                ComboBox.Items.Clear();
                ComboBox.Text = "获取网卡失败";
            }
        }
        // 修改后的 NetMonitor_Load（移除 InitializeTimer/InitAutoRunMenuItem）
        private void NetMonitor_Load(object sender, EventArgs e)
        {
            // Load 触发时消息循环尚未启动，不能用 Invoke（会死锁），直接调用即可
            // （Load 本身就在 UI 线程）
            Debug.WriteLine("[NetMonitor_Load] 触发，设置 GIF 背景");
            SetGifBackground();

            // 定时器和开机自启初始化已移至 OnShown，避免启动阶段干扰界面显示
        }

        private void NetMonitor_MouseDown(object sender, MouseEventArgs e)
        {
            ReleaseCapture();
            SendMessage(this.Handle, WM_SYSCOMMAND, SC_MOVE + HTCAPTION, 0);
            writeUserSettings();
        }

        private void Exit_Menu_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Menu.Hide();//隐藏一些东西
                if (MessageBox.Show("你确定关闭流量悬浮窗么？", "提示", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    this.Close();
                }

            }
        }

        private void ComboBox_DropDownClosed(object sender, EventArgs e)
        {
            Menu.Hide();
        }

        private void AutoRun_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!CheckProgramNameNotChanged())
            {
                return;
            }
            else
            {
                this.SetAutoRun(this.AutoRun_ToolStripMenuItem.Checked);
                //this.AutoRun_ToolStripMenuItem.Text = this.AutoRun_ToolStripMenuItem.Checked ? "开机自启(已启用)" : "开机自启(已禁用)";

            }
        }
        private bool SetAutoRun(bool enable)
        {
            try
            {
                string runKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runKey, true))
                {
                    if (enable)
                    {
                        key.SetValue("NetMonitor", Application.ExecutablePath);
                    }
                    else
                    {
                        key.DeleteValue("NetMonitor", false);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("设置开机自启失败: " + ex.Message);
                return false;
            }
        }
        private bool GetAutoRun()
        {
            try
            {
                string runKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runKey, false))
                {
                    var value = key.GetValue("NetMonitor");
                    if (value != null && value.ToString() == Application.ExecutablePath)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // 忽略异常，默认为未启用
            }
            return false;
        }
        private bool CheckProgramNameAndPathNotChanged()
        {
            try
            {
                string expectedPath = Application.ExecutablePath;
                string runKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runKey, false))
                {
                    var value = key.GetValue("NetMonitor");
                    if (value != null && value.ToString() != expectedPath)
                    {
                        MessageBox.Show("程序名称或路径已被修改,请重新设定开机自启！。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }
                }
            }
            catch
            {
                // 忽略异常，继续执行
            }
            return true;
        }
        private void InitAutoRunMenuItem()
        {
            if (this.GetAutoRun() && this.CheckProgramNameAndPathNotChanged())
            {
                this.AutoRun_ToolStripMenuItem.Checked = true;
                //this.AutoRun_ToolStripMenuItem.Text = "开机自启(已启用)";
            }
            else
            {
                this.AutoRun_ToolStripMenuItem.Checked = false;
                //this.AutoRun_ToolStripMenuItem.Text = "开机自启(已禁用)";
                this.SetAutoRun(false);
            }
        }
        private bool CheckProgramNameNotChanged()
        {
            string processName = Process.GetCurrentProcess().ProcessName;
            if (processName != "NetMonitor")
            {
                MessageBox.Show("程序名称已被修改,请恢复原名称后再试！。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            return true;
        }
        private bool compareNetworkLists(NetworkInterface[] list1, NetworkInterface[] list2)
        {
            if (list1.Length != list2.Length)
            {
                return false;
            }
            for (int i = 0; i < list1.Length; i++)
            {
                if (list1[i].Id != list2[i].Id)
                {
                    return false;
                }
            }
            return true;
        }
        private bool IsNetworkInterfaceListChanged()
        {
            try
            {
                var currentNics = NetworkInterface.GetAllNetworkInterfaces();
                var filteredCurrentNics = currentNics.Where(nic =>
                    nic.OperationalStatus == OperationalStatus.Up &&
                    !IsVirtualNetworkInterface(nic)).ToArray();
                if (!compareNetworkLists(filteredCurrentNics, nicArr))
                {
                    return true;
                }
            }
            catch
            {
                // 忽略异常，假设没有变化
            }
            return false;
        }


        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        // 新增：用于获取任务栏句柄与 APPBAR 状态
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public int lParam;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

        private const int ABM_GETSTATE = 0x00000004;
        private const int ABS_AUTOHIDE = 0x1;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        // 新增 P/Invoke：检测 UWP / Modern 窗口是否被 cloaked（隐藏在桌面外）
        // DwmGetWindowAttribute 用于判断窗口是否被系统 cloaked（例如 UWP 启动期间、虚拟桌面等场景）
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
        private const int DWMWA_CLOAKED = 14;

        // 新增常量与 P/Invoke（放在类中合适位置）
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        private const uint GW_OWNER = 4;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_LAYERED = 0x00080000;

        // 可配置：忽略刚启动的进程阈值（秒）
        private const double IgnoreNewProcessSeconds = 3.0;

        // 替换现有 isFullScreen 实现为下列更鲁棒版本
        private bool isFullScreen()
        {
            const int tolerance = 8;
            try
            {
                IntPtr fg = GetForegroundWindow();
                Debug.WriteLine($"[isFullScreen] GetForegroundWindow() = {fg}, this.Handle = {this.Handle}, this.Visible = {this.Visible}");

                if (fg == IntPtr.Zero) return false;

                // ── 自身判断 ──────────────────────────────────────────────────────
                // 原始逻辑：fg == this.Handle 直接比较句柄
                // 【注意】使用 SetParent 后主窗口是子窗口，GetForegroundWindow() 永远不会
                //   返回子窗口句柄（只返回顶级窗口），因此 fg == this.Handle 永远为 false。
                // 修复：改用进程 ID 判断 —— 前台窗口属于本进程则不是"其他全屏"。
                try
                {
                    uint fgPid;
                    GetWindowThreadProcessId(fg, out fgPid);
                    uint selfPid = (uint)Process.GetCurrentProcess().Id;
                    Debug.WriteLine($"[isFullScreen] fg进程ID = {fgPid}, 本进程ID = {selfPid}");
                    if (fgPid == selfPid)
                    {
                        Debug.WriteLine("[isFullScreen] 前台窗口属于本进程，返回 false");
                        return false;
                    }
                }
                catch { }

                // 排除被 DWM cloaked 的窗口（UWP / 尚未显示）
                try
                {
                    int cloaked = 0;
                    if (DwmGetWindowAttribute(fg, DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0)
                        return false;
                }
                catch { /* 忽略 */ }

                // 必须可见
                if (!IsWindowVisible(fg)) return false;

                // 排除有 owner 的窗口（通常是 splash、临时子窗口）
                try
                {
                    if (GetWindow(fg, GW_OWNER) != IntPtr.Zero)
                        return false;
                }
                catch { }

                // 排除 toolwindow / layered 等非典型应用窗口
                try
                {
                    int ex = GetWindowLong(fg, GWL_EXSTYLE);
                    if ((ex & WS_EX_TOOLWINDOW) != 0)
                        return false;
                    if ((ex & WS_EX_LAYERED) != 0)
                    {
                        // layer 窗口通常用于透明或动画 HUD，忽略为全屏（可按需修改）
                        return false;
                    }
                }
                catch { }
                /*大型程序在双击启动到真正显示主窗口之间，会创建临时或拥有者窗口（splash、launcher、无标题窗口、layered window）
                 * 或者进程刚启动尚未初始化主窗口。isFullScreen 在这些短暂过渡期仍然看到一个"前台窗口且占满屏幕"的句柄，因而误判为全屏。
                 * 解决思路：在判定为"其他全屏应用"前再做几步防护 —— 排除有 owner 的窗口（splash / 子窗口）
                 * 排除 toolwindow/ layered 等特殊窗体、排除刚启动的进程以及未准备好主窗口的进程；并保持去抖（连续多次）确认为全屏。
                */
                // 获取窗口矩形
                RECT rect;
                if (!GetWindowRect(fg, out rect)) return false;
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                if (width <= 0 || height <= 0) return false;

                // 如果前台窗口属于刚启动的进程，先忽略，等待进程稳定
                try
                {
                    uint pid;
                    GetWindowThreadProcessId(fg, out pid);
                    if (pid != 0)
                    {
                        try
                        {
                            var p = Process.GetProcessById((int)pid);
                            var startedUtc = p.StartTime.ToUniversalTime();
                            if ((DateTime.UtcNow - startedUtc).TotalSeconds < IgnoreNewProcessSeconds)
                            {
                                // 进程刚启动，忽略当前"全屏"判断
                                return false;
                            }

                            // 如果进程尚无主窗口标题或 MainWindowHandle 未就绪，说明还在初始化，忽略
                            if (p.MainWindowHandle == IntPtr.Zero && string.IsNullOrEmpty(p.MainWindowTitle))
                            {
                                return false;
                            }
                        }
                        catch
                        {
                            // 无法获取进程信息则继续后续判断
                        }
                    }
                }
                catch { }

                var winRect = new Rectangle(rect.Left, rect.Top, Math.Max(1, width), Math.Max(1, height));
                Screen screen;
                try
                {
                    screen = Screen.FromRectangle(winRect);
                }
                catch
                {
                    screen = Screen.PrimaryScreen;
                }
                var sb = screen.Bounds;

                bool coversScreen =
                    Math.Abs(rect.Left - sb.Left) <= tolerance &&
                    Math.Abs(rect.Top - sb.Top) <= tolerance &&
                    Math.Abs(rect.Right - sb.Right) <= tolerance &&
                    Math.Abs(rect.Bottom - sb.Bottom) <= tolerance;

                // 任务栏检测（保留原逻辑）
                IntPtr taskbarHwnd = FindWindow("Shell_TrayWnd", null);
                APPBARDATA abd = new APPBARDATA();
                abd.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
                int abmState = 0;
                try { abmState = SHAppBarMessage(ABM_GETSTATE, ref abd).ToInt32(); } catch { }
                bool taskbarAutoHide = (abmState & ABS_AUTOHIDE) == ABS_AUTOHIDE;

                RECT taskRect = new RECT();
                bool hasTaskRect = false;
                if (taskbarHwnd != IntPtr.Zero)
                {
                    if (GetWindowRect(taskbarHwnd, out taskRect))
                        hasTaskRect = true;
                }

                bool coversTaskbar = false;
                if (hasTaskRect)
                {
                    if (rect.Left <= taskRect.Left + tolerance &&
                        rect.Top <= taskRect.Top + tolerance &&
                        rect.Right >= taskRect.Right - tolerance &&
                        rect.Bottom >= taskRect.Bottom - tolerance)
                    {
                        coversTaskbar = true;
                    }
                }

                if (coversScreen && (coversTaskbar || taskbarAutoHide))
                {
                    Debug.WriteLine("前台窗口被判定为全屏（覆盖任务栏或任务栏自动隐藏）");
                    return true;
                }

                double areaRatio = (double)(width * height) / (sb.Width * sb.Height);
                // 仅在面积非常接近屏幕时才以面积判定为全屏
                if (areaRatio >= 0.995)
                {
                    Debug.WriteLine("前台窗口面积占比接近屏幕，判定为全屏");
                    return true;
                }

                Debug.WriteLine("前台窗口未判定为全屏");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("isFullScreen 检测异常: " + ex.Message);
                return false;
            }
        }
        /// <summary>
        /// 从用户设置读取并应用到窗体。改进要点：
        /// 1. 合并布尔赋值，去掉不必要的 if/else，使代码更简洁可读；
        /// 2. 对位置进行空/默认值保护，避免把无效位置（如默认 Point）覆盖到窗体上；
        /// 3. 增加异常保护，防止设置读取出错导致崩溃；
        /// 4. 保持幂等性：多次调用不会改变已正确设置的状态。
        /// Properties.Settings.Default 不是 C# 的 default 关键字。
        /// Default 是自动生成的 Settings 类的静态属性（单例），表示当前运行时的设置实例。它封装了应用的设计时默认值与用户上次保存的值。
        /// </summary>
        private void readUserSettings()
        {
            try
            {
                // 读取并应用窗口位置：仅在设置非 null 且不为默认 Point(0,0) 时才应用，
                // 避免意外把未初始化的设置覆盖到窗体位置。
                var loc = Properties.Settings.Default.WinowLocation;
                if (loc != null && loc != default(System.Drawing.Point))
                {
                    this.Location = loc;
                }

                // 直接赋值 Checked 属性，更简洁且语义明确
                this.ShowInFullScreen_ToolStripMenuItem.Checked = Properties.Settings.Default.ShowInFullScreen;
                this.MultNicMode_ToolStripMenuItem.Checked = Properties.Settings.Default.MultNicMode;
            }
            catch (Exception ex)
            {
                // 最小化处理，记录调试信息但不抛出，保证程序稳定性
                this.Location = new System.Drawing.Point(710, 10); // 默认位置
                this.ShowInFullScreen_ToolStripMenuItem.Checked = false; // 默认不在全屏显示
                this.MultNicMode_ToolStripMenuItem.Checked = false; // 默认单网卡模式
                System.Diagnostics.Debug.WriteLine("读取用户设置失败: " + ex.Message);
            }
        }
        private void writeUserSettings()
        {
            Properties.Settings.Default.WinowLocation = this.Location;
            Properties.Settings.Default.ShowInFullScreen = this.ShowInFullScreen_ToolStripMenuItem.Checked;
            Properties.Settings.Default.MultNicMode = this.MultNicMode_ToolStripMenuItem.Checked;
            Debug.WriteLine("保存用户设置: Location=" + this.Location + ", ShowInFullScreen=" + this.ShowInFullScreen_ToolStripMenuItem.Checked);
            Properties.Settings.Default.Save();
        }
        private void NetMonitor_FormClosing(object sender, FormClosingEventArgs e)
        {
            // 在窗体关闭时保存用户设置
            writeUserSettings();

            // 显式隐藏托盘图标，避免程序退出后图标滞留在通知区域（"幽灵图标"）
            // 即使 components.Dispose() 会自动调用 trayIcon.Dispose()，
            // 在某些 Windows 版本下不主动设置 Visible=false 仍可能出现残留图标。
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                Debug.WriteLine("[FormClosing] 托盘图标已隐藏");
            }
        }
        private long GetAllNicBytesSent(NetworkInterface[] nics)
        {
            long total = 0;
            foreach (var nic in nics)
            {
                try
                {
                    total += nic.GetIPv4Statistics().BytesSent;
                }
                catch { }
            }
            return total;
        }
        private long GetAllNicBytesReceived(NetworkInterface[] nics)
        {
            long total = 0;
            foreach (var nic in nics)
            {
                try
                {
                    total += nic.GetIPv4Statistics().BytesReceived;
                }
                catch { }
            }
            return total;
        }
        private long GetSigleNicBytesSent(NetworkInterface nic)
        {
            try
            {
                return nic.GetIPv4Statistics().BytesSent;
            }
            catch
            {
                return 0;
            }
        }
        private long GetSigleNicBytesReceived(NetworkInterface nic)
        {
            try
            {
                return nic.GetIPv4Statistics().BytesReceived;
            }
            catch
            {
                return 0;
            }
        }
    }
}