using NetMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Net.NetworkInformation;
using System.Timers;
using System.Windows.Forms;

namespace NetMonitor
{
    public partial class NetMonitor : Form
    {
        //网卡与速度计算 
        private NetworkInterface[] nicArr;          // 当前可用网卡列表
        private string speedSampleKey = null;       // 当前采样键（单网卡=选中网卡Id，多网卡=网卡集合Id），防突跳用
        private int  zeroSpeedCount   = 0;          // 连续零速次数（自动切卡阈值）
        private bool speedCalcReady   = false;      // 首次采样初始化标志
        private long prevBytesSent    = 0;
        private long prevBytesRecv    = 0;
        private DateTime prevSampleTime = DateTime.MinValue;

        //GIF 背景动画 
        private System.Timers.Timer gifTimer;
        private Image[] gifFrames;
        private int gifFrameIndex = 0;
        private int gifFrameCount = 0;
        private int gifStep       = 1;   // GIF 速度档位：1=60fps / 2=80fps / 3=100fps

        // 帧调度精确计时：System.Timers.Timer 心跳 + Stopwatch 累积，保证精确帧率、不跳帧
        private System.Diagnostics.Stopwatch gifSw;
        private double gifAccumMs = 0;
        private double gifFrameIntervalMs = 1000.0 / 60;   // 目标帧间隔，随档位变化

        // 平滑后的「上传+下载」总速率，用于档位判定（EMA 削弱每秒瞬时波动）
        private double smoothedTotalRate = 0;

        // 系统定时器 
        private System.Timers.Timer updateTimer;

        // 窗体状态
        private bool formInitialized = false;   // 幂等保护：OnShown 只初始化一次

        /// <summary>per-pixel alpha 渲染画布（复用，避免每帧重新分配内存）。</summary>
        private Bitmap renderBitmap;

        /// <summary>系统托盘图标，与右键菜单 Menu 共用同一 ContextMenuStrip 实例。</summary>
        private NotifyIcon trayIcon;

        /// <summary>多网卡模式开关：true = 累加所有网卡，false = 单网卡模式。状态持久化到用户设置。</summary>
        private bool isMultiMode = false;

        /// <summary>多网卡模式下参与累加的网卡 Id 集合；null 表示未自定义（默认全选）。</summary>
        private HashSet<string> multiNicSelectedIds = null;

        /// <summary>无网自动切卡开关。下阶段绑定到菜单项，目前默认启用。</summary>
        private bool autoSwitchNicEnabled = true;

        public NetMonitor()
        {
            InitializeComponent();
        }

        /// <summary>分层窗口：启用 WS_EX_LAYERED 以支持 UpdateLayeredWindow 逐像素 alpha 透明。</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeApi.WS_EX_LAYERED;
                return cp;
            }
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

            // 屏蔽子控件渲染：分层窗口下不渲染子控件，它们仅作数据源（Label.Text/Font/Location）
            panel.Visible = false;

            // 订阅网卡变动事件：插拔/启停网卡时自动刷新网卡列表
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

            // 托盘图标：components 托管生命周期；Menu 复用同一实例保持菜单状态同步
            trayIcon = new NotifyIcon(this.components)
            {
                Icon             = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
                Text             = "NetMonitor - 网络速度监控",
                Visible          = true,
                ContextMenuStrip = this.Menu
            };
            trayIcon.DoubleClick += (s, ev) => { this.Visible = !this.Visible; };

            // 首次渲染：呈现初始画面（GIF 首帧 + 胶囊 + 文字）
            RenderFrame();
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

        #region Per-Pixel Alpha 渲染（UpdateLayeredWindow，替代色键透明）

