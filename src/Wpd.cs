using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace PhotoImport
{
    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey
    {
        public Guid Fmtid;
        public uint Pid;
        public PropertyKey(string fmtid, uint pid) { Fmtid = new Guid(fmtid); Pid = pid; }
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PropVariant
    {
        [FieldOffset(0)] public ushort Vt;
        [FieldOffset(8)] public double Dbl;
    }

    // Method order in every interface below mirrors the vtable in PortableDeviceApi.h / PortableDeviceTypes.h
    [ComImport, Guid("a1567595-4c2f-4574-a6fa-ecef917b9a40"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPortableDeviceManager
    {
        [PreserveSig] int GetDevices([In, Out, MarshalAs(UnmanagedType.LPArray)] IntPtr[] ids, ref uint count);
        [PreserveSig] int RefreshDeviceList();
        [PreserveSig] int GetDeviceFriendlyName([MarshalAs(UnmanagedType.LPWStr)] string id,
            [In, Out, MarshalAs(UnmanagedType.LPArray)] ushort[] name, ref uint count);
    }

    [ComImport, Guid("625e2df8-6392-4cf0-9ad1-3cfa5f17775c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPortableDevice
    {
        [PreserveSig] int Open([MarshalAs(UnmanagedType.LPWStr)] string id, IPortableDeviceValues clientInfo);
        void SendCommand_Unused();
        [PreserveSig] int Content(out IPortableDeviceContent content);
        void Capabilities_Unused();
        [PreserveSig] int Cancel();
        [PreserveSig] int Close();
    }

    [ComImport, Guid("6a96ed84-7c73-4480-9938-bf5af477d426"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPortableDeviceContent
    {
        [PreserveSig] int EnumObjects(uint flags, [MarshalAs(UnmanagedType.LPWStr)] string parentId,
            IPortableDeviceValues filter, out IEnumPortableDeviceObjectIDs result);
        [PreserveSig] int Properties(out IPortableDeviceProperties properties);
        [PreserveSig] int Transfer(out IPortableDeviceResources resources);
    }

    [ComImport, Guid("10ece955-cf41-4728-bfa0-41eedf1bbf19"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnumPortableDeviceObjectIDs
    {
        [PreserveSig] int Next(uint count, [In, Out, MarshalAs(UnmanagedType.LPArray)] IntPtr[] ids, ref uint fetched);
    }

    [ComImport, Guid("7f6d695c-03df-4439-a809-59266beee3a6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPortableDeviceProperties
    {
        void GetSupportedProperties_Unused();
        void GetPropertyAttributes_Unused();
        [PreserveSig] int GetValues([MarshalAs(UnmanagedType.LPWStr)] string id,
            IPortableDeviceKeyCollection keys, out IPortableDeviceValues values);
    }

    [ComImport, Guid("fd8878ac-d841-4d17-891c-e6829cdb6934"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPortableDeviceResources
    {
        void GetSupportedResources_Unused();
        void GetResourceAttributes_Unused();
        [PreserveSig] int GetStream([MarshalAs(UnmanagedType.LPWStr)] string id, ref PropertyKey key, uint mode,
            ref uint optimalBufferSize, out IStream stream);
        void Delete_Unused();
        [PreserveSig] int Cancel();
    }

    [ComImport, Guid("6848f6f2-3155-4f86-b6f5-263eeeab3143"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPortableDeviceValues
    {
        void GetCount_Unused();
        void GetAt_Unused();
        void SetValue_Unused();
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetStringValue(ref PropertyKey key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int GetStringValue(ref PropertyKey key, out IntPtr value);
        [PreserveSig] int SetUnsignedIntegerValue(ref PropertyKey key, uint value);
        [PreserveSig] int GetUnsignedIntegerValue(ref PropertyKey key, out uint value);
        void SetSignedIntegerValue_Unused();
        void GetSignedIntegerValue_Unused();
        void SetUnsignedLargeIntegerValue_Unused();
        [PreserveSig] int GetUnsignedLargeIntegerValue(ref PropertyKey key, out ulong value);
    }

    [ComImport, Guid("dada2357-e0ad-492e-98db-dd61c53ba353"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPortableDeviceKeyCollection
    {
        void GetCount_Unused();
        void GetAt_Unused();
        [PreserveSig] int Add(ref PropertyKey key);
    }

    public class WpdProvider : IDeviceProvider
    {
        static readonly Guid ManagerClsid = new Guid("0af10cec-2ecd-4b92-9581-34f6ae0637f3");

        public List<DeviceRef> List()
        {
            var list = new List<DeviceRef>();
            var manager = (IPortableDeviceManager)Activator.CreateInstance(Type.GetTypeFromCLSID(ManagerClsid));
            try
            {
                manager.RefreshDeviceList();
                uint count = 0;
                if (manager.GetDevices(null, ref count) < 0 || count == 0) return list;
                var ptrs = new IntPtr[count];
                if (manager.GetDevices(ptrs, ref count) < 0) return list;
                for (int i = 0; i < count && i < ptrs.Length; i++)
                {
                    if (ptrs[i] == IntPtr.Zero) continue;
                    string id = Marshal.PtrToStringUni(ptrs[i]);
                    Marshal.FreeCoTaskMem(ptrs[i]);
                    list.Add(new DeviceRef { Id = id, Name = FriendlyName(manager, id) });
                }
            }
            finally { Marshal.ReleaseComObject(manager); }
            return list;
        }

        static string FriendlyName(IPortableDeviceManager manager, string id)
        {
            try
            {
                uint len = 0;
                manager.GetDeviceFriendlyName(id, null, ref len);
                if (len == 0) return "Phone";
                var buf = new ushort[len];
                if (manager.GetDeviceFriendlyName(id, buf, ref len) < 0) return "Phone";
                var chars = new char[buf.Length];
                int n = 0;
                while (n < buf.Length && buf[n] != 0) { chars[n] = (char)buf[n]; n++; }
                return n == 0 ? "Phone" : new string(chars, 0, n);
            }
            catch { return "Phone"; }
        }

        public IDevice Open(DeviceRef device) { return new WpdDevice(device.Id); }
    }

    public class WpdDevice : IDevice
    {
        const string ObjectProps = "EF6B490D-5CD8-437A-AFFC-DA8B60EE4A3C";
        const string ClientProps = "204D9F0C-2292-4080-9F42-40664E70F859";
        static PropertyKey KeyName = new PropertyKey(ObjectProps, 4);
        static PropertyKey KeySize = new PropertyKey(ObjectProps, 11);
        static PropertyKey KeyFileName = new PropertyKey(ObjectProps, 12);
        static PropertyKey KeyCreated = new PropertyKey(ObjectProps, 18);
        static PropertyKey KeyModified = new PropertyKey(ObjectProps, 19);
        static PropertyKey KeyWidth = new PropertyKey("2ED8BA05-0AD3-42DC-B0D0-BC95AC396AC8", 22);
        static PropertyKey KeyHeight = new PropertyKey("2ED8BA05-0AD3-42DC-B0D0-BC95AC396AC8", 23);
        static PropertyKey ResDefault = new PropertyKey("E81E79BE-34F0-41BF-B53F-F1A06AE87842", 0);
        static PropertyKey ResThumb = new PropertyKey("C7C407BA-98FA-46B5-9960-23FEC124CFDE", 0);
        static readonly Guid DeviceClsid = new Guid("f7c0039a-4762-488a-b4b3-760ef9a1ba9b");
        static readonly Guid ValuesClsid = new Guid("0c15d503-d017-47ce-9016-7b3f978721cc");
        static readonly Guid KeysClsid = new Guid("de2d022d-2480-43be-97f0-d1fa2cf98f4f");
        const int MaxDepth = 6;
        const ushort VtDate = 7;

        public static Action<string> Log = delegate { };
        public int Problems;
        int headLogs, thumbLogs;
        const int StallMs = 30000;
        volatile bool busy;
        volatile int lastBeat;
        System.Threading.Timer watchdog;

        // A transfer the phone stops answering would block forever; this cancels it so it can be retried
        void Watch()
        {
            if (!busy || unchecked(Environment.TickCount - lastBeat) < StallMs) return;
            lastBeat = Environment.TickCount;
            Log("no data from the phone for 30 s, cancelling the transfer");
            try { IPortableDeviceResources r = resources; if (r != null) r.Cancel(); } catch { }
        }

        IPortableDevice device;
        IPortableDeviceContent content;
        IPortableDeviceProperties properties;
        IPortableDeviceResources resources;
        IPortableDeviceKeyCollection keys;
        IPortableDeviceKeyCollection sizeKeys;

        public WpdDevice(string id)
        {
            var info = (IPortableDeviceValues)Activator.CreateInstance(Type.GetTypeFromCLSID(ValuesClsid));
            var name = new PropertyKey(ClientProps, 2);
            var major = new PropertyKey(ClientProps, 3);
            var minor = new PropertyKey(ClientProps, 4);
            var revision = new PropertyKey(ClientProps, 5);
            var access = new PropertyKey(ClientProps, 9);
            info.SetStringValue(ref name, "Photo Import");
            info.SetUnsignedIntegerValue(ref major, 2);
            info.SetUnsignedIntegerValue(ref minor, 0);
            info.SetUnsignedIntegerValue(ref revision, 0);
            info.SetUnsignedIntegerValue(ref access, 0x80000000);

            device = (IPortableDevice)Activator.CreateInstance(Type.GetTypeFromCLSID(DeviceClsid));
            Check(device.Open(id, info), "open the phone");
            Check(device.Content(out content), "read the phone");
            Check(content.Properties(out properties), "read the phone");
            Check(content.Transfer(out resources), "read the phone");

            watchdog = new System.Threading.Timer(delegate(object o) { Watch(); }, null, 5000, 5000);
            keys = (IPortableDeviceKeyCollection)Activator.CreateInstance(Type.GetTypeFromCLSID(KeysClsid));
            keys.Add(ref KeyName);
            keys.Add(ref KeyFileName);
            keys.Add(ref KeySize);
            keys.Add(ref KeyCreated);
            keys.Add(ref KeyModified);
            sizeKeys = (IPortableDeviceKeyCollection)Activator.CreateInstance(Type.GetTypeFromCLSID(KeysClsid));
            sizeKeys.Add(ref KeyWidth);
            sizeKeys.Add(ref KeyHeight);
        }

        static void Check(int hr, string what)
        {
            if (hr < 0) throw new IOException("Could not " + what + " (0x" + hr.ToString("X8") + ").");
        }

        // Lists a folder's children, retrying when the phone refuses the request
        List<string> Children(string parentId, string label)
        {
            var ids = new List<string>();
            IEnumPortableDeviceObjectIDs en = null;
            int open = -1;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                open = content.EnumObjects(0, parentId, null, out en);
                if (open >= 0 && en != null) break;
                Log("list FAILED [" + label + "] attempt " + attempt + " hr=0x" + open.ToString("X8"));
                System.Threading.Thread.Sleep(700);
            }
            if (open < 0 || en == null)
            {
                Problems++;
                return ids;
            }
            int calls = 0, last = 0;
            try
            {
                var buf = new IntPtr[64];
                while (true)
                {
                    uint fetched = 0;
                    int hr = en.Next((uint)buf.Length, buf, ref fetched);
                    calls++;
                    last = hr;
                    for (int i = 0; i < fetched; i++)
                    {
                        if (buf[i] == IntPtr.Zero) continue;
                        ids.Add(Marshal.PtrToStringUni(buf[i]));
                        Marshal.FreeCoTaskMem(buf[i]);
                        buf[i] = IntPtr.Zero;
                    }
                    if (hr != 0 || fetched == 0) break;
                }
            }
            finally { Marshal.ReleaseComObject(en); }
            Log("list [" + label + "] children=" + ids.Count + " calls=" + calls + " lastHr=0x" + last.ToString("X8"));
            if (last < 0) Problems++;
            return ids;
        }

        // The top level is listed again until no new folders appear, because an iPhone can publish them gradually
        public void Enumerate(Action<Item> found, Func<bool> cancelled)
        {
            List<string> storages = Children("DEVICE", "device");
            if (storages.Count == 0) throw new PhoneLockedException();
            var seen = new HashSet<string>();
            int total = 0;
            Action<Item> counted = delegate(Item it) { total++; found(it); };
            for (int pass = 1; pass <= 6 && !cancelled(); pass++)
            {
                int before = seen.Count;
                foreach (string storage in storages) Walk(storage, "", 1, counted, cancelled, seen);
                Log("pass " + pass + " top-level=" + seen.Count + " items=" + total + " problems=" + Problems);
                if (seen.Count == before) break;
                System.Threading.Thread.Sleep(1500);
            }
        }

        void Walk(string parentId, string rel, int depth, Action<Item> found, Func<bool> cancelled, HashSet<string> seen)
        {
            if (depth > MaxDepth) return;
            List<string> ids = Children(parentId, rel.Length == 0 ? "storage" : rel);
            int unread = 0, files = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                if (cancelled()) return;
                string id = ids[i];
                if (seen != null && seen.Contains(id)) continue;
                IPortableDeviceValues values;
                int got = properties.GetValues(id, keys, out values);
                if (got < 0 || values == null)
                {
                    if (unread++ == 0) Log("properties FAILED in [" + rel + "] hr=0x" + got.ToString("X8"));
                    continue;
                }
                if (seen != null) seen.Add(id);
                try
                {
                    string name = ReadString(values, ref KeyFileName) ?? ReadString(values, ref KeyName) ?? "";
                    if (Media.IsSidecar(name)) continue;
                    if (!Media.IsMedia(name))
                    {
                        if (name.Length > 0) Walk(id, rel + name + "/", depth + 1, found, cancelled, null);
                        continue;
                    }
                    ulong size;
                    if (values.GetUnsignedLargeIntegerValue(ref KeySize, out size) < 0) size = 0;
                    files++;
                    found(new Item
                    {
                        Id = id,
                        RelPath = rel,
                        Name = name,
                        Size = (long)size,
                        Date = ReadDate(values, ref KeyCreated) ?? ReadDate(values, ref KeyModified) ?? FolderDate(rel) ?? DateTime.Now
                    });
                }
                finally { Marshal.ReleaseComObject(values); }
            }
            if (unread > 0) Problems++;
            if (files > 0 || unread > 0) Log("folder [" + rel + "] files=" + files + " unreadable=" + unread);
        }

        static string ReadString(IPortableDeviceValues values, ref PropertyKey key)
        {
            IntPtr p;
            if (values.GetStringValue(ref key, out p) < 0 || p == IntPtr.Zero) return null;
            string s = Marshal.PtrToStringUni(p);
            Marshal.FreeCoTaskMem(p);
            return string.IsNullOrEmpty(s) ? null : s;
        }

        static DateTime? ReadDate(IPortableDeviceValues values, ref PropertyKey key)
        {
            PropVariant v;
            if (values.GetValue(ref key, out v) < 0 || v.Vt != VtDate) return null;
            try
            {
                DateTime d = DateTime.FromOADate(v.Dbl);
                return Library.Plausible(d) ? (DateTime?)d : null;
            }
            catch { return null; }
        }

        // iPhone folders are named like 202610_b
        public static DateTime? FolderDate(string rel)
        {
            foreach (string part in rel.Split(new[] { '/' }))
            {
                int y, m;
                if (part.Length >= 6 && int.TryParse(part.Substring(0, 4), out y) && int.TryParse(part.Substring(4, 2), out m)
                    && y >= 1990 && y <= DateTime.Now.Year + 1 && m >= 1 && m <= 12)
                    return new DateTime(y, m, 1);
            }
            return null;
        }

        public byte[] Thumbnail(Item item)
        {
            uint buf = 0;
            IStream stream;
            int hr = resources.GetStream(item.Id, ref ResThumb, 0, ref buf, out stream);
            if (hr < 0 || stream == null)
            {
                if (thumbLogs++ < 40) Log("thumbnail FAILED " + item.RelPath + item.Name + " hr=0x" + hr.ToString("X8"));
                return null;
            }
            try
            {
                using (var ms = new MemoryStream())
                {
                    Pump(stream, ms, buf, null, null);
                    return ms.Length == 0 ? null : ms.ToArray();
                }
            }
            finally { Marshal.ReleaseComObject(stream); }
        }

        // Width and height as the phone reports them; a plain property query, no file transfer
        public Size Dimensions(Item item)
        {
            IPortableDeviceValues values;
            int hr = properties.GetValues(item.Id, sizeKeys, out values);
            if (hr < 0 || values == null)
            {
                if (headLogs++ < 25) Log("size FAILED " + item.Name + " hr=0x" + hr.ToString("X8"));
                return Size.Empty;
            }
            try
            {
                uint w, h;
                int a = values.GetUnsignedIntegerValue(ref KeyWidth, out w), b = values.GetUnsignedIntegerValue(ref KeyHeight, out h);
                if (headLogs++ < 25) Log("size " + item.Name + " = " + w + "x" + h + " hr=0x" + a.ToString("X8") + "/0x" + b.ToString("X8"));
                return a < 0 || b < 0 ? Size.Empty : new Size((int)w, (int)h);
            }
            finally { Marshal.ReleaseComObject(values); }
        }

        public void Download(Item item, string path, Action<long> progress, Func<bool> cancelled)
        {
            uint buf = 0;
            IStream stream;
            int hr = resources.GetStream(item.Id, ref ResDefault, 0, ref buf, out stream);
            if (hr < 0) Log("download FAILED " + item.RelPath + item.Name + " hr=0x" + hr.ToString("X8"));
            Check(hr, "get " + item.Name + " from the phone");
            if (stream == null) throw new IOException("The phone did not send " + item.Name + ".");
            try
            {
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    Pump(stream, fs, buf, progress, cancelled);
            }
            finally { Marshal.ReleaseComObject(stream); }
        }

        void Pump(IStream source, Stream target, uint suggested, Action<long> progress, Func<bool> cancelled)
        {
            int size = (int)Math.Max(64 * 1024, Math.Min(suggested, 1024 * 1024));
            var buffer = new byte[size];
            IntPtr readPtr = Marshal.AllocCoTaskMem(sizeof(int));
            lastBeat = Environment.TickCount;
            busy = true;
            try
            {
                long total = 0;
                while (cancelled == null || !cancelled())
                {
                    Marshal.WriteInt32(readPtr, 0);
                    source.Read(buffer, size, readPtr);
                    lastBeat = Environment.TickCount;
                    int read = Marshal.ReadInt32(readPtr);
                    if (read <= 0) break;
                    target.Write(buffer, 0, read);
                    total += read;
                    if (progress != null) progress(total);
                }
            }
            finally
            {
                busy = false;
                Marshal.FreeCoTaskMem(readPtr);
            }
        }

        public void Dispose()
        {
            try { if (watchdog != null) watchdog.Dispose(); } catch { }
            try { if (device != null) device.Close(); } catch { }
            foreach (object o in new object[] { sizeKeys, keys, resources, properties, content, device })
            {
                try { if (o != null) Marshal.ReleaseComObject(o); } catch { }
            }
            device = null;
        }
    }
}
