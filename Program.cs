using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MatrixRain
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0].ToLowerInvariant().Trim() : "/c";
            if (mode.Length > 2) mode = mode.Substring(0, 2); // handles "/c:1234"

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (mode == "/w")
            {
                bool created;
                using (var m = new System.Threading.Mutex(true, "MatrixRainWatcher", out created))
                {
                    if (!created) return;
                    Application.Run(new Watcher());
                }
            }
            else if (mode == "/s" || mode == "/t")
                Application.Run(new RainForm(Settings.Load()));
            else if (mode == "/p")
                return; // tiny preview pane in the Windows dialog: nothing to draw
            else
                Application.Run(new SettingsForm());
        }
    }

    sealed class Settings
    {
        public int Speed = 5;          // 1..10
        public int Size = 6;           // 1..10 (bigger = larger characters)
        public int Density = 8;        // 1..10 (more simultaneous streams per column)
        public int Brightness = 8;     // 1..10 (10 = full, 8 = 80%)
        public int IdleMinutes = 10;   // watcher mode: start after this many idle minutes
        public bool TimeLimit = true;  // watcher mode: only run inside the time window
        public int WinStart = 120;     // minutes after midnight (02:00)
        public int WinEnd = 600;       // minutes after midnight (10:00)
        public bool Glow = true;
        public bool Retro = true;      // scanlines + slight pixelation
        public bool Intro = true;      // desktop-to-rain start
        public bool WakeClick = true;  // left mouse button
        public bool WakeSpace = true;  // space bar

        const string KeyPath = @"Software\MatrixRain";

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    if (k == null) return s;
                    s.Speed = Clamp((int)k.GetValue("Speed", s.Speed), 1, 10);
                    s.Size = Clamp((int)k.GetValue("Size", s.Size), 1, 10);
                    s.Density = Clamp((int)k.GetValue("Density", s.Density), 1, 10);
                    s.Brightness = Clamp((int)k.GetValue("Brightness", s.Brightness), 1, 10);
                    s.IdleMinutes = Clamp((int)k.GetValue("IdleMinutes", s.IdleMinutes), 1, 120);
                    s.TimeLimit = (int)k.GetValue("TimeLimit", 1) != 0;
                    s.WinStart = Clamp((int)k.GetValue("WinStart", s.WinStart), 0, 1439);
                    s.WinEnd = Clamp((int)k.GetValue("WinEnd", s.WinEnd), 0, 1439);
                    s.Glow = (int)k.GetValue("Glow", 1) != 0;
                    s.Retro = (int)k.GetValue("Retro", 1) != 0;
                    s.Intro = (int)k.GetValue("Intro", 1) != 0;
                    s.WakeClick = (int)k.GetValue("WakeClick", 1) != 0;
                    s.WakeSpace = (int)k.GetValue("WakeSpace", 1) != 0;
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    k.SetValue("Speed", Speed);
                    k.SetValue("Size", Size);
                    k.SetValue("Density", Density);
                    k.SetValue("Brightness", Brightness);
                    k.SetValue("IdleMinutes", IdleMinutes);
                    k.SetValue("TimeLimit", TimeLimit ? 1 : 0);
                    k.SetValue("WinStart", WinStart);
                    k.SetValue("WinEnd", WinEnd);
                    k.SetValue("Glow", Glow ? 1 : 0);
                    k.SetValue("Retro", Retro ? 1 : 0);
                    k.SetValue("Intro", Intro ? 1 : 0);
                    k.SetValue("WakeClick", WakeClick ? 1 : 0);
                    k.SetValue("WakeSpace", WakeSpace ? 1 : 0);
                }
            }
            catch { }
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }

    sealed class SettingsForm : Form
    {
        readonly Settings s = Settings.Load();
        readonly TrackBar speed = new TrackBar { Minimum = 1, Maximum = 10, TickFrequency = 1, Width = 260 };
        readonly TrackBar bright = new TrackBar { Minimum = 1, Maximum = 10, TickFrequency = 1, Width = 260 };
        readonly TrackBar size = new TrackBar { Minimum = 1, Maximum = 10, TickFrequency = 1, Width = 260 };
        readonly TrackBar density = new TrackBar { Minimum = 1, Maximum = 10, TickFrequency = 1, Width = 260 };
        readonly NumericUpDown idle = new NumericUpDown { Minimum = 1, Maximum = 120, Width = 60 };
        readonly CheckBox limit = new CheckBox { Text = "Only run between these times (background mode):", AutoSize = true };
        readonly DateTimePicker tStart = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 70 };
        readonly DateTimePicker tEnd = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 70 };
        readonly CheckBox glow = new CheckBox { Text = "Glow around characters", AutoSize = true };
        readonly CheckBox retro = new CheckBox { Text = "Old monitor look (scanlines + pixelation)", AutoSize = true };
        readonly CheckBox intro = new CheckBox { Text = "Start with the desktop turning into rain", AutoSize = true };
        readonly CheckBox wClick = new CheckBox { Text = "Left mouse click", AutoSize = true };
        readonly CheckBox wSpace = new CheckBox { Text = "Space bar", AutoSize = true };

        public SettingsForm()
        {
            Text = "Matrix Rain Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(330, 790);

            int y = 12;
            Controls.Add(new Label { Text = "Rain speed (slow to fast)", AutoSize = true, Left = 16, Top = y }); y += 20;
            speed.Left = 10; speed.Top = y; speed.Value = s.Speed; Controls.Add(speed); y += 50;
            Controls.Add(new Label { Text = "Rain density (light to heavy)", AutoSize = true, Left = 16, Top = y }); y += 20;
            density.Left = 10; density.Top = y; density.Value = s.Density; Controls.Add(density); y += 50;
            Controls.Add(new Label { Text = "Character size (small to large)", AutoSize = true, Left = 16, Top = y }); y += 20;
            size.Left = 10; size.Top = y; size.Value = s.Size; Controls.Add(size); y += 54;
            Controls.Add(new Label { Text = "Brightness (dim to full)", AutoSize = true, Left = 16, Top = y }); y += 20;
            bright.Left = 10; bright.Top = y; bright.Value = s.Brightness; Controls.Add(bright); y += 54;

            glow.Left = 16; glow.Top = y; glow.Checked = s.Glow; Controls.Add(glow); y += 26;
            retro.Left = 16; retro.Top = y; retro.Checked = s.Retro; Controls.Add(retro); y += 26;
            intro.Left = 16; intro.Top = y; intro.Checked = s.Intro; Controls.Add(intro); y += 34;
            Controls.Add(new Label { Text = "Start after this many idle minutes (background mode):", AutoSize = true, Left = 16, Top = y }); y += 22;
            idle.Left = 16; idle.Top = y; idle.Value = s.IdleMinutes; Controls.Add(idle); y += 36;
            limit.Left = 16; limit.Top = y; limit.Checked = s.TimeLimit; Controls.Add(limit); y += 26;
            tStart.Left = 30; tStart.Top = y; tStart.Value = DateTime.Today.AddMinutes(s.WinStart); Controls.Add(tStart);
            Controls.Add(new Label { Text = "to", AutoSize = true, Left = 108, Top = y + 3 });
            tEnd.Left = 130; tEnd.Top = y; tEnd.Value = DateTime.Today.AddMinutes(s.WinEnd); Controls.Add(tEnd); y += 40;

            Controls.Add(new Label { Text = "Wake the screen (exit the screensaver) on:", AutoSize = true, Left = 16, Top = y }); y += 24;
            wClick.Left = 30; wClick.Top = y; wClick.Checked = s.WakeClick; Controls.Add(wClick); y += 26;
            wSpace.Left = 30; wSpace.Top = y; wSpace.Checked = s.WakeSpace; Controls.Add(wSpace); y += 34;
            Controls.Add(new Label
            {
                Text = "Mouse movement and other keys never wake it. If both boxes are unticked, Alt+F4 still closes it. How long Windows waits before starting it, and whether it asks for a password afterwards, are set in Windows' own Screen Saver Settings.",
                Left = 16, Top = y, Width = 300, Height = 64
            }); y += 70;

            bool found = RainForm.FindMatrixFont() != null;
            Controls.Add(new Label
            {
                Text = found ? "Font: Matrix Code NFI found - using the movie glyphs."
                             : "Font: Matrix Code NFI NOT found - using a plain katakana fallback.",
                Left = 16, Top = y, Width = 300, Height = 34,
                ForeColor = found ? Color.DarkGreen : Color.Firebrick
            });

            var ok = new Button { Text = "OK", Left = 150, Top = 750, Width = 80 };
            var cancel = new Button { Text = "Cancel", Left = 240, Top = 750, Width = 80 };
            ok.Click += (a, b) =>
            {
                s.Speed = speed.Value; s.Size = size.Value; s.Density = density.Value; s.Brightness = bright.Value; s.IdleMinutes = (int)idle.Value;
                s.TimeLimit = limit.Checked;
                s.WinStart = tStart.Value.Hour * 60 + tStart.Value.Minute;
                s.WinEnd = tEnd.Value.Hour * 60 + tEnd.Value.Minute;
                s.Glow = glow.Checked; s.Retro = retro.Checked; s.Intro = intro.Checked;
                s.WakeClick = wClick.Checked; s.WakeSpace = wSpace.Checked;
                s.Save();
                Environment.Exit(0);
            };
            cancel.Click += (a, b) => Environment.Exit(0);
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
        }
    }

    sealed class Watcher : ApplicationContext
    {
        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO p);
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);

        readonly Timer timer = new Timer();
        RainForm form;

        public Watcher()
        {
            timer.Interval = 2000;
            timer.Tick += (s, e) => Check();
            timer.Start();
        }

        static bool InWindow(Settings c)
        {
            if (!c.TimeLimit) return true;
            int now = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
            if (c.WinStart == c.WinEnd) return true;
            return c.WinStart < c.WinEnd ? (now >= c.WinStart && now < c.WinEnd)
                                         : (now >= c.WinStart || now < c.WinEnd);
        }

        // true when the front window covers a whole monitor (borderless or exclusive full-screen game / video)
        static bool FullscreenForeground()
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero || h == GetShellWindow() || h == GetDesktopWindow()) return false;
                RECT r;
                if (!GetWindowRect(h, out r)) return false;
                Rectangle b = Screen.FromHandle(h).Bounds;
                if (r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom)
                {
                    var sb = new StringBuilder(64);
                    GetClassName(h, sb, 64);
                    string cls = sb.ToString();
                    if (cls == "Progman" || cls == "WorkerW") return false;
                    return true;
                }
            }
            catch { }
            return false;
        }

        void Check()
        {
            var cfg = Settings.Load();
            if (form != null)
            {
                // the time window ended while the rain was showing: stop it
                if (!form.IsDisposed && !InWindow(cfg)) form.Close();
                return;
            }
            if (!InWindow(cfg)) return;
            var li = new LASTINPUTINFO();
            li.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref li)) return;
            uint idle = unchecked((uint)Environment.TickCount - li.dwTime);
            if (idle < (uint)cfg.IdleMinutes * 60000u) return;
            // never start over a full-screen game, video or presentation
            int state;
            if (SHQueryUserNotificationState(out state) != 0 || state != 5) return;
            if (FullscreenForeground()) return;
            var f = new RainForm(cfg);
            form = f;
            f.FormClosed += (s, e) => { form = null; };
            f.Show();
        }
    }

    sealed class RainForm : Form
    {
        // Fallback glyph set (used only when the Matrix Code NFI font is not available).
        const string Kana = "ｦｱｳｴｵｶｷｹｺｻｼｽｾｿﾀﾂﾃﾅﾆﾇﾈﾊﾋﾎﾏﾐﾑﾒﾓﾔﾕﾗﾘﾜ";
        const string Digits = "0123456789";
        const string Letters = "BCDEHIJLMPSTUWYZ";
        const string Symbols = ":.\"=*+-<>^_";

        const int Levels = 24;        // brightness steps in the trail
        const int HeadLevel = Levels; // extra "white head" level

        readonly Settings cfg;
        readonly Rectangle bounds;
        readonly Random rng = new Random();
        readonly Timer timer = new Timer();

        static PrivateFontCollection privateFonts;
        FontFamily matrixFamily;

        int cellW, cellH, cols, rows, lanes;
        string glyphs;
        int[] pick;                // weighted table of glyph indexes
        Bitmap[][] atlas;          // [level][glyph]
        int[] glyphGrid;
        float[] bright;
        float[] colDecay;          // brightness lost per frame in each column
        int[] colTrail;            // trail length (rows) per column
        float[] headY;             // per stream (cols * lanes)
        float[] speed;
        bool[] active;
        int[] respawnWait;
        Bitmap frame;
        static readonly Color Key = Color.FromArgb(255, 0, 255); // shows the live desktop through the window
        bool outro;
        float[] outHead;
        float[] outSpeed;
        int outLeft;
        bool[] introDone;
        int introLeft;
        float speedMul;

        public RainForm(Settings settings)
        {
            cfg = settings;
            bounds = SystemInformation.VirtualScreen; // every monitor as one canvas
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            if (cfg.Intro) TransparencyKey = Key;   // live desktop until the rain covers it
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor.Hide();

            speedMul = 0.4f + cfg.Speed * 0.12f;
            lanes = 1 + cfg.Density / 4;           // 1..3 streams per column
            BuildAtlas();
            InitGrid();

            frame = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(frame))
            {
                g.Clear(cfg.Intro ? Key : Color.Black);
            }

            timer.Interval = 33; // ~30 fps
            timer.Tick += (s, e) => Step();
            timer.Start();

            KeyDown += (s, e) => { if (cfg.WakeSpace && e.KeyCode == Keys.Space) Wake(); };
            MouseDown += (s, e) => { if (cfg.WakeClick && e.Button == MouseButtons.Left) Wake(); };
            Shown += (s, e) => { Activate(); Focus(); };   // so the space bar reaches this window
        }

        // Wake: play the intro in reverse (black columns fall away and the desktop comes back), then close.
        void Wake()
        {
            if (outro) return;
            if (!cfg.Intro || introLeft > 0) { Close(); return; }
            outro = true;
            TransparencyKey = Key;
            outHead = new float[cols];
            outSpeed = new float[cols];
            outLeft = cols;
            for (int c = 0; c < cols; c++)
            {
                outSpeed[c] = NewSpeed() * 1.6f;
                outHead[c] = -outSpeed[c] * rng.Next(0, 60) - 1;
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            Cursor.Show();
            base.OnFormClosed(e);
        }

        int RandGlyph() { return pick[rng.Next(pick.Length)]; }

        bool IsMirrored(char ch) { return matrixFamily == null && ((ch >= 'ｦ' && ch <= 'ﾝ')); }

        internal static FontFamily FindMatrixFont()
        {
            try
            {
                using (var f = new InstalledFontCollection())
                    foreach (var fam in f.Families)
                        if (fam.Name == "Matrix Code NFI") return fam;
            }
            catch { }
            string[] dirs =
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MatrixRain")
            };
            string[] names = { "matrix code nfi.ttf", "matrix_code_nfi.ttf", "MatrixCodeNFI.ttf" };
            foreach (var d in dirs)
                foreach (var n in names)
                {
                    string path = Path.Combine(d, n);
                    if (!File.Exists(path)) continue;
                    try
                    {
                        privateFonts = new PrivateFontCollection();
                        privateFonts.AddFontFile(path);
                        if (privateFonts.Families.Length > 0) return privateFonts.Families[0];
                    }
                    catch { }
                }
            return null;
        }

        void BuildAtlas()
        {
            int minH = int.MaxValue;
            foreach (var s in Screen.AllScreens) minH = Math.Min(minH, s.Bounds.Height);
            int targetRows = 66 - cfg.Size * 4;                 // Size 6 -> 42 rows
            cellH = Math.Max(15, (int)Math.Round(minH / (double)targetRows / 3.0) * 3); // multiple of 3 for scanlines
            cellW = Math.Max(9, (int)Math.Round(cellH * 0.68));
            matrixFamily = FindMatrixFont();

            if (matrixFamily != null)
            {
                // The font itself carries the film's glyphs (katakana, mirrored numerals, Latin capitals).
                var sbg = new System.Text.StringBuilder();
                for (int ch = 33; ch <= 126; ch++)
                    if (ch != '@' && ch != '`') sbg.Append((char)ch);
                glyphs = sbg.ToString();
            }
            else glyphs = Kana + Digits + Letters + Symbols;

            var table = new List<int>();
            for (int i = 0; i < glyphs.Length; i++)
            {
                int weight = 1;
                if (matrixFamily == null) weight = i < Kana.Length ? 3 : (i < Kana.Length + Digits.Length ? 2 : 1);
                for (int w = 0; w < weight; w++) table.Add(i);
            }
            pick = table.ToArray();

            FontFamily fam;
            FontStyle style;
            if (matrixFamily != null) { fam = matrixFamily; style = FontStyle.Regular; }
            else
            {
                string fontName = FontFamilyExists("MS Gothic") ? "MS Gothic"
                                : FontFamilyExists("Yu Gothic") ? "Yu Gothic" : "Meiryo";
                fam = new FontFamily(fontName);
                style = fam.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
            }

            // With the old-monitor look, glyphs are drawn at ~70% size and scaled up with hard pixel edges.
            float k = cfg.Retro ? 0.70f : 1f;
            int sw = Math.Max(6, (int)Math.Round(cellW * k));
            int sh = Math.Max(9, (int)Math.Round(cellH * k));
            float em = sh * 0.80f;

            atlas = new Bitmap[Levels + 1][];
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
            using (var darken = new SolidBrush(Color.FromArgb(105, 0, 0, 0)))
            {
                for (int lv = 0; lv <= Levels; lv++)
                {
                    atlas[lv] = new Bitmap[glyphs.Length];
                    Color c = Dim(LevelColor(lv));
                    Color halo = lv == HeadLevel ? Dim(Color.FromArgb(90, 255, 150)) : c;
                    for (int gi = 0; gi < glyphs.Length; gi++)
                    {
                        var small = new Bitmap(sw, sh, PixelFormat.Format32bppPArgb);
                        using (var g = Graphics.FromImage(small))
                        using (var path = new GraphicsPath())
                        using (var fill = new SolidBrush(c))
                        {
                            g.Clear(Color.Black);
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            if (IsMirrored(glyphs[gi]))
                            {
                                g.TranslateTransform(sw, 0);
                                g.ScaleTransform(-1, 1);
                            }
                            path.AddString(glyphs[gi].ToString(), fam, (int)style, em,
                                           new RectangleF(0, 0, sw, sh), sf);
                            if (cfg.Glow)
                            {
                                using (var p1 = new Pen(Color.FromArgb(lv == HeadLevel ? 55 : 38, halo), sh * 0.20f) { LineJoin = LineJoin.Round })
                                using (var p2 = new Pen(Color.FromArgb(lv == HeadLevel ? 110 : 80, halo), sh * 0.10f) { LineJoin = LineJoin.Round })
                                {
                                    g.DrawPath(p1, path);
                                    g.DrawPath(p2, path);
                                }
                            }
                            using (var p3 = new Pen(c, Math.Max(1f, sh * 0.04f)) { LineJoin = LineJoin.Round })
                                g.DrawPath(p3, path);
                            g.FillPath(fill, path);
                        }

                        var tile = new Bitmap(cellW, cellH, PixelFormat.Format32bppPArgb);
                        using (var g = Graphics.FromImage(tile))
                        {
                            g.Clear(Color.Black);
                            g.InterpolationMode = k < 1f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.Half;
                            g.DrawImage(small, new Rectangle(0, 0, cellW, cellH), 0, 0, sw, sh, GraphicsUnit.Pixel);
                            if (cfg.Retro)
                            {
                                // scanlines: every third pixel row is darkened (cell height is a multiple of 3)
                                g.CompositingMode = CompositingMode.SourceOver;
                                for (int yy = 2; yy < cellH; yy += 3)
                                    g.FillRectangle(darken, 0, yy, cellW, 1);
                            }
                        }
                        small.Dispose();
                        atlas[lv][gi] = tile;
                    }
                }
            }
        }

        Color Dim(Color x)
        {
            float f = cfg.Brightness / 10f;
            return Color.FromArgb(x.A, (int)(x.R * f), (int)(x.G * f), (int)(x.B * f));
        }

        static bool FontFamilyExists(string name)
        {
            using (var f = new InstalledFontCollection())
                foreach (var fam in f.Families) if (fam.Name == name) return true;
            return false;
        }

        // lv 0 = nearly dark trail end ... lv Levels-1 = full green ... lv Levels = white head
        static Color LevelColor(int lv)
        {
            if (lv == HeadLevel) return Color.FromArgb(225, 255, 232);
            float t = (lv + 1) / (float)Levels;
            t = (float)Math.Pow(t, 1.35);
            int g = (int)(255 * t);
            int r = (int)(40 * t * t);
            int b = (int)(60 * t);
            return Color.FromArgb(r, Math.Max(g, 8), Math.Max(b, 3));
        }

        void InitGrid()
        {
            cols = bounds.Width / cellW + 1;
            rows = bounds.Height / cellH + 1;
            glyphGrid = new int[cols * rows];
            bright = new float[cols * rows];
            for (int i = 0; i < glyphGrid.Length; i++) glyphGrid[i] = RandGlyph();
            colDecay = new float[cols];
            colTrail = new int[cols];
            int n = cols * lanes;
            headY = new float[n];
            speed = new float[n];
            active = new bool[n];
            respawnWait = new int[n];
            introDone = new bool[cols];
            introLeft = cols;
            for (int c = 0; c < cols; c++)
            {
                colTrail[c] = 14 + rng.Next(rows > 40 ? 40 : 24);
                colDecay[c] = 1f / colTrail[c];
                for (int l = 0; l < lanes; l++)
                {
                    int d = c * lanes + l;
                    speed[d] = NewSpeed();
                    if (l == 0 && cfg.Intro)
                    {
                        // stream 0 does the desktop-to-rain reveal; each column starts at its own moment (up to ~3 s)
                        headY[d] = -speed[d] * rng.Next(0, 90) - 1;
                        active[d] = true;
                    }
                    else if (cfg.Intro)
                    {
                        headY[d] = -1;
                        active[d] = false;
                        respawnWait[d] = 60 + rng.Next(0, 120);
                    }
                    else
                    {
                        headY[d] = -rng.Next(rows * 2);
                        active[d] = true;
                    }
                }
                if (!cfg.Intro) { introDone[c] = true; introLeft--; }
            }
        }

        float NewSpeed() { return (0.30f + (float)rng.NextDouble() * 0.60f) * speedMul; }

        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);

        void Step()
        {
            // wake check that does not depend on window focus
            if (!outro && (cfg.WakeSpace && (GetAsyncKeyState(0x20) & 0x8000) != 0) ||
                (!outro && cfg.WakeClick && (GetAsyncKeyState(0x01) & 0x8000) != 0))
            {
                Wake();
                if (IsDisposed) return;
            }
            // fade
            for (int c = 0; c < cols; c++)
            {
                float decay = colDecay[c] * 0.7f;
                for (int r = 0; r < rows; r++)
                {
                    int i = r * cols + c;
                    if (bright[i] > 0f) { bright[i] -= decay; if (bright[i] < 0f) bright[i] = 0f; }
                }
            }
            // advance every stream
            for (int c = 0; c < cols; c++)
            {
                for (int l = 0; l < lanes; l++)
                {
                    int d = c * lanes + l;
                    if (!active[d])
                    {
                        if (--respawnWait[d] <= 0) active[d] = true;
                        continue;
                    }
                    int prev = (int)Math.Floor(headY[d]);
                    headY[d] += speed[d];
                    int cur = (int)Math.Floor(headY[d]);
                    for (int r = prev + 1; r <= cur; r++)
                    {
                        if (r >= 0 && r < rows)
                        {
                            int i = r * cols + c;
                            bright[i] = 1.5f; // >1 marks "head"
                            glyphGrid[i] = RandGlyph();
                        }
                    }
                    bool introLane = (l == 0 && !introDone[c]);
                    if (!introLane && cur > rows + colTrail[c])
                    {
                        // stream has left the screen: start another soon (small gap = heavier rain)
                        speed[d] = NewSpeed();
                        headY[d] = -rng.Next(4);
                        respawnWait[d] = rng.Next(0, 12 + 8 * (3 - lanes));
                        active[d] = false;
                        if (rng.Next(6) == 0)
                        {
                            colTrail[c] = 14 + rng.Next(rows > 40 ? 40 : 24);
                            colDecay[c] = 1f / colTrail[c];
                        }
                    }
                }
            }
            if (outro)
            {
                int left = 0;
                for (int c = 0; c < cols; c++)
                {
                    outHead[c] += outSpeed[c];
                    int oe = (int)Math.Floor(outHead[c]) + 1;
                    if (oe < rows)
                    {
                        left++;
                        if (oe >= 0)
                        {
                            int i = oe * cols + c;
                            bright[i] = 1.5f;
                            glyphGrid[i] = RandGlyph();
                        }
                    }
                }
                outLeft = left;
            }
            // flicker inside trails
            int flickers = cols * rows / 300;
            for (int q = 0; q < flickers; q++)
            {
                int i = rng.Next(glyphGrid.Length);
                if (bright[i] > 0f) glyphGrid[i] = RandGlyph();
            }
            Draw();
            Invalidate();
            if (outro && outLeft <= 0) Close();
        }

        void Draw()
        {
            using (var keyBrush = new SolidBrush(Key))
            using (var g = Graphics.FromImage(frame))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                for (int c = 0; c < cols; c++)
                {
                    int x = c * cellW;
                    int edge = rows; // rows [0, edge) show rain, the rest still show the desktop
                    if (!introDone[c])
                    {
                        edge = Math.Max(0, Math.Min(rows, (int)Math.Floor(headY[c * lanes]) + 1));
                        if (edge >= rows)
                        {
                            introDone[c] = true;
                            introLeft--;
                        }
                        else
                        {
                            int y0 = edge * cellH;
                            g.FillRectangle(keyBrush, x, y0, cellW, bounds.Height - y0);
                        }
                    }
                    int oEdge = outro ? Math.Max(0, Math.Min(rows, (int)Math.Floor(outHead[c]) + 1)) : 0;
                    if (oEdge > 0) g.FillRectangle(keyBrush, x, 0, cellW, oEdge * cellH);
                    for (int r = oEdge; r < edge; r++)
                    {
                        int i = r * cols + c;
                        float b = bright[i];
                        int lv;
                        if (b > 1f) lv = HeadLevel;
                        else if (b <= 0.02f) lv = -1;
                        else lv = Math.Min(Levels - 1, (int)(b * Levels));
                        if (lv >= 0) g.DrawImageUnscaled(atlas[lv][glyphGrid[i]], x, r * cellH);
                        else g.FillRectangle(Brushes.Black, x, r * cellH, cellW, cellH);
                    }
                }
            }
            if (introLeft <= 0 && !outro && !TransparencyKey.IsEmpty) TransparencyKey = Color.Empty; // fully opaque once covered
            for (int i = 0; i < bright.Length; i++) if (bright[i] > 1f) bright[i] = 0.999f;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.CompositingMode = CompositingMode.SourceCopy;
            e.Graphics.DrawImageUnscaled(frame, 0, 0);
        }
    }
}
