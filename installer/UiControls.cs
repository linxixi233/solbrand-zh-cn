using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SolbrandZh {
    static class Palette {
        internal static readonly Color Paper = Color.FromArgb(244, 247, 249);
        internal static readonly Color Navy = Color.FromArgb(19, 41, 56);
        internal static readonly Color Ink = Color.FromArgb(31, 51, 65);
        internal static readonly Color Muted = Color.FromArgb(105, 122, 134);
        internal static readonly Color Teal = Color.FromArgb(16, 122, 116);
        internal static readonly Color Line = Color.FromArgb(221, 229, 234);
        internal static GraphicsPath Round(RectangleF r, float radius) {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
    }
    sealed class SurfacePanel : Panel {
        Color fill = Color.White, line = Palette.Line;
        internal Color FillColor { get { return fill; } set { fill = value; Invalidate(); } }
        internal Color LineColor { get { return line; } set { line = value; Invalidate(); } }
        internal int Radius = 12;
        internal SurfacePanel() { DoubleBuffered = true; BackColor = Palette.Paper; }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Palette.Round(new RectangleF(.5F, .5F, Width - 1, Height - 1), Radius * e.Graphics.DpiX / 96F)) {
                using (var b = new SolidBrush(fill)) e.Graphics.FillPath(b, p);
                using (var pen = new Pen(line)) e.Graphics.DrawPath(pen, p);
            }
            base.OnPaint(e);
        }
    }
    sealed class BrandPanel : Panel {
        internal BrandPanel() { DoubleBuffered = true; BackColor = Palette.Navy; }
        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new LinearGradientBrush(ClientRectangle, Palette.Navy, Color.FromArgb(26, 62, 75), 0F)) g.FillRectangle(b, ClientRectangle);
            float s = DeviceDpiScale(), cx = Width - 92 * s, cy = Height / 2F;
            using (var pen = new Pen(Color.FromArgb(31, 255, 255, 255), 1F * s)) {
                for (int i = 0; i < 4; i++) {
                    float r = (49 + i * 19) * s;
                    g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                }
                g.DrawLine(pen, Width - 290 * s, Height - 23 * s, Width, Height - 23 * s);
                g.DrawLine(pen, cx, 0, cx, Height);
            }
            using (var pen = new Pen(Color.FromArgb(133, 194, 191), 1.3F * s)) g.DrawEllipse(pen, cx - 29 * s, cy - 29 * s, 58 * s, 58 * s);
            PointF top = new PointF(cx + 10 * s, cy - 24 * s), right = new PointF(cx + 5 * s, cy + 5 * s);
            PointF bottom = new PointF(cx - 10 * s, cy + 24 * s), left = new PointF(cx - 5 * s, cy - 5 * s);
            using (var b = new SolidBrush(Color.FromArgb(107, 215, 195))) g.FillPolygon(b, new[] { top, right, new PointF(cx, cy), left });
            using (var b = new SolidBrush(Color.FromArgb(224, 239, 233))) g.FillPolygon(b, new[] { bottom, left, new PointF(cx, cy), right });
            using (var pen = new Pen(Color.FromArgb(97, 204, 187), 3 * s)) g.DrawLine(pen, 28 * s, Height - 1.5F * s, 88 * s, Height - 1.5F * s);
            base.OnPaint(e);
        }
        float DeviceDpiScale() { using (var g = CreateGraphics()) return g.DpiX / 96F; }
    }
    sealed class PatchButton : Button {
        internal bool Primary, Quiet;
        bool hovered, pressed;
        internal PatchButton() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            BackColor = Palette.Paper; Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var fill = Primary ? Palette.Teal : Quiet ? Palette.Paper : Color.White;
            var text = Primary ? Color.White : Palette.Ink;
            var edge = Primary ? Palette.Teal : Quiet ? Palette.Paper : Palette.Line;
            if (!Enabled) { fill = Primary ? Color.FromArgb(225, 235, 235) : Palette.Paper; text = Color.FromArgb(151, 166, 175); edge = Primary ? fill : Palette.Line; }
            else if (pressed) fill = Primary ? Color.FromArgb(10, 93, 88) : Color.FromArgb(224, 235, 237);
            else if (hovered) { fill = Primary ? Color.FromArgb(19, 142, 134) : Color.FromArgb(233, 242, 242); edge = Primary ? fill : Color.FromArgb(157, 190, 190); }
            using (var path = Palette.Round(new RectangleF(.5F, .5F, Width - 1, Height - 1), 9 * g.DpiX / 96F)) {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var p = new Pen(edge)) g.DrawPath(p, path);
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix));
            if (Focused && ShowFocusCues) {
                using (var p = new Pen(Primary && Enabled ? Color.White : Palette.Teal)) {
                    p.DashStyle = DashStyle.Dot;
                    using (var path = Palette.Round(new RectangleF(4, 4, Width - 9, Height - 9), 6)) g.DrawPath(p, path);
                }
            }
        }
    }
    sealed class StepStrip : Control {
        internal StepStrip() { DoubleBuffered = true; BackColor = Palette.Paper; }
        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var labels = new[] { "确认游戏位置", "安装 / 更新汉化", "启动游戏" };
            float scale = g.DpiX / 96F;
            int unit = Width / 3, diameter = Math.Min((int)(24 * scale), Height - (int)(8 * scale)), y = (int)(2 * scale);
            using (var numberFont = new Font(Font.FontFamily, 8.5F, FontStyle.Bold)) {
                for (int i = 0; i < 3; i++) {
                    int x = i * unit;
                    using (var b = new SolidBrush(Color.FromArgb(226, 237, 237))) g.FillEllipse(b, x, y, diameter, diameter);
                    TextRenderer.DrawText(g, (i + 1).ToString("00"), numberFont, new Rectangle(x, y, diameter, diameter), Palette.Teal, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    TextRenderer.DrawText(g, labels[i], Font, new Rectangle(x + diameter + (int)(9 * scale), y, unit - diameter - (int)(30 * scale), diameter), Palette.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                    if (i < 2) {
                        using (var p = new Pen(Palette.Line, 1)) {
                            int a = x + unit - (int)(32 * scale), b = y + diameter / 2;
                            g.DrawLine(p, a, b, a + 13, b); g.DrawLine(p, a + 9, b - 3, a + 13, b); g.DrawLine(p, a + 9, b + 3, a + 13, b);
                        }
                    }
                }
            }
            base.OnPaint(e);
        }
    }
    sealed class ThinProgress : Control {
        int value;
        internal int Value { get { return value; } set { this.value = Math.Max(0, Math.Min(100, value)); AccessibleDescription = this.value + "%"; Invalidate(); } }
        internal ThinProgress() { DoubleBuffered = true; BackColor = Palette.Paper; AccessibleName = "文件处理进度"; }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Palette.Round(new RectangleF(0, 0, Width, Height), 3 * e.Graphics.DpiX / 96F)) {
                using (var b = new SolidBrush(Color.FromArgb(224, 232, 236))) e.Graphics.FillPath(b, path);
                if (value > 0) {
                    e.Graphics.SetClip(path);
                    using (var b = new SolidBrush(Palette.Teal)) e.Graphics.FillRectangle(b, 0, 0, Width * value / 100F, Height);
                    e.Graphics.ResetClip();
                }
            }
            base.OnPaint(e);
        }
    }
}
