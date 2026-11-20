using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace PhotoImport
{
    // HEIC decoding through the bundled libheif DLLs that sit next to this assembly
    public static class Heif
    {
#if LINUXTEST
        const string Lib = "libheif.so.1";
#else
        const string Lib = "libheif-18c57b017b404cb1ef96dee8a6eef027.dll";
#endif
        const int ColorspaceRgb = 1, ChromaInterleavedRgb = 10, ChannelInterleaved = 10;
        const uint LoadWithAlteredSearchPath = 8;

        [StructLayout(LayoutKind.Sequential)]
        struct Error
        {
            public int Code;
            public int Subcode;
            public IntPtr Message;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr heif_context_alloc();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern void heif_context_free(IntPtr ctx);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern Error heif_context_read_from_memory(IntPtr ctx, IntPtr mem, UIntPtr size, IntPtr options);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern Error heif_context_get_primary_image_handle(IntPtr ctx, out IntPtr handle);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern void heif_image_handle_release(IntPtr handle);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int heif_image_handle_get_width(IntPtr handle);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int heif_image_handle_get_height(IntPtr handle);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int heif_image_handle_get_number_of_thumbnails(IntPtr handle);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int heif_image_handle_get_list_of_thumbnail_IDs(IntPtr handle, [In, Out] uint[] ids, int count);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern Error heif_image_handle_get_thumbnail(IntPtr handle, uint id, out IntPtr thumb);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern Error heif_decode_image(IntPtr handle, out IntPtr image, int colorspace, int chroma, IntPtr options);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr heif_image_get_plane_readonly(IntPtr image, int channel, out int stride);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int heif_image_get_width(IntPtr image, int channel);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int heif_image_get_height(IntPtr image, int channel);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern void heif_image_release(IntPtr image);

        static readonly object gate = new object();
        static bool loaded;

        public static bool IsHeic(string path)
        {
            string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
            return ext == ".heic" || ext == ".heif";
        }

        // Loads libheif by full path so its companion DLLs are found in the same folder, whatever process hosts us
        static void EnsureLoaded()
        {
            lock (gate)
            {
                if (loaded) return;
#if !LINUXTEST
                string dir = Path.GetDirectoryName(typeof(Heif).Assembly.Location);
                string full = Path.Combine(dir, Lib);
                if (LoadLibraryExW(full, IntPtr.Zero, LoadWithAlteredSearchPath) == IntPtr.Zero)
                    throw new DllNotFoundException("The HEIC decoder could not be loaded from " + full
                        + " (error " + Marshal.GetLastWin32Error() + ").");
#endif
                loaded = true;
            }
        }

        static void Check(Error e, string what)
        {
            if (e.Code == 0) return;
            string detail = e.Message == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(e.Message);
            throw new InvalidDataException("Cannot " + what + ": " + detail);
        }

        public static Bitmap DecodeFile(string path, int maxSize)
        {
            return Decode(File.ReadAllBytes(path), maxSize);
        }

        // maxSize 0 decodes at full size; otherwise the picture is fitted inside maxSize, using the small
        // preview stored in the file when that is big enough (much faster than decoding the whole photo)
        public static Bitmap Decode(byte[] data, int maxSize)
        {
            EnsureLoaded();
            IntPtr ctx = heif_context_alloc(), primary = IntPtr.Zero, thumb = IntPtr.Zero, image = IntPtr.Zero;
            GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                Check(heif_context_read_from_memory(ctx, pin.AddrOfPinnedObject(), (UIntPtr)(ulong)data.Length, IntPtr.Zero), "read the HEIC file");
                Check(heif_context_get_primary_image_handle(ctx, out primary), "find the picture in the HEIC file");
                IntPtr source = primary;
                if (maxSize > 0 && heif_image_handle_get_number_of_thumbnails(primary) > 0)
                {
                    var ids = new uint[1];
                    if (heif_image_handle_get_list_of_thumbnail_IDs(primary, ids, 1) == 1
                        && heif_image_handle_get_thumbnail(primary, ids[0], out thumb).Code == 0 && thumb != IntPtr.Zero
                        && Math.Max(heif_image_handle_get_width(thumb), heif_image_handle_get_height(thumb)) >= maxSize)
                        source = thumb;
                }
                Check(heif_decode_image(source, out image, ColorspaceRgb, ChromaInterleavedRgb, IntPtr.Zero), "decode the HEIC picture");
                int stride;
                IntPtr plane = heif_image_get_plane_readonly(image, ChannelInterleaved, out stride);
                int w = heif_image_get_width(image, ChannelInterleaved), h = heif_image_get_height(image, ChannelInterleaved);
                if (plane == IntPtr.Zero || w <= 0 || h <= 0) throw new InvalidDataException("The HEIC picture is empty.");
                Bitmap full = ToBitmap(plane, stride, w, h);
                if (maxSize <= 0 || Math.Max(w, h) <= maxSize) return full;
                try { return Fit(full, maxSize); }
                finally { full.Dispose(); }
            }
            finally
            {
                pin.Free();
                if (image != IntPtr.Zero) heif_image_release(image);
                if (thumb != IntPtr.Zero) heif_image_handle_release(thumb);
                if (primary != IntPtr.Zero) heif_image_handle_release(primary);
                heif_context_free(ctx);
            }
        }

        // libheif gives RGB rows; GDI+ wants BGR
        static Bitmap ToBitmap(IntPtr plane, int stride, int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            BitmapData bits = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                var row = new byte[w * 3];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(new IntPtr(plane.ToInt64() + (long)y * stride), row, 0, row.Length);
                    for (int x = 0; x < row.Length; x += 3)
                    {
                        byte r = row[x];
                        row[x] = row[x + 2];
                        row[x + 2] = r;
                    }
                    Marshal.Copy(row, 0, new IntPtr(bits.Scan0.ToInt64() + (long)y * bits.Stride), row.Length);
                }
            }
            finally { bmp.UnlockBits(bits); }
            return bmp;
        }

        public static Bitmap Fit(Image source, int maxSize)
        {
            double scale = Math.Min(1.0, (double)maxSize / Math.Max(source.Width, source.Height));
            int w = Math.Max(1, (int)Math.Round(source.Width * scale)), h = Math.Max(1, (int)Math.Round(source.Height * scale));
            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (var wrap = new ImageAttributes())
                {
                    wrap.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(source, new Rectangle(0, 0, w, h), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, wrap);
                }
            }
            return bmp;
        }
    }
}
