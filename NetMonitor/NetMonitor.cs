using NetMonitor.Properties;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Timers;
using System.Windows.Forms;

namespace NetMonitor
{
    public partial class NetMonitor : Form
    {
        // ── 网卡与速度计算 ─────────────────────────────────────────────────────────
        private NetworkInterface[] nicArr;          // 当前可用网卡列表
        private int  interfaceSelect  = 0;          // 上次选中的网卡下标（防突跳用）
        private int  zeroSpeedCount   = 0;          // 连续零速次数（自动切卡阈值）
        private bool speedCalcReady   = false;      // 首次采样初始化标志
        private long prevBytesSent    = 0;
        private long prevBytesRecv    = 0;
        private DateTime prevSampleTime = DateTime.MinValue;

        // ── GIF 背景动画 ───────────────────────────────────────────────────────────
        private System.Windows.Forms.Timer gifTimer;
        private Image[] gifFrames;
        private int gifFrameIndex = 0;
        private int gifFrameCount = 0;
        private int gifStep       = 1;   // GIF 步进：1=慢 / 2=中 / 4=快

        // ── 系统定时器 ────────────────────────────────────────────────────────────
        private System.Timers.Timer updateTimer;

        // ── 窗体状态 ──────────────────────────────────────────────────────────────
        private bool formInitialized = false;   // 幂等保护：OnShown 只初始化一次

        /// <summary>系统托盘图标，与右键菜单 Menu 共用同一 ContextMenuStrip 实例。</summary>
        private NotifyIcon trayIcon;

        /// <summary>临时：false = 单网卡模式，true = 多网卡模式。下阶段改为持久化设置。</summary>
        private bool temp＿isMultiMode = false;

        /// <summary>无网自动切卡开关。下阶段绑定到菜单项，目前默认启用。</summary>
        private bool autoSwitchNicEnabled = true;

        public NetMonitor()
        {
            InitializeComponent();
        }

        // OnShown 说明：
        //   Load/构造阶段句柄未就绪，且 Application.Run 尚未启动，
        //   耗时操作（注册表/网卡枚举）放到 OnShown 避免阻塞首屏渲染。
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (formInitialized) return;

            InitNetworkInterface();
            InitializeTimer();
            ReadUserSettings();
#if !DEBUG
            InitAutoRunMenuItem();
#endif
            formInitialized = true;

            // 托盘图标：components 托管生命周期；Menu 复用同一实例保持菜单状态同步
            trayIcon = new NotifyIcon(this.components)
            {
                Icon             = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
                Text             = "NetMonitor - 网络速度监控",
                Visible          = true,
                ContextMenuStrip = this.Menu
            };
            trayIcon.DoubleClick += (s, ev) => { this.Visible = !this.Visible; };
        }

        private void InitializeTimer()
        {
            updateTimer = new System.Timers.Timer { Interval = 1000 };
            updateTimer.Elapsed += Timer_Elapsed;
            updateTimer.Start();
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            // 非阻塞地将更新请求排入 UI 消息队列
            if (this.IsHandleCreated && !this.IsDisposed)
            {
                try { this.BeginInvoke((Action)UpdateNetworkInterface); }
                catch (InvalidOperationException) { /* 句柄已销毁，忽略 */ }
            }
        }

        private void SetGifBackground()
        {
            Image gif = Properties.Resources.Cadogt;
            if (gif == null) return;

            // 清理旧计时器
            try
            {
                if (gifTimer != null)
                {
                    gifTimer.Stop();
                    gifTimer.Tick -= GifTimer_Tick;
                    gifTimer.Dispose();
                    gifTimer = null;
                }
            }
            catch { }

            try
            {
                var fd = new System.Drawing.Imaging.FrameDimension(gif.FrameDimensionsList[0]);
                gifFrameCount = gif.GetFrameCount(fd);
                if (gifFrameCount <= 0) return;

                // 释放旧帧
                if (gifFrames != null)
                    foreach (var img in gifFrames)
                        try { img?.Dispose(); } catch { }

                gifFrames = new Image[gifFrameCount];

                // 逐帧提取为独立 Bitmap（保留 Alpha）
                for (int i = 0; i < gifFrameCount; i++)
                {
                    gif.SelectActiveFrame(fd, i);
                    var bmp = new Bitmap(gif.Width, gif.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.CompositingMode    = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                        g.InterpolationMode  = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode      = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        g.DrawImage(gif, 0, 0, gif.Width, gif.Height);
                    }
                    gifFrames[i] = bmp;
                }

                // ~60 FPS，实际速度由 gifStep 控制
                gifTimer = new System.Windows.Forms.Timer { Interval = 16 };
                gifFrameIndex = 0;
                if (gifStep < 1) gifStep = 1;
                gifTimer.Tick += GifTimer_Tick;
                gifTimer.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine("SetGifBackground 错误: " + ex.Message);
            }
        }

