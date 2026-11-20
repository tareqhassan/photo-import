using System; using System.Drawing; using System.Drawing.Imaging; using PhotoImport;
static class HeifTest {
    static int Main(string[] a) {
        int fails = 0;
        using (Bitmap full = Heif.DecodeFile(a[0], 0)) {
            Color c = full.GetPixel(100, 100), d = full.GetPixel(900, 620), top = full.GetPixel(600, 10);
            Console.WriteLine("full " + full.Width + "x" + full.Height + " white=" + c + " red=" + d + " top=" + top);
            if (full.Width != 1200 || full.Height != 800 || c.R < 230 || c.G < 230 || c.B < 230 || d.R < 200 || d.G > 70 || d.B > 70 || top.R < 200 || top.B > 80) fails++;
            full.Save("/tmp/heic_full.png", ImageFormat.Png);
        }
        using (Bitmap t = Heif.DecodeFile(a[0], 200)) { Console.WriteLine("thumb " + t.Width + "x" + t.Height); if (t.Width != 200 || t.Height != 133) fails++; }
        using (Bitmap t = Heif.DecodeFile(a[0], 600)) { Console.WriteLine("mid " + t.Width + "x" + t.Height); if (t.Width != 600) fails++; }
        using (Bitmap p = Heif.DecodeFile(a[1], 0)) { Console.WriteLine("portrait " + p.Width + "x" + p.Height); if (p.Width != 800) fails++; }
        try { Heif.Decode(new byte[100], 0); fails++; } catch (System.IO.InvalidDataException e) { Console.WriteLine("bad data -> " + e.Message); }
        Console.WriteLine(fails == 0 ? "HEIF OK" : "HEIF FAILED " + fails);
        return fails;
    }
}
