using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace PhotoImport
{
    public enum ItemState { New, Imported, Queued, Copying, Done, Failed }

    public class Item
    {
        public string Id = "";
        public string RelPath = "";
        public string Name = "";
        public long Size;
        public DateTime Date;
        public ItemState State;
        public bool Selected;
        public Bitmap Thumb;
        public bool ThumbAsked;
        public bool TurnPending;
        public bool TurnAsked;

        public bool IsVideo
        {
            get { return Media.IsVideo(Name); }
        }

        public DateTime Month
        {
            get { return new DateTime(Date.Year, Date.Month, 1); }
        }

        // Same key format as version 1, so earlier imports are still recognised
        public string Key
        {
            get
            {
                return (RelPath + Path.GetFileNameWithoutExtension(Name)).ToLowerInvariant()
                    + "|" + Size.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    public static class Media
    {
        static readonly HashSet<string> Photo = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".heic", ".heif", ".jpg", ".jpeg", ".png", ".gif", ".tif", ".tiff", ".dng", ".webp", ".bmp" };
        static readonly HashSet<string> Video = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".mov", ".mp4", ".m4v", ".avi", ".3gp" };

        public static bool IsVideo(string name) { return Video.Contains(Path.GetExtension(name ?? "")); }
        public static bool IsMedia(string name)
        {
            string ext = Path.GetExtension(name ?? "");
            return Photo.Contains(ext) || Video.Contains(ext);
        }
        public static bool IsSidecar(string name)
        {
            return string.Equals(Path.GetExtension(name ?? ""), ".aae", StringComparison.OrdinalIgnoreCase);
        }
    }

    public class DeviceRef
    {
        public string Id;
        public string Name;
    }

    public class PhoneLockedException : Exception
    {
        public PhoneLockedException() : base("The phone is locked.") { }
    }

    public interface IDevice : IDisposable
    {
        void Enumerate(Action<Item> found, Func<bool> cancelled);
        byte[] Thumbnail(Item item);
        void Download(Item item, string path, Action<long> progress, Func<bool> cancelled);
        Size Dimensions(Item item);
    }

    // Reads how a photo must be turned from the first bytes of the file (the phone's previews come unrotated)
    public static class Orientation
    {
        public static RotateFlipType FromExif(int value)
        {
            return value == 3 ? RotateFlipType.Rotate180FlipNone : value == 6 ? RotateFlipType.Rotate90FlipNone
                : value == 8 ? RotateFlipType.Rotate270FlipNone : RotateFlipType.RotateNoneFlipNone;
        }

        public static RotateFlipType FromHead(byte[] b)
        {
            if (b == null || b.Length < 16) return RotateFlipType.RotateNoneFlipNone;
            if (b[0] == 0xFF && b[1] == 0xD8) return FromExif(JpegOrientation(b));
            // HEIC: an 'irot' box holds the anticlockwise quarter turns
            for (int i = 4; i + 4 < b.Length; i++)
            {
                if (b[i] != 'i' || b[i + 1] != 'r' || b[i + 2] != 'o' || b[i + 3] != 't') continue;
                if (b[i - 4] != 0 || b[i - 3] != 0 || b[i - 2] != 0 || b[i - 1] != 9) continue;
                int turns = b[i + 4] & 3;
                return turns == 1 ? RotateFlipType.Rotate270FlipNone : turns == 2 ? RotateFlipType.Rotate180FlipNone
                    : turns == 3 ? RotateFlipType.Rotate90FlipNone : RotateFlipType.RotateNoneFlipNone;
            }
            return RotateFlipType.RotateNoneFlipNone;
        }

        static int JpegOrientation(byte[] b)
        {
            int p = 2;
            while (p + 4 < b.Length && b[p] == 0xFF)
            {
                int marker = b[p + 1], len = (b[p + 2] << 8) | b[p + 3];
                if (marker == 0xE1 && p + 10 < b.Length && b[p + 4] == 'E' && b[p + 5] == 'x' && b[p + 6] == 'i' && b[p + 7] == 'f')
                {
                    int t = p + 10;
                    if (t + 8 > b.Length) return 0;
                    bool le = b[t] == 'I';
                    int ifd = t + (int)U32(b, t + 4, le);
                    if (ifd + 2 > b.Length) return 0;
                    int n = U16(b, ifd, le);
                    for (int k = 0; k < n; k++)
                    {
                        int e = ifd + 2 + k * 12;
                        if (e + 12 > b.Length) return 0;
                        if (U16(b, e, le) == 0x0112) return U16(b, e + 8, le);
                    }
                    return 0;
                }
                if (marker == 0xDA || len < 2) return 0;
                p += 2 + len;
            }
            return 0;
        }

        static int U16(byte[] b, int o, bool le) { return le ? b[o] | (b[o + 1] << 8) : (b[o] << 8) | b[o + 1]; }
        static uint U32(byte[] b, int o, bool le)
        {
            return le ? (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24))
                : (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
        }
    }

    public interface IDeviceProvider
    {
        List<DeviceRef> List();
        IDevice Open(DeviceRef device);
    }

    public enum ImportOutcome { Imported, Duplicate, Cancelled }

    // Destination folder handling: the skip index, the staging folder and date-folder placement
    public class Library
    {
        public const string IndexName = ".photo-import-index.txt";
        public const string StagingName = ".importing";
        public const int Attempts = 3;

        public readonly string Root;
        readonly HashSet<string> index = new HashSet<string>(StringComparer.Ordinal);
        public int RetryPauseMs = 2000;

        public Library(string root)
        {
            Root = root;
            string path = Path.Combine(root, IndexName);
            if (!File.Exists(path)) return;
            foreach (string line in File.ReadAllLines(path))
            {
                string t = line.Trim();
                if (t.Length > 0) index.Add(t);
            }
        }

        public bool Has(Item item) { lock (index) return index.Contains(item.Key); }

        void Remember(Item item)
        {
            lock (index) if (!index.Add(item.Key)) return;
            string path = Path.Combine(Root, IndexName);
            bool fresh = !File.Exists(path);
            File.AppendAllText(path, item.Key + Environment.NewLine, new UTF8Encoding(false));
            if (fresh) Hide(path);
        }

        static void Hide(string path)
        {
            try { File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden); } catch { }
        }

        // Downloads one item with retries, then files it under YYYY\YYYY-MM
        public ImportOutcome Import(IDevice device, Item item, Action<long> progress, Func<bool> cancelled, out string finalPath)
        {
            finalPath = null;
            Directory.CreateDirectory(Root);
            string staging = Path.Combine(Root, StagingName);
            if (!Directory.Exists(staging))
            {
                Directory.CreateDirectory(staging);
                Hide(staging);
            }
            string temp = Path.Combine(staging, SafeName(item.Name));
            Exception last = null;
            for (int attempt = 1; attempt <= Attempts; attempt++)
            {
                if (cancelled()) return ImportOutcome.Cancelled;
                DateTime started = DateTime.UtcNow;
                try
                {
                    if (File.Exists(temp)) File.Delete(temp);
                    device.Download(item, temp, progress, cancelled);
                    if (cancelled())
                    {
                        TryDelete(temp);
                        return ImportOutcome.Cancelled;
                    }
                    if (new FileInfo(temp).Length == 0)
                        throw new IOException("the phone sent an empty file");
                    last = null;
                    break;
                }
                catch (Exception ex)
                {
                    last = ex;
                    TryDelete(temp);
                    // a failure that took this long means the phone has stopped answering; retrying only adds waiting
                    if ((DateTime.UtcNow - started).TotalSeconds > 12) break;
                    if (attempt < Attempts) Thread.Sleep(RetryPauseMs);
                }
            }
            if (last != null) throw last;

            DateTime taken = ExifDate(temp) ?? item.Date;
            try
            {
                File.SetCreationTime(temp, taken);
                File.SetLastWriteTime(temp, taken);
            }
            catch { }
            bool duplicate;
            finalPath = Place(temp, Root, taken, out duplicate);
            Remember(item);
            return duplicate ? ImportOutcome.Duplicate : ImportOutcome.Imported;
        }

        public void CleanStaging()
        {
            try { Directory.Delete(Path.Combine(Root, StagingName), true); } catch { }
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        public static bool Plausible(DateTime d)
        {
            return d.Year >= 1990 && d < DateTime.Now.AddDays(2);
        }

        static DateTime? ExifDate(string file)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext != ".jpg" && ext != ".jpeg") return null;
            try
            {
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var img = Image.FromStream(fs, false, false))
                {
                    foreach (int id in new[] { 0x9003, 0x9004, 0x0132 })
                    {
                        if (Array.IndexOf(img.PropertyIdList, id) < 0) continue;
                        string raw = Encoding.ASCII.GetString(img.GetPropertyItem(id).Value).Trim('\0', ' ');
                        DateTime d;
                        if (DateTime.TryParseExact(raw, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)
                            && Plausible(d))
                            return d;
                    }
                }
            }
            catch { }
            return null;
        }

        // Identical name+size already in the date folder counts as a duplicate; a different file gets " (2)"
        public static string Place(string staged, string destRoot, DateTime taken, out bool duplicate)
        {
            duplicate = false;
            string dir = Path.Combine(destRoot, taken.ToString("yyyy", CultureInfo.InvariantCulture),
                taken.ToString("yyyy-MM", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(dir);
            string name = SafeName(Path.GetFileName(staged));
            string stem = Path.GetFileNameWithoutExtension(name);
            string ext = Path.GetExtension(name);
            long len = new FileInfo(staged).Length;
            string target = Path.Combine(dir, name);
            for (int n = 2; File.Exists(target); n++)
            {
                if (new FileInfo(target).Length == len)
                {
                    File.Delete(staged);
                    duplicate = true;
                    return target;
                }
                target = Path.Combine(dir, stem + " (" + n + ")" + ext);
            }
            File.Move(staged, target);
            return target;
        }

        static string SafeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim(' ', '.');
            return name.Length == 0 ? "file" : name;
        }

        public static string DateFolder(DateTime d)
        {
            return d.ToString("yyyy", CultureInfo.InvariantCulture) + " \\ " + d.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        }
    }
}