        private void GifTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (gifFrames == null || gifFrameCount == 0) return;
                gifFrameIndex = (gifFrameIndex + gifStep) % gifFrameCount;
                this.pictureBox.BackgroundImage = gifFrames[gifFrameIndex];
            }
            catch (Exception ex)
            {
                try { gifTimer?.Stop(); } catch { }
                Console.WriteLine("GIF 播放错误: " + ex.Message);
            }
        }

        public void UpdateNetworkInterface()
        {
            // 确保在 UI 线程执行；BeginInvoke 避免阻塞计时器线程
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(UpdateNetworkInterface));
                return;
            }

            // ── 公用：全屏判定 + 窗体可见性 ──────────────────────────────────────
            UpdateVisibility();

            // ── 按模式分发 ────────────────────────────────────────────────────────
            if (temp＿isMultiMode)
                UpdateMultiMode();
            else
                UpdateSingleMode();
        }
        private void UpdateVisibility()
        {
            bool fullScreen = isFullScreen();
            bool shouldShow = !fullScreen || this.ShowInFullScreen_ToolStripMenuItem.Checked;
            Debug.WriteLine(
                $"[UpdateVisibility] isFullScreen={fullScreen}, " +
                $"ShowInFullScreen={this.ShowInFullScreen_ToolStripMenuItem.Checked}, " +
                $"shouldShow={shouldShow}, currentVisible={this.Visible}");
            this.Visible = shouldShow;
        }
        private void UpdateSingleMode()
        {
            // 网卡为空数据判定 + 网卡刷新（SingleMode）───────────────
            // nicArr 为空说明网络接口尚未加载或全部失效，重置速度并重新初始化
            if (nicArr == null || nicArr.Length == 0)
            {
                ResetSpeedData();
                InitNetworkInterface();
                return;
            }

            // 防越界判定（SingleMode）──────────────────────────────
            // ComboBox 下标越界时跳过本次更新，避免数组越界异常
            if (ComboBox.SelectedIndex < 0 || ComboBox.SelectedIndex >= nicArr.Length)
                return;

            // 取当前选中网卡的原始字节数
            var nic = nicArr[ComboBox.SelectedIndex];
            long bytesSent = GetSingleNicBytesSent(nic);
            long bytesRecv = GetSingleNicBytesReceived(nic);

            // 数据初始化/时间计算 + 防数据突跳 + 差值
            CalcSpeedAndGifStep(
                bytesSent, bytesRecv,
                out long netSendPerSec, out long netRecvPerSec);

            // UI 更新（公用逻辑）────────────────────────────────────────
            UpdateSpeedToUI(netSendPerSec, netRecvPerSec);

            // 无网时网卡自动切换（）──────────────────────
            AutoSwitchNicOnZeroSpeed_SingleMode(netSendPerSec, netRecvPerSec);
        }

        private void UpdateMultiMode()
        {
            // TODO：多网卡模式实现（下阶段）
            // 参考：GetAllNicBytesSent / GetAllNicBytesReceived
        }

        /// <summary>
        /// 时间间隔计算 → 防突跳 → 差值转速率 → gifStep 调整。
        /// 输出 <paramref name="netSendPerSec"/> / <paramref name="netRecvPerSec"/>（bytes/s）。
        /// </summary>
        private void CalcSpeedAndGifStep(
            long bytesSent, long bytesRecv,
            out long netSendPerSec, out long netRecvPerSec)
        {
            // 时间间隔（首次或回退时取 1s）
            var now = DateTime.UtcNow;
            double deltaSeconds = 1.0;
            if (prevSampleTime != DateTime.MinValue)
            {
                deltaSeconds = (now - prevSampleTime).TotalSeconds;
                if (deltaSeconds <= 0) deltaSeconds = 1.0;
            }
            prevSampleTime = now;

            // 防突跳：切换网卡或首次采样时将 delta 置零
            long deltaSent, deltaRecv;
            if (speedCalcReady && interfaceSelect == ComboBox.SelectedIndex)
            {
                deltaSent = bytesSent - prevBytesSent;
                deltaRecv = bytesRecv - prevBytesRecv;
            }
            else
            {
                speedCalcReady = true;
                interfaceSelect = ComboBox.SelectedIndex;
                deltaSent = 0;
                deltaRecv = 0;
            }
            prevBytesSent = bytesSent;
            prevBytesRecv = bytesRecv;

            // bytes/s
            netSendPerSec = (long)(deltaSent / deltaSeconds);
            netRecvPerSec = (long)(deltaRecv / deltaSeconds);

            // gifStep：按下载速度分三档
            const long OneMB = 1024 * 1024;
            try
            {
                if      (netRecvPerSec < OneMB)       gifStep = 1;   // < 1 MB/s
                else if (netRecvPerSec < 10 * OneMB)  gifStep = 2;   // 1–10 MB/s
                else                                   gifStep = 4;   // > 10 MB/s
                if (gifStep < 1) gifStep = 1;
            }
            catch { /* 保留当前 gifStep */ }
        }

        /// <summary>
        /// 将计算好的速率写入速度标签 UI。
        /// Single/MultiMode 均可调用。
        /// </summary>
        private void UpdateSpeedToUI(long netSendPerSec, long netRecvPerSec)
        {
            Lable_SpeedUP.Text   = FormatSpeed(netSendPerSec);
            Lable_SpeedDown.Text = FormatSpeed(netRecvPerSec);
        }

        /// <summary>
        /// 将速度数据归零（网卡无效或刷新时调用）。
        /// </summary>
        private void ResetSpeedData()
        {
            prevBytesSent = 0;
            prevBytesRecv = 0;
            Lable_SpeedUP.Text   = "  0B/S";
            Lable_SpeedDown.Text = "  0B/S";
        }

        // 无网时网卡自动切换
        /// <summary>
        /// 【SingleMode】连续零速时轮询切换网卡；受 <see cref="autoSwitchNicEnabled"/> 开关控制。
        /// </summary>
        private void AutoSwitchNicOnZeroSpeed_SingleMode(long netSendPerSec, long netRecvPerSec)
        {
            if (!autoSwitchNicEnabled)
            {
                zeroSpeedCount = 0;
                return;
            }

            if (netRecvPerSec == 0 && netSendPerSec == 0)
            {
                zeroSpeedCount++;
                if (zeroSpeedCount >= 3)
                {
                    if (zeroSpeedCount < nicArr.Length * 3)
                    {
                        // 未轮询完一圈，切下一块
                        ComboBox.SelectedIndex = (ComboBox.SelectedIndex + 1) % nicArr.Length;
                        zeroSpeedCount++;
                    }
                    else if (IsNetworkInterfaceListChanged())
                    {
                        // 网卡列表变化（插拔等），重新初始化
                        zeroSpeedCount = 0;
                        InitNetworkInterface();
                    }
                    else
                    {
                        // 轮询一圈仍为零，钳位防溢出
                        ComboBox.SelectedIndex = (ComboBox.SelectedIndex + 1) % nicArr.Length;
                        zeroSpeedCount = nicArr.Length * 3;
                    }
                }
            }
            else
            {
                zeroSpeedCount = 0;
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
            speedCalcReady = false;
            try
            {
                ComboBox.Text = "";
                ComboBox.Items.Clear();

                var all = NetworkInterface.GetAllNetworkInterfaces();
                nicArr = all.Where(nic =>
                    nic.OperationalStatus == OperationalStatus.Up &&
                    !IsVirtualNetworkInterface(nic)).ToArray();

                foreach (var nic in nicArr)
                    ComboBox.Items.Add(nic.Name);

                if (nicArr.Length > 0)
                    ComboBox.SelectedIndex = 0;
                else
                    ComboBox.Text = "无可用网络接口";

                Debug.WriteLine("可用网卡数量: " + nicArr.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine("InitNetworkInterface 错误: " + ex.Message);
                nicArr = new NetworkInterface[0];
                ComboBox.Items.Clear();
                ComboBox.Text = "获取网卡失败";
            }
        }
        // Load 在消息循环启动前触发，不能 Invoke，直接调用 SetGifBackground 即可。
        // 定时器与开机自启初始化移至 OnShown。
        private void NetMonitor_Load(object sender, EventArgs e)
        {
            SetGifBackground();
        }

        private void NetMonitor_MouseDown(object sender, MouseEventArgs e)
        {
            // 无边框窗体拖动：释放鼠标捕获后发送系统移动命令
            NativeApi.ReleaseCapture();
            NativeApi.SendMessage(this.Handle,
                NativeApi.WM_SYSCOMMAND,
                NativeApi.SC_MOVE + NativeApi.HTCAPTION, 0);
            WriteUserSettings();
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
            if (!CheckProgramNameNotChanged()) return;
            this.SetAutoRun(this.AutoRun_ToolStripMenuItem.Checked);
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
        private bool CompareNetworkLists(NetworkInterface[] list1, NetworkInterface[] list2)
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
                if (!CompareNetworkLists(filteredCurrentNics, nicArr))
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

        // 全屏判定：刚启动进程（< 3s）或主窗口未就绪时统一忽略，防止 splash / launcher 误触发
        private const double IgnoreNewProcessSeconds = 3.0;

        /// <summary>
        /// 判断当前前台窗口是否为全屏应用。
        /// 依次排除：本进程 → DWM cloaked → 不可见 → 有 owner → ToolWindow/Layered → 刚启动进程。
        /// 通过后比较窗口矩形与屏幕边界（含任务栏覆盖/自动隐藏逻辑）。
        /// </summary>
        private bool isFullScreen()
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
                            var p = Process.GetProcessById((int)pid);
                            if ((DateTime.UtcNow - p.StartTime.ToUniversalTime()).TotalSeconds < IgnoreNewProcessSeconds)
                                return false;
                            if (p.MainWindowHandle == IntPtr.Zero && string.IsNullOrEmpty(p.MainWindowTitle))
                                return false;
                        }
                        catch { }
                    }
                }
                catch { }

                // 取窗口所在屏幕
                var winRect = new Rectangle(rect.Left, rect.Top, Math.Max(1, width), Math.Max(1, height));
                Screen screen;
                try   { screen = Screen.FromRectangle(winRect); }
                catch { screen = Screen.PrimaryScreen; }
                var sb = screen.Bounds;

                bool coversScreen =
                    Math.Abs(rect.Left   - sb.Left)   <= tolerance &&
                    Math.Abs(rect.Top    - sb.Top)     <= tolerance &&
                    Math.Abs(rect.Right  - sb.Right)   <= tolerance &&
                    Math.Abs(rect.Bottom - sb.Bottom)  <= tolerance;

                // 任务栏状态
                IntPtr taskbarHwnd = NativeApi.FindWindow("Shell_TrayWnd", null);
                var abd = new NativeApi.APPBARDATA { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeApi.APPBARDATA)) };
                int abmState = 0;
                try { abmState = NativeApi.SHAppBarMessage(NativeApi.ABM_GETSTATE, ref abd).ToInt32(); } catch { }
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
                    Debug.WriteLine("前台窗口被判定为全屏（覆盖任务栏或任务栏自动隐藏）");
                    return true;
                }

                // 面积覆盖率 ≥ 99.5% 时也认为是全屏
                if ((double)(width * height) / (sb.Width * sb.Height) >= 0.995)
                {
                    Debug.WriteLine("前台窗口面积占比接近屏幕，判定为全屏");
                    return true;
                }

                Debug.WriteLine("前台窗口未判定为全屏");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("isFullScreen 异常: " + ex.Message);
                return false;
            }
        }
        /// <summary>从持久化设置读取并应用到窗体；读取失败时回退到默认值。</summary>
        private void ReadUserSettings()
        {
            try
            {
                var loc = Properties.Settings.Default.WinowLocation;
                if (loc != null && loc != default(System.Drawing.Point))
                    this.Location = loc;

                this.ShowInFullScreen_ToolStripMenuItem.Checked = Properties.Settings.Default.ShowInFullScreen;
                this.MultNicMode_ToolStripMenuItem.Checked      = Properties.Settings.Default.MultNicMode;
            }
            catch (Exception ex)
            {
                this.Location = new System.Drawing.Point(710, 10);
                this.ShowInFullScreen_ToolStripMenuItem.Checked = false;
                this.MultNicMode_ToolStripMenuItem.Checked      = false;
                Debug.WriteLine("读取用户设置失败: " + ex.Message);
            }
        }

        private void WriteUserSettings()
        {
            Properties.Settings.Default.WinowLocation   = this.Location;
            Properties.Settings.Default.ShowInFullScreen = this.ShowInFullScreen_ToolStripMenuItem.Checked;
            Properties.Settings.Default.MultNicMode      = this.MultNicMode_ToolStripMenuItem.Checked;
            Properties.Settings.Default.Save();
        }

        private void NetMonitor_FormClosing(object sender, FormClosingEventArgs e)
        {
            WriteUserSettings();

            // 退出前显式隐藏托盘图标，防止幽灵图标残留
            if (trayIcon != null)
                trayIcon.Visible = false;

            // Program.cs 使用空 ApplicationContext，关闭窗体不会自动停止消息循环，
            // 需手动调用 Application.Exit() 终止进程。
            Application.Exit();
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
        private long GetSingleNicBytesSent(NetworkInterface nic)
        {
            try   { return nic.GetIPv4Statistics().BytesSent; }
            catch { return 0; }
        }

        private long GetSingleNicBytesReceived(NetworkInterface nic)
        {
            try   { return nic.GetIPv4Statistics().BytesReceived; }
            catch { return 0; }
        }
    }
}