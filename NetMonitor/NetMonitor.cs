using System;
using System.Drawing;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Timers;
using System.Windows.Forms;
using System.Threading;
using System.Linq;
using System.Diagnostics;

namespace NetMonitor
{
    public partial class NetMonitor : Form
    {
        private int InterfaceSelect = 0;
        private int ZreoTimes = 0;
        private bool Start = false;
        private NetworkInterface[] nicArr;      //网卡集合
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

        public NetMonitor()
        {
            InitializeComponent();
            InitNetworkInterface();
            InitializeTimer();
#if !DEBUG
            InitAutoRunMenuItem();
#endif
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
        /// 在 Windows 消息中表示“系统命令”消息（消息编号 0x0112）。
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

            var now = DateTime.UtcNow;
            double deltaSeconds = 1.0; // 默认 1s，适用于首次采样或回退情况

            if (prevSampleTime != DateTime.MinValue)
            {
                deltaSeconds = (now - prevSampleTime).TotalSeconds;
                if (deltaSeconds <= 0) deltaSeconds = 1.0;
            }
            prevSampleTime = now;

            if (nicArr == null || nicArr.Length == 0)
            {
                prevBytesSent = 0;
                prevBytesRecv = 0;
                Lable_SpeedUP.Text = "  0B/S";
                Lable_SpeedDown.Text = "  0B/S";
                return;
            }

            if (ComboBox.SelectedIndex >= 0 && ComboBox.SelectedIndex < nicArr.Length)
            {
                var nic = nicArr[ComboBox.SelectedIndex];
                var stats = nic.GetIPv4Statistics();
                long bytesSent = stats.BytesSent;
                long bytesRecv = stats.BytesReceived;

                long deltaSent = 0;
                long deltaRecv = 0;

                if (Start && InterfaceSelect == ComboBox.SelectedIndex)
                {
                    deltaSent = bytesSent - prevBytesSent;
                    deltaRecv = bytesRecv - prevBytesRecv;
                }
                else
                {
                    // 切换网卡或首次启动：初始化，上次值设为当前值，避免突跳
                    Start = true;
                    InterfaceSelect = ComboBox.SelectedIndex;
                    deltaSent = 0;
                    deltaRecv = 0;
                }

                // 更新“前一次”值（用于下一次差值计算）
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
                if (ComboBox.Text == "无可用网络接口")//这里仍然有可能出现无网卡的情况，需要修补
                {
                    InitNetworkInterface();
                }
                else
                {
                    if (netRecvPerSec == 0 && netSendPerSec == 0)
                    {
                        ZreoTimes++;
                        if (ZreoTimes >= 3)
                        {
                            ComboBox.SelectedIndex = (ComboBox.SelectedIndex + 1) % nicArr.Length;
                        }
                    }
                    else
                    {
                        ZreoTimes = 0;
                    }

                }
            }
        }

        private string FormatSpeed(long bytes)
        {
            if (bytes < 1000)//处于1000~1024b之间显示时这里分母改成1000
            {
                return $"{bytes,3}B/S";
            }
            else if (bytes < 1024 * 1000)
            {
                return $"{(bytes / 1024.0),3:F0}K/S";
            }
            else
            {
                return $"{(bytes / (1024.0 * 1000)),3:F0}M/S";
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
                }
                else
                {
                    // 如果没有找到任何物理/非虚拟网卡，保证界面不会因 SelectedIndex 异常崩溃
                    ComboBox.Text = "无可用网络接口";
                }
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
        private void NetMonitor_Load(object sender, EventArgs e)
        {
            //var accent = Color.FromArgb(0, 120, 215);
            //if (this.panel != null) this.panel.BackColor = accent;
            //if (this.Lable_SpeedUP != null) this.Lable_SpeedUP.BackColor = accent;
            //if (this.Lable_SpeedDown != null) this.Lable_SpeedDown.BackColor = accent;
            //cat 版本的代码此处如果恢复注意修改颜色和panel的背景图片

            this.Invoke((EventHandler)delegate
            {
                SetGifBackground();
            });
        }

        private void NetMonitor_MouseDown(object sender, MouseEventArgs e)
        {
            ReleaseCapture();
            SendMessage(this.Handle, WM_SYSCOMMAND, SC_MOVE + HTCAPTION, 0);
        }

        private void Exit_Menu_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Menu.Hide();//隐藏一些东西
                if (MessageBox.Show("你确定关闭流量悬浮窗么？", "提示", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    base.Dispose();//这个是啥？我忘了
                    Application.Exit();
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
    }

}