        /// <summary>构造胶囊形（两端半圆）路径。</summary>
        private static GraphicsPath BuildCapsulePath(RectangleF r)
        {
            var path = new GraphicsPath();
            float d = r.Height;                      // 圆角直径 = 高度 → 完全胶囊形
            path.AddArc(r.X, r.Y, d, d, 90, 180);    // 左半圆
            path.AddArc(r.Right - d, r.Y, d, d, 270, 180); // 右半圆
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// 每帧合成：GIF 当前帧 + 玻璃胶囊 + 文字 → 32bpp ARGB 位图 → UpdateLayeredWindow 呈现。
        /// 由 gifTimer(16ms) 驱动；速度文本写入 Label 后下一帧自然反映。
        /// 透明区域 alpha=0，系统对这类像素的命中测试自动穿透（不拦截鼠标）。
        /// </summary>
        private void RenderFrame()
        {
            if (!this.IsHandleCreated || this.IsDisposed) return;

            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            // 复用画布，尺寸变化时才重建
            if (renderBitmap == null || renderBitmap.Width != w || renderBitmap.Height != h)
            {
                if (renderBitmap != null) renderBitmap.Dispose();
                renderBitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            }

            using (var g = Graphics.FromImage(renderBitmap))
            {
                g.Clear(Color.Transparent);   // 全透明起步，逐层叠加
                DrawGifFrame(g);
                DrawGlassCapsule(g);
                DrawText(g);
            }

            PresentLayered(renderBitmap);
        }

        /// <summary>绘制 GIF 当前帧（拉伸到 pictureBox 区域，透明部分保持 alpha=0）。</summary>
        private void DrawGifFrame(Graphics g)
        {
            if (gifFrames == null || gifFrameCount == 0) return;
            var frame = gifFrames[gifFrameIndex];
            if (frame == null) return;

            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(frame, pictureBox.Location.X, pictureBox.Location.Y, pictureBox.Width, pictureBox.Height);
        }

        /// <summary>
        /// 绘制玻璃胶囊：纯白内部 + 纯黑描边，仅右侧文字区。
        /// 分层窗口下可安全抗锯齿——边缘 alpha 渐变，无白边、丝滑。
        /// </summary>
        private void DrawGlassCapsule(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            const float gap      = 2f;    // 胶囊与窗体右缘的缝隙
            const float insetY   = 4f;    // 上下留白
            const float capWidth = 130f;  // 胶囊绘图区域长度（宽度）
            float left = this.ClientSize.Width - capWidth - gap;
            var r = new RectangleF(left, insetY, capWidth, this.ClientSize.Height - insetY * 2);

            using (var path = BuildCapsulePath(r))
            {
                using (var brush = new SolidBrush(Color.White))
                    g.FillPath(brush, path);

                using (var pen = new Pen(Color.Black, 1.75f))
                    g.DrawPath(pen, path);
            }
        }

        /// <summary>绘制文字：复用 Label 的字体/颜色/位置，保证与原有布局一致。</summary>
        private void DrawText(Graphics g)
        {
            // ClearType 子像素抗锯齿：文字最平滑，消除灰度 AA 的“颗粒感”。
            // 文字全部落在不透明的白色胶囊之上，ClearType 以白色为底正确渲染，
            // 不会像画在透明背景上那样产生彩色杂边（文字区域不越过胶囊边界）。
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            DrawLabel(g, labelup);
            DrawLabel(g, Lable_SpeedUP);
            DrawLabel(g, labeldwon);
            DrawLabel(g, Lable_SpeedDown);
        }

        private void DrawLabel(Graphics g, Label lbl)
        {
            using (var brush = new SolidBrush(lbl.ForeColor))
                g.DrawString(lbl.Text, lbl.Font, brush, lbl.Location.X, lbl.Location.Y);
        }

        /// <summary>把 ARGB 位图呈现为分层窗口内容（premultiplied alpha，逐像素混合）。</summary>
        private void PresentLayered(Bitmap bmp)
        {
            IntPtr hdcScreen = NativeApi.GetDC(IntPtr.Zero);
            IntPtr hdcMem    = NativeApi.CreateCompatibleDC(hdcScreen);
            IntPtr hBitmap   = bmp.GetHbitmap(Color.FromArgb(0));   // 转 premultiplied alpha
            IntPtr hOld      = NativeApi.SelectObject(hdcMem, hBitmap);

            try
            {
                var size  = new NativeApi.SIZE  { cx = bmp.Width, cy = bmp.Height };
                var ptSrc = new NativeApi.POINT { X = 0, Y = 0 };
                var ptDst = new NativeApi.POINT { X = this.Left, Y = this.Top };
                var blend = new NativeApi.BLENDFUNCTION
                {
                    BlendOp             = NativeApi.AC_SRC_OVER,
                    BlendFlags          = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat         = NativeApi.AC_SRC_ALPHA
                };

                NativeApi.UpdateLayeredWindow(
                    this.Handle, hdcScreen, ref ptDst, ref size,
                    hdcMem, ref ptSrc, 0, ref blend, NativeApi.ULW_ALPHA);
            }
            finally
            {
                NativeApi.SelectObject(hdcMem, hOld);
                NativeApi.DeleteObject(hBitmap);
                NativeApi.DeleteDC(hdcMem);
                NativeApi.ReleaseDC(IntPtr.Zero, hdcScreen);
            }
        }

        #endregion

        private void SetGifBackground()
        {
            Image gif = Properties.Resources.Enjoywork;
            if (gif == null) return;

            // 清理旧计时器
            try
            {
                if (gifTimer != null)
                {
                    gifTimer.Stop();
                    gifTimer.Elapsed -= GifTimer_Elapsed;
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

                // 提升系统定时器分辨率到 1ms（80/100fps 精确渲染的前提）
                NativeApi.timeBeginPeriod(1);

                // 帧率由 gifStep 档位决定（60/80/100fps），不再跳帧
                gifFrameIndex = 0;
                if (gifStep < 1) gifStep = 1;
                gifFrameIntervalMs = GetGifFrameIntervalMs();
                gifSw = System.Diagnostics.Stopwatch.StartNew();

                // 5ms 心跳 + Stopwatch 累积：心跳只负责“到点检查”，实际帧推进由累积时间决定
                gifTimer = new System.Timers.Timer { Interval = 5, AutoReset = true };
                gifTimer.Elapsed += GifTimer_Elapsed;
                gifTimer.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine("SetGifBackground 错误: " + ex.Message);
            }
        }

        /// <summary>档位 → 目标帧间隔（毫秒）。gifStep：1=30fps / 2=100fps / 3=240fps。</summary>
        private double GetGifFrameIntervalMs()
        {
            switch (gifStep)
            {
                case 3:  return 1000.0 / 120;   // 120fps（高速）
                case 2:  return 1000.0 / 50;   // 50fps（中速）
                default: return 1000.0 / 15;    // 15fps（低速）
            }
        }

        private void GifTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                if (gifFrames == null || gifFrameCount == 0) return;
                if (gifSw == null) { gifSw = System.Diagnostics.Stopwatch.StartNew(); return; }

                // Stopwatch 累积实际经过时间，达到目标帧间隔才推进一帧（精确帧率，不跳帧）
                gifAccumMs += gifSw.Elapsed.TotalMilliseconds;
                gifSw.Restart();

                if (gifAccumMs < gifFrameIntervalMs) return;
                gifAccumMs = 0;

                // 跨线程回 UI 线程渲染
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    try
                    {
                        this.BeginInvoke((Action)(() =>
                        {
                            if (gifFrames == null || gifFrameCount == 0) return;
                            gifFrameIndex = (gifFrameIndex + 1) % gifFrameCount;
                            RenderFrame();
                        }));
                    }
                    catch (InvalidOperationException) { /* 句柄已销毁，忽略 */ }
                }
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

            // 全屏判定 + 窗体可见性 
            UpdateVisibility();

            //按模式分发 
            if (isMultiMode)
                UpdateMultiMode();
            else
                UpdateSingleMode();
        }
        private void UpdateVisibility()
        {
            bool fullScreen = isFullScreen();
            bool shouldShow = !fullScreen || this.ShowInFullScreen_ToolStripMenuItem.Checked;
            //Debug.WriteLine(
            //    $"[UpdateVisibility] isFullScreen={fullScreen}, " +
            //    $"ShowInFullScreen={this.ShowInFullScreen_ToolStripMenuItem.Checked}, " +
            //    $"shouldShow={shouldShow}, currentVisible={this.Visible}");
            this.Visible = shouldShow;
        }
        private void UpdateSingleMode()
        {
            // 网卡为空数据判定 + 网卡刷新（SingleMode
            // nicArr 为空说明网络接口尚未加载或全部失效，重置速度并重新初始化
            if (nicArr == null || nicArr.Length == 0)
            {
                ResetSpeedData();
                InitNetworkInterface();
                return;
            }

            // 防越界判定（SingleMode）
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

            // UI 更新（公用逻辑）
            UpdateSpeedToUI(netSendPerSec, netRecvPerSec);

            // 无网时网卡自动切换（）
            AutoSwitchNicOnZeroSpeed_SingleMode(netSendPerSec, netRecvPerSec);
        }

