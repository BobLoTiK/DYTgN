using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NetSuite
{
    // Rounded panel. Children should paint their own background with Fill.
    public class Card : Panel
    {
        public int Radius = 12;
        public Color FillColor = Theme.Card;
        public Color StrokeColor = Theme.Border;

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle full = new Rectangle(0, 0, Width, Height);
            Color under = Parent != null ? Parent.BackColor : Theme.Bg;
            using (SolidBrush b = new SolidBrush(under)) g.FillRectangle(b, full);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.Fill(g, r, Radius, FillColor);
            if (StrokeColor.A > 0) Theme.Stroke(g, r, Radius, StrokeColor);
        }
    }

    public enum BtnStyle { Primary, Ghost, Danger, Subtle, Success }

    public class Btn : Control
    {
        public BtnStyle Style = BtnStyle.Ghost;
        public int Radius = 8;
        bool hover, down;

        public Btn()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            Font = Theme.Bold;
            Height = 34;
            Width = 120;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill, text, stroke;
            bool active = Enabled && !down;
            switch (Style)
            {
                case BtnStyle.Primary:
                    fill = Theme.Accent; text = Color.White; stroke = Color.Empty;
                    if (hover && active) fill = Theme.Lift(Theme.Accent, 0.14);
                    break;
                case BtnStyle.Success:
                    fill = Color.FromArgb(0x2A, 0x5E, 0x48); text = Theme.Ok;
                    stroke = Color.FromArgb(0x33, 0x6E, 0x55);
                    if (hover && active) fill = Theme.Lift(fill, 0.12);
                    break;
                case BtnStyle.Danger:
                    fill = Color.FromArgb(0x35, 0x1C, 0x21); text = Theme.Err;
                    stroke = Color.FromArgb(0x55, 0x2B, 0x32);
                    if (hover && active) fill = Theme.Lift(fill, 0.12);
                    break;
                case BtnStyle.Subtle:
                    fill = hover && active ? Theme.Hover : Color.Empty;
                    text = hover && active ? Theme.Text : Theme.Muted;
                    stroke = Color.Empty;
                    break;
                default: // Ghost
                    fill = hover && active ? Theme.Hover : Theme.CardAlt;
                    text = Theme.Text; stroke = Theme.Border;
                    break;
            }
            if (down) fill = Theme.Mix(fill == Color.Empty ? Theme.Card : fill, Color.Black, 0.18);
            if (!Enabled) { text = Theme.Dim; if (Style != BtnStyle.Subtle) fill = Theme.Mix(fill == Color.Empty ? Theme.Card : fill, Theme.Bg, 0.5); }

            if (fill.A > 0) Theme.Fill(g, r, Radius, fill);
            if (stroke.A > 0) Theme.Stroke(g, r, Radius, stroke);
            Theme.Text_(g, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    // Left-rail navigation item: accent bar when active, hover wash otherwise.
    public class NavBtn : Control
    {
        public bool Active;
        public string Badge = "";
        bool hover;

        public NavBtn(string caption)
        {
            Text = caption;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 38;
            Cursor = Cursors.Hand;
            Font = Theme.Base;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);
            Rectangle r = new Rectangle(8, 2, Width - 16, Height - 4);
            if (Active) Theme.Fill(g, r, 8, Theme.Hover);
            else if (hover) Theme.Fill(g, r, 8, Theme.CardAlt);
            if (Active)
            {
                using (GraphicsPath p = Theme.Round(new Rectangle(0, 8, 3, Height - 16), 2))
                using (SolidBrush b = new SolidBrush(Theme.Accent))
                    g.FillPath(b, p);
            }
            Color c = Active ? Theme.Text : (hover ? Theme.Text : Theme.Muted);
            Theme.Text_(g, Text, Active ? Theme.Bold : Theme.Base,
                new Rectangle(22, 0, Width - 60, Height), c,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Badge != null && Badge.Length > 0)
            {
                Size sz = TextRenderer.MeasureText(Badge, Theme.Small);
                Rectangle br = new Rectangle(Width - 22 - sz.Width, (Height - 17) / 2, sz.Width + 8, 17);
                Theme.Fill(g, br, 8, Theme.AccentSoft);
                Theme.Text_(g, Badge, Theme.Small, br, Theme.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    // Small status lamp. Draws a soft ring so it reads at a glance.
    public class Dot : Control
    {
        Color _c = Theme.Dim;
        public Dot(int size)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Width = size; Height = size;
        }
        public Color Color
        {
            get { return _c; }
            set { if (_c != value) { _c = value; Invalidate(); } }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);
            int d = Math.Min(Width, Height);
            Color halo = Color.FromArgb(50, _c.R, _c.G, _c.B);
            using (SolidBrush hb = new SolidBrush(halo))
                g.FillEllipse(hb, new Rectangle(0, 0, d - 1, d - 1));
            using (SolidBrush b = new SolidBrush(_c))
                g.FillEllipse(b, new Rectangle(d / 4, d / 4, d / 2 - 1, d / 2 - 1));
        }
    }

    // Rounded chip with a state word.
    public class Pill : Control
    {
        Color _c = Theme.Dim;
        public Pill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Theme.Small;
            Height = 20;
        }
        public Color Color
        {
            get { return _c; }
            set { _c = value; Invalidate(); }
        }
        public void Set(string text, Color c) { Text = text; _c = c; Width = TextRenderer.MeasureText(text, Font).Width + 16; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);
            Theme.Fill(g, new Rectangle(0, 0, Width - 1, Height - 1), Height / 2,
                Color.FromArgb(38, _c.R, _c.G, _c.B));
            Theme.Text_(g, Text, Font, new Rectangle(0, 0, Width, Height), _c,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // Console-style log that never steals focus and always shows the last line.
    public class LogView : RichTextBox
    {
        public LogView()
        {
            BorderStyle = BorderStyle.None;
            BackColor = Theme.LogBg;
            ForeColor = Theme.Muted;
            Font = Theme.LogFont;
            ReadOnly = true;
            WordWrap = false;
            ScrollBars = RichTextBoxScrollBars.Vertical;
            DetectUrls = false;
            TabStop = false;
        }

        public void AppendLine(string text, Color c)
        {
            if (IsDisposed) return;
            if (TextLength > 200000) Clear();
            SelectionStart = TextLength;
            SelectionLength = 0;
            SelectionColor = c;
            AppendText(text + Environment.NewLine);
            SelectionStart = TextLength;
            ScrollToCaret();
        }
    }

    // Thin progress bar with a caption.
    public class Bar : Control
    {
        double _v;
        public Bar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 8;
        }
        public double Value
        {
            get { return _v; }
            set { _v = Math.Max(0, Math.Min(1, value)); Invalidate(); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);
            Rectangle r = new Rectangle(0, (Height - 6) / 2, Width - 1, 6);
            Theme.Fill(g, r, 3, Theme.CardAlt);
            if (_v > 0)
            {
                int w = (int)Math.Round((Width - 1) * _v);
                if (w < 6) w = 6;
                Theme.Fill(g, new Rectangle(0, (Height - 6) / 2, w, 6), 3, Theme.Accent);
            }
        }
    }

    // Card with a caption + value, used for the numbers on the diagnostics page.
    public class StatTile : Card
    {
        string _cap = "", _val = "-", _sub = "";
        Color _valColor = Theme.Text;
        public StatTile(string caption)
        {
            _cap = caption;
            FillColor = Theme.Card;
            Padding = new Padding(14, 10, 14, 10);
        }
        public string Value { get { return _val; } set { _val = value; Invalidate(); } }
        public string Sub { get { return _sub; } set { _sub = value; Invalidate(); } }
        public Color ValueColor { get { return _valColor; } set { _valColor = value; Invalidate(); } }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Theme.Text_(g, _cap, Theme.Small, new Rectangle(12, 8, Width - 24, 16), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            Theme.Text_(g, _val, Theme.Num, new Rectangle(10, Height / 2 - 20, Width - 20, 30), _valColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (_sub.Length > 0)
                Theme.Text_(g, _sub, Theme.Small, new Rectangle(12, Height - 24, Width - 24, 16), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
