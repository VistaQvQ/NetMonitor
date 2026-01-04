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
        
        public NetMonitor()
        {
            InitializeComponent();
            InitNetworkInterface();
            InitializeTimer();
        }

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        public static extern bool SendMessage(IntPtr hwnd, int wMsg, int wParam, int lParam);
        public const int WM_SYSCOMMAND = 0x0112;
        public const int SC_MOVE = 0xF010;
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

        /*        private void SetGifBackground()
                {
                    Image gif = Resource.Cat;
                    System.Drawing.Imaging.FrameDimension fd = new System.Drawing.Imaging.FrameDimension(gif.FrameDimensionsList[0]);
                    int count = gif.GetFrameCount(fd);    //获取帧数(gif图片可能包含多帧，其它格式图片一般仅一帧)
                    System.Windows.Forms.Timer giftimer = new System.Windows.Forms.Timer();
                    giftimer.Interval = 120;//这里是可以调节速度的
                    int i = 0;
                    Image bgImg = null;
                    System.IO.Stream stream = new System.IO.MemoryStream();
                    giftimer.Tick += (s, e) =>
                    {
                            if (i >= count)
                            {
                                i = 0;
                            }
                            gif.SelectActiveFrame(fd, i);
                            gif.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                            if (bgImg != null)
                            {
                                bgImg.Dispose();
                            }
                            bgImg = Image.FromStream(stream);
                            this.pictureBox.BackgroundImage = bgImg;///
                            i++;
                        Thread.Sleep(0);
                    };
                    giftimer.Start();
                }*/
        private void SetGifBackground()
        {
            Image gif = Properties.Resources.Cat;
            System.Drawing.Imaging.FrameDimension fd = new System.Drawing.Imaging.FrameDimension(gif.FrameDimensionsList[0]);
            int count = gif.GetFrameCount(fd);
            System.Windows.Forms.Timer giftimer = new System.Windows.Forms.Timer();
            giftimer.Interval = 120;
            int i = 0;
            Image bgImg = null;
            System.IO.Stream stream = new System.IO.MemoryStream();
            giftimer.Tick += (s, e) =>
            {
                try
                {
                    if (i >= count)
                    {
                        i = 0;
                    }
                    gif.SelectActiveFrame(fd, i);
                    stream.SetLength(0); // Clear the stream
                    gif.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                    stream.Seek(0, System.IO.SeekOrigin.Begin); // Reset the stream position
                    if (bgImg != null)
                    {
                        bgImg.Dispose();
                    }
                    bgImg = Image.FromStream(stream);
                    if (this.pictureBox.InvokeRequired)
                    {
                        this.pictureBox.Invoke(new Action(() => this.pictureBox.BackgroundImage = bgImg));
                    }
                    else
                    {
                        this.pictureBox.BackgroundImage = bgImg;
                    }
                    i++;
                }
                catch (Exception ex)
                {
                    // Handle the exception, e.g., log it or stop the timer
                    giftimer.Stop();
                    Console.WriteLine("An error occurred: " + ex.Message);
                }
            };
            giftimer.Start();
        }

        public void UpdateNetworkInterface()
        {
            long netSend;
            long netRecv;
            if (ComboBox.Owner.InvokeRequired)
            {
                ComboBox.Owner.Invoke(new Action(UpdateNetworkInterface));
                return;
            }

            if (nicArr == null || nicArr.Length == 0)
            {
                // 没有网卡可用，清空显示并返回
                Lable_TotalUP.Text = "0";
                Lable_TotalDown.Text = "0";
                Lable_SpeedUP.Text = "上传：0B/S";
                Lable_SpeedDown.Text = "下载：0B/S";
                return;
            }

            if (ComboBox.SelectedIndex >= 0 && ComboBox.SelectedIndex < nicArr.Length)
            {
                NetworkInterface nic = nicArr[ComboBox.SelectedIndex];
                // 获取 IPv4 统计
                IPv4InterfaceStatistics interfaceStats = nic.GetIPv4Statistics();
                if (InterfaceSelect == ComboBox.SelectedIndex && Start)
                {
                    netSend = interfaceStats.BytesSent - long.Parse(Lable_TotalUP.Text);
                    netRecv = interfaceStats.BytesReceived - long.Parse(Lable_TotalDown.Text);
                    Lable_TotalUP.Text = interfaceStats.BytesSent.ToString();
                    Lable_TotalDown.Text = interfaceStats.BytesReceived.ToString();
                    System.Diagnostics.Debug.WriteLine(ComboBox.SelectedIndex.ToString());
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
                else
                {
                    netSend = 0;
                    netRecv = 0;
                    Lable_TotalUP.Text = interfaceStats.BytesSent.ToString();
                    Lable_TotalDown.Text = interfaceStats.BytesReceived.ToString();
                    InterfaceSelect = ComboBox.SelectedIndex;
                    Start = !Start;
                }

                Lable_SpeedUP.Text = "上传：" + FormatSpeed(netSend);
                Lable_SpeedDown.Text = "下载：" + FormatSpeed(netRecv);
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
            var accent = Color.FromArgb(0, 120, 215);
            if (this.panel != null) this.panel.BackColor = accent;
            if (this.Lable_SpeedUP != null) this.Lable_SpeedUP.BackColor = accent;
            if (this.Lable_SpeedDown != null) this.Lable_SpeedDown.BackColor = accent;

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