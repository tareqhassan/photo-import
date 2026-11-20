using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;

namespace PhotoImport
{
    // Stand-in phone used with --demo, for trying the interface without an iPhone
    public class DemoProvider : IDeviceProvider
    {
        readonly bool empty;
        public DemoProvider(bool empty) { this.empty = empty; }

        public List<DeviceRef> List()
        {
            var list = new List<DeviceRef>();
            if (!empty) list.Add(new DeviceRef { Id = "demo", Name = "Apple iPhone" });
            return list;
        }

        public IDevice Open(DeviceRef device) { return new DemoDevice(); }
    }

    public class DemoDevice : IDevice
    {
        public int FailEvery;
        public int Downloads;

        public void Enumerate(Action<Item> found, Func<bool> cancelled)
        {
            int n = 4318;
            var month = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            foreach (int count in new[] { 14, 19, 9, 23, 12 })
            {
                for (int i = 0; i < count && !cancelled(); i++)
                {
                    bool video = n % 6 == 0;
                    found(new Item
                    {
                        Id = "o" + n,
                        RelPath = month.ToString("yyyyMM") + "__/",
                        Name = "IMG_" + n + (video ? ".MOV" : n % 4 == 0 ? ".JPG" : ".HEIC"),
                        Size = 40000 + n % 9 * 9000,
                        Date = month.AddDays(Math.Max(0, 25 - i)).AddHours(9)
                    });
                    n--;
                }
                month = month.AddMonths(-1);
            }
        }

        public byte[] Thumbnail(Item item)
        {
            int seed = Math.Abs(item.Name.GetHashCode());
            using (var bmp = new Bitmap(160, 120))
            using (Graphics g = Graphics.FromImage(bmp))
            using (var ms = new MemoryStream())
            {
                Color a = Theme.Tints[seed % Theme.Tints.Length], b = Theme.Tints[(seed / 7 + 3) % Theme.Tints.Length];
                g.Clear(a);
                using (var brush = new SolidBrush(Color.FromArgb(Math.Min(255, b.R + 50), Math.Min(255, b.G + 50), Math.Min(255, b.B + 50))))
                {
                    g.FillEllipse(brush, 20 + seed % 60, 15 + seed % 30, 70, 70);
                    g.FillRectangle(brush, 0, 85 + seed % 15, 160, 40);
                }
                bmp.Save(ms, ImageFormat.Jpeg);
                return ms.ToArray();
            }
        }

        public void Download(Item item, string path, Action<long> progress, Func<bool> cancelled)
        {
            Downloads++;
            if (FailEvery > 0 && Downloads % FailEvery == 0) throw new IOException("simulated transfer failure");
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var chunk = new byte[8192];
                long done = 0;
                while (done < item.Size && !cancelled())
                {
                    int n = (int)Math.Min(chunk.Length, item.Size - done);
                    fs.Write(chunk, 0, n);
                    done += n;
                    progress(done);
                    if (FailEvery == 0) Thread.Sleep(15);
                }
            }
        }

        public Size Dimensions(Item item) { return Size.Empty; }

        public void Dispose() { }
    }
}
