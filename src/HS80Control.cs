// HS80Control.cs – Corsair HS80 MAX (Windows)
// headsetcontrol-compatible CLI for HeadsetControl-GUI
// C# 5 / .NET Framework 4.x compatible
//
//   -b              battery
//   -s <0-128>      sidetone
//   -i <0-90>       inactive minutes
//   -l <0|1>        lights off/on
//   -o json|env     structured output
//   mic             mic status (human)

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

class HS80Control
{
    const ushort VID = 0x1B1C;
    const ushort PID = 0x0A97;
    const ushort USAGE_PAGE = 0xFF42;
    const ushort USAGE = 0x0001;
    const byte RID_OUT = 0x02;
    const byte EP_RX = 0x08;
    const byte EP_HS = 0x09;
    const int REPORT_LEN = 65;
    const int HIDP_STATUS_SUCCESS = 0x00110000;
    const string APP_VERSION = "4.1.0-hs80fix";
    const string API_VERSION = "1.5";

    #region Native
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr preparsed);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_FreePreparsedData(IntPtr preparsed);
    [DllImport("hid.dll", SetLastError = true)] static extern int HidP_GetCaps(IntPtr preparsed, out HIDP_CAPS caps);
    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool HidD_GetProductString(SafeFileHandle h, byte[] buf, int bufLen);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_FlushQueue(SafeFileHandle h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadFile(SafeFileHandle h, byte[] buf, int n, out int read, ref NativeOverlapped ov);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(SafeFileHandle h, byte[] buf, int n, out int written, ref NativeOverlapped ov);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetOverlappedResult(SafeFileHandle h, ref NativeOverlapped ov, out int transferred, bool wait);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CancelIo(SafeFileHandle h);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateEvent(IntPtr sec, bool manual, bool initial, string name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr hDevInfo, IntPtr devInfo, ref Guid iface, uint index, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr hDevInfo, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, uint detailSize, out uint required, IntPtr devInfoData);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiDestroyDeviceInfoList(IntPtr hDevInfo);

    const uint DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;
    const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    const uint FILE_SHARE_READ = 0x1, FILE_SHARE_WRITE = 0x2, OPEN_EXISTING = 3;
    const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    const uint WAIT_OBJECT_0 = 0;
    const int ERROR_IO_PENDING = 997;

    [StructLayout(LayoutKind.Sequential)] struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }
    [StructLayout(LayoutKind.Sequential)] struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }
    [StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved;
    }
    #endregion

    class HidCandidate { public string Path; public ushort UsagePage; public ushort Usage; public string Product; }

    static List<HidCandidate> EnumerateCorsair()
    {
        var list = new List<HidCandidate>();
        Guid hidGuid; HidD_GetHidGuid(out hidGuid);
        IntPtr hDev = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (hDev == IntPtr.Zero || hDev == new IntPtr(-1)) return list;
        try
        {
            uint index = 0;
            var ifData = new SP_DEVICE_INTERFACE_DATA();
            ifData.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
            while (SetupDiEnumDeviceInterfaces(hDev, IntPtr.Zero, ref hidGuid, index, ref ifData))
            {
                index++;
                uint needed = 0;
                SetupDiGetDeviceInterfaceDetail(hDev, ref ifData, IntPtr.Zero, 0, out needed, IntPtr.Zero);
                if (needed == 0) continue;
                IntPtr detailBuf = Marshal.AllocHGlobal((int)needed);
                try
                {
                    Marshal.WriteInt32(detailBuf, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(hDev, ref ifData, detailBuf, needed, out needed, IntPtr.Zero)) continue;
                    string path = Marshal.PtrToStringUni(IntPtr.Add(detailBuf, 4));
                    if (string.IsNullOrEmpty(path)) continue;
                    string up = path.ToUpperInvariant();
                    if (!up.Contains("VID_1B1C") || !up.Contains("PID_0A97")) continue;

                    using (var fh = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
                    {
                        if (fh.IsInvalid) continue;
                        var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)) };
                        if (!HidD_GetAttributes(fh, ref attr) || attr.VendorID != VID || attr.ProductID != PID) continue;
                        ushort page = 0, usage = 0;
                        IntPtr preparsed;
                        if (HidD_GetPreparsedData(fh, out preparsed))
                        {
                            try
                            {
                                HIDP_CAPS caps;
                                if (HidP_GetCaps(preparsed, out caps) == HIDP_STATUS_SUCCESS)
                                { page = caps.UsagePage; usage = caps.Usage; }
                            }
                            finally { HidD_FreePreparsedData(preparsed); }
                        }
                        string product = "";
                        try
                        {
                            byte[] pbuf = new byte[256];
                            if (HidD_GetProductString(fh, pbuf, pbuf.Length))
                                product = Encoding.Unicode.GetString(pbuf).TrimEnd('\0');
                        }
                        catch { }
                        list.Add(new HidCandidate { Path = path, UsagePage = page, Usage = usage, Product = product });
                    }
                }
                finally { Marshal.FreeHGlobal(detailBuf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(hDev); }
        return list;
    }

    static HidCandidate PickControl(List<HidCandidate> all)
    {
        foreach (var c in all) if (c.UsagePage == USAGE_PAGE && c.Usage == USAGE) return c;
        foreach (var c in all) if (c.UsagePage == USAGE_PAGE) return c;
        foreach (var c in all) if (c.Path.ToUpperInvariant().Contains("COL04")) return c;
        return null;
    }

    static SafeFileHandle OpenOverlapped(string path)
    {
        return CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
    }

    static bool WriteReport(SafeFileHandle h, byte[] data)
    {
        IntPtr evt = CreateEvent(IntPtr.Zero, true, false, null);
        try
        {
            var ov = new NativeOverlapped { EventHandle = evt };
            int written;
            bool ok = WriteFile(h, data, data.Length, out written, ref ov);
            if (!ok)
            {
                if (Marshal.GetLastWin32Error() != ERROR_IO_PENDING) return false;
                if (WaitForSingleObject(evt, 1000) != WAIT_OBJECT_0) { CancelIo(h); return false; }
                if (!GetOverlappedResult(h, ref ov, out written, false)) return false;
            }
            return written > 0;
        }
        finally { CloseHandle(evt); }
    }

    static byte[] ReadReport(SafeFileHandle h, int timeoutMs)
    {
        IntPtr evt = CreateEvent(IntPtr.Zero, true, false, null);
        try
        {
            var ov = new NativeOverlapped { EventHandle = evt };
            byte[] buf = new byte[64];
            int read;
            bool ok = ReadFile(h, buf, buf.Length, out read, ref ov);
            if (!ok)
            {
                if (Marshal.GetLastWin32Error() != ERROR_IO_PENDING) return null;
                if (WaitForSingleObject(evt, (uint)timeoutMs) != WAIT_OBJECT_0) { CancelIo(h); return null; }
                if (!GetOverlappedResult(h, ref ov, out read, false) || read <= 0) return null;
            }
            if (read <= 0) return null;
            var r = new byte[read];
            Array.Copy(buf, r, read);
            return r;
        }
        finally { CloseHandle(evt); }
    }

    static void Flush(SafeFileHandle h)
    {
        try { HidD_FlushQueue(h); } catch { }
        for (int i = 0; i < 8; i++) if (ReadReport(h, 20) == null) break;
    }

    static byte[] MakeOut(params byte[] payload)
    {
        var buf = new byte[REPORT_LEN];
        buf[0] = RID_OUT;
        Array.Copy(payload, 0, buf, 1, Math.Min(payload.Length, REPORT_LEN - 1));
        return buf;
    }

    static byte[] Transact(SafeFileHandle h, params byte[] payload)
    {
        Flush(h);
        if (!WriteReport(h, MakeOut(payload))) return null;
        Thread.Sleep(60);
        return ReadReport(h, 600);
    }

    static bool Wake(SafeFileHandle h)
    {
        Transact(h, EP_RX, 0x02, 0x12);
        var r = Transact(h, EP_HS, 0x02, 0x12);
        if (r == null || r.Length < 6) return false;
        return (r[4] | (r[5] << 8)) == 0x0A96;
    }

    static bool Initialize(SafeFileHandle h)
    {
        Transact(h, EP_RX, 0x02, 0x13);
        WriteReport(h, MakeOut(EP_RX, 0x01, 0x03, 0x00, 0x02));
        Thread.Sleep(40);
        Transact(h, EP_RX, 0x02, 0x12);
        WriteReport(h, MakeOut(EP_HS, 0x01, 0x03, 0x00, 0x02));
        Thread.Sleep(40);
        Flush(h);
        var r = Transact(h, EP_HS, 0x02, 0x12);
        if (r == null || r.Length < 6) return false;
        return (r[4] | (r[5] << 8)) == 0x0A96;
    }

    static int? GetBattery(SafeFileHandle h)
    {
        var r = Transact(h, EP_HS, 0x02, 0x0F);
        if (r == null || r.Length < 6) return null;
        int raw = r[4] | (r[5] << 8);
        // Some firmware reports 0-100, some 0-1000
        if (raw >= 0 && raw <= 100) return raw;
        if (raw > 100 && raw <= 1000) return raw / 10;
        return null;
    }

    // Battery status prop 0x10: 0=discharging, 1=charging, 2=full, 3=error
    static int? GetBatteryStatus(SafeFileHandle h)
    {
        var r = Transact(h, EP_HS, 0x02, 0x10);
        if (r == null || r.Length < 5) return null;
        return r[4];
    }

    // Property 0xA6: 0 = open/unmuted, 1 = muted (boom up)
    // Returns: null unknown, true = muted, false = open
    static bool? GetMicMuted(SafeFileHandle h)
    {
        var r = Transact(h, EP_HS, 0x02, 0xA6);
        if (r == null || r.Length < 5) return null;
        return r[4] == 0x01;
    }

    static bool SetSidetone(SafeFileHandle h, int level)
    {
        if (level < 0) level = 0;
        if (level > 128) level = 128;
        int mapped = (int)Math.Round(level * 1000.0 / 128.0 / 10.0) * 10;
        if (mapped > 1000) mapped = 1000;
        byte lo = (byte)(mapped & 0xFF);
        byte hi = (byte)((mapped >> 8) & 0xFF);
        if (!Initialize(h)) return false;
        var anc = MakeOut(EP_HS, 0x01, 0xD1);
        var stToggle = MakeOut(EP_HS, 0x01, 0x46);
        if (level == 0) { stToggle[6] = 0x01; anc[6] = 0x01; }
        WriteReport(h, anc); Thread.Sleep(40);
        WriteReport(h, stToggle); Thread.Sleep(40);
        return WriteReport(h, MakeOut(EP_HS, 0x01, 0x47, 0x00, lo, hi));
    }

    static bool SetInactive(SafeFileHandle h, int minutes)
    {
        if (minutes < 0) minutes = 0;
        if (minutes > 90) minutes = 90;
        if (!Initialize(h)) return false;
        byte enable = (byte)(minutes == 0 ? 0x00 : 0x01);
        WriteReport(h, MakeOut(EP_HS, 0x01, 0x0D, 0x00, enable));
        Thread.Sleep(40);
        if (minutes > 0)
        {
            uint ms = (uint)minutes * 60u * 1000u;
            WriteReport(h, MakeOut(EP_HS, 0x01, 0x0E, 0x00,
                (byte)(ms & 0xFF), (byte)((ms >> 8) & 0xFF),
                (byte)((ms >> 16) & 0xFF), (byte)((ms >> 24) & 0xFF)));
        }
        return true;
    }

    // Lights per Bragi protocol docs:
    //   OFF: SET render_mode=SW (0x03=2), SET brightness=0
    //   ON:  SET render_mode=SW, SET brightness=1000,
    //        OPEN_HANDLE 0x22, WRITE_DATA handle+size+RGB, CLOSE_HANDLE
    // Packet layout (after Report ID 0x02):
    //   [magic=0x09][cmd][args...]
    // WRITE_DATA: [0x09, 0x06, handle, data_size, 0x12, 0x00, R0,G0,B0, R1,G1,B1]
    // GET property, return full response or null
    static byte[] GetProp(SafeFileHandle h, byte endpoint, byte prop)
    {
        return Transact(h, endpoint, 0x02, prop);
    }

    // SET property (with optional 1 or 2 value bytes), wait for ack
    static bool SetProp(SafeFileHandle h, byte endpoint, byte prop, params byte[] valueBytes)
    {
        // Build: magic, CMD_SET=0x01, prop, 0x00, values...
        byte[] payload = new byte[3 + valueBytes.Length];
        payload[0] = endpoint;
        payload[1] = 0x01; // SET
        payload[2] = prop;
        // protocol: [magic, cmd, prop, 0x00, values...]
        // rebuild properly
        var list = new System.Collections.Generic.List<byte>();
        list.Add(endpoint);
        list.Add(0x01);
        list.Add(prop);
        list.Add(0x00);
        list.AddRange(valueBytes);
        Flush(h);
        if (!WriteReport(h, MakeOut(list.ToArray()))) return false;
        Thread.Sleep(50);
        // try read ack (optional)
        ReadReport(h, 200);
        return true;
    }

    // Lights per Bragi docs. Returns true if USB writes ok.
    // After change, brightness/mode are read back for verification when verbose.
    static bool SetLights(SafeFileHandle h, bool on, bool verbose)
    {
        if (!Initialize(h)) return false;

        if (!on)
        {
            // SW mode + brightness 0
            SetProp(h, EP_HS, 0x03, 0x02);
            SetProp(h, EP_HS, 0x02, 0x00, 0x00);
        }
        else
        {
            // Path A: stay in SW mode, set brightness, write white RGB
            SetProp(h, EP_HS, 0x03, 0x02);
            SetProp(h, EP_HS, 0x02, 0xE8, 0x03); // 1000

            // OPEN_HANDLE 0x22
            Flush(h);
            WriteReport(h, MakeOut(EP_HS, 0x0D, 0x22, 0x00));
            Thread.Sleep(50);
            byte[] openResp = ReadReport(h, 300);
            byte handle = 0x00;
            if (openResp != null && openResp.Length > 4)
                handle = openResp[4];

            if (verbose)
            {
                Console.WriteLine("  OPEN_HANDLE resp: " + (openResp == null ? "(none)" : BitConverter.ToString(openResp, 0, Math.Min(12, openResp.Length))));
                Console.WriteLine("  handle=" + handle.ToString("X2"));
            }

            // WRITE_DATA: [magic, 0x06, handle, size_size=8, 0x12, 0x00, RGB x2]
            Flush(h);
            WriteReport(h, MakeOut(EP_HS, 0x06, handle, 0x08,
                0x12, 0x00,
                0xFF, 0xFF, 0xFF,
                0xFF, 0xFF, 0xFF));
            Thread.Sleep(50);
            byte[] writeResp = ReadReport(h, 300);
            if (verbose)
                Console.WriteLine("  WRITE_DATA resp: " + (writeResp == null ? "(none)" : BitConverter.ToString(writeResp, 0, Math.Min(12, writeResp.Length))));

            // CLOSE_HANDLE
            Flush(h);
            WriteReport(h, MakeOut(EP_HS, 0x05, handle, 0x00));
            Thread.Sleep(50);
            ReadReport(h, 200);

            // Path B fallback: also try HW mode restore
            SetProp(h, EP_HS, 0x03, 0x01);
            SetProp(h, EP_HS, 0x02, 0xE8, 0x03);
        }

        if (verbose)
        {
            byte[] mode = GetProp(h, EP_HS, 0x03);
            byte[] br = GetProp(h, EP_HS, 0x02);
            Console.WriteLine("  mode(0x03)  : " + (mode == null ? "(none)" : BitConverter.ToString(mode, 0, Math.Min(12, mode.Length))));
            Console.WriteLine("  bright(0x02): " + (br == null ? "(none)" : BitConverter.ToString(br, 0, Math.Min(12, br.Length))));
            if (br != null && br.Length >= 6)
            {
                int v = br[4] | (br[5] << 8);
                Console.WriteLine("  brightness value = " + v);
            }
        }
        return true;
    }

    static bool SetLights(SafeFileHandle h, bool on)
    {
        return SetLights(h, on, false);
    }

    static int? GetBrightness(SafeFileHandle h)
    {
        var r = Transact(h, EP_HS, 0x02, 0x02);
        if (r == null || r.Length < 6) return null;
        return r[4] | (r[5] << 8);
    }

    static void PrintHelp()
    {
        Console.WriteLine("HS80Control – Corsair HS80 MAX (headsetcontrol-compatible)");
        Console.WriteLine();
        Console.WriteLine("  -b, --battery              Query battery");
        Console.WriteLine("  -s, --sidetone <0-128>     Set sidetone");
        Console.WriteLine("  -i, --inactive <0-90>      Inactive timer (minutes, 0=off)");
        Console.WriteLine("  -l, --light <0|1>          LED off (0) or on (1)");
        Console.WriteLine("  -o, --output <json|env>    Output format");
        Console.WriteLine("  -h, --help");
        Console.WriteLine();
        Console.WriteLine("Also: battery | mic | sidetone N | inactive N | list");
        Console.WriteLine("For GUI: copy/rename to headsetcontrol.exe");
    }

    static string JsonEscape(string s)
    {
        if (s == null) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    static int Main(string[] args)
    {
        bool wantBattery = false, wantMic = false, wantHelp = false, wantList = false, wantConnected = false;
        bool shortOutput = false; // SystemTray: -bc prints bare integer
        string output = "human";
        int? sidetone = null, inactive = null, lights = null;
        bool lightsToggle = false;
        bool anyAction = false, anyQuery = false;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string al = a.ToLowerInvariant();

            if (al == "-h" || al == "--help" || al == "help") { wantHelp = true; continue; }
            if (al == "list" || al == "--list") { wantList = true; continue; }
            if (al == "-bc" || al == "-cb")
            {
                // HeadsetControl-SystemTray style: battery, short numeric only
                wantBattery = true; shortOutput = true; anyQuery = true; continue;
            }
            if (al == "-b" || al == "--battery" || al == "battery") { wantBattery = true; anyQuery = true; continue; }
            if (al == "mic") { wantMic = true; anyQuery = true; continue; }
            if (al == "-c" || al == "--short" || al == "--short-output")
            {
                // short numeric output (for scripts / SystemTray)
                shortOutput = true; anyQuery = true; continue;
            }
            if (al == "--connected") { wantConnected = true; anyQuery = true; continue; }
            if (al == "-n" || al == "--not-short") continue;

            if (al == "-o" || al == "--output")
            {
                if (i + 1 < args.Length) output = args[++i].ToLowerInvariant();
                continue;
            }
            if (al.StartsWith("-o=") || al.StartsWith("--output="))
            {
                output = al.Substring(al.IndexOf('=') + 1);
                continue;
            }

            if (al == "-s" || al == "--sidetone" || al == "sidetone")
            {
                int sv;
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out sv))
                { sidetone = sv; i++; anyAction = true; }
                continue;
            }
            if (al.StartsWith("-s=") || al.StartsWith("--sidetone="))
            {
                int sv;
                if (int.TryParse(al.Substring(al.IndexOf('=') + 1), out sv))
                { sidetone = sv; anyAction = true; }
                continue;
            }

            if (al == "-i" || al == "--inactive" || al == "inactive")
            {
                int iv;
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out iv))
                { inactive = iv; i++; anyAction = true; }
                continue;
            }
            if (al.StartsWith("-i=") || al.StartsWith("--inactive="))
            {
                int iv;
                if (int.TryParse(al.Substring(al.IndexOf('=') + 1), out iv))
                { inactive = iv; anyAction = true; }
                continue;
            }

            if (al == "-l" || al == "--light" || al == "--lights" || al == "light" || al == "lights")
            {
                if (i + 1 < args.Length)
                {
                    string nv = args[i + 1].ToLowerInvariant();
                    if (nv == "toggle" || nv == "t")
                    { lightsToggle = true; i++; anyAction = true; }
                    else
                    {
                        int lv;
                        if (int.TryParse(args[i + 1], out lv))
                        { lights = lv != 0 ? 1 : 0; i++; anyAction = true; }
                    }
                }
                continue;
            }
            if (al.StartsWith("-l=") || al.StartsWith("--light="))
            {
                int lv;
                if (int.TryParse(al.Substring(al.IndexOf('=') + 1), out lv))
                { lights = lv != 0 ? 1 : 0; anyAction = true; }
                continue;
            }
        }

        if (wantHelp) { PrintHelp(); return 0; }

        if (!anyQuery && !anyAction && !wantList)
        {
            wantBattery = true;
            wantMic = true;
            anyQuery = true;
        }

        var all = EnumerateCorsair();

        if (wantList)
        {
            Console.WriteLine("Found {0} HID collection(s) for VID_1B1C/PID_0A97:", all.Count);
            foreach (var c in all)
            {
                Console.WriteLine("  UP=0x{0:X4} U=0x{1:X4}  {2}", c.UsagePage, c.Usage, c.Product);
                Console.WriteLine("    {0}", c.Path);
            }
            return all.Count > 0 ? 0 : 1;
        }

        if (all.Count == 0)
        {
            if (output == "json")
            {
                Console.WriteLine("{");
                Console.WriteLine("  \"name\": \"HeadsetControl\",");
                Console.WriteLine("  \"version\": \"" + APP_VERSION + "\",");
                Console.WriteLine("  \"api_version\": \"" + API_VERSION + "\",");
                Console.WriteLine("  \"device_count\": 0,");
                Console.WriteLine("  \"devices\": []");
                Console.WriteLine("}");
            }
            else
                Console.WriteLine("No supported headset found");
            return 1;
        }

        var pick = PickControl(all);
        if (pick == null)
        {
            if (output != "json") Console.WriteLine("No control collection (need UP 0xFF42).");
            return 1;
        }

        string product = string.IsNullOrEmpty(pick.Product) ? "CORSAIR HS80 MAX WIRELESS Gaming Receiver" : pick.Product;

        using (var h = OpenOverlapped(pick.Path))
        {
            if (h.IsInvalid)
            {
                if (output != "json")
                    Console.WriteLine("Failed to open device (error {0}).", Marshal.GetLastWin32Error());
                return 1;
            }

            bool connected = Wake(h);
            int? bat = null;
            int? batStatus = null; // 0 discharge, 1 charge, 2 full
            bool? micMuted = null;
            int? brightness = null;
            bool sidetoneOk = false, inactiveOk = false, lightsOk = false;

            if (connected)
            {
                if (wantBattery || wantConnected || shortOutput || output == "json" || output == "env")
                {
                    bat = GetBattery(h);
                    batStatus = GetBatteryStatus(h);
                }
                if (wantMic || output == "json" || output == "env")
                    micMuted = GetMicMuted(h);
                if (output == "json" || output == "env" || lightsToggle)
                    brightness = GetBrightness(h);

                if (sidetone.HasValue)
                    sidetoneOk = SetSidetone(h, sidetone.Value);
                if (inactive.HasValue)
                    inactiveOk = SetInactive(h, inactive.Value);
                if (lightsToggle)
                {
                    bool currentlyOn = brightness.HasValue && brightness.Value > 0;
                    lightsOk = SetLights(h, !currentlyOn, false);
                    if (lightsOk) brightness = currentlyOn ? 0 : 1000;
                }
                else if (lights.HasValue)
                {
                    lightsOk = SetLights(h, lights.Value != 0, output == "human" && !shortOutput);
                    if (lightsOk) brightness = lights.Value != 0 ? 1000 : 0;
                }
            }

            if (output == "json")
            {
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"name\": \"HeadsetControl\",");
                sb.AppendLine("  \"version\": \"" + APP_VERSION + "\",");
                sb.AppendLine("  \"api_version\": \"" + API_VERSION + "\",");
                sb.AppendLine("  \"hidapi_version\": \"n/a\",");
                if (anyAction)
                {
                    sb.AppendLine("  \"actions\": [");
                    bool firstA = true;
                    if (sidetone.HasValue)
                    {
                        sb.Append("    { \"capability\": \"CAP_SIDETONE\", \"device\": \"" + JsonEscape(product) + "\", \"status\": \"" + (sidetoneOk ? "success" : "failure") + "\" }");
                        firstA = false;
                    }
                    if (inactive.HasValue)
                    {
                        if (!firstA) sb.AppendLine(",");
                        sb.Append("    { \"capability\": \"CAP_INACTIVE_TIME\", \"device\": \"" + JsonEscape(product) + "\", \"status\": \"" + (inactiveOk ? "success" : "failure") + "\" }");
                        firstA = false;
                    }
                    if (lights.HasValue)
                    {
                        if (!firstA) sb.AppendLine(",");
                        sb.Append("    { \"capability\": \"CAP_LIGHTS\", \"device\": \"" + JsonEscape(product) + "\", \"status\": \"" + (lightsOk ? "success" : "failure") + "\" }");
                        firstA = false;
                    }
                    sb.AppendLine();
                    sb.AppendLine("  ],");
                }
                sb.AppendLine("  \"device_count\": 1,");
                sb.AppendLine("  \"devices\": [");
                sb.AppendLine("    {");
                sb.AppendLine("      \"status\": \"" + (connected ? "success" : "failure") + "\",");
                sb.AppendLine("      \"device\": \"" + JsonEscape(product) + "\",");
                sb.AppendLine("      \"vendor\": \"Corsair\",");
                sb.AppendLine("      \"product\": \"" + JsonEscape(product) + "\",");
                sb.AppendLine("      \"id_vendor\": \"0x1b1c\",");
                sb.AppendLine("      \"id_product\": \"0x0a97\",");
                sb.AppendLine("      \"capabilities\": [\"CAP_SIDETONE\", \"CAP_BATTERY_STATUS\", \"CAP_INACTIVE_TIME\", \"CAP_LIGHTS\", \"CAP_MICROPHONE_MUTE\"],");
                sb.AppendLine("      \"capabilities_str\": [\"sidetone\", \"battery\", \"inactive_time\", \"lights\", \"microphone_mute\"],");

                if (bat.HasValue)
                {
                    sb.AppendLine("      \"battery\": {");
                    sb.AppendLine("        \"status\": \"BATTERY_AVAILABLE\",");
                    sb.AppendLine("        \"level\": " + bat.Value + ",");
                    if (micMuted.HasValue)
                        sb.AppendLine("        \"microphone\": \"" + (micMuted.Value ? "muted" : "open") + "\"");
                    else
                        sb.AppendLine("        \"microphone\": \"unknown\"");
                    sb.AppendLine("      },");
                }
                else
                {
                    sb.AppendLine("      \"battery\": { \"status\": \"BATTERY_UNAVAILABLE\", \"level\": -1 },");
                }

                // Microphone boom / mute (GUI-friendly)
                if (micMuted.HasValue)
                {
                    sb.AppendLine("      \"microphone\": {");
                    sb.AppendLine("        \"muted\": " + (micMuted.Value ? "true" : "false") + ",");
                    sb.AppendLine("        \"status\": \"" + (micMuted.Value ? "muted" : "open") + "\"");
                    sb.AppendLine("      },");
                }
                else
                {
                    sb.AppendLine("      \"microphone\": { \"muted\": null, \"status\": \"unknown\" },");
                }

                // Lights
                if (brightness.HasValue)
                {
                    int on = brightness.Value > 0 ? 1 : 0;
                    sb.AppendLine("      \"lights\": {");
                    sb.AppendLine("        \"status\": \"success\",");
                    sb.AppendLine("        \"level\": " + on + ",");
                    sb.AppendLine("        \"brightness\": " + brightness.Value);
                    sb.AppendLine("      },");
                }
                else
                {
                    sb.AppendLine("      \"lights\": { \"status\": \"unavailable\", \"level\": -1 },");
                }

                sb.AppendLine("      \"equalizer\": null");
                sb.AppendLine("    }");
                sb.AppendLine("  ]");
                sb.AppendLine("}");
                Console.Write(sb.ToString());
                return connected ? 0 : 1;
            }

            if (output == "env")
            {
                Console.WriteLine("DEVICE_COUNT=1");
                Console.WriteLine("DEVICE_0_NAME=\"" + product.Replace("\"", "") + "\"");
                Console.WriteLine("DEVICE_0_VENDOR=Corsair");
                Console.WriteLine("DEVICE_0_ID_VENDOR=0x1b1c");
                Console.WriteLine("DEVICE_0_ID_PRODUCT=0x0a97");
                if (bat.HasValue)
                {
                    Console.WriteLine("DEVICE_0_BATTERY_STATUS=BATTERY_AVAILABLE");
                    Console.WriteLine("DEVICE_0_BATTERY_LEVEL=" + bat.Value);
                }
                else
                {
                    Console.WriteLine("DEVICE_0_BATTERY_STATUS=BATTERY_UNAVAILABLE");
                    Console.WriteLine("DEVICE_0_BATTERY_LEVEL=-1");
                }
                if (micMuted.HasValue)
                    Console.WriteLine("DEVICE_0_MICROPHONE_MUTED=" + (micMuted.Value ? "1" : "0"));
                if (brightness.HasValue)
                    Console.WriteLine("DEVICE_0_LIGHTS=" + (brightness.Value > 0 ? "1" : "0"));
                return connected ? 0 : 1;
            }

            // Short numeric output for HeadsetControl-SystemTray (-bc)
            // Prints only an integer: level, or negative when charging, or nothing usable -> fail
            if (shortOutput)
            {
                if (!connected || !bat.HasValue)
                    return 1;
                int level = bat.Value;
                // SystemTray treats value < 0 as charging icon
                if (batStatus.HasValue && batStatus.Value == 1)
                    level = -level;
                if (batStatus.HasValue && batStatus.Value == 2 && level >= 100)
                    level = 100;
                Console.Write(level.ToString());
                return 0;
            }

            // Human
            if (!connected)
            {
                Console.WriteLine("Headset not connected");
                return 1;
            }
            if (wantBattery || wantConnected)
                Console.WriteLine(bat.HasValue ? "Battery: " + bat.Value + "%" : "Battery: Unavailable");
            if (wantMic)
            {
                if (micMuted.HasValue)
                    Console.WriteLine("Microphone: " + (micMuted.Value ? "muted (boom up)" : "open"));
                else
                    Console.WriteLine("Microphone: unknown");
            }
            if (sidetone.HasValue)
                Console.WriteLine(sidetoneOk ? "Success" : "Failed to set sidetone");
            if (inactive.HasValue)
                Console.WriteLine(inactiveOk ? "Success" : "Failed to set inactive time");
            if (lights.HasValue)
                Console.WriteLine(lightsOk ? "Success" : "Failed to set lights");
            return 0;
        }
    }
}
