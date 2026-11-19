using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NetSuite
{
    // One place for colors, fonts and rounded-rect drawing. Everything is drawn by
    // hand because WinForms in a dark theme looks like 1998 if you let it.
    static class Theme
    {
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hWnd, string app, string idlist);

        // Native scrollbars in a scrollable panel are bright white by default.
        // The explorer dark-mode theme paints them dark, no custom scrollbar needed.
        public static void DarkScroll(Control c)
        {
            try { if (c != null && c.IsHandleCreated) SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
        }

        public static readonly Color Bg = Color.FromArgb(0x11, 0x13, 0x18);
        public static readonly Color Sidebar = Color.FromArgb(0x15, 0x18, 0x1F);
        public static readonly Color Card = Color.FromArgb(0x1B, 0x1F, 0x28);
        public static readonly Color CardAlt = Color.FromArgb(0x17, 0x1B, 0x23);
        public static readonly Color Hover = Color.FromArgb(0x22, 0x27, 0x33);
        public static readonly Color Border = Color.FromArgb(0x2A, 0x30, 0x3D);
        public static readonly Color BorderSoft = Color.FromArgb(0x22, 0x27, 0x31);
        public static readonly Color Text = Color.FromArgb(0xE9, 0xEC, 0xF3);
        public static readonly Color Muted = Color.FromArgb(0x8D, 0x96, 0xA9);
        public static readonly Color Dim = Color.FromArgb(0x5C, 0x65, 0x76);
        public static readonly Color Accent = Color.FromArgb(0x4C, 0x8D, 0xFF);
        public static readonly Color AccentSoft = Color.FromArgb(0x24, 0x33, 0x52);
        public static readonly Color Ok = Color.FromArgb(0x3D, 0xD6, 0x8C);
        public static readonly Color Warn = Color.FromArgb(0xF5, 0xA5, 0x24);
        public static readonly Color Err = Color.FromArgb(0xF2, 0x6D, 0x6D);
        public static readonly Color LogBg = Color.FromArgb(0x0E, 0x10, 0x15);

        public static readonly Font Base = new Font("Segoe UI", 9.75f);
        public static readonly Font Small = new Font("Segoe UI", 8.5f);
        public static readonly Font Bold = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font H1 = new Font("Segoe UI Semibold", 15f);
        public static readonly Font H2 = new Font("Segoe UI Semibold", 11.5f);
        public static readonly Font Num = new Font("Segoe UI Semibold", 19f);
        public static readonly Font LogFont = new Font("Consolas", 8.75f);

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            if (radius <= 0)
            {
                p.AddRectangle(r);
                return p;
            }
            int d = Math.Min(Math.Min(radius * 2, r.Width), r.Height);
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, Rectangle r, int radius, Color c)
        {
            if (c.A == 0) return;
            using (GraphicsPath p = Round(r, radius))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static void Stroke(Graphics g, Rectangle r, int radius, Color c)
        {
            if (c.A == 0) return;
            using (GraphicsPath p = Round(r, radius))
            using (Pen pen = new Pen(c))
                g.DrawPath(pen, p);
        }

        public static Color Mix(Color a, Color b, double t)
        {
            if (t < 0) t = 0; if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        // Lighten a color towards white by amount (0..1) - used for hover states.
        public static Color Lift(Color c, double amount)
        {
            return Mix(c, Color.White, amount);
        }

        public static void Text_(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, s, f, r, c, flags | TextFormatFlags.NoPadding);
        }

        public static void SectionLabel(Graphics g, string s, int x, int y, int w)
        {
            Text_(g, s.ToUpperInvariant(), Small, new Rectangle(x, y, w, 16), Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