        private void UpdateMultiMode()
        {
            // 多网卡模式：累加用户筛选出的网卡流量（参照 neobox speedbox 的汇总逻辑）
            if (nicArr == null || nicArr.Length == 0)
            {
                ResetSpeedData();
                InitNetworkInterface();
                return;
            }

            var selected = GetSelectedNics();
            long bytesSent = GetAllNicBytesSent(selected);
            long bytesRecv = GetAllNicBytesReceived(selected);

            CalcSpeedAndGifStep(bytesSent, bytesRecv, out long netSendPerSec, out long netRecvPerSec);
            UpdateSpeedToUI(netSendPerSec, netRecvPerSec);
        }

        /// <summary>多网卡模式下参与累加的网卡集合；未自定义（null）时返回全部网卡。</summary>
        private NetworkInterface[] GetSelectedNics()
        {
            if (nicArr == null || nicArr.Length == 0)
                return new NetworkInterface[0];

            if (multiNicSelectedIds == null)
                return nicArr;  // 未自定义 → 全选

            return nicArr.Where(n => multiNicSelectedIds.Contains(n.Id)).ToArray();
        }

        /// <summary>
        /// 时间间隔计算 → 防突跳 → 差值转速率 → gifStep 调整。
        /// 输出 <paramref name="netSendPerSec"/> / <paramref name="netRecvPerSec"/>（bytes/s）。
        /// </summary>
        private void CalcSpeedAndGifStep(long bytesSent, long bytesRecv,out long netSendPerSec, out long netRecvPerSec)
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

            // 防突跳：采样源（单网卡/多网卡集合）变化或首次采样时，将 delta 置零
            long deltaSent, deltaRecv;
            string sampleKey = BuildSpeedSampleKey();
            if (speedCalcReady && sampleKey == speedSampleKey)
            {
                deltaSent = bytesSent - prevBytesSent;
                deltaRecv = bytesRecv - prevBytesRecv;
            }
            else
            {
                speedCalcReady = true;
                speedSampleKey = sampleKey;
                deltaSent = 0;
                deltaRecv = 0;
            }
            prevBytesSent = bytesSent;
            prevBytesRecv = bytesRecv;

            // bytes/s
            netSendPerSec = (long)(deltaSent / deltaSeconds);
            netRecvPerSec = (long)(deltaRecv / deltaSeconds);

            // gifStep：按「上传+下载」总速率分三档（决定动画帧率 60/80/100fps）
            // 用 EMA 平滑 + 滞回判定，避免瞬时速率在阈值附近波动导致档位频繁跳变
            const long OneMB = 1024 * 1024;
            try
            {
                long totalRate = netSendPerSec + netRecvPerSec;   // 上传 + 下载总速率

                // EMA 平滑（α=0.4）：削弱每秒瞬时波动，让档位判定更稳定
                if (smoothedTotalRate <= 0)
                    smoothedTotalRate = totalRate;
                else
                    smoothedTotalRate = smoothedTotalRate * 0.6 + totalRate * 0.4;

                double rate = smoothedTotalRate;

                // 滞回判定：升档/降档用不同阈值，中间留滞回带，避免临界点抖动
                int newStep = gifStep;   // 默认保持当前档位
                switch (gifStep)
                {
                    case 1:   // 档1 → 档2：需 > 1.2 MB/s
                        if (rate >= OneMB * 1.2) newStep = 2;
                        break;
                    case 2:   // 档2 → 档3：需 > 12 MB/s；档2 → 档1：需 < 0.8 MB/s
                        if      (rate >= OneMB * 12)  newStep = 3;
                        else if (rate <  OneMB * 0.8) newStep = 1;
                        break;
                    case 3:   // 档3 → 档2：需 < 8 MB/s
                        if (rate < OneMB * 8) newStep = 2;
                        break;
                }

                // 档位变化时才更新目标帧间隔（避免每秒反复重设）
                if (newStep != gifStep)
                {
                    gifStep = newStep;
                    gifFrameIntervalMs = GetGifFrameIntervalMs();
                }
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
                    else if (isNetworkInterfaceListChanged())
                    {
                        // 网卡列表变化（插拔等），重新初始化
                        zeroSpeedCount = 0;
                        InitNetworkInterface();
                        ComboBox.SelectedIndex = 0;// 重置到第一块，等待下一轮速度更新后再判断是否继续切卡
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

        private bool isVirtualNetworkInterface(NetworkInterface nic)
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
                    nic.OperationalStatus == OperationalStatus.Up && //这里的OperationalStatus.Up 可能在多网卡模式下导致某些网卡无法显示，后续可以考虑放宽条件或提供更多选项
                    !isVirtualNetworkInterface(nic)).ToArray();

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

            // 网卡列表变化后，同步重建多网卡筛选子菜单
            BuildMultiNicFilterMenu();
        }
        // Load 在消息循环启动前触发，不能 Invoke，直接调用 SetGifBackground 即可。
        // 定时器与开机自启初始化移至 OnShown。
        private void NetMonitor_Load(object sender, EventArgs e)
        {
            SetGifBackground();
        }

