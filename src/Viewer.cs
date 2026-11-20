using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PhotoImport
{
    static class Pictures
    {
        static readonly string[] Extensions = { ".heic", ".heif", ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff" };

        public static bool Supported(string path)
        {
            return Array.IndexOf(Extensions, Path.GetExtension(path ?? "").ToLowerInvariant()) >= 0;
        }

        // maxSize 0 = full size. The result never keeps the file open.
        public static Bitmap Load(string path, int maxSize)
        {
            if (Heif.IsHeic(path)) return Heif.DecodeFile(path, maxSize);
            using (var ms = new MemoryStream(File.ReadAllBytes(path)))
            using (Image img = Image.FromStream(ms))
            {
                try
                {
                    if (Array.IndexOf(img.PropertyIdList, 0x0112) >= 0)
                    {
                        RotateFlipType turn = Orientation.FromExif(img.GetPropertyItem(0x0112).Value[0]);
                        if (turn != RotateFlipType.RotateNoneFlipNone) img.RotateFlip(turn);
                    }
                }
                catch { }
                return Heif.Fit(img, maxSize > 0 ? maxSize : Math.Max(img.Width, img.Height));
            }
        }
    }

    class ImageView : Canvas
    {
        public event EventHandler Previous, Next, ZoomChanged;
        public string Message = "";
        Bitmap image, fitted;
        bool preview;
        float zoom;
        PointF pan;
        Point dragFrom;
        PointF panFrom;
        bool dragging;
        int hoverArrow;

        public ImageView()
        {
            BackColor = Color.FromArgb(9, 10, 13);
            Dock = DockStyle.Fill;
        }

        public bool Fitted { get { return zoom == 0; } }

        public void Display(Bitmap bmp, bool isPreview, bool keepView)
        {
            image = bmp;
            preview = isPreview;
            if (!keepView)
            {
                zoom = 0;
                pan = PointF.Empty;
            }
            DropFitted();
            Invalidate();
            if (ZoomChanged != null) ZoomChanged(this, EventArgs.Empty);
        }

        void DropFitted()
        {
            if (fitted != null) fitted.Dispose();
            fitted = null;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            DropFitted();
        }

        float FitScale()
        {
            if (image == null) return 1f;
            float s = Math.Min((float)Width / image.Width, (float)(Height - Theme.S(16)) / image.Height);
            return preview ? s : Math.Min(1f, s);
        }

        public void ToggleZoom()
        {
            if (image == null || preview) return;
            zoom = zoom == 0 ? 1f : 0;
            pan = PointF.Empty;
            Invalidate();
            if (ZoomChanged != null) ZoomChanged(this, EventArgs.Empty);
        }

        public void ZoomBy(float factor, Point around)
        {
            if (image == null || preview) return;
            float fit = FitScale(), old = zoom == 0 ? fit : zoom, next = Math.Max(fit, Math.Min(8f, old * factor));
            if (Math.Abs(next - fit) < 0.0005f)
            {
                zoom = 0;
                pan = PointF.Empty;
            }
            else
            {
                float cx = Width / 2f + pan.X, cy = Height / 2f + pan.Y;
                pan = new PointF(around.X - (around.X - cx) * next / old - Width / 2f, around.Y - (around.Y - cy) * next / old - Height / 2f);
                zoom = next;
            }
            Clamp();
            Invalidate();
            if (ZoomChanged != null) ZoomChanged(this, EventArgs.Empty);
        }

        void Clamp()
        {
            if (image == null || zoom == 0) return;
            float mx = Math.Max(0, (image.Width * zoom - Width) / 2f), my = Math.Max(0, (image.Height * zoom - Height) / 2f);
            pan = new PointF(Math.Max(-mx, Math.Min(mx, pan.X)), Math.Max(-my, Math.Min(my, pan.Y)));
        }

        Rectangle Arrow(int side)
        {
            int s = Theme.S(48);
            return new Rectangle(side < 0 ? Theme.S(18) : Width - Theme.S(18) - s, (Height - s) / 2, s, s);
        }

        protected override void Render(Graphics g)
        {
            if (image == null)
            {
                Theme.Draw(g, Message, Theme.Font(13f, false), Theme.Muted, ClientRectangle, Theme.Center);
            }
            else if (zoom == 0)
            {
                float s = FitScale();
                int w = Math.Max(1, (int)(image.Width * s)), h = Math.Max(1, (int)(image.Height * s));
                var dest = new Rectangle((Width - w) / 2, (Height - h) / 2, w, h);
                if (preview || s >= 1f)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(image, dest);
                }
                else
                {
                    if (fitted == null || fitted.Width != w || fitted.Height != h)
                    {
                        DropFitted();
                        fitted = Heif.Fit(image, Math.Max(w, h));
                    }
                    g.DrawImageUnscaled(fitted, (Width - fitted.Width) / 2, (Height - fitted.Height) / 2);
                }
            }
            else
            {
                float w = image.Width * zoom, h = image.Height * zoom;
                g.InterpolationMode = zoom >= 2f ? InterpolationMode.NearestNeighbor : InterpolationMode.Bilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(image, new RectangleF(Width / 2f + pan.X - w / 2f, Height / 2f + pan.Y - h / 2f, w, h));
            }

            for (int side = -1; side <= 1; side += 2)
            {
                Rectangle r = Arrow(side);
                using (var b = new SolidBrush(Color.FromArgb(hoverArrow == side ? 230 : 130, 23, 26, 33))) g.FillEllipse(b, r);
                using (var pen = new Pen(Color.FromArgb(hoverArrow == side ? 255 : 190, 255, 255, 255), Math.Max(2f, Theme.K * 2.4f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    float cx = r.X + r.Width / 2f + side * r.Width * 0.06f, cy = r.Y + r.Height / 2f, q = r.Width * 0.16f;
                    g.DrawLines(pen, new[] { new PointF(cx - side * q, cy - q * 1.3f), new PointF(cx + side * q * 0.4f, cy), new PointF(cx - side * q, cy + q * 1.3f) });
                }
            }
        }

        int ArrowAt(Point p)
        {
            return Arrow(-1).Contains(p) ? -1 : Arrow(1).Contains(p) ? 1 : 0;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            int side = ArrowAt(e.Location);
            if (side < 0 && Previous != null) Previous(this, EventArgs.Empty);
            else if (side > 0 && Next != null) Next(this, EventArgs.Empty);
            else if (zoom != 0)
            {
                dragging = true;
                dragFrom = e.Location;
                panFrom = pan;
                Cursor = Cursors.SizeAll;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging)
            {
                pan = new PointF(panFrom.X + e.X - dragFrom.X, panFrom.Y + e.Y - dragFrom.Y);
                Clamp();
                Invalidate();
                return;
            }
            int side = ArrowAt(e.Location);
            if (side != hoverArrow)
            {
                hoverArrow = side;
                Invalidate();
            }
            Cursor = side != 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false;
            Cursor = Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverArrow = 0;
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (ArrowAt(e.Location) == 0) ToggleZoom();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ZoomBy(e.Delta > 0 ? 1.25f : 0.8f, e.Location);
        }

        protected override void OnMouseEnter(EventArgs e) { Focus(); }
    }

    class Filmstrip : Canvas
    {
        public event Action<int> Picked;
        public event Action<int> ThumbNeeded;
        public int Count, Current;
        public readonly Dictionary<int, Bitmap> Thumbs = new Dictionary<int, Bitmap>();
        public readonly HashSet<int> Asked = new HashSet<int>();
        float scroll;

        public Filmstrip()
        {
            BackColor = Theme.Panel;
            Height = Theme.S(96);
            Dock = DockStyle.Bottom;
        }

        int Tile { get { return Theme.S(68); } }
        int Step { get { return Tile + Theme.S(8); } }

        public void Center()
        {
            scroll = Current * Step + Tile / 2f - Width / 2f;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Center();
        }

        Rectangle Slot(int i)
        {
            return new Rectangle((int)(i * Step - scroll), (Height - Tile) / 2, Tile, Tile);
        }

        protected override void Render(Graphics g)
        {
            using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, 0, 0, Width, 0);
            int first = Math.Max(0, (int)(scroll / Step) - 1), last = Math.Min(Count - 1, (int)((scroll + Width) / Step) + 1);
            for (int i = first; i <= last; i++)
            {
                Rectangle r = Slot(i);
                Bitmap t;
                if (Thumbs.TryGetValue(i, out t))
                {
                    using (GraphicsPath p = Theme.Round(r, Theme.S(8)))
                    {
                        GraphicsState st = g.Save();
                        g.SetClip(p, CombineMode.Intersect);
                        int side = Math.Min(t.Width, t.Height);
                        g.DrawImage(t, r, new Rectangle((t.Width - side) / 2, (t.Height - side) / 2, side, side), GraphicsUnit.Pixel);
                        g.Restore(st);
                    }
                }
                else
                {
                    Theme.FillRound(g, Theme.Card, r, Theme.S(8));
                    if (Asked.Add(i) && ThumbNeeded != null) ThumbNeeded(i);
                }
                if (i == Current) Theme.StrokeRound(g, Theme.AccentText, new Rectangle(r.X - 2, r.Y - 2, r.Width + 3, r.Height + 3), Theme.S(9), Theme.S(3));
                else Theme.FillRound(g, Color.FromArgb(70, Theme.Panel), new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), Theme.S(8));
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = (int)((e.X + scroll) / Step);
            if (i >= 0 && i < Count && Slot(i).Contains(e.Location) && Picked != null) Picked(i);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            float max = Math.Max(0, Count * Step - Width / 2f);
            scroll = Math.Max(-Width / 2f, Math.Min(max, scroll - e.Delta / 120f * Step * 3));
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { Focus(); }
    }

    class ViewerBar : Canvas
    {
        public string Title = "", Sub = "";
        public readonly FlatButton Folder = new FlatButton("Show in folder");
        public readonly FlatButton Zoom = new FlatButton("Actual size");
        public readonly FlatButton Full = new FlatButton("Full screen");

        public ViewerBar()
        {
            BackColor = Theme.Panel;
            Height = Theme.S(62);
            Dock = DockStyle.Top;
            Controls.Add(Folder);
            Controls.Add(Zoom);
            Controls.Add(Full);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int h = Theme.S(38), y = (Height - h) / 2, x = Width - Theme.S(20);
            foreach (FlatButton b in new[] { Full, Zoom, Folder })
            {
                int w = Theme.Width(b.Text, Theme.Font(b.FontSize, false)) + Theme.S(30);
                x -= w;
                b.Bounds = new Rectangle(x, y, w, h);
                x -= Theme.S(8);
            }
        }

        public void Set(string title, string sub)
        {
            Title = title;
            Sub = sub;
            Invalidate();
        }

        protected override void Render(Graphics g)
        {
            using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            int x = Theme.S(22), w = Math.Max(10, Folder.Left - x - Theme.S(16));
            Theme.Draw(g, Title, Theme.Font(13f, true), Theme.Text, new Rectangle(x, Theme.S(9), w, Theme.S(24)), Theme.Left);
            Theme.Draw(g, Sub, Theme.Font(10.5f, false), Theme.Muted, new Rectangle(x, Theme.S(33), w, Theme.S(20)), Theme.Left);
        }
    }

    public class ViewerForm : Form
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        readonly List<string> files = new List<string>();
        readonly ViewerBar bar = new ViewerBar();
        readonly ImageView view = new ImageView();
        readonly Filmstrip strip = new Filmstrip();
        readonly Dictionary<int, Bitmap> cache = new Dictionary<int, Bitmap>();
        readonly object gate = new object();
        readonly List<int> thumbQueue = new List<int>();
        int index, wanted = -1, direction = 1;
        volatile int generation;
        volatile bool closing;
        bool fullScreen;
        FormWindowState stateBefore;

        public ViewerForm(string path)
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Theme.K = Math.Max(1f, g.DpiX / 96f);
            Theme.LoadFonts();
            Text = "Photo Viewer";
            BackColor = Theme.Bg;
            AutoScaleMode = AutoScaleMode.None;
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(Theme.S(1280), area.Width - 40), Math.Min(Theme.S(820), area.Height - 60));
            MinimumSize = new Size(Math.Min(Theme.S(640), area.Width), Math.Min(Theme.S(480), area.Height));
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Controls.Add(view);
            Controls.Add(strip);
            Controls.Add(bar);

            ListFolder(path);
            strip.Count = files.Count;
            view.Previous += delegate { Go(index - 1); };
            view.Next += delegate { Go(index + 1); };
            view.ZoomChanged += delegate { bar.Zoom.Text = view.Fitted ? "Actual size" : "Fit to window"; bar.PerformLayout(); };
            strip.Picked += Go;
            strip.ThumbNeeded += delegate(int i) { lock (gate) { thumbQueue.Add(i); Monitor.PulseAll(gate); } };
            bar.Folder.Click += delegate { ShowInFolder(); };
            bar.Zoom.Click += delegate { view.ToggleZoom(); };
            bar.Full.Click += delegate { ToggleFullScreen(); };

            Shown += delegate
            {
                new Thread(MainLoader) { IsBackground = true }.Start();
                new Thread(ThumbLoader) { IsBackground = true }.Start();
                if (files.Count == 0)
                {
                    view.Message = "This file type cannot be shown.";
                    bar.Set(Path.GetFileName(path), "");
                    view.Invalidate();
                }
                else Go(index);
            };
            FormClosing += delegate
            {
                closing = true;
                lock (gate) Monitor.PulseAll(gate);
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int on = 1;
                DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));
            }
            catch { }
        }

        void ListFolder(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                string dir = Directory.Exists(full) ? full : Path.GetDirectoryName(full);
                foreach (string f in Directory.GetFiles(dir))
                    if (Pictures.Supported(f)) files.Add(f);
                files.Sort(StringComparer.OrdinalIgnoreCase);
                index = Math.Max(0, files.FindIndex(delegate(string f) { return string.Equals(f, full, StringComparison.OrdinalIgnoreCase); }));
            }
            catch { }
        }

        void Go(int i)
        {
            if (files.Count == 0) return;
            i = Math.Max(0, Math.Min(files.Count - 1, i));
            direction = i >= index ? 1 : -1;
            index = i;
            strip.Current = i;
            strip.Center();
            string file = files[i];
            string when = "";
            try { when = "  ·  " + File.GetLastWriteTime(file).ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture); } catch { }
            bar.Set(Path.GetFileName(file), (i + 1).ToString("N0") + " of " + files.Count.ToString("N0") + when);
            Text = Path.GetFileName(file) + " - Photo Viewer";

            Bitmap ready;
            if (cache.TryGetValue(i, out ready)) view.Display(ready, false, false);
            else
            {
                Bitmap small;
                if (strip.Thumbs.TryGetValue(i, out small)) view.Display(small, true, false);
                else
                {
                    view.Message = "Opening…";
                    view.Display(null, false, false);
                }
            }
            lock (gate)
            {
                generation++;
                wanted = i;
                Monitor.PulseAll(gate);
            }
        }

        void Ui(MethodInvoker action)
        {
            try { if (!closing && IsHandleCreated && !IsDisposed) BeginInvoke(action); } catch { }
        }

        // Decodes the current picture, then the next one in the direction of travel so it opens instantly
        void MainLoader()
        {
            while (!closing)
            {
                int target, gen;
                lock (gate)
                {
                    if (wanted < 0)
                    {
                        Monitor.Wait(gate, 1000);
                        continue;
                    }
                    target = wanted;
                    gen = generation;
                    wanted = -1;
                }
                LoadInto(target, gen, true);
                int ahead = target + direction;
                if (gen == generation && ahead >= 0 && ahead < files.Count) LoadInto(ahead, gen, false);
            }
        }

        void LoadInto(int i, int gen, bool show)
        {
            bool have = false;
            lock (cache) have = cache.ContainsKey(i);
            if (have) return;
            Bitmap bmp = null;
            string error = null;
            try { bmp = Pictures.Load(files[i], 0); }
            catch (Exception ex) { error = ex.Message; }
            Ui(delegate
            {
                if (bmp != null)
                {
                    lock (cache)
                    {
                        if (cache.ContainsKey(i))
                        {
                            bmp.Dispose();
                            return;
                        }
                        cache[i] = bmp;
                        foreach (int key in new List<int>(cache.Keys))
                        {
                            if (Math.Abs(key - index) <= 1) continue;
                            cache[key].Dispose();
                            cache.Remove(key);
                        }
                    }
                }
                if (!show || i != index) return;
                if (bmp != null) view.Display(bmp, false, false);
                else
                {
                    view.Message = "Cannot open this picture: " + error;
                    view.Display(null, false, false);
                }
            });
        }

        void ThumbLoader()
        {
            while (!closing)
            {
                int i;
                lock (gate)
                {
                    if (thumbQueue.Count == 0)
                    {
                        Monitor.Wait(gate, 1000);
                        continue;
                    }
                    i = thumbQueue[thumbQueue.Count - 1];
                    thumbQueue.RemoveAt(thumbQueue.Count - 1);
                }
                Bitmap t = null;
                try { t = Pictures.Load(files[i], Math.Max(160, Theme.S(140))); } catch { }
                if (t == null) continue;
                Ui(delegate
                {
                    strip.Thumbs[i] = t;
                    strip.Invalidate();
                });
            }
        }

        void ShowInFolder()
        {
            try { if (files.Count > 0) Process.Start("explorer.exe", "/select,\"" + files[index] + "\""); } catch { }
        }

        void ToggleFullScreen()
        {
            fullScreen = !fullScreen;
            if (fullScreen)
            {
                stateBefore = WindowState;
                WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Maximized;
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                WindowState = stateBefore;
            }
            bar.Full.Text = fullScreen ? "Exit full screen" : "Full screen";
            bar.PerformLayout();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Left: case Keys.Up: case Keys.PageUp: Go(index - 1); return true;
                case Keys.Right: case Keys.Down: case Keys.PageDown: case Keys.Space: Go(index + 1); return true;
                case Keys.Home: Go(0); return true;
                case Keys.End: Go(files.Count - 1); return true;
                case Keys.F11: case Keys.F: ToggleFullScreen(); return true;
                case Keys.Add: case Keys.Oemplus: view.ZoomBy(1.25f, new Point(view.Width / 2, view.Height / 2)); return true;
                case Keys.Subtract: case Keys.OemMinus: view.ZoomBy(0.8f, new Point(view.Width / 2, view.Height / 2)); return true;
                case Keys.D0: case Keys.NumPad0: if (!view.Fitted) view.ToggleZoom(); return true;
                case Keys.Escape:
                    if (fullScreen) ToggleFullScreen(); else Close();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
