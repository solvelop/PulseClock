// Pulse tray clock (C# port). Shows the current @pulse beat AS the system-tray
// icon. Left-click opens a flyout with the clock + a two-way converter;
// right-click opens a menu. No network calls: the clock and converter are pure
// UTC math, and the booking link is built from a handle stored locally.
//
// Written to compile with the in-box .NET Framework C# compiler (csc.exe) - no
// C# 6+ features - so build.cmd needs no Visual Studio or SDK.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PulseClock
{
    static class Program
    {
        private static System.Threading.Mutex _mutex;

        [STAThread]
        static void Main()
        {
            bool createdNew;
            _mutex = new System.Threading.Mutex(true, "PulseClock_SingleInstance_v1", out createdNew);
            if (!createdNew) return; // already running

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppSettings.Load();
            Application.Run(new TrayContext());
            GC.KeepAlive(_mutex);
        }
    }

    // -----------------------------------------------------------------------
    // Universal Internet Time math. @500 = noon UTC. Day split into 1000 beats.
    // -----------------------------------------------------------------------
    static class PulseTime
    {
        public static double BeatFraction(DateTime d)
        {
            DateTime u = d.ToUniversalTime();
            double msOfDay = (u.Hour * 3600.0 + u.Minute * 60.0 + u.Second) * 1000.0 + u.Millisecond;
            return msOfDay / 86400.0; // 0..1000
        }

        public static int WholeBeat(DateTime d)
        {
            int w = (int)Math.Floor(BeatFraction(d));
            if (w < 0) w = 0;
            if (w > 999) w = 999;
            return w;
        }

        public static int LocalTimeToBeat(DateTime localTime)
        {
            DateTime now = DateTime.Now;
            DateTime composed = new DateTime(now.Year, now.Month, now.Day, localTime.Hour, localTime.Minute, 0, DateTimeKind.Local);
            return WholeBeat(composed);
        }

        public static DateTime BeatToLocalTime(int beat)
        {
            if (beat < 0) beat = 0;
            if (beat > 999) beat = 999;
            DateTime nowUtc = DateTime.UtcNow;
            double msOfDay = (beat / 1000.0) * 86400000.0;
            DateTime midnightUtc = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, 0, 0, 0, DateTimeKind.Utc);
            return midnightUtc.AddMilliseconds(msOfDay).ToLocalTime();
        }
    }

    // -----------------------------------------------------------------------
    // Local settings (booking handle). %AppData%\PulseClock\settings.txt
    // -----------------------------------------------------------------------
    static class AppSettings
    {
        public static string Handle = "";

        private static string FolderPath()
        {
            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(baseDir, "PulseClock");
        }

        private static string FilePath()
        {
            return Path.Combine(FolderPath(), "settings.txt");
        }

        public static void Load()
        {
            try
            {
                string fp = FilePath();
                if (!File.Exists(fp)) return;
                foreach (string line in File.ReadAllLines(fp))
                {
                    int ix = line.IndexOf('=');
                    if (ix > 0)
                    {
                        string key = line.Substring(0, ix).Trim().ToLowerInvariant();
                        string val = line.Substring(ix + 1).Trim();
                        if (key == "handle") Handle = val;
                    }
                }
            }
            catch { /* ignore a corrupt settings file */ }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(FolderPath());
                File.WriteAllText(FilePath(), "handle=" + Handle + Environment.NewLine);
            }
            catch { /* ignore write failures */ }
        }

        public static string BookingUrl()
        {
            if (string.IsNullOrEmpty(Handle)) return "";
            return "https://pulses.day/book/" + Handle;
        }
    }

    // -----------------------------------------------------------------------
    // Start-with-Windows via the per-user Run key.
    // -----------------------------------------------------------------------
    static class StartupReg
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "PulseClock";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    return k.GetValue(ValueName) != null;
                }
            }
            catch { return false; }
        }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (enabled)
                        k.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                    else if (k.GetValue(ValueName) != null)
                        k.DeleteValue(ValueName, false);
                }
            }
            catch { /* ignore registry failures */ }
        }
    }

    static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr handle);
    }

    // -----------------------------------------------------------------------
    // Tray controller: owns the NotifyIcon, the tick timer, and the menus.
    // -----------------------------------------------------------------------
    sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon _ni;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly ContextMenuStrip _menu;
        private ToolStripMenuItem _startItem;
        private int _lastBeat = -1;
        private FrmFlyout _flyout;

        private static readonly Color Teal = Color.FromArgb(31, 122, 100);

        public TrayContext()
        {
            _menu = BuildMenu();

            _ni = new NotifyIcon();
            _ni.Visible = true;
            _ni.Text = "Pulse";
            _ni.ContextMenuStrip = _menu;
            _ni.MouseClick += OnIconClick;

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 1000;
            _timer.Tick += OnTick;
            _timer.Start();

            OnTick(null, EventArgs.Empty); // paint immediately
        }

        private ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();

            var openFlyout = new ToolStripMenuItem("Show clock");
            openFlyout.Click += delegate { ToggleFlyout(); };
            m.Items.Add(openFlyout);

            m.Items.Add(new ToolStripSeparator());

            var copyLink = new ToolStripMenuItem("Copy booking link");
            copyLink.Click += delegate { CopyBookingLink(); };
            m.Items.Add(copyLink);

            var openPulse = new ToolStripMenuItem("Open Pulse");
            openPulse.Click += delegate { OpenUrl("https://pulses.day/meetings.aspx"); };
            m.Items.Add(openPulse);

            m.Items.Add(new ToolStripSeparator());

            var settings = new ToolStripMenuItem("Settings...");
            settings.Click += delegate { ShowSettings(); };
            m.Items.Add(settings);

            _startItem = new ToolStripMenuItem("Start with Windows");
            _startItem.CheckOnClick = true;
            _startItem.Checked = StartupReg.IsEnabled();
            _startItem.Click += delegate { StartupReg.SetEnabled(_startItem.Checked); };
            m.Items.Add(_startItem);

            m.Items.Add(new ToolStripSeparator());

            var quit = new ToolStripMenuItem("Quit");
            quit.Click += delegate { QuitApp(); };
            m.Items.Add(quit);

            return m;
        }

        private void OnTick(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            double frac = PulseTime.BeatFraction(now);
            int whole = (int)Math.Floor(frac);
            if (whole < 0) whole = 0;
            if (whole > 999) whole = 999;

            // Re-render the tray icon only when the whole beat changes (~86.4s).
            if (whole != _lastBeat)
            {
                _lastBeat = whole;
                Icon newIcon = MakeBeatIcon(whole);
                Icon old = _ni.Icon;
                _ni.Icon = newIcon;
                if (old != null) old.Dispose();
            }

            int centi = (int)Math.Floor((frac - Math.Floor(frac)) * 100.0);
            _ni.Text = "@" + whole.ToString("000", CultureInfo.InvariantCulture) + "." +
                       centi.ToString("00", CultureInfo.InvariantCulture) +
                       "  " + now.ToString("HH:mm", CultureInfo.InvariantCulture) + " local";
        }

        private Icon MakeBeatIcon(int beat)
        {
            using (var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);
                    string s = beat.ToString(CultureInfo.InvariantCulture);
                    float px = s.Length >= 3 ? 15f : 20f;
                    using (var f = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var sf = new StringFormat())
                    using (var br = new SolidBrush(Teal))
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        g.DrawString(s, f, br, new RectangleF(0, 0, 32, 32), sf);
                    }
                }

                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (Icon temp = Icon.FromHandle(hIcon))
                        return (Icon)temp.Clone();
                }
                finally
                {
                    NativeMethods.DestroyIcon(hIcon);
                }
            }
        }

        private void OnIconClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) ToggleFlyout();
        }

        private void ToggleFlyout()
        {
            if (_flyout != null && !_flyout.IsDisposed && _flyout.Visible)
            {
                _flyout.Hide();
                return;
            }
            if (_flyout == null || _flyout.IsDisposed)
            {
                _flyout = new FrmFlyout();
                _flyout.CopyLinkRequested += delegate { CopyBookingLink(); };
                _flyout.OpenPulseRequested += delegate { OpenUrl("https://pulses.day/meetings.aspx"); };
            }
            _flyout.ShowNearTray();
        }

        private void CopyBookingLink()
        {
            string url = AppSettings.BookingUrl();
            if (string.IsNullOrEmpty(url))
            {
                ShowSettings();
                return;
            }
            try
            {
                Clipboard.SetText(url);
                _ni.ShowBalloonTip(1500, "Pulse", "Booking link copied", ToolTipIcon.Info);
            }
            catch { /* clipboard can transiently fail */ }
        }

        private void ShowSettings()
        {
            using (var f = new FrmSettings())
                f.ShowDialog();
        }

        private void OpenUrl(string url)
        {
            try
            {
                var psi = new ProcessStartInfo(url);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch { /* ignore if no default browser */ }
        }

        private void QuitApp()
        {
            _timer.Stop();
            _ni.Visible = false;
            _ni.Dispose();
            ExitThread();
        }
    }

    // -----------------------------------------------------------------------
    // Flyout: live clock + two-way converter. Borderless, hides on losing focus.
    // -----------------------------------------------------------------------
    sealed class FrmFlyout : Form
    {
        public event Action CopyLinkRequested;
        public event Action OpenPulseRequested;

        private readonly Label _beat = new Label();
        private readonly Label _localUtc = new Label();
        private readonly DateTimePicker _dtpLocal = new DateTimePicker();
        private readonly Label _lblL2B = new Label();
        private readonly NumericUpDown _nudBeat = new NumericUpDown();
        private readonly Label _lblB2L = new Label();
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();

        private static readonly Color Teal = Color.FromArgb(31, 122, 100);
        private static readonly Color Ink = Color.FromArgb(20, 23, 28);
        private static readonly Color Muted = Color.FromArgb(91, 100, 112);

        public FrmFlyout()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.White;
            Width = 296;
            Height = 280;
            Font = new Font("Segoe UI", 9f);
            Padding = new Padding(1);
            Paint += delegate (object s, PaintEventArgs e)
            {
                using (var p = new Pen(Color.FromArgb(40, 20, 23, 28)))
                    e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            };

            var title = new Label();
            title.Text = "pulse";
            title.ForeColor = Ink;
            title.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            title.SetBounds(16, 12, 120, 22);
            Controls.Add(title);

            _beat.ForeColor = Teal;
            _beat.Font = new Font("Consolas", 30f, FontStyle.Bold);
            _beat.TextAlign = ContentAlignment.MiddleCenter;
            _beat.SetBounds(8, 36, Width - 16, 48);
            Controls.Add(_beat);

            _localUtc.ForeColor = Muted;
            _localUtc.Font = new Font("Consolas", 9.5f);
            _localUtc.TextAlign = ContentAlignment.MiddleCenter;
            _localUtc.SetBounds(8, 84, Width - 16, 18);
            Controls.Add(_localUtc);

            var l1 = new Label();
            l1.Text = "Local";
            l1.ForeColor = Muted;
            l1.SetBounds(16, 120, 44, 22);
            Controls.Add(l1);

            _dtpLocal.Format = DateTimePickerFormat.Time;
            _dtpLocal.ShowUpDown = true;
            _dtpLocal.SetBounds(64, 116, 90, 24);
            _dtpLocal.Value = DateTime.Now;
            _dtpLocal.ValueChanged += delegate { RecalcL2B(); };
            Controls.Add(_dtpLocal);

            _lblL2B.Font = new Font("Consolas", 11f, FontStyle.Bold);
            _lblL2B.ForeColor = Teal;
            _lblL2B.TextAlign = ContentAlignment.MiddleRight;
            _lblL2B.SetBounds(164, 116, 116, 24);
            Controls.Add(_lblL2B);

            var l2 = new Label();
            l2.Text = "@pulse";
            l2.ForeColor = Muted;
            l2.SetBounds(16, 156, 48, 22);
            Controls.Add(l2);

            _nudBeat.Minimum = 0;
            _nudBeat.Maximum = 999;
            _nudBeat.SetBounds(64, 152, 90, 24);
            _nudBeat.ValueChanged += delegate { RecalcB2L(); };
            Controls.Add(_nudBeat);

            _lblB2L.Font = new Font("Consolas", 11f, FontStyle.Bold);
            _lblB2L.ForeColor = Ink;
            _lblB2L.TextAlign = ContentAlignment.MiddleRight;
            _lblB2L.SetBounds(164, 152, 116, 24);
            Controls.Add(_lblB2L);

            var btnCopy = new Button();
            btnCopy.Text = "Copy booking link";
            btnCopy.FlatStyle = FlatStyle.Flat;
            btnCopy.BackColor = Teal;
            btnCopy.ForeColor = Color.White;
            btnCopy.FlatAppearance.BorderSize = 0;
            btnCopy.SetBounds(16, 196, 152, 32);
            btnCopy.Click += delegate
            {
                if (CopyLinkRequested != null) CopyLinkRequested();
            };
            Controls.Add(btnCopy);

            var btnOpen = new Button();
            btnOpen.Text = "Open Pulse";
            btnOpen.FlatStyle = FlatStyle.Flat;
            btnOpen.BackColor = Color.White;
            btnOpen.ForeColor = Ink;
            btnOpen.FlatAppearance.BorderColor = Color.FromArgb(60, 20, 23, 28);
            btnOpen.SetBounds(176, 196, 104, 32);
            btnOpen.Click += delegate
            {
                if (OpenPulseRequested != null) OpenPulseRequested();
            };
            Controls.Add(btnOpen);

            var hint = new Label();
            hint.Text = "@500 = noon UTC. The day has 1000 beats.";
            hint.ForeColor = Muted;
            hint.Font = new Font("Segoe UI", 8f);
            hint.SetBounds(16, 238, Width - 32, 30);
            Controls.Add(hint);

            _timer.Interval = 1000;
            _timer.Tick += delegate { Tick(); };
            Deactivate += delegate { Hide(); };
        }

        private void Tick()
        {
            DateTime now = DateTime.Now;
            double frac = PulseTime.BeatFraction(now);
            int whole = (int)Math.Floor(frac);
            int centi = (int)Math.Floor((frac - Math.Floor(frac)) * 100.0);
            _beat.Text = "@" + whole.ToString("000", CultureInfo.InvariantCulture) + "." +
                         centi.ToString("00", CultureInfo.InvariantCulture);
            _localUtc.Text = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " local   " +
                             now.ToUniversalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";
        }

        private void RecalcL2B()
        {
            int beat = PulseTime.LocalTimeToBeat(_dtpLocal.Value);
            _lblL2B.Text = "= @" + beat.ToString("000", CultureInfo.InvariantCulture);
        }

        private void RecalcB2L()
        {
            DateTime lt = PulseTime.BeatToLocalTime((int)_nudBeat.Value);
            _lblB2L.Text = "= " + lt.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        public void ShowNearTray()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 8, wa.Bottom - Height - 8);
            _dtpLocal.Value = DateTime.Now;
            RecalcL2B();
            RecalcB2L();
            Tick();
            _timer.Start();
            Show();
            Activate();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) _timer.Stop();
        }
    }

    // -----------------------------------------------------------------------
    // Settings dialog: set the booking handle.
    // -----------------------------------------------------------------------
    sealed class FrmSettings : Form
    {
        private readonly TextBox _txtHandle = new TextBox();

        public FrmSettings()
        {
            Text = "Pulse settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(360, 150);
            Font = new Font("Segoe UI", 9f);

            var lbl = new Label();
            lbl.Text = "Your Pulse booking handle";
            lbl.SetBounds(16, 16, 320, 20);
            Controls.Add(lbl);

            var prefix = new Label();
            prefix.Text = "pulses.day/book/";
            prefix.ForeColor = Color.FromArgb(91, 100, 112);
            prefix.AutoSize = true;
            prefix.SetBounds(16, 44, 120, 20);
            Controls.Add(prefix);

            _txtHandle.SetBounds(132, 40, 210, 24);
            _txtHandle.Text = AppSettings.Handle;
            Controls.Add(_txtHandle);

            var ok = new Button();
            ok.Text = "Save";
            ok.SetBounds(176, 100, 80, 30);
            ok.Click += delegate
            {
                AppSettings.Handle = _txtHandle.Text.Trim().TrimStart('/');
                AppSettings.Save();
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(ok);

            var cancel = new Button();
            cancel.Text = "Cancel";
            cancel.SetBounds(262, 100, 80, 30);
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