        private void NetMonitor_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                // 右键弹出菜单（原由子控件 ContextMenuStrip 自动触发，现改手动）
                Menu.Show(this, e.Location);
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                // 无边框窗体拖动：释放鼠标捕获后发送系统移动命令
                NativeApi.ReleaseCapture();
                NativeApi.SendMessage(this.Handle,
                    NativeApi.WM_SYSCOMMAND,
                    NativeApi.SC_MOVE + NativeApi.HTCAPTION, 0);
                WriteUserSettings();
            }
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
        private bool isNetworkInterfaceListChanged()
        {
            try
            {
                var currentNics = NetworkInterface.GetAllNetworkInterfaces();
                var filteredCurrentNics = currentNics.Where(nic =>
                    nic.OperationalStatus == OperationalStatus.Up &&
                    !isVirtualNetworkInterface(nic)).ToArray();
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

        /// <summary>网卡变动事件回调：切回 UI 线程后刷新网卡列表。</summary>
        private void OnNetworkAddressChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated && !this.IsDisposed)
            {
                try { this.BeginInvoke((Action)RefreshNetworkInterfaceList); }
                catch (InvalidOperationException) { /* 句柄已销毁，忽略 */ }
            }
        }

        /// <summary>网卡列表变化时重新初始化，并尽量恢复原选中网卡（按 Id 匹配）。</summary>
        private void RefreshNetworkInterfaceList()
        {
            // 列表未实际变化（如仅 IP 地址变化）则不处理，避免频繁重置采样导致速率归零
            if (!isNetworkInterfaceListChanged())
                return;

            // 保存当前选中网卡 Id，供重新初始化后恢复
            string selectedId = null;
            if (nicArr != null && ComboBox.SelectedIndex >= 0 && ComboBox.SelectedIndex < nicArr.Length)
                selectedId = nicArr[ComboBox.SelectedIndex].Id;

            InitNetworkInterface();  // 内部会 speedCalcReady = false，重置采样避免速率突跳

            // 恢复原选中网卡；若已不存在则保持默认（第 0 项）
            if (selectedId != null && nicArr != null)
            {
                for (int i = 0; i < nicArr.Length; i++)
                {
                    if (nicArr[i].Id == selectedId)
                    {
                        ComboBox.SelectedIndex = i;
                        break;
                    }
                }
            }
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
                    //Debug.WriteLine("前台窗口被判定为全屏（覆盖任务栏或任务栏自动隐藏）");
                    return true;
                }

                // 面积覆盖率 ≥ 99.5% 时也认为是全屏
                if ((double)(width * height) / (sb.Width * sb.Height) >= 0.995)
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

                // 读取多网卡筛选网卡集合；空串 = 未自定义（全选）
                string savedIds = Properties.Settings.Default.MultiNicSelectedIds;
                if (!string.IsNullOrEmpty(savedIds))
                    multiNicSelectedIds = new HashSet<string>(
                        savedIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else
                    multiNicSelectedIds = null;
            }
            catch (Exception ex)
            {
                this.Location = new System.Drawing.Point(710, 10);
                this.ShowInFullScreen_ToolStripMenuItem.Checked = false;
                this.MultNicMode_ToolStripMenuItem.Checked      = false;
                multiNicSelectedIds = null;
                Debug.WriteLine("读取用户设置失败: " + ex.Message);
            }

            // 恢复多网卡模式开关状态，并据此调整网卡选择菜单可用性
            isMultiMode = this.MultNicMode_ToolStripMenuItem.Checked;
            UpdateInterfaceMenuState();

            // 按持久化的勾选状态刷新多网卡筛选子菜单
            BuildMultiNicFilterMenu();
        }

        /// <summary>切换多网卡/单网卡模式。切换后立即重置采样并持久化设置。</summary>
        private void MultNicMode_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            isMultiMode = this.MultNicMode_ToolStripMenuItem.Checked;
            // 模式切换后采样基数不同，重置采样避免速率突跳
            speedCalcReady = false;
            UpdateInterfaceMenuState();
            // 开关变化 → 重建/清空筛选子菜单（未开启时不显示）
            BuildMultiNicFilterMenu();
            WriteUserSettings();
        }

        /// <summary>多网卡模式下禁用网卡选择菜单（累加筛选网卡，无需手动选择）。</summary>
        private void UpdateInterfaceMenuState()
        {
            this.Interface_Menu.Enabled = !isMultiMode;
        }

        /// <summary>重建「多网卡模式」的筛选子菜单：每块网卡一个勾选项，悬停展开。仅多网卡模式开启时显示。</summary>
        private void BuildMultiNicFilterMenu()
        {
            MultNicMode_ToolStripMenuItem.DropDownItems.Clear();

            // 多网卡模式未开启时不显示筛选子菜单
            if (!isMultiMode)
                return;

            if (nicArr == null || nicArr.Length == 0)
                return;

            foreach (var nic in nicArr)
            {
                // null（未自定义）视为全选
                bool isChecked = multiNicSelectedIds == null || multiNicSelectedIds.Contains(nic.Id);
                var item = new ToolStripMenuItem
                {
                    Text = nic.Name,
                    CheckOnClick = true,
                    Checked = isChecked,
                    Tag = nic.Id
                };
                item.CheckedChanged += MultiNicFilterItem_CheckedChanged;
                MultNicMode_ToolStripMenuItem.DropDownItems.Add(item);
            }
        }

        /// <summary>筛选子菜单勾选变化：更新选中集合并立即持久化。</summary>
        private void MultiNicFilterItem_CheckedChanged(object sender, EventArgs e)
        {
            var item = sender as ToolStripMenuItem;
            string id = item?.Tag as string;
            if (id == null) return;

            // 首次操作：从「全选」基线转为具体集合
            if (multiNicSelectedIds == null)
                multiNicSelectedIds = new HashSet<string>(
                    nicArr != null ? nicArr.Select(n => n.Id) : Enumerable.Empty<string>());

            if (item.Checked)
                multiNicSelectedIds.Add(id);
            else
                multiNicSelectedIds.Remove(id);

            // 筛选集合变化 → 重置采样避免速率突跳
            speedCalcReady = false;
            WriteUserSettings();
        }

        /// <summary>构建当前采样键：单网卡 = 选中网卡 Id，多网卡 = 筛选网卡集合 Id。</summary>
        private string BuildSpeedSampleKey()
        {
            if (isMultiMode)
            {
                var selected = GetSelectedNics();
                if (selected.Length == 0)
                    return "M:empty";
                var ids = selected.Select(n => n.Id).OrderBy(x => x).ToArray();
                return "M:" + string.Join(",", ids);
            }

            if (nicArr == null || ComboBox.SelectedIndex < 0 || ComboBox.SelectedIndex >= nicArr.Length)
                return "S:empty";
            return "S:" + nicArr[ComboBox.SelectedIndex].Id;
        }

        private void WriteUserSettings()
        {
            Properties.Settings.Default.WinowLocation   = this.Location;
            Properties.Settings.Default.ShowInFullScreen = this.ShowInFullScreen_ToolStripMenuItem.Checked;
            Properties.Settings.Default.MultNicMode      = this.MultNicMode_ToolStripMenuItem.Checked;
            Properties.Settings.Default.MultiNicSelectedIds =
                multiNicSelectedIds == null ? "" : string.Join(",", multiNicSelectedIds);
            Properties.Settings.Default.Save();
        }

        private void NetMonitor_FormClosing(object sender, FormClosingEventArgs e)
        {
            WriteUserSettings();

            // 取消网卡变动订阅，防止退出后残留引用
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;

            // 停止 GIF 动画并还原系统定时器分辨率
            try { gifTimer?.Stop(); gifTimer?.Dispose(); } catch { }
            NativeApi.timeEndPeriod(1);

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
                total += GetSingleNicBytesSent(nic);
            return total;
        }
        private long GetAllNicBytesReceived(NetworkInterface[] nics)
        {
            long total = 0;
            foreach (var nic in nics)
                total += GetSingleNicBytesReceived(nic);
            return total;
        }
        private long GetSingleNicBytesSent(NetworkInterface nic)
        {
            try   { return nic.GetIPStatistics().BytesSent; }
            catch { return 0; }
        }

        private long GetSingleNicBytesReceived(NetworkInterface nic)
        {
            try   { return nic.GetIPStatistics().BytesReceived; }
            catch { return 0; }
        }
    }
}