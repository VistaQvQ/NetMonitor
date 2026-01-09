using System;
using System.Drawing;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Timers;
using System.Windows.Forms;
using System.Threading;
using System.Linq;

namespace NetMonitor
{
    public partial class NetMonitor : Form
    {
        int InterfaceSelect = 0;
        int ZreoTimes = 0;
        bool Start = false;
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
        
        public NetMonitor()
        {
            InitializeComponent();
            InitNetworkInterface();
            InitializeTimer();
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
            this.Invoke((EventHandler)delegate
            {
                UpdateNetworkInterface();
                Thread.Sleep(0);
            });
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

                if (this.pictureBox.InvokeRequired)
                {
                    this.pictureBox.Invoke(new Action(() => this.pictureBox.BackgroundImage = frame));
                }
                else
                {
                    this.pictureBox.BackgroundImage = frame;
                }
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
                this.Invoke(new Action(UpdateNetworkInterface));
                return;
            }

            if (nicArr == null || nicArr.Length == 0)
            {
                prevBytesSent = 0;
                prevBytesRecv = 0;
                Lable_SpeedUP.Text = "上传：0B/S";
                Lable_SpeedDown.Text = "下载：0B/S";
                return;
            }

            if (ComboBox.SelectedIndex >= 0 && ComboBox.SelectedIndex < nicArr.Length)
            {
                var nic = nicArr[ComboBox.SelectedIndex];
                var stats = nic.GetIPv4Statistics();
                long bytesSent = stats.BytesSent;
                long bytesRecv = stats.BytesReceived;

                long netSend = 0;
                long netRecv = 0;

                if (Start && InterfaceSelect == ComboBox.SelectedIndex)
                {
                    // 使用 long 字段保存上一次值，避免依赖 UI 控件文本
                    netSend = bytesSent - prevBytesSent;
                    netRecv = bytesRecv - prevBytesRecv;
                }
                else
                {
                    // 切换网卡或首次启动：不计算差值，只初始化前一次值
                    Start = true;
                    InterfaceSelect = ComboBox.SelectedIndex;
                    netSend = 0;
                    netRecv = 0;
                }

                // 更新“前一次”值（用于下一次差值计算）
                prevBytesSent = bytesSent;
                prevBytesRecv = bytesRecv;

                // 根据当前下载速度（bytes/s）调整 gifStep
                // 1MB = 1024 * 1024 bytes
                const long OneMB = 1024 * 1024;
                try
                {
                    if (netRecv < OneMB)
                    {
                        gifStep = 1;
                    }
                    else if (netRecv < 10 * OneMB)
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

                // 更新 UI
                Lable_SpeedUP.Text = "上传：" + FormatSpeed(netSend);
                Lable_SpeedDown.Text = "下载：" + FormatSpeed(netRecv);

                if (netRecv == 0 && netSend == 0)
                {
                    ZreoTimes++;
                }
                else
                {
                    ZreoTimes = 0;
                }
                if (ZreoTimes >= 3)
                {
                    ComboBox.SelectedIndex = (ComboBox.SelectedIndex + 1) % nicArr.Length;
                }
            }
        }

        private string FormatSpeed(long bytes)
        {
            if (bytes < 1024)
            {
                return $"{bytes}B/S";
            }
            else if (bytes < 1024 * 1024)
            {
                return $"{(bytes / 1024.0):F2}K/S";
            }
            else
            {
                return $"{(bytes / (1024.0 * 1024)):F2}M/S";
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
                ComboBox.Items.Clear();

                var all = NetworkInterface.GetAllNetworkInterfaces();

                // 先根据状态和类型粗筛
                var candidates = all.Where(nic =>
                    nic.OperationalStatus == OperationalStatus.Up &&
                    nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                    nic.NetworkInterfaceType != NetworkInterfaceType.Unknown
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
    }
}