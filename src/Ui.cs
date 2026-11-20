using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PhotoImport
{
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(15, 17, 21);
        public static readonly Color Panel = Color.FromArgb(19, 22, 28);
        public static readonly Color Card = Color.FromArgb(23, 26, 33);
        public static readonly Color Line = Color.FromArgb(38, 43, 54);
        public static readonly Color Text = Color.FromArgb(232, 234, 240);
        public static readonly Color Soft = Color.FromArgb(201, 207, 218);
        public static readonly Color Muted = Color.FromArgb(154, 163, 178);
        public static readonly Color Accent = Color.FromArgb(47, 107, 224);
        public static readonly Color AccentBright = Color.FromArgb(76, 141, 255);
        public static readonly Color AccentText = Color.FromArgb(127, 176, 255);
        public static readonly Color Amber = Color.FromArgb(255, 180, 84);
        public static readonly Color Green = Color.FromArgb(94, 211, 154);
        public static readonly Color GreenDeep = Color.FromArgb(30, 122, 78);
        public static readonly Color DangerBg = Color.FromArgb(42, 23, 23);
        public static readonly Color DangerLine = Color.FromArgb(91, 42, 42);
        public static readonly Color DangerText = Color.FromArgb(255, 180, 180);
        public static readonly Color Active = Color.FromArgb(31, 41, 55);
        public static readonly Color[] Tints =
        {
            Color.FromArgb(59, 74, 90), Color.FromArgb(90, 75, 59), Color.FromArgb(63, 90, 74), Color.FromArgb(90, 63, 79),
            Color.FromArgb(69, 70, 107), Color.FromArgb(106, 90, 58), Color.FromArgb(53, 82, 92), Color.FromArgb(92, 64, 53)
        };

        public static float K = 1f;
        public static int S(int v) { return (int)Math.Round(v * K); }

        [DllImport("gdi32.dll")]
        static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, ref uint pcFonts);

        static readonly PrivateFontCollection collection = new PrivateFontCollection();
        static readonly Dictionary<string, Font> cache = new Dictionary<string, Font>();
        static bool loaded;

        // Loads the bundled Afacad Flux files; falls back to Segoe UI if Windows refuses them
        public static void LoadFonts()
        {
            try
            {
                foreach (string res in new[] { "AfacadFlux-Regular.ttf", "AfacadFlux-SemiBold.ttf" })
                {
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(res))
                    {
                        if (s == null) return;
                        byte[] data = new byte[s.Length];
                        int read = 0;
                        while (read < data.Length) read += s.Read(data, read, data.Length - read);
                        IntPtr mem = Marshal.AllocCoTaskMem(data.Length);
                        Marshal.Copy(data, 0, mem, data.Length);
                        uint n = 0;
                        AddFontMemResourceEx(mem, (uint)data.Length, IntPtr.Zero, ref n);
                        collection.AddMemoryFont(mem, data.Length);
                    }
                }
                loaded = collection.Families.Length > 0;
            }
            catch { loaded = false; }
        }

        public static Font Font(float size, bool strong)
        {
            string key = size.ToString(CultureInfo.InvariantCulture) + (strong ? "b" : "r");
            Font f;
            if (cache.TryGetValue(key, out f)) return f;
            f = null;
            if (loaded)
            {
                try
                {
                    foreach (FontFamily fam in collection.Families)
                        if (fam.Name.Contains("SemiBold") == strong) f = new Font(fam, size, FontStyle.Regular, GraphicsUnit.Point);
                }
                catch { f = null; }
            }
            if (f == null) f = new Font("Segoe UI", size - 1f, strong ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
            cache[key] = f;
            return f;
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = Math.Max(2, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, Rectangle r, int radius)
        {
            using (GraphicsPath p = Round(r, radius))
            using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, Color c, Rectangle r, int radius, float width)
        {
            using (GraphicsPath p = Round(r, radius))
            using (var pen = new Pen(c, width)) g.DrawPath(pen, p);
        }

        public const TextFormatFlags Left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        public const TextFormatFlags Center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        public const TextFormatFlags Right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        public static void Draw(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, s ?? "", f, r, c, flags);
        }

        public static int Width(string s, Font f)
        {
            return TextRenderer.MeasureText(s ?? "", f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
        }

        public static void Check(Graphics g, Rectangle r, Color c, float width)
        {
            using (var pen = new Pen(c, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[]
                {
                    new PointF(r.X + r.Width * 0.24f, r.Y + r.Height * 0.52f),
                    new PointF(r.X + r.Width * 0.43f, r.Y + r.Height * 0.71f),
                    new PointF(r.X + r.Width * 0.77f, r.Y + r.Height * 0.31f)
                });
        }

        public static void Pill(Graphics g, string text, Rectangle anchor, bool top, bool left, Color bg, Color fg)
        {
            Font f = Font(9f, true);
            int w = Width(text, f) + S(16), h = S(20), m = S(8);
            var r = new Rectangle(left ? anchor.X + m : anchor.Right - m - w, top ? anchor.Y + m : anchor.Bottom - m - h, w, h);
            FillRound(g, bg, r, h / 2);
            Draw(g, text, f, fg, r, Center);
        }

        public static void PictureIcon(Graphics g, Rectangle tile)
        {
            int s = Math.Min(S(30), tile.Width / 3);
            var r = new Rectangle(tile.X + (tile.Width - s) / 2, tile.Y + (tile.Height - s * 3 / 4) / 2, s, s * 3 / 4);
            using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), Math.Max(1.5f, K * 1.6f)) { LineJoin = LineJoin.Round })
            {
                using (GraphicsPath p = Round(r, S(3))) g.DrawPath(pen, p);
                g.DrawLines(pen, new[]
                {
                    new PointF(r.X + 2, r.Bottom - r.Height * 0.2f), new PointF(r.X + r.Width * 0.35f, r.Y + r.Height * 0.55f),
                    new PointF(r.X + r.Width * 0.55f, r.Bottom - r.Height * 0.3f), new PointF(r.X + r.Width * 0.7f, r.Y + r.Height * 0.6f),
                    new PointF(r.Right - 2, r.Bottom - r.Height * 0.2f)
                });
            }
        }
    }

    class Canvas : Control
    {
        public Canvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            Render(e.Graphics);
        }

        protected virtual void Render(Graphics g) { }
    }

    enum ButtonKind { Plain, Primary, Danger, Refresh }

    class FlatButton : Canvas
    {
        public ButtonKind Kind = ButtonKind.Plain;
        public float FontSize = 11.5f;
        bool hover, down;

        public FlatButton(string text)
        {
            Text = text;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Focus(); Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty);
            base.OnKeyDown(e);
        }

        static Color Shift(Color c, int d)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, c.R + d)), Math.Max(0, Math.Min(255, c.G + d)), Math.Max(0, Math.Min(255, c.B + d)));
        }

        protected override void Render(Graphics g)
        {
            if (Parent != null) g.Clear(Parent.BackColor);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = Kind == ButtonKind.Primary ? Theme.Accent : Kind == ButtonKind.Danger ? Theme.DangerBg : Theme.Card;
            Color line = Kind == ButtonKind.Primary ? Theme.Accent : Kind == ButtonKind.Danger ? Theme.DangerLine : Theme.Line;
            Color text = Kind == ButtonKind.Primary ? Color.White : Kind == ButtonKind.Danger ? Theme.DangerText : Theme.Text;
            if (!Enabled)
            {
                fill = Theme.Card;
                line = Theme.Line;
                text = Theme.Muted;
            }
            else if (down) fill = Shift(fill, -8);
            else if (hover) fill = Shift(fill, 12);
            int radius = Theme.S(Kind == ButtonKind.Primary ? 12 : 10);
            Theme.FillRound(g, fill, r, radius);
            Theme.StrokeRound(g, Focused ? Theme.AccentText : line, r, radius, 1f);
            if (Kind == ButtonKind.Refresh)
            {
                int s = Theme.S(16);
                var a = new Rectangle((Width - s) / 2, (Height - s) / 2, s, s);
                using (var pen = new Pen(text, Math.Max(1.6f, Theme.K * 1.8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawArc(pen, a, 20, 290);
                    g.DrawLines(pen, new[] { new PointF(a.Right - s * 0.42f, a.Y + s * 0.42f), new PointF(a.Right, a.Y + s * 0.42f), new PointF(a.Right, a.Y) });
                }
                return;
            }
            Theme.Draw(g, Text, Theme.Font(FontSize, Kind == ButtonKind.Primary || Kind == ButtonKind.Danger), text, new Rectangle(0, 0, Width, Height), Theme.Center);
        }
    }

    class HeaderBar : Canvas
    {
        public string Device = "";
        public string Status = "Looking for a phone";
        public Color Dot = Theme.Amber;
        public readonly FlatButton Refresh = new FlatButton("") { Kind = ButtonKind.Refresh };

        public HeaderBar()
        {
            BackColor = Theme.Panel;
            Height = Theme.S(62);
            Dock = DockStyle.Top;
            Refresh.AccessibleName = "Refresh phone";
            Controls.Add(Refresh);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int s = Theme.S(40);
            Refresh.Bounds = new Rectangle(Width - Theme.S(24) - s, (Height - s) / 2, s, s);
        }

        public void Set(string device, string status, Color dot)
        {
            Device = device;
            Status = status;
            Dot = dot;
            Invalidate();
        }

        protected override void Render(Graphics g)
        {
            using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            int x = Theme.S(24), logo = Theme.S(32);
            var lr = new Rectangle(x, (Height - logo) / 2, logo, logo);
            Theme.FillRound(g, Theme.Accent, lr, Theme.S(9));
            using (var pen = new Pen(Color.White, Math.Max(2f, Theme.K * 2.2f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                float cx = lr.X + logo / 2f, t = lr.Y + logo * 0.22f, b = lr.Y + logo * 0.6f, q = logo * 0.17f;
                g.DrawLine(pen, cx, t, cx, b);
                g.DrawLines(pen, new[] { new PointF(cx - q, b - q), new PointF(cx, b), new PointF(cx + q, b - q) });
                g.DrawLine(pen, cx - logo * 0.24f, lr.Y + logo * 0.78f, cx + logo * 0.24f, lr.Y + logo * 0.78f);
            }
            Theme.Draw(g, "Photo Import", Theme.Font(15f, true), Theme.Text, new Rectangle(lr.Right + Theme.S(12), 0, Theme.S(240), Height), Theme.Left);

            Font strong = Theme.Font(11.5f, true), plain = Theme.Font(11.5f, false);
            int wd = Device.Length > 0 ? Theme.Width(Device, strong) + Theme.S(10) : 0, ws = Theme.Width(Status, plain);
            int pw = Theme.S(14) + Theme.S(9) + Theme.S(10) + wd + ws + Theme.S(14), ph = Theme.S(36);
            var pill = new Rectangle(Refresh.Left - Theme.S(10) - pw, (Height - ph) / 2, pw, ph);
            Theme.FillRound(g, Theme.Card, pill, ph / 2);
            Theme.StrokeRound(g, Theme.Line, pill, ph / 2, 1f);
            using (var b = new SolidBrush(Dot)) g.FillEllipse(b, pill.X + Theme.S(14), pill.Y + (ph - Theme.S(9)) / 2, Theme.S(9), Theme.S(9));
            int tx = pill.X + Theme.S(33);
            if (wd > 0) Theme.Draw(g, Device, strong, Theme.Text, new Rectangle(tx, pill.Y, wd, ph), Theme.Left);
            Theme.Draw(g, Status, plain, Device.Length > 0 ? Theme.Muted : Theme.Soft, new Rectangle(tx + wd, pill.Y, ws + Theme.S(4), ph), Theme.Left);
        }
    }

    enum Filter { New, All, Imported, Videos }

    class Sidebar : Canvas
    {
        public Filter Filter = Filter.New;
        public DateTime? Month;
        public int CountNew, CountAll, CountImported, CountVideos;
        public List<KeyValuePair<DateTime, int>> Months = new List<KeyValuePair<DateTime, int>>();
        public event EventHandler Changed;
        int scroll, contentHeight, hoverIndex = -1;

        class Row { public Rectangle R; public string Label; public string Count; public bool Active; public bool Badge; public int FilterIndex = -1; public DateTime? Month; public bool Caption; }

        public Sidebar()
        {
            BackColor = Theme.Panel;
            Width = Theme.S(236);
            Dock = DockStyle.Left;
        }

        List<Row> Rows()
        {
            var rows = new List<Row>();
            int x = Theme.S(14), w = Width - Theme.S(28), y = Theme.S(20) - scroll, rh = Theme.S(42), ch = Theme.S(26);
            rows.Add(new Row { R = new Rectangle(x, y, w, ch), Label = "SHOW", Caption = true });
            y += ch;
            string[] names = { "New on phone", "Everything", "Already imported", "Videos only" };
            int[] counts = { CountNew, CountAll, CountImported, CountVideos };
            for (int i = 0; i < 4; i++)
            {
                rows.Add(new Row { R = new Rectangle(x, y, w, rh), Label = names[i], Count = counts[i].ToString("N0"), Active = (int)Filter == i, Badge = i == 0, FilterIndex = i });
                y += rh + Theme.S(4);
            }
            y += Theme.S(18);
            if (Months.Count > 0)
            {
                rows.Add(new Row { R = new Rectangle(x, y, w, ch), Label = "MONTHS", Caption = true });
                y += ch;
                foreach (KeyValuePair<DateTime, int> m in Months)
                {
                    rows.Add(new Row { R = new Rectangle(x, y, w, rh), Label = m.Key.ToString("MMMM yyyy", CultureInfo.InvariantCulture), Count = m.Value.ToString("N0"), Active = Month.HasValue && Month.Value == m.Key, Month = m.Key });
                    y += rh + Theme.S(4);
                }
            }
            contentHeight = y + scroll + Theme.S(12);
            return rows;
        }

        protected override void Render(Graphics g)
        {
            List<Row> rows = Rows();
            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                if (r.R.Bottom < 0 || r.R.Y > Height) continue;
                if (r.Caption)
                {
                    Theme.Draw(g, r.Label, Theme.Font(9f, true), Theme.Muted, new Rectangle(r.R.X + Theme.S(12), r.R.Y, r.R.Width, r.R.Height - Theme.S(4)), Theme.Left);
                    continue;
                }
                if (r.Active) Theme.FillRound(g, Theme.Active, r.R, Theme.S(9));
                else if (i == hoverIndex) Theme.FillRound(g, Theme.Card, r.R, Theme.S(9));
                Font cf = Theme.Font(10.5f, r.Badge);
                int cw = Theme.Width(r.Count, cf);
                var cr = new Rectangle(r.R.Right - Theme.S(12) - cw, r.R.Y, cw, r.R.Height);
                if (r.Badge)
                {
                    var br = new Rectangle(cr.X - Theme.S(9), r.R.Y + (r.R.Height - Theme.S(22)) / 2, cw + Theme.S(18), Theme.S(22));
                    br.X = r.R.Right - Theme.S(10) - br.Width;
                    Theme.FillRound(g, Theme.Amber, br, br.Height / 2);
                    Theme.Draw(g, r.Count, cf, Color.FromArgb(26, 18, 0), br, Theme.Center);
                    cr = br;
                }
                else Theme.Draw(g, r.Count, cf, Theme.Muted, cr, Theme.Right);
                Theme.Draw(g, r.Label, Theme.Font(11.5f, r.Active), r.Active ? Color.White : Theme.Soft,
                    new Rectangle(r.R.X + Theme.S(12), r.R.Y, cr.X - r.R.X - Theme.S(18), r.R.Height), Theme.Left);
            }
            using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
        }

        int Hit(Point p)
        {
            List<Row> rows = Rows();
            for (int i = 0; i < rows.Count; i++)
                if (!rows[i].Caption && rows[i].R.Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h != hoverIndex) { hoverIndex = h; Invalidate(); }
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e) { hoverIndex = -1; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h < 0) return;
            Row r = Rows()[h];
            if (r.FilterIndex >= 0) Filter = (Filter)r.FilterIndex;
            else Month = Month.HasValue && Month.Value == r.Month.Value ? null : r.Month;
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Rows();
            int max = Math.Max(0, contentHeight - Height);
            scroll = Math.Max(0, Math.Min(max, scroll - e.Delta / 120 * Theme.S(60)));
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { Focus(); }
    }

    class Grid : Canvas
    {
        public bool ImportMode;
        public bool AllowTurn;
        public event Action<Item> ThumbNeeded;
        public event EventHandler SelectionChanged;
        public string EmptyText = "";

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        class Band { public bool Header; public string Label; public int Count; public int Y, H; public List<Item> Items; public int Start, N; }

        readonly VScrollBar bar = new VScrollBar { Dock = DockStyle.Right };
        readonly List<Band> bands = new List<Band>();
        List<Item> items = new List<Item>();
        int cols = 1, tile = 100, total;

        public Grid()
        {
            Dock = DockStyle.Fill;
            Controls.Add(bar);
            bar.ValueChanged += delegate { Invalidate(); };
            bar.HandleCreated += delegate { try { SetWindowTheme(bar.Handle, "DarkMode_Explorer", null); } catch { } };
        }

        public void SetItems(List<Item> list, bool keepScroll)
        {
            items = list;
            if (!keepScroll) bar.Value = 0;
            Relayout();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

        void Relayout()
        {
            bands.Clear();
            int pad = Theme.S(28), gap = Theme.S(10), min = Theme.S(128), headerH = Theme.S(46);
            int avail = Math.Max(min, Width - bar.Width - pad * 2);
            cols = Math.Max(1, (avail + gap) / (min + gap));
            tile = (avail - (cols - 1) * gap) / cols;
            int y = Theme.S(4);
            int i = 0;
            while (i < items.Count)
            {
                DateTime month = items[i].Month;
                int j = i;
                while (j < items.Count && items[j].Month == month) j++;
                bands.Add(new Band { Header = true, Label = month.ToString("MMMM yyyy", CultureInfo.InvariantCulture), Count = j - i, Y = y, H = headerH });
                y += headerH;
                for (int k = i; k < j; k += cols)
                {
                    bands.Add(new Band { Y = y, H = tile + gap, Items = items, Start = k, N = Math.Min(cols, j - k) });
                    y += tile + gap;
                }
                y += Theme.S(12);
                i = j;
            }
            total = y + Theme.S(12);
            int view = Math.Max(1, Height);
            bar.Maximum = Math.Max(view, total);
            bar.LargeChange = view;
            bar.SmallChange = Theme.S(60);
            bar.Enabled = total > view;
            int maxValue = Math.Max(0, bar.Maximum - bar.LargeChange + 1);
            if (bar.Value > maxValue) bar.Value = maxValue;
            Invalidate();
        }

        public void ScrollTo(Item item)
        {
            foreach (Band b in bands)
            {
                if (b.Header) continue;
                for (int n = 0; n < b.N; n++)
                {
                    if (items[b.Start + n] != item) continue;
                    if (b.Y < bar.Value || b.Y + b.H > bar.Value + Height)
                        bar.Value = Math.Max(0, Math.Min(Math.Max(0, bar.Maximum - bar.LargeChange + 1), b.Y - Theme.S(60)));
                    return;
                }
            }
        }

        Rectangle TileRect(Band b, int n, int scroll)
        {
            return new Rectangle(Theme.S(28) + n * (tile + Theme.S(10)), b.Y - scroll, tile, tile);
        }

        protected override void Render(Graphics g)
        {
            if (items.Count == 0)
            {
                Theme.Draw(g, EmptyText, Theme.Font(12.5f, false), Theme.Muted, new Rectangle(0, 0, Width - bar.Width, Math.Min(Height, Theme.S(220))), Theme.Center);
                return;
            }
            int scroll = bar.Value;
            foreach (Band b in bands)
            {
                if (b.Y + b.H < scroll || b.Y > scroll + Height) continue;
                if (b.Header)
                {
                    Font hf = Theme.Font(13f, true);
                    int x = Theme.S(28), w = Theme.Width(b.Label, hf);
                    Theme.Draw(g, b.Label, hf, Theme.Text, new Rectangle(x, b.Y - scroll, w + 4, b.H), Theme.Left);
                    Theme.Draw(g, b.Count.ToString("N0") + (b.Count == 1 ? " item" : " items"), Theme.Font(11f, false), Theme.Muted,
                        new Rectangle(x + w + Theme.S(10), b.Y - scroll + Theme.S(1), Theme.S(200), b.H), Theme.Left);
                    continue;
                }
                for (int n = 0; n < b.N; n++) DrawTile(g, items[b.Start + n], TileRect(b, n, scroll));
            }
        }

        void DrawTile(Graphics g, Item it, Rectangle r)
        {
            int radius = Theme.S(10);
            if (it.Thumb == null && !it.ThumbAsked)
            {
                it.ThumbAsked = true;
                if (ThumbNeeded != null) ThumbNeeded(it);
            }
            if (it.Thumb != null && it.TurnPending && !it.TurnAsked && AllowTurn)
            {
                it.TurnAsked = true;
                if (ThumbNeeded != null) ThumbNeeded(it);
            }
            if (it.Thumb != null)
            {
                using (GraphicsPath p = Theme.Round(r, radius))
                {
                    GraphicsState st = g.Save();
                    g.SetClip(p, CombineMode.Intersect);
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(it.Thumb, r);
                    g.Restore(st);
                }
            }
            else
            {
                Theme.FillRound(g, Theme.Tints[Math.Abs(it.Name.GetHashCode()) % Theme.Tints.Length], r, radius);
                Theme.PictureIcon(g, r);
            }

            bool dim = ImportMode ? it.State == ItemState.Queued : it.State == ItemState.Imported;
            if (dim) Theme.FillRound(g, Color.FromArgb(150, Theme.Bg), new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), radius);

            int c = Theme.S(24), m = Theme.S(8);
            var circle = new Rectangle(r.Right - m - c, r.Y + m, c, c);
            bool selectable = !ImportMode && (it.State == ItemState.New || it.State == ItemState.Failed);
            if (selectable && it.Selected)
            {
                Theme.StrokeRound(g, Theme.AccentText, new Rectangle(r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3), radius, Theme.S(3));
                Mark(g, circle, Theme.Accent);
            }
            else if (selectable)
            {
                using (var b = new SolidBrush(Color.FromArgb(90, Theme.Bg))) g.FillEllipse(b, circle);
                using (var pen = new Pen(Color.White, Theme.S(2))) g.DrawEllipse(pen, circle);
            }
            if (it.State == ItemState.Done) Mark(g, circle, Theme.GreenDeep);
            if (it.State == ItemState.Copying)
            {
                Theme.StrokeRound(g, Theme.AccentText, new Rectangle(r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3), radius, Theme.S(3));
                Theme.Pill(g, "Copying", r, true, true, Theme.Bg, Color.White);
            }
            if (it.State == ItemState.Imported && !ImportMode) Theme.Pill(g, "Imported", r, true, true, Theme.Bg, Theme.Soft);
            if (it.State == ItemState.Failed) Theme.Pill(g, "Failed", r, true, true, Theme.DangerBg, Theme.DangerText);
            if (it.IsVideo) Theme.Pill(g, "▶ Video", r, false, true, Theme.Bg, Color.White);
        }

        static void Mark(Graphics g, Rectangle circle, Color fill)
        {
            using (var b = new SolidBrush(fill)) g.FillEllipse(b, circle);
            using (var pen = new Pen(Color.White, Theme.S(2))) g.DrawEllipse(pen, circle);
            Theme.Check(g, circle, Color.White, Math.Max(2f, Theme.K * 2.4f));
        }

        Item Hit(Point p)
        {
            int scroll = bar.Value;
            foreach (Band b in bands)
            {
                if (b.Header || p.Y + scroll < b.Y || p.Y + scroll > b.Y + b.H) continue;
                for (int n = 0; n < b.N; n++)
                    if (TileRect(b, n, scroll).Contains(p)) return items[b.Start + n];
            }
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            Item it = Hit(e.Location);
            if (it == null || ImportMode || (it.State != ItemState.New && it.State != ItemState.Failed)) return;
            it.Selected = !it.Selected;
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Item it = Hit(e.Location);
            Cursor = it != null && !ImportMode && (it.State == ItemState.New || it.State == ItemState.Failed) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseEnter(EventArgs e) { Focus(); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int max = Math.Max(0, bar.Maximum - bar.LargeChange + 1);
            bar.Value = Math.Max(0, Math.Min(max, bar.Value - e.Delta * Theme.S(110) / 120));
        }
    }

    class TitleBar : Canvas
    {
        public string Heading = "New on phone";
        public string Sub = "";
        public readonly FlatButton SelectAll = new FlatButton("Select all new");
        public readonly FlatButton Clear = new FlatButton("Clear");

        public TitleBar()
        {
            Height = Theme.S(84);
            Dock = DockStyle.Top;
            Controls.Add(SelectAll);
            Controls.Add(Clear);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int h = Theme.S(40), y = Theme.S(22);
            Clear.Bounds = new Rectangle(Width - Theme.S(28) - Theme.S(76), y, Theme.S(76), h);
            SelectAll.Bounds = new Rectangle(Clear.Left - Theme.S(8) - Theme.S(132), y, Theme.S(132), h);
        }

        public void Set(string heading, string sub)
        {
            Heading = heading;
            Sub = sub;
            Invalidate();
        }

        protected override void Render(Graphics g)
        {
            int x = Theme.S(28), w = Math.Max(10, SelectAll.Left - x - Theme.S(12));
            Theme.Draw(g, Heading, Theme.Font(19f, true), Theme.Text, new Rectangle(x, Theme.S(14), w, Theme.S(36)), Theme.Left);
            Theme.Draw(g, Sub, Theme.Font(11.5f, false), Theme.Muted, new Rectangle(x, Theme.S(50), w, Theme.S(24)), Theme.Left);
        }
    }

    class FooterBar : Canvas
    {
        public string Dest = "";
        public readonly FlatButton Change = new FlatButton("Change");
        public readonly FlatButton Import = new FlatButton("Import") { Kind = ButtonKind.Primary, FontSize = 13f };

        public FooterBar()
        {
            BackColor = Theme.Panel;
            Height = Theme.S(76);
            Dock = DockStyle.Bottom;
            Controls.Add(Change);
            Controls.Add(Import);
        }

        int destWidth;

        protected override void OnLayout(LayoutEventArgs e)
        {
            int iw = Math.Max(Theme.S(190), Theme.Width(Import.Text, Theme.Font(13f, true)) + Theme.S(52));
            Import.Bounds = new Rectangle(Width - Theme.S(28) - iw, (Height - Theme.S(48)) / 2, iw, Theme.S(48));
            int chips = Width > Theme.S(1150) ? Theme.S(390) : 0;
            destWidth = Math.Max(Theme.S(120), Math.Min(Theme.Width(Dest, Theme.Font(11.5f, true)) + Theme.S(8),
                Import.Left - Theme.S(28) - Theme.S(32) - Theme.S(96) - Theme.S(30) - chips));
            Change.Bounds = new Rectangle(Theme.S(28) + Theme.S(32) + destWidth + Theme.S(10), (Height - Theme.S(40)) / 2, Theme.S(86), Theme.S(40));
        }

        public void SetDest(string dest)
        {
            Dest = dest;
            PerformLayout();
            Invalidate();
        }

        public void SetImportText(string text)
        {
            Import.Text = text;
            PerformLayout();
            Invalidate();
        }

        protected override void Render(Graphics g)
        {
            using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, 0, 0, Width, 0);
            int x = Theme.S(28), cy = Height / 2;
            using (var pen = new Pen(Theme.Muted, Math.Max(1.5f, Theme.K * 1.7f)) { LineJoin = LineJoin.Round })
            {
                int fw = Theme.S(20), fh = Theme.S(15), fy = cy - fh / 2;
                g.DrawLines(pen, new[]
                {
                    new Point(x, fy + fh), new Point(x, fy), new Point(x + fw * 2 / 5, fy), new Point(x + fw / 2, fy + Theme.S(3)),
                    new Point(x + fw, fy + Theme.S(3)), new Point(x + fw, fy + fh), new Point(x, fy + fh)
                });
            }
            int tx = x + Theme.S(32);
            Theme.Draw(g, "Save to", Theme.Font(9.5f, false), Theme.Muted, new Rectangle(tx, cy - Theme.S(21), destWidth, Theme.S(18)), Theme.Left);
            Theme.Draw(g, Dest, Theme.Font(11.5f, true), Theme.Text, new Rectangle(tx, cy - Theme.S(3), destWidth, Theme.S(24)), Theme.Left);

            if (Width <= Theme.S(1150)) return;
            int px = Change.Right + Theme.S(16);
            foreach (string label in new[] { "Year and month folders", "Skip already imported" })
            {
                Font f = Theme.Font(11f, false);
                int w = Theme.Width(label, f) + Theme.S(44), h = Theme.S(34);
                if (px + w > Import.Left - Theme.S(12)) break;
                var r = new Rectangle(px, cy - h / 2, w, h);
                Theme.FillRound(g, Theme.Card, r, h / 2);
                Theme.StrokeRound(g, Theme.Line, r, h / 2, 1f);
                Theme.Check(g, new Rectangle(r.X + Theme.S(10), r.Y + (h - Theme.S(16)) / 2, Theme.S(16), Theme.S(16)), Theme.Green, Math.Max(2f, Theme.K * 2.2f));
                Theme.Draw(g, label, f, Theme.Soft, new Rectangle(r.X + Theme.S(32), r.Y, w - Theme.S(36), h), Theme.Left);
                px += w + Theme.S(8);
            }
        }
    }

    class ProgressPanel : Canvas
    {
        public string Heading = "", Line1 = "", Line2 = "", Note = "";
        public float Fraction;
        public Bitmap Thumb;
        public readonly FlatButton Stop = new FlatButton("Stop import") { Kind = ButtonKind.Danger };
        public readonly FlatButton Back = new FlatButton("Back to photos") { Kind = ButtonKind.Primary };
        public readonly FlatButton Open = new FlatButton("Open folder");
        public readonly FlatButton Fix = new FlatButton("Show HEIC thumbnails in Windows") { Visible = false };
        public readonly FlatButton View = new FlatButton("View photos") { Visible = false };

        public ProgressPanel()
        {
            Height = Theme.S(216);
            Dock = DockStyle.Top;
            Controls.Add(Stop);
            Controls.Add(Back);
            Controls.Add(Open);
            Controls.Add(Fix);
            Controls.Add(View);
            SetFinished(false);
        }

        public void SetFinished(bool finished)
        {
            Stop.Visible = !finished;
            Back.Visible = finished;
            Open.Visible = finished;
            if (!finished) Fix.Visible = View.Visible = false;
            PerformLayout();
            Invalidate();
        }

        Rectangle Card { get { return new Rectangle(Theme.S(28), Theme.S(20), Width - Theme.S(56), Height - Theme.S(28)); } }
        int TextLeft { get { return Card.X + Theme.S(24) + Theme.S(132) + Theme.S(24); } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int y = Card.Bottom - Theme.S(24) - Theme.S(40), x = TextLeft;
            Stop.Bounds = new Rectangle(x, y, Theme.S(128), Theme.S(40));
            Back.Bounds = new Rectangle(x, y, Theme.S(150), Theme.S(40));
            View.Bounds = new Rectangle(x + Theme.S(160), y, Theme.S(120), Theme.S(40));
            Open.Bounds = new Rectangle((View.Visible ? View.Right : Back.Right) + Theme.S(10), y, Theme.S(120), Theme.S(40));
            Fix.Bounds = new Rectangle(Open.Right + Theme.S(10), y, Theme.S(270), Theme.S(40));
        }

        protected override void Render(Graphics g)
        {
            Rectangle card = Card;
            Theme.FillRound(g, Theme.Card, card, Theme.S(16));
            Theme.StrokeRound(g, Theme.Line, card, Theme.S(16), 1f);
            foreach (Control c in Controls) c.BackColor = Theme.Card;

            int t = Theme.S(132);
            var tr = new Rectangle(card.X + Theme.S(24), card.Y + (card.Height - t) / 2, t, t);
            if (Thumb != null)
            {
                using (GraphicsPath p = Theme.Round(tr, Theme.S(12)))
                {
                    GraphicsState st = g.Save();
                    g.SetClip(p, CombineMode.Intersect);
                    g.DrawImage(Thumb, tr);
                    g.Restore(st);
                }
            }
            else
            {
                Theme.FillRound(g, Theme.Tints[6], tr, Theme.S(12));
                Theme.PictureIcon(g, tr);
            }

            int x = TextLeft, w = Math.Max(10, card.Right - Theme.S(24) - x), y = card.Y + Theme.S(18);
            string pct = Math.Round(Fraction * 100) + "%";
            Font pf = Theme.Font(16f, true);
            int pw = Theme.Width(pct, pf) + 4;
            Theme.Draw(g, Heading, Theme.Font(20f, true), Theme.Text, new Rectangle(x, y, w - pw - Theme.S(8), Theme.S(38)), Theme.Left);
            Theme.Draw(g, pct, pf, Theme.AccentText, new Rectangle(x + w - pw, y, pw, Theme.S(38)), Theme.Right);
            var track = new Rectangle(x, y + Theme.S(48), w, Theme.S(10));
            Theme.FillRound(g, Theme.Line, track, Theme.S(5));
            int fill = (int)(w * Math.Max(0f, Math.Min(1f, Fraction)));
            if (fill >= Theme.S(10)) Theme.FillRound(g, Theme.AccentBright, new Rectangle(x, track.Y, fill, track.Height), Theme.S(5));
            int l2 = Theme.Width(Line2, Theme.Font(11.5f, false)) + 4;
            Theme.Draw(g, Line1, Theme.Font(11.5f, false), Theme.Muted, new Rectangle(x, track.Bottom + Theme.S(8), w - l2 - Theme.S(10), Theme.S(24)), Theme.Left);
            Theme.Draw(g, Line2, Theme.Font(11.5f, false), Theme.Muted, new Rectangle(x + w - l2, track.Bottom + Theme.S(8), l2, Theme.S(24)), Theme.Right);
            Control last = Stop.Visible ? (Control)Stop : Fix.Visible ? (Control)Fix : Open;
            Theme.Draw(g, Note, Theme.Font(11.5f, false), Theme.Muted, new Rectangle(last.Right + Theme.S(14), last.Top, Math.Max(10, card.Right - Theme.S(24) - last.Right - Theme.S(14)), last.Height), Theme.Left);
        }
    }

    class ConnectView : Canvas
    {
        public int Stage;
        public string Message = "Your photos appear here as soon as the phone allows access.";
        public readonly FlatButton Retry = new FlatButton("Check again") { FontSize = 12f };

        public ConnectView()
        {
            Dock = DockStyle.Fill;
            Controls.Add(Retry);
        }

        public void Set(int stage, string message)
        {
            Stage = stage;
            if (message != null) Message = message;
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int w = Theme.S(160), top = Math.Max(Theme.S(16), (Height - Theme.S(560)) / 2);
            Retry.Bounds = new Rectangle((Width - w) / 2, top + Theme.S(500), w, Theme.S(46));
        }

        protected override void Render(Graphics g)
        {
            int cw = Math.Min(Theme.S(560), Width - Theme.S(48)), x = (Width - cw) / 2;
            int y = Math.Max(Theme.S(16), (Height - Theme.S(560)) / 2);
            int box = Theme.S(96);
            var ir = new Rectangle((Width - box) / 2, y, box, box);
            Theme.FillRound(g, Theme.Card, ir, Theme.S(24));
            Theme.StrokeRound(g, Theme.Line, ir, Theme.S(24), 1f);
            var phone = new Rectangle(ir.X + box * 3 / 10, ir.Y + box / 5, box * 2 / 5, box * 3 / 5);
            Theme.StrokeRound(g, Theme.AccentText, phone, Theme.S(8), Math.Max(2f, Theme.K * 2.2f));
            using (var pen = new Pen(Theme.AccentText, Math.Max(2f, Theme.K * 2.2f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, phone.X + phone.Width * 0.38f, phone.Bottom - Theme.S(9), phone.X + phone.Width * 0.62f, phone.Bottom - Theme.S(9));

            y = ir.Bottom + Theme.S(24);
            Theme.Draw(g, "Connect your iPhone", Theme.Font(23f, true), Theme.Text, new Rectangle(x, y, cw, Theme.S(44)), Theme.Center);
            Theme.Draw(g, Message, Theme.Font(12.5f, false), Theme.Muted, new Rectangle(x, y + Theme.S(46), cw, Theme.S(28)), Theme.Center | TextFormatFlags.EndEllipsis);

            y += Theme.S(100);
            string[] steps = { "Plug it in with a USB cable", "Unlock the phone", "Tap Allow on the phone's screen" };
            for (int i = 0; i < 3; i++)
            {
                bool done = i < Stage, waiting = i == Stage;
                var r = new Rectangle(x, y, cw - 1, Theme.S(58));
                Theme.FillRound(g, Theme.Card, r, Theme.S(12));
                Theme.StrokeRound(g, waiting ? Theme.AccentText : Theme.Line, r, Theme.S(12), 1f);
                var num = new Rectangle(r.X + Theme.S(16), r.Y + (r.Height - Theme.S(30)) / 2, Theme.S(30), Theme.S(30));
                using (var b = new SolidBrush(done ? Theme.GreenDeep : waiting ? Theme.Accent : Theme.Line)) g.FillEllipse(b, num);
                Theme.Draw(g, (i + 1).ToString(), Theme.Font(11.5f, true), done || waiting ? Color.White : Theme.Soft, num, Theme.Center);
                string tag = done ? "Done" : waiting ? "Waiting" : "";
                int tw = Theme.Width(tag, Theme.Font(11.5f, true)) + 4;
                Theme.Draw(g, steps[i], Theme.Font(12.5f, false), done || waiting ? Theme.Text : Theme.Soft,
                    new Rectangle(num.Right + Theme.S(14), r.Y, r.Width - Theme.S(90) - tw, r.Height), Theme.Left);
                if (tag.Length > 0)
                    Theme.Draw(g, tag, Theme.Font(11.5f, true), done ? Theme.Green : Theme.AccentText, new Rectangle(r.Right - Theme.S(16) - tw, r.Y, tw, r.Height), Theme.Right);
                y += Theme.S(68);
            }
        }
    }
}
