using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

[assembly: AssemblyTitle("Apple Photo Importer HEIC thumbnails")]
[assembly: AssemblyCompany("Magnetic WP")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: ComVisible(false)]

namespace PhotoImport
{
    [ComImport, Guid("e357fccd-a995-4576-b01f-234630154e96"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IThumbnailProvider
    {
        [PreserveSig] int GetThumbnail(uint cx, out IntPtr hbitmap, out int alphaType);
    }

    [ComImport, Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithStream
    {
        [PreserveSig] int Initialize(IStream stream, uint mode);
    }

    // File Explorer loads this class to draw thumbnails for .heic and .heif files
    [ComVisible(true), Guid("7C3A5B1E-92D4-4F6A-B0E8-5D2F4A9C6E31"), ClassInterface(ClassInterfaceType.None)]
    public class HeicThumbnailProvider : IThumbnailProvider, IInitializeWithStream
    {
        const int Ok = 0, Fail = unchecked((int)0x80004005), AlphaRgb = 1;
        const int MaxBytes = 200 * 1024 * 1024;
        byte[] data;

        public int Initialize(IStream stream, uint mode)
        {
            try
            {
                using (var ms = new MemoryStream())
                {
                    var buffer = new byte[256 * 1024];
                    IntPtr readPtr = Marshal.AllocCoTaskMem(sizeof(int));
                    try
                    {
                        while (ms.Length < MaxBytes)
                        {
                            Marshal.WriteInt32(readPtr, 0);
                            stream.Read(buffer, buffer.Length, readPtr);
                            int read = Marshal.ReadInt32(readPtr);
                            if (read <= 0) break;
                            ms.Write(buffer, 0, read);
                        }
                    }
                    finally { Marshal.FreeCoTaskMem(readPtr); }
                    data = ms.ToArray();
                }
                return Ok;
            }
            catch (Exception ex)
            {
                Note(ex);
                return Fail;
            }
        }

        public int GetThumbnail(uint cx, out IntPtr hbitmap, out int alphaType)
        {
            hbitmap = IntPtr.Zero;
            alphaType = AlphaRgb;
            try
            {
                if (data == null || data.Length == 0) return Fail;
                using (Bitmap bmp = Heif.Decode(data, (int)Math.Max(32, Math.Min(cx, 4096))))
                    hbitmap = bmp.GetHbitmap();
                return Ok;
            }
            catch (Exception ex)
            {
                Note(ex);
                return Fail;
            }
        }

        static void Note(Exception ex)
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "PhotoImport-thumbnails.txt");
                if (File.Exists(path) && new FileInfo(path).Length > 200000) return;
                File.AppendAllText(path, DateTime.Now.ToString("s") + "  " + ex + Environment.NewLine);
            }
            catch { }
        }
    }
}
