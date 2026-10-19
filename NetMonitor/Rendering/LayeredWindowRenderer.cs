using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace NetMonitor.Rendering
{
    /// <summary>
    /// Per-Pixel Alpha 渲染器：合成 GIF 当前帧 + 玻璃胶囊 + 文字 → 32bpp ARGB → UpdateLayeredWindow 呈现。
    /// 不依赖 WinForms 控件，数据由窗体以 <see cref="RenderContext"/> 快照传入。
    /// </summary>
    internal sealed class LayeredWindowRenderer : IDisposable
    {
        /// <summary>渲染所需输入快照（由窗体在渲染时构造）。</summary>
        internal struct RenderContext
        {
            public IntPtr Handle;          // 窗体句柄
            public Point FormLocation;     // 窗体屏幕位置（Left/Top）
            public Size ClientSize;        // 客户区尺寸
            public Rectangle GifArea;      // GIF 绘制区域（pictureBox 区域）
            public Image CurrentFrame;     // GIF 当前帧
            public LabelInfo[] Labels;     // 文字标签快照
        }

        /// <summary>文字标签快照：复用 Label 的字体/颜色/位置，保证与原有布局一致。</summary>
        internal struct LabelInfo
        {
            public string Text;
            public Font Font;
            public Color ForeColor;
            public Point Location;
        }

        /// <summary>per-pixel alpha 渲染画布（复用，避免每帧重新分配内存）。</summary>
        private Bitmap renderBitmap;

        public void Render(RenderContext context)
        {
            int width  = context.ClientSize.Width;
            int height = context.ClientSize.Height;
            if (width <= 0 || height <= 0) return;

            // 复用画布，尺寸变化时才重建
            if (renderBitmap == null || renderBitmap.Width != width || renderBitmap.Height != height)
            {
                if (renderBitmap != null) renderBitmap.Dispose();
                renderBitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            }

            using (Graphics graphics = Graphics.FromImage(renderBitmap))
            {
                graphics.Clear(Color.Transparent);   // 全透明起步，逐层叠加
                DrawGifFrame(graphics, context);
                DrawGlassCapsule(graphics, context.ClientSize);
                DrawText(graphics, context.Labels);
            }

            PresentLayered(context.Handle, context.FormLocation, renderBitmap);
        }

        /// <summary>绘制 GIF 当前帧（拉伸到 pictureBox 区域，透明部分保持 alpha=0）。</summary>
        private static void DrawGifFrame(Graphics graphics, RenderContext context)
        {
            Image frame = context.CurrentFrame;
            if (frame == null) return;

            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(frame, context.GifArea.X, context.GifArea.Y, context.GifArea.Width, context.GifArea.Height);
        }

        /// <summary>
        /// 绘制玻璃胶囊：纯白内部 + 纯黑描边，仅右侧文字区。
        /// 分层窗口下可安全抗锯齿——边缘 alpha 渐变，无白边、丝滑。
        /// </summary>
        private static void DrawGlassCapsule(Graphics graphics, Size clientSize)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            const float gap      = 2f;    // 胶囊与窗体右缘的缝隙
            const float insetY   = 4f;    // 上下留白
            const float capWidth = 130f;  // 胶囊绘图区域长度（宽度）
            float left = clientSize.Width - capWidth - gap;
            RectangleF capsuleRect = new RectangleF(left, insetY, capWidth, clientSize.Height - insetY * 2);

            using (GraphicsPath path = BuildCapsulePath(capsuleRect))
            {
                using (SolidBrush brush = new SolidBrush(Color.White))
                    graphics.FillPath(brush, path);

                using (Pen pen = new Pen(Color.Black, 1.75f))
                    graphics.DrawPath(pen, path);
            }
        }

        /// <summary>绘制文字：复用 Label 的字体/颜色/位置，保证与原有布局一致。</summary>
        private static void DrawText(Graphics graphics, LabelInfo[] labels)
        {
            // ClearType 子像素抗锯齿：文字最平滑，消除灰度 AA 的"颗粒感"。
            // 文字全部落在不透明的白色胶囊之上，ClearType 以白色为底正确渲染，
            // 不会像画在透明背景上那样产生彩色杂边（文字区域不越过胶囊边界）。
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            if (labels == null) return;
            foreach (LabelInfo label in labels)
                DrawLabel(graphics, label);
        }

        private static void DrawLabel(Graphics graphics, LabelInfo label)
        {
            using (SolidBrush brush = new SolidBrush(label.ForeColor))
                graphics.DrawString(label.Text, label.Font, brush, label.Location.X, label.Location.Y);
        }

        /// <summary>构造胶囊形（两端半圆）路径。</summary>
        private static GraphicsPath BuildCapsulePath(RectangleF bounds)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = bounds.Height;                      // 圆角直径 = 高度 → 完全胶囊形
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 90, 180);    // 左半圆
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 180); // 右半圆
            path.CloseFigure();
            return path;
        }

        /// <summary>把 ARGB 位图呈现为分层窗口内容（premultiplied alpha，逐像素混合）。</summary>
        private static void PresentLayered(IntPtr handle, Point location, Bitmap bitmap)
        {
            IntPtr hdcScreen = NativeApi.GetDC(IntPtr.Zero);
            IntPtr hdcMem    = NativeApi.CreateCompatibleDC(hdcScreen);
            IntPtr hBitmap   = bitmap.GetHbitmap(Color.FromArgb(0));   // 转 premultiplied alpha
            IntPtr hOld      = NativeApi.SelectObject(hdcMem, hBitmap);

            try
            {
                NativeApi.SIZE size  = new NativeApi.SIZE  { cx = bitmap.Width, cy = bitmap.Height };
                NativeApi.POINT ptSrc = new NativeApi.POINT { X = 0, Y = 0 };
                NativeApi.POINT ptDst = new NativeApi.POINT { X = location.X, Y = location.Y };
                NativeApi.BLENDFUNCTION blend = new NativeApi.BLENDFUNCTION
                {
                    BlendOp             = NativeApi.AC_SRC_OVER,
                    BlendFlags          = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat         = NativeApi.AC_SRC_ALPHA
                };

                NativeApi.UpdateLayeredWindow(
                    handle, hdcScreen, ref ptDst, ref size,
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

        public void Dispose()
        {
            if (renderBitmap != null)
            {
                renderBitmap.Dispose();
                renderBitmap = null;
            }
        }
    }
}
