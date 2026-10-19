using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Timers;

namespace NetMonitor.Core
{
    /// <summary>
    /// GIF 帧解析 + 帧调度。
    /// System.Timers.Timer 心跳 + Stopwatch 累积，保证精确帧率、不跳帧。
    /// 帧推进信号在「线程池线程」触发，订阅者须自行切回 UI 线程。
    /// </summary>
    internal sealed class GifAnimator : IDisposable
    {
        private Image[] frames;
        private int frameIndex = 0;
        private int frameCount = 0;

        private int step = 1;                    // GIF 速度档位：1/2/3
        private System.Timers.Timer timer;
        private Stopwatch stopwatch;
        private double accumMs = 0;
        private double frameIntervalMs = 1000.0 / 60;   // 目标帧间隔，随档位变化
        private bool periodStarted = false;      // timeBeginPeriod 是否生效（与 timeEndPeriod 配对）

        /// <summary>帧推进信号（线程池线程触发）；订阅者应自行 BeginInvoke 切回 UI 线程。</summary>
        public event Action FrameAdvanced;

        /// <summary>速度档位：1/2/3，设置时同步换算目标帧间隔。</summary>
        public int Step
        {
            get { return step; }
            set
            {
                step = value < 1 ? 1 : value;
                frameIntervalMs = GetFrameIntervalMs();
            }
        }

        /// <summary>当前帧（供渲染器读取）。</summary>
        public Image CurrentFrame
        {
            get { return (frames != null && frameCount > 0) ? frames[frameIndex] : null; }
        }

        /// <summary>解析 GIF 并提取逐帧位图（保留 Alpha）。</summary>
        public void Load(Image gif)
        {
            if (gif == null) return;

            DisposeTimer();

            try
            {
                FrameDimension frameDimension = new FrameDimension(gif.FrameDimensionsList[0]);
                int count = gif.GetFrameCount(frameDimension);
                if (count <= 0) return;

                // 释放旧帧
                if (frames != null)
                    foreach (Image image in frames)
                        try { if (image != null) image.Dispose(); } catch { }

                frames = new Image[count];

                // 逐帧提取为独立 Bitmap（保留 Alpha）
                for (int i = 0; i < count; i++)
                {
                    gif.SelectActiveFrame(frameDimension, i);
                    Bitmap bitmap = new Bitmap(gif.Width, gif.Height, PixelFormat.Format32bppArgb);
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.Clear(Color.Transparent);
                        graphics.CompositingMode    = CompositingMode.SourceOver;
                        graphics.CompositingQuality = CompositingQuality.HighQuality;
                        graphics.InterpolationMode  = InterpolationMode.HighQualityBicubic;
                        graphics.SmoothingMode      = SmoothingMode.HighQuality;
                        graphics.DrawImage(gif, 0, 0, gif.Width, gif.Height);
                    }
                    frames[i] = bitmap;
                }

                frameCount = count;
                frameIndex = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("SetGifBackground 错误: " + ex.Message);
            }
        }

        /// <summary>启动帧调度（提升系统定时器分辨率到 1ms）。</summary>
        public void Start()
        {
            if (frames == null || frameCount == 0) return;

            // 提升系统定时器分辨率到 1ms（高帧率精确渲染的前提）
            if (!periodStarted)
            {
                NativeApi.timeBeginPeriod(1);
                periodStarted = true;
            }

            frameIndex = 0;
            frameIntervalMs = GetFrameIntervalMs();
            stopwatch = Stopwatch.StartNew();
            accumMs = 0;

            DisposeTimer();

            // 5ms 心跳 + Stopwatch 累积：心跳只负责"到点检查"，实际帧推进由累积时间决定
            timer = new System.Timers.Timer { Interval = 5, AutoReset = true };
            timer.Elapsed += TimerElapsed;
            timer.Start();
        }

        /// <summary>推进到下一帧（须在 UI 线程调用）。</summary>
        public void Advance()
        {
            if (frames == null || frameCount == 0) return;
            frameIndex = (frameIndex + 1) % frameCount;
        }

        /// <summary>档位 → 目标帧间隔（毫秒）。</summary>
        private double GetFrameIntervalMs()
        {
            switch (step)
            {
                case 3:  return 1000.0 / 120;   // 120fps（高速）
                case 2:  return 1000.0 / 50;    // 50fps（中速）
                default: return 1000.0 / 15;    // 15fps（低速）
            }
        }

        private void TimerElapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                if (frames == null || frameCount == 0) return;
                if (stopwatch == null) { stopwatch = Stopwatch.StartNew(); return; }

                // Stopwatch 累积实际经过时间，达到目标帧间隔才推进一帧（精确帧率，不跳帧）
                accumMs += stopwatch.Elapsed.TotalMilliseconds;
                stopwatch.Restart();

                if (accumMs < frameIntervalMs) return;
                accumMs = 0;

                FrameAdvanced?.Invoke();
            }
            catch (Exception ex)
            {
                try { DisposeTimer(); } catch { }
                Console.WriteLine("GIF 播放错误: " + ex.Message);
            }
        }

        private void DisposeTimer()
        {
            if (timer != null)
            {
                timer.Stop();
                timer.Elapsed -= TimerElapsed;
                timer.Dispose();
                timer = null;
            }
        }

        public void Dispose()
        {
            DisposeTimer();
            if (periodStarted)
            {
                NativeApi.timeEndPeriod(1);
                periodStarted = false;
            }
            if (frames != null)
                foreach (Image image in frames)
                    try { if (image != null) image.Dispose(); } catch { }
            frames = null;
            frameCount = 0;
        }
    }
}
