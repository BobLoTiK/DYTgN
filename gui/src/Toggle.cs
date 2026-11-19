using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NetSuite
{
    // iOS-style switch. WinForms' own checkbox looks wrong in a dark theme.
    public class Toggle : Control
    {
        bool _on;
        public event Action Toggled;

        public Toggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(44, 24);
            Cursor = Cursors.Hand;
        }

        public bool Checked
        {
            get { return _on; }
            set { if (_on != value) { _on = value; Invalidate(); } }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            _on = !_on;
            Invalidate();
            Action h = Toggled;
            if (h != null) h();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);
            Rectangle track = new Rectangle(0, 2, Width - 1, Height - 5);
            Theme.Fill(g, track, track.Height / 2, _on ? Theme.Accent : Theme.Border);
            int knob = Height - 10;
            int x = _on ? Width - knob - 3 : 3;
            using (SolidBrush b = new SolidBrush(_on ? Color.White : Color.FromArgb(0xB0, 0xB8, 0xC8)))
                g.FillEllipse(b, new Rectangle(x, 5, knob, knob));
        }
    }
}
