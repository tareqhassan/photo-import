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
using Microsoft.Win32;

namespace PhotoImport
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        // Start-up failures are shown instead of the window silently never appearing
        [STAThread]
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Report(e.ExceptionObject); };
            try { Start(args); }
            catch (Exception ex) { Report(ex); }
        }

        static void Report(object error)
        {
            string text = Convert.ToString(error);
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "PhotoImport-error.txt"), text); } catch { }
            MessageBox.Show(text.Length > 1500 ? text.Substring(0, 1500) : text, "Apple Photo Importer could not start", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void Start(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                MessageBox.Show(e.Exception.Message, "Apple Photo Importer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            foreach (string a in args)
            {
                if (a.StartsWith("--", StringComparison.Ordinal) || !(File.Exists(a) || Directory.Exists(a))) continue;
                Application.Run(new ViewerForm(a));
                return;
            }
            bool demo = Array.IndexOf(args, "--demo") >= 0;
            IDeviceProvider provider = demo ? (IDeviceProvider)new DemoProvider(Array.IndexOf(args, "--demo-empty") >= 0) : new WpdProvider();
            Application.Run(new MainForm(provider, Array.IndexOf(args, "--demo-import") >= 0));
        }
    }

    public class MainForm : Form
    {
        enum Mode { Connect, Browse, Importing, Finished }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        const int ThumbCap = 900;
        const int ThumbQueueCap = 300;

        readonly IDeviceProvider provider;
        readonly bool autoImport;
        readonly HeaderBar header;
        readonly Sidebar sidebar;
        readonly TitleBar title;
        readonly Grid grid;
        readonly FooterBar footer;
        readonly ProgressPanel progress;
        readonly ConnectView connect;
        readonly Panel content;
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 2500 };
        readonly string settingsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iPhonePhotoImport", "settings.txt");

        readonly List<Item> items = new List<Item>();
        readonly LinkedList<Item> thumbOrder = new LinkedList<Item>();
        List<Item> importSet = new List<Item>();
        Library library;
        string dest;
        Mode mode = Mode.Connect;
        bool enumerating, lostDuringImport;
        string deviceName = "";
        Item current;

        readonly object gate = new object();
        readonly LinkedList<Action> jobs = new LinkedList<Action>();
        readonly List<Item> thumbQueue = new List<Item>();
        Thread worker;
        IDevice device;
        string deviceId;
        volatile bool closing, cancelImport, cancelEnum, connectQueued;
        volatile int generation;

        public MainForm(IDeviceProvider provider, bool autoImport)
        {
            this.provider = provider;
            this.autoImport = autoImport;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Theme.K = Math.Max(1f, g.DpiX / 96f);
            WpdDevice.Log = Note;
            Theme.LoadFonts();

            Text = "Apple Photo Importer";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            AutoScaleMode = AutoScaleMode.None;
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(Theme.S(1280), area.Width - 40), Math.Min(Theme.S(820), area.Height - 60));
            MinimumSize = new Size(Math.Min(Theme.S(900), area.Width), Math.Min(Theme.S(620), area.Height));
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            dest = LoadDest();
            library = OpenLibrary(dest);
            try { File.Delete(LogPath()); } catch { }

            header = new HeaderBar();
            sidebar = new Sidebar();
            footer = new FooterBar();
            title = new TitleBar();
            progress = new ProgressPanel { Visible = false };
            grid = new Grid();
            connect = new ConnectView();
            content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            content.Controls.Add(grid);
            content.Controls.Add(connect);
            content.Controls.Add(title);
            content.Controls.Add(progress);
            Controls.Add(content);
            Controls.Add(sidebar);
            Controls.Add(footer);
            Controls.Add(header);
            Controls.Add(new CreditsBar());

            footer.SetDest(dest);
            header.Refresh.Click += delegate { Reconnect("Looking for a phone…"); };
            connect.Retry.Click += delegate { Reconnect("Looking for a phone…"); };
            sidebar.Changed += delegate { Rebuild(false); };
            grid.SelectionChanged += delegate { UpdateTexts(); };
            grid.ThumbNeeded += RequestThumb;
            title.SelectAll.Click += delegate { SelectVisible(); };
            title.Clear.Click += delegate
            {
                foreach (Item it in items) it.Selected = false;
                grid.Invalidate();
                UpdateTexts();
            };
            footer.Change.Click += delegate { ChangeDest(); };
            footer.Import.Click += delegate { StartImport(); };
            progress.Stop.Click += delegate
            {
                cancelImport = true;
                progress.Line1 = "Stopping after the current file…";
                progress.Invalidate();
            };
            progress.Back.Click += delegate { LeaveImport(); };
            progress.Open.Click += delegate { OpenDest(); };
            progress.Fix.Click += delegate { FixHeic(); };
            progress.View.Click += delegate { OpenViewer(); };
            poll.Tick += delegate { OnPoll(); };

            SetMode(Mode.Connect);
            Shown += delegate
            {
                worker = new Thread(WorkerLoop) { IsBackground = true };
                worker.SetApartmentState(ApartmentState.MTA);
                worker.Start();
                QueueConnect();
                poll.Start();
            };
            FormClosing += OnClosing;
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

        // ---------- settings and destination ----------

        string LoadDest()
        {
            try
            {
                if (File.Exists(settingsFile))
                {
                    string saved = File.ReadAllText(settingsFile).Trim();
                    if (saved.Length > 0) return saved;
                }
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "iPhone Import");
        }

        static Library OpenLibrary(string root)
        {
            try { return new Library(root); }
            catch { return new Library(Path.Combine(Path.GetTempPath(), "photo-import-unavailable")); }
        }

        void ChangeDest()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Choose where imported photos are saved", ShowNewFolderButton = true })
            {
                if (Directory.Exists(dest)) dlg.SelectedPath = dest;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                dest = dlg.SelectedPath;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsFile));
                File.WriteAllText(settingsFile, dest);
            }
            catch { }
            library = OpenLibrary(dest);
            footer.SetDest(dest);
            foreach (Item it in items)
            {
                bool had = library.Has(it);
                it.State = had ? ItemState.Imported : ItemState.New;
                it.Selected = !had;
            }
            Rebuild(false);
        }

        // Opens the built-in viewer on the most recently imported picture
        void OpenViewer()
        {
            try
            {
                string target = lastImported != null && File.Exists(lastImported) ? lastImported : dest;
                Process.Start(Application.ExecutablePath, "\"" + target + "\"");
            }
            catch { }
        }

        volatile string lastImported;

        void OpenDest()
        {
            try
            {
                Directory.CreateDirectory(dest);
                Process.Start("explorer.exe", "\"" + dest + "\"");
            }
            catch { }
        }

        // ---------- worker thread: every call to the phone happens here ----------

        void WorkerLoop()
        {
            while (!closing)
            {
                Action job = null;
                Item thumb = null;
                lock (gate)
                {
                    if (jobs.Count > 0)
                    {
                        job = jobs.First.Value;
                        jobs.RemoveFirst();
                    }
                    else if (thumbQueue.Count > 0)
                    {
                        thumb = thumbQueue[thumbQueue.Count - 1];
                        thumbQueue.RemoveAt(thumbQueue.Count - 1);
                    }
                    else
                    {
                        Monitor.Wait(gate, 500);
                        continue;
                    }
                }
                try
                {
                    if (job != null) job();
                    else LoadThumb(thumb);
                }
                catch { }
            }
            CloseDevice();
        }

        void Enqueue(Action job)
        {
            lock (gate)
            {
                jobs.AddLast(job);
                Monitor.Pulse(gate);
            }
        }

        void Ui(MethodInvoker action)
        {
            try { if (!closing && IsHandleCreated && !IsDisposed) BeginInvoke(action); } catch { }
        }

        void CloseDevice()
        {
            IDevice d = device;
            device = null;
            deviceId = null;
            try { if (d != null) d.Dispose(); } catch { }
        }

        void QueueConnect()
        {
            if (connectQueued) return;
            connectQueued = true;
            Enqueue(ConnectJob);
        }

        // Finds the phone, opens it and streams its photo list to the window in batches
        void ConnectJob()
        {
            int gen = generation;
            try
            {
                CloseDevice();
                DeviceRef pick = null;
                foreach (DeviceRef d in provider.List())
                {
                    string n = (d.Name ?? "").ToLowerInvariant();
                    bool apple = n.Contains("iphone") || n.Contains("ipad") || n.Contains("apple");
                    if (pick == null || apple) pick = d;
                    if (apple) break;
                }
                if (pick == null)
                {
                    Ui(delegate { ShowConnect(gen, 0, null); });
                    return;
                }
                Note("---- connecting to " + pick.Name + " ----");
                device = provider.Open(pick);
                deviceId = pick.Id;
                listing = true;
                string name = pick.Name;
                var batch = new List<Item>();
                var clock = Stopwatch.StartNew();
                cancelEnum = false;
                Action flush = delegate
                {
                    List<Item> chunk = batch;
                    batch = new List<Item>();
                    clock.Restart();
                    Ui(delegate { AddItems(gen, name, chunk); });
                };
                device.Enumerate(delegate(Item it)
                {
                    batch.Add(it);
                    if (batch.Count >= 80 || clock.ElapsedMilliseconds > 300) flush();
                    ServiceThumbs(2);
                }, delegate { return cancelEnum || closing || gen != generation; });
                flush();
                listing = false;
                var wpd = device as WpdDevice;
                int problems = wpd == null ? 0 : wpd.Problems;
                Note("listing finished, problems=" + problems);
                Ui(delegate { EnumerationDone(gen, name, problems); });
            }
            catch (PhoneLockedException)
            {
                CloseDevice();
                Ui(delegate { ShowConnect(gen, 1, "Phone found. Unlock it and tap Allow on its screen."); });
            }
            catch (Exception ex)
            {
                CloseDevice();
                Note("connect error: " + ex);
                string message = "Could not read the phone: " + ex.Message;
                Ui(delegate { ShowConnect(gen, 0, message); });
            }
            finally
            {
                listing = false;
                connectQueued = false;
            }
        }

        void ServiceThumbs(int max)
        {
            for (int i = 0; i < max; i++)
            {
                Item it;
                lock (gate)
                {
                    if (thumbQueue.Count == 0) return;
                    it = thumbQueue[thumbQueue.Count - 1];
                    thumbQueue.RemoveAt(thumbQueue.Count - 1);
                }
                LoadThumb(it);
            }
        }

        void LoadThumb(Item it)
        {
            IDevice d = device;
            if (d == null || it == null) return;
            int gen = generation;
            if (it.Thumb != null) return;
            byte[] data = null;
            try { data = d.Thumbnail(it); } catch (Exception ex) { Note("thumbnail error " + it.Name + ": " + ex.Message); }
            Size dims = Size.Empty;
            if (data != null && !it.IsVideo)
            {
                try { dims = d.Dimensions(it); } catch (Exception ex) { Note("size error " + it.Name + ": " + ex.Message); }
            }
            Bitmap bmp = data == null ? null : MakeThumb(data, Math.Min(240, Theme.S(170)), dims, it.Name);
            if (bmp == null) return;
            Ui(delegate
            {
                if (gen != generation || it.Thumb != null)
                {
                    bmp.Dispose();
                    return;
                }
                it.Thumb = bmp;
                thumbOrder.AddLast(it);
                while (thumbOrder.Count > ThumbCap)
                {
                    Item old = thumbOrder.First.Value;
                    thumbOrder.RemoveFirst();
                    if (old.Thumb == null || old == current) continue;
                    old.Thumb.Dispose();
                    old.Thumb = null;
                    old.ThumbAsked = false;
                    old.TurnPending = false;
                    old.TurnAsked = false;
                }
                if (it == current)
                {
                    progress.Thumb = bmp;
                    progress.Invalidate();
                }
                grid.Invalidate();
            });
        }

        int turnNotes;
        volatile bool listing;

        string LogPath()
        {
            string dir = dest;
            try { Directory.CreateDirectory(dir); } catch { dir = Path.GetTempPath(); }
            return Path.Combine(dir, "PhotoImport-log.txt");
        }

        readonly object logGate = new object();
        int logLines;

        void Note(string line)
        {
            lock (logGate)
            {
                if (logLines++ > 4000) return;
                try
                {
                    File.AppendAllText(LogPath(),
                        DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + line + Environment.NewLine);
                }
                catch { }
            }
        }

        // The phone sends previews unrotated: turn a landscape preview when the photo itself is reported as portrait
        Bitmap MakeThumb(byte[] data, int px, Size dims, string name)
        {
            try
            {
                using (var ms = new MemoryStream(data))
                using (Image img = Image.FromStream(ms))
                {
                    try
                    {
                        RotateFlipType turn = RotateFlipType.RotateNoneFlipNone;
                        bool tagged = Array.IndexOf(img.PropertyIdList, 0x0112) >= 0;
                        if (tagged) turn = Orientation.FromExif(img.GetPropertyItem(0x0112).Value[0]);
                        else if (dims.Height > dims.Width && img.Width > img.Height) turn = RotateFlipType.Rotate90FlipNone;
                        if (turnNotes++ < 25)
                            Note("preview " + name + " " + img.Width + "x" + img.Height + " bytes=" + data.Length + " exifOrientation="
                                + (tagged ? img.GetPropertyItem(0x0112).Value[0].ToString() : "none") + " tags=" + img.PropertyIdList.Length
                                + " photo=" + dims.Width + "x" + dims.Height + " turn=" + turn);
                        if (turn != RotateFlipType.RotateNoneFlipNone) img.RotateFlip(turn);
                    }
                    catch { }
                    var bmp = new Bitmap(px, px, PixelFormat.Format32bppPArgb);
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        int side = Math.Min(img.Width, img.Height);
                        g.DrawImage(img, new Rectangle(0, 0, px, px),
                            new Rectangle((img.Width - side) / 2, (img.Height - side) / 2, side, side), GraphicsUnit.Pixel);
                    }
                    return bmp;
                }
            }
            catch { return null; }
        }

        bool StillConnected()
        {
            try
            {
                string id = deviceId;
                foreach (DeviceRef d in provider.List())
                    if (d.Id == id) return true;
            }
            catch { }
            return false;
        }

        // Copies the selected items one by one; failures are retried inside Library.Import
        void ImportJob(List<Item> targets, Library lib)
        {
            long totalBytes = 0, doneBytes = 0;
            foreach (Item t in targets) totalBytes += Math.Max(1, t.Size);
            int imported = 0, duplicates = 0, failed = 0, streak = 0;
            bool heic = false, lost = false;
            var clock = Stopwatch.StartNew();
            var throttle = Stopwatch.StartNew();

            for (int i = 0; i < targets.Count; i++)
            {
                if (cancelImport || closing) break;
                Item it = targets[i];
                IDevice d = device;
                if (d == null) { lost = true; break; }
                int index = i;
                long baseBytes = doneBytes;
                Ui(delegate
                {
                    it.State = ItemState.Copying;
                    current = it;
                    progress.Heading = "Importing " + (index + 1).ToString("N0") + " of " + targets.Count.ToString("N0");
                    progress.Line1 = "Copying " + it.Name + " to " + Library.DateFolder(it.Date);
                    progress.Thumb = it.Thumb;
                    progress.Fraction = (float)baseBytes / totalBytes;
                    progress.Invalidate();
                    grid.ScrollTo(it);
                    grid.Invalidate();
                });
                if (it.Thumb == null) LoadThumb(it);
                try
                {
                    string final;
                    if (index < 3 || index % 50 == 0) Note("importing " + (index + 1) + " of " + targets.Count + ": " + it.RelPath + it.Name);
                    ImportOutcome outcome = lib.Import(d, it, delegate(long bytes)
                    {
                        if (throttle.ElapsedMilliseconds < 150) return;
                        throttle.Restart();
                        long done = baseBytes + Math.Min(bytes, Math.Max(1, it.Size));
                        string left = TimeLeft(clock.Elapsed.TotalSeconds, done, totalBytes);
                        Ui(delegate
                        {
                            progress.Fraction = (float)done / totalBytes;
                            progress.Line2 = left;
                            progress.Invalidate();
                        });
                    }, delegate { return cancelImport || closing; }, out final);
                    if (outcome == ImportOutcome.Cancelled)
                    {
                        Ui(delegate { it.State = ItemState.Queued; });
                        break;
                    }
                    if (outcome == ImportOutcome.Imported) imported++; else duplicates++;
                    if (final != null && Pictures.Supported(final) && (lastImported == null || index == 0)) lastImported = final;
                    streak = 0;
                    string ext = Path.GetExtension(it.Name).ToLowerInvariant();
                    if (ext == ".heic" || ext == ".heif") heic = true;
                    Ui(delegate { it.State = ItemState.Done; grid.Invalidate(); });
                }
                catch (Exception ex)
                {
                    Note("import FAILED " + it.RelPath + it.Name + ": " + ex.Message);
                    failed++;
                    streak++;
                    Ui(delegate { it.State = ItemState.Failed; grid.Invalidate(); });
                    if (streak >= 2)
                    {
                        Note("import aborted after " + streak + " failures in a row");
                        lost = true;
                        break;
                    }
                }
                doneBytes = baseBytes + Math.Max(1, it.Size);
                ServiceThumbs(3);
            }
            lib.CleanStaging();
            bool stopped = cancelImport;
            float fraction = totalBytes == 0 ? 1f : (float)doneBytes / totalBytes;
            Ui(delegate { FinishImport(imported, duplicates, failed, stopped, lost, heic, fraction); });
        }

        static string TimeLeft(double elapsed, long done, long total)
        {
            if (done <= 0 || elapsed < 3) return "";
            double left = elapsed / done * (total - done);
            if (left < 60) return "Less than a minute left";
            return "About " + Math.Ceiling(left / 60).ToString("N0") + " minutes left";
        }

        // ---------- window state ----------

        void SetMode(Mode m)
        {
            mode = m;
            bool browse = m == Mode.Browse, importing = m == Mode.Importing || m == Mode.Finished;
            SuspendLayout();
            connect.Visible = m == Mode.Connect;
            sidebar.Visible = browse;
            title.Visible = browse;
            progress.Visible = importing;
            grid.Visible = browse || importing;
            footer.Visible = m != Mode.Connect;
            footer.Change.Enabled = browse;
            grid.ImportMode = importing;
            ResumeLayout(true);
            UpdateTexts();
        }

        void ShowConnect(int gen, int stage, string message)
        {
            if (gen != generation || mode == Mode.Importing || mode == Mode.Finished) return;
            ClearItems();
            enumerating = false;
            deviceName = "";
            connect.Set(stage, message ?? "Your photos appear here as soon as the phone allows access.");
            header.Set("", stage == 0 ? "Looking for a phone" : "Phone is locked", Theme.Amber);
            if (mode != Mode.Connect) SetMode(Mode.Connect);
        }

        void ClearItems()
        {
            lock (gate) thumbQueue.Clear();
            foreach (Item it in items)
                if (it.Thumb != null) it.Thumb.Dispose();
            items.Clear();
            thumbOrder.Clear();
            current = null;
            grid.SetItems(new List<Item>(), false);
        }

        void Reconnect(string message)
        {
            if (mode == Mode.Importing) return;
            generation++;
            cancelEnum = true;
            ClearItems();
            enumerating = false;
            connect.Set(0, message);
            header.Set("", "Looking for a phone", Theme.Amber);
            SetMode(Mode.Connect);
            QueueConnect();
        }

        void OnPoll()
        {
            if (mode == Mode.Connect) QueueConnect();
            else if (mode == Mode.Browse && !enumerating)
            {
                int gen = generation;
                lock (gate) if (jobs.Count > 0) return;
                Enqueue(delegate
                {
                    if (gen == generation && device != null && !StillConnected())
                        Ui(delegate { if (gen == generation && mode == Mode.Browse) Reconnect("The phone was disconnected."); });
                });
            }
        }

        void AddItems(int gen, string name, List<Item> chunk)
        {
            if (gen != generation) return;
            deviceName = name;
            enumerating = true;
            grid.AllowTurn = false;
            foreach (Item it in chunk)
            {
                bool had = library.Has(it);
                it.State = had ? ItemState.Imported : ItemState.New;
                it.Selected = !had;
                items.Add(it);
            }
            if (mode == Mode.Connect) SetMode(Mode.Browse);
            header.Set(name, "Reading photos… " + items.Count.ToString("N0"), Theme.Green);
            if (mode == Mode.Browse) Rebuild(true);
        }

        void EnumerationDone(int gen, string name, int problems)
        {
            if (gen != generation) return;
            deviceName = name;
            enumerating = false;
            grid.AllowTurn = true;
            if (mode == Mode.Connect) SetMode(Mode.Browse);
            if (problems > 0) header.Set(name, "List may be incomplete, press refresh", Theme.Amber);
            else header.Set(name, items.Count.ToString("N0") + " items, connected over USB", Theme.Green);
            if (mode == Mode.Browse) Rebuild(true);
            if (autoImport && mode == Mode.Browse) StartImport();
        }

        bool Matches(Item it)
        {
            if (sidebar.Month.HasValue && it.Month != sidebar.Month.Value) return false;
            switch (sidebar.Filter)
            {
                case Filter.New: return it.State != ItemState.Imported;
                case Filter.Imported: return it.State == ItemState.Imported;
                case Filter.Videos: return it.IsVideo;
                default: return true;
            }
        }

        static int Newest(Item a, Item b)
        {
            int c = b.Date.CompareTo(a.Date);
            return c != 0 ? c : string.CompareOrdinal(b.Name, a.Name);
        }

        // Recounts the sidebar and refills the grid for the current filter
        void Rebuild(bool keepScroll)
        {
            var months = new SortedDictionary<DateTime, int>();
            int nNew = 0, nDone = 0, nVideo = 0;
            var shown = new List<Item>();
            foreach (Item it in items)
            {
                int n;
                months.TryGetValue(it.Month, out n);
                months[it.Month] = n + 1;
                if (it.State == ItemState.Imported) nDone++; else nNew++;
                if (it.IsVideo) nVideo++;
                if (Matches(it)) shown.Add(it);
            }
            shown.Sort(Newest);
            sidebar.CountNew = nNew;
            sidebar.CountAll = items.Count;
            sidebar.CountImported = nDone;
            sidebar.CountVideos = nVideo;
            sidebar.Months.Clear();
            foreach (KeyValuePair<DateTime, int> m in months) sidebar.Months.Insert(0, m);
            sidebar.Invalidate();
            grid.EmptyText = enumerating ? "Reading the phone's photos…" : items.Count == 0 ? "No photos found on the phone."
                : sidebar.Filter == Filter.New ? "Everything here is already imported." : "Nothing to show for this filter.";
            grid.SetItems(shown, keepScroll);
            UpdateTexts();
        }

        void UpdateTexts()
        {
            int selected = 0, fresh = 0;
            foreach (Item it in items)
            {
                if (it.State != ItemState.New && it.State != ItemState.Failed) continue;
                fresh++;
                if (it.Selected) selected++;
            }
            string[] headings = { "New on phone", "Everything", "Already imported", "Videos only" };
            string heading = headings[(int)sidebar.Filter];
            if (sidebar.Month.HasValue) heading += "  ·  " + sidebar.Month.Value.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            title.Set(heading, selected.ToString("N0") + " of " + fresh.ToString("N0") + " selected. Items you already imported are dimmed.");
            if (mode == Mode.Importing || mode == Mode.Finished)
            {
                footer.Import.Enabled = false;
                footer.SetImportText("Importing…");
            }
            else if (enumerating)
            {
                footer.Import.Enabled = false;
                footer.SetImportText("Reading photos… " + items.Count.ToString("N0"));
            }
            else
            {
                footer.Import.Enabled = selected > 0;
                footer.SetImportText(selected > 0 ? "Import " + selected.ToString("N0") + " selected" : "Nothing selected");
            }
            if (mode == Mode.Finished) footer.SetImportText("Import finished");
        }

        void SelectVisible()
        {
            foreach (Item it in items)
                if ((it.State == ItemState.New || it.State == ItemState.Failed) && Matches(it)) it.Selected = true;
            grid.Invalidate();
            UpdateTexts();
        }

        void StartImport()
        {
            if (mode != Mode.Browse || enumerating) return;
            try { Directory.CreateDirectory(dest); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Cannot use that folder: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            importSet = new List<Item>();
            foreach (Item it in items)
                if ((it.State == ItemState.New || it.State == ItemState.Failed) && it.Selected) importSet.Add(it);
            if (importSet.Count == 0) return;
            importSet.Sort(Newest);
            foreach (Item it in importSet) it.State = ItemState.Queued;

            cancelImport = false;
            lostDuringImport = false;
            progress.Heading = "Importing 1 of " + importSet.Count.ToString("N0");
            progress.Line1 = "Starting…";
            progress.Line2 = "";
            progress.Note = "Stopping is safe. Finished items stay imported.";
            progress.Fraction = 0;
            progress.Thumb = null;
            progress.Fix.Visible = false;
            progress.View.Visible = false;
            lastImported = null;
            progress.SetFinished(false);
            SetMode(Mode.Importing);
            grid.SetItems(importSet, false);
            header.Set(deviceName, "Keep it unlocked", Theme.Green);
            List<Item> targets = importSet;
            Library lib = library;
            Enqueue(delegate { ImportJob(targets, lib); });
        }

        void FinishImport(int imported, int duplicates, int failed, bool stopped, bool lost, bool heic, float fraction)
        {
            lostDuringImport = lost;
            current = null;
            progress.Heading = lost ? "The phone stopped responding" : stopped ? "Import stopped" : "Import complete";
            progress.Line1 = imported.ToString("N0") + " imported"
                + (duplicates > 0 ? ", " + duplicates.ToString("N0") + " already in the folder" : "")
                + (failed > 0 ? ", " + failed.ToString("N0") + " failed" : "") + ".";
            progress.Line2 = "";
            progress.Fraction = lost || stopped ? fraction : 1f;
            progress.Note = lost ? "Unplug it, plug it back in and unlock it, then import again."
                : failed > 0 ? "Failed items stay selected, so the next import retries them." : "";
            progress.Fix.Visible = heic && !HeicReady() && !OwnThumbnails();
            progress.View.Visible = lastImported != null;
            if (progress.Fix.Visible) progress.Note = "";
            progress.SetFinished(true);
            header.Set(deviceName, lost ? "Disconnected" : "Connected over USB", lost ? Theme.Amber : Theme.Green);
            SetMode(Mode.Finished);
            grid.Invalidate();
        }

        void LeaveImport()
        {
            foreach (Item it in importSet)
            {
                if (it.State == ItemState.Done)
                {
                    it.State = ItemState.Imported;
                    it.Selected = false;
                }
                else if (it.State != ItemState.Failed) it.State = ItemState.New;
            }
            importSet = new List<Item>();
            if (lostDuringImport)
            {
                mode = Mode.Browse;
                Reconnect("The phone stopped responding. Unplug it, plug it back in and unlock it.");
                return;
            }
            SetMode(Mode.Browse);
            Rebuild(false);
        }

        // ---------- HEIC thumbnails in File Explorer ----------

        static bool PackageInstalled(string prefix)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"))
                {
                    if (key == null) return false;
                    foreach (string name in key.GetSubKeyNames())
                        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }
            return false;
        }

        static bool OwnThumbnails()
        {
            try { return File.Exists(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "HeicThumb.dll")); }
            catch { return false; }
        }

        static bool HeicReady()
        {
            return PackageInstalled("Microsoft.HEIFImageExtension") && PackageInstalled("Microsoft.HEVCVideoExtension");
        }

        // File Explorer only draws HEIC thumbnails once Microsoft's two codec add-ons are installed
        void FixHeic()
        {
            bool heif = PackageInstalled("Microsoft.HEIFImageExtension"), hevc = PackageInstalled("Microsoft.HEVCVideoExtension");
            string need = (!heif ? "\n  • HEIF Image Extensions" : "") + (!hevc ? "\n  • HEVC Video Extensions" : "");
            if (MessageBox.Show(this,
                "File Explorer needs Microsoft's HEIC add-ons to show thumbnails for iPhone photos. Missing on this PC:" + need
                + "\n\nOpen the Microsoft Store to install them? After installing, the thumbnails appear in the folder by themselves.",
                Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            try
            {
                if (!hevc) Process.Start("ms-windows-store://pdp/?ProductId=" + HevcStoreId);
                if (!heif) Process.Start("ms-windows-store://pdp/?ProductId=" + HeifStoreId);
            }
            catch
            {
                MessageBox.Show(this, "Could not open the Microsoft Store. Search the Store for \"HEIF Image Extensions\" and \"HEVC Video Extensions\".",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        const string HeifStoreId = "9PMMSR1CGPWG";
        const string HevcStoreId = "9NMZLZ57R3T7";

        void RequestThumb(Item it)
        {
            lock (gate)
            {
                thumbQueue.Add(it);
                if (thumbQueue.Count > ThumbQueueCap)
                {
                    thumbQueue[0].ThumbAsked = false;
                    thumbQueue.RemoveAt(0);
                }
                Monitor.Pulse(gate);
            }
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (mode == Mode.Importing
                && MessageBox.Show(this, "An import is running. Stop it and close?", Text, MessageBoxButtons.YesNo) == DialogResult.No)
            {
                e.Cancel = true;
                return;
            }
            closing = true;
            cancelImport = true;
            cancelEnum = true;
            poll.Stop();
            lock (gate) Monitor.PulseAll(gate);
        }
    }
}
