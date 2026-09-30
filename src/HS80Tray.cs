// HS80Tray.cs – System tray for Corsair HS80 MAX
// Build: build_hs80_tray.bat  (uses .NET Framework csc, no Visual Studio)
// Requires headsetcontrol.exe in the same folder.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

static class HS80Tray
{
    static NotifyIcon tray;
    static System.Windows.Forms.Timer timer;
    static string hcPath;
    static bool micMuted;
    static int lastBattery = -2; // -2 unknown, -1 disconnected

    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main()
    {
        try { SetProcessDPIAware(); } catch { }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string dir = AppDomain.CurrentDomain.BaseDirectory;
        hcPath = Path.Combine(dir, "headsetcontrol.exe");
        if (!File.Exists(hcPath))
            hcPath = Path.Combine(dir, "HS80Control.exe");

        tray = new NotifyIcon();
        tray.Text = "HS80 MAX";
        tray.Visible = true;
        tray.Icon = MakeIcon("---", Color.Gray, false);

        var menu = new ContextMenuStrip();
        menu.Items.Add("LED Toggle", null, (s, e) => {
            RunHc(new string[] { "-l", "toggle" });
            RefreshNow();
        });
        menu.Items.Add("Reload", null, (s, e) => RefreshNow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) => {
            tray.Visible = false;
            Application.Exit();
        });
        tray.ContextMenuStrip = menu;

        timer = new System.Windows.Forms.Timer();
        timer.Interval = 60000; // 60s like upstream
        timer.Tick += (s, e) => RefreshNow();
        timer.Start();

        // First update shortly after start
        var t = new System.Windows.Forms.Timer();
        t.Interval = 500;
        t.Tick += (s, e) => { t.Stop(); t.Dispose(); RefreshNow(); };
        t.Start();

        Application.Run();
        tray.Dispose();
    }

    static void RefreshNow()
    {
        try
        {
            string batOut = RunHc(new string[] { "-bc" });
            micMuted = QueryMicMuted();

            if (string.IsNullOrWhiteSpace(batOut))
            {
                lastBattery = -1;
                tray.Icon = MakeIcon("!", Color.Red, false);
                tray.Text = "HS80: not connected";
                return;
            }

            int val;
            if (!int.TryParse(batOut.Trim(), out val))
            {
                tray.Icon = MakeIcon("?", Color.Gray, false);
                tray.Text = "HS80: bad battery data";
                return;
            }

            lastBattery = val;
            Color color;
            string label;

            if (val < 0)
            {
                // charging (negative convention)
                color = Color.Yellow;
                label = Math.Abs(val).ToString();
                if (label == "0") label = "100";
            }
            else if (val >= 100)
            {
                color = Color.FromArgb(255, 255, 0);
                label = "100";
            }
            else if (val <= 15)
            {
                color = Color.Red;
                label = val.ToString();
            }
            else if (val <= 25)
            {
                color = Color.Yellow;
                label = val.ToString();
            }
            else
            {
                color = Color.White;
                label = val.ToString();
            }

            tray.Icon = MakeIcon(label, color, micMuted);
            string tip = "Battery " + Math.Abs(val) + "%";
            if (val < 0) tip += " (charging)";
            if (micMuted) tip += " | Mic muted";
            // NotifyIcon.Text max ~63 chars
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            tray.Text = tip;
        }
        catch (Exception ex)
        {
            tray.Text = "HS80 error";
            try { tray.Icon = MakeIcon("x", Color.Red, false); } catch { }
            Debug.WriteLine(ex);
        }
    }

    static bool QueryMicMuted()
    {
        string json = RunHc(new string[] { "-o", "json" });
        if (string.IsNullOrEmpty(json)) return false;
        // lightweight parse – avoid System.Web.Extensions dependency
        // "muted": true  or  "microphone": "muted"
        if (Regex.IsMatch(json, "\"muted\"\\s*:\\s*true", RegexOptions.IgnoreCase))
            return true;
        if (Regex.IsMatch(json, "\"microphone\"\\s*:\\s*\"muted\"", RegexOptions.IgnoreCase))
            return true;
        if (Regex.IsMatch(json, "\"status\"\\s*:\\s*\"muted\"", RegexOptions.IgnoreCase))
            return true;
        return false;
    }

    static string RunHc(string[] args)
    {
        if (!File.Exists(hcPath))
            return null;
        try
        {
            var psi = new ProcessStartInfo();
            psi.FileName = hcPath;
            psi.Arguments = string.Join(" ", args);
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            psi.WorkingDirectory = Path.GetDirectoryName(hcPath);

            using (var p = Process.Start(psi))
            {
                string stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit(8000);
                return stdout.Trim();
            }
        }
        catch
        {
            return null;
        }
    }

    static Icon MakeIcon(string text, Color color, bool slash)
    {
        // Solid badge + GDI TextRenderer = sharp digits at 16/32px tray sizes
        int size = 32;
        using (var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);

            // Opaque dark square with small corner radius look (filled rect is sharpest)
            using (var bg = new SolidBrush(Color.FromArgb(255, 15, 15, 15)))
                g.FillRectangle(bg, 0, 0, size, size);
            using (var border = new Pen(Color.FromArgb(255, 50, 50, 50), 1))
                g.DrawRectangle(border, 0, 0, size - 1, size - 1);

            // Prefer fonts with open, distinct digit shapes
            string[] fontNames = { "Consolas", "Cascadia Mono", "Segoe UI", "Arial" };
            Font font = null;
            int fontPx = text.Length >= 3 ? 14 : 18;
            foreach (string name in fontNames)
            {
                try
                {
                    font = new Font(name, fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
                    break;
                }
                catch { }
            }
            if (font == null)
                font = new Font(FontFamily.GenericSansSerif, fontPx, FontStyle.Bold, GraphicsUnit.Pixel);

            try
            {
                // Shrink until it fits
                for (int i = 0; i < 12; i++)
                {
                    Size ts = TextRenderer.MeasureText(g, text, font, new Size(size * 2, size * 2),
                        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    if (ts.Width <= size - 2 && ts.Height <= size - 1)
                        break;
                    float next = font.Size - 1f;
                    if (next < 10f) break;
                    string fn = font.FontFamily.Name;
                    font.Dispose();
                    font = new Font(fn, next, FontStyle.Bold, GraphicsUnit.Pixel);
                }

                Color fill = color;
                if (fill.R >= 200 && fill.G >= 200 && fill.B >= 200)
                    fill = Color.White;

                var flags = TextFormatFlags.HorizontalCenter
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.NoPrefix
                    | TextFormatFlags.NoPadding
                    | TextFormatFlags.SingleLine
                    | TextFormatFlags.GlyphOverhangPadding;

                var bounds = new Rectangle(0, 0, size, size);
                // GDI text – much clearer in the tray than GDI+ DrawString
                TextRenderer.DrawText(g, text, font, bounds, fill, flags);
            }
            finally
            {
                font.Dispose();
            }

            if (slash)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(Color.FromArgb(255, 255, 45, 45), 2.5f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, 3, 3, size - 4, size - 4);
                }
            }

            IntPtr hIcon = bmp.GetHicon();
            try
            {
                using (Icon tmp = Icon.FromHandle(hIcon))
                    return (Icon)tmp.Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool DestroyIcon(IntPtr handle);
}
