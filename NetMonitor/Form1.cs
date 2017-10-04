﻿using System;
using System.Timers;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Net.NetworkInformation;
using System.Drawing;
using System.Diagnostics;

namespace NetMonitor
{
    public partial class Form1 : Form
    {
        int i = 0;
        bool Start = false;
        private NetworkInterface[] nicArr;      //网卡集合
        private System.Timers.Timer timers; 
        //计时器
        int netSend;
        int netRecv;
        
        public Form1()
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


        private void Form1_Load(object sender, EventArgs e)
        {
            //LoadNetlist();//++
            //InitNetworkInterface();
            FormBorderStyle = FormBorderStyle.None;//alt+tab 不显示此窗体
            this.panel1.BackColor = this.label1.BackColor = this.label2.BackColor = Color.FromArgb(0, 120, 215);
            //timer1.Start();
            SetGifBackground();
        }
       
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
            });
        }
        private void SetGifBackground()
        {
            Image gif = NetMonitor.Resource.doge;
            System.Drawing.Imaging.FrameDimension fd = new System.Drawing.Imaging.FrameDimension(gif.FrameDimensionsList[0]);
            int count = gif.GetFrameCount(fd);    //获取帧数(gif图片可能包含多帧，其它格式图片一般仅一帧)
            System.Windows.Forms.Timer giftimer = new System.Windows.Forms.Timer();
            giftimer.Interval = 120;//这里是可以调节速度的
            int i = 0;
            Image bgImg = null;
            giftimer.Tick += (s, e) =>
            {
                if (i >= count) { i = 0; }
                gif.SelectActiveFrame(fd, i);
                System.IO.Stream stream = new System.IO.MemoryStream();
                gif.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                if (bgImg != null) { bgImg.Dispose(); }
                bgImg = Image.FromStream(stream);
                this.pictureBox1.BackgroundImage = bgImg;///
                i++;
            };
            giftimer.Start();
        }
        public void UpdateNetworkInterface()
        {
            
            

            NetworkInterface nic = nicArr[toolStripComboBox1.SelectedIndex];
            
            IPv4InterfaceStatistics interfaceStats = nic.GetIPv4Statistics();
            if (i == toolStripComboBox1.SelectedIndex&&Start)
            {
                netSend = (int)(interfaceStats.BytesSent - double.Parse(label3.Text));//这个时是干扰？
                netRecv = (int)(interfaceStats.BytesReceived - double.Parse(label4.Text));
                label3.Text = interfaceStats.BytesSent.ToString();
                label4.Text = interfaceStats.BytesReceived.ToString();
                

            }
            else
            {
                netSend = 0;
                netRecv = 0;
                label3.Text = interfaceStats.BytesSent.ToString();
                label4.Text = interfaceStats.BytesReceived.ToString();
                i = toolStripComboBox1.SelectedIndex;
                Start = !Start;
                
            }

            string netRecvText = "";
            string netSendText = "";
            if (netRecv < 1024)
            {
                netRecvText = ((double)netRecv).ToString("0") + "B/S";
            }
            else if (netRecv<1024*1024)
            {
                netRecvText = ((double)netRecv / 1024).ToString("0.00") + "KB/S";
            }
            else if (netRecv >= 1024 * 1024)
            {
                netRecvText = ((double)netRecv / (1024 * 1024)).ToString("0.00") + "MB/S";
            }


            if (netSend < 1024)
            {
                netSendText = ((double)netSend ).ToString("0") + "B/S";
            }
            else if (netSend < 1024*1024)
            {
                netSendText = ((double)netSend/1024).ToString("0.00") + "KB/S";
            }
            else if (netSend >= 1024 * 1024)
            {
                netSendText = ((double)netSend / (1024 * 1024)).ToString("0.00") + "MB/S";
            }


            label1.Text = "上传：" + netSendText;
            label2.Text = "下载：" + netRecvText;


        }

        private void Form1_MouseDown(object sender, MouseEventArgs e)
        {
            ReleaseCapture();
            SendMessage(this.Handle, WM_SYSCOMMAND, SC_MOVE + HTCAPTION, 0);
        }


        private void 退出ToolStripMenuItem_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                contextMenuStrip1.Hide();//这个。。。好尴尬啊
                if (MessageBox.Show("你确定关闭流量悬浮窗么？", "提示", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    base.Dispose();//这个是啥？我忘了
                    Application.Exit();

                }

            }
        }
        public void InitNetworkInterface()
        {
            nicArr = NetworkInterface.GetAllNetworkInterfaces();
            for (int i = 0; i < nicArr.Length; i++)
                toolStripComboBox1.Items.Add(nicArr[i].Name);
            toolStripComboBox1.SelectedIndex = 0;
            
        }

       
        private void toolStripComboBox1_DropDownClosed(object sender, EventArgs e)
        {
            contextMenuStrip1.Hide();
        }
    }
}