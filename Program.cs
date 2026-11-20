using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
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

            if (mode == "/s")
                Application.Run(new RainForm(Settings.Load(), false));
            else if (mode == "/t")
                Application.Run(new RainForm(Settings.Load(), true)); // test run, same as /s
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
        public bool Glow = true;
        public bool Intro = true;      // desktop-to-rain start
        public bool WakeClick = true;   // left mouse button
        public bool WakeSpace = true;   // space bar

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
                    s.Glow = (int)k.GetValue("Glow", 1) != 0;
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
                    k.SetValue("Glow", Glow ? 1 : 0);
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
        readonly TrackBar size = new TrackBar { Minimum = 1, Maximum = 10, TickFrequency = 1, Width = 260 };
        readonly CheckBox glow = new CheckBox { Text = "Glow around characters", AutoSize = true };
        readonly CheckBox intro = new CheckBox { Text = "Start with the desktop turning into rain", AutoSize = true };
        readonly CheckBox wClick = new CheckBox { Text = "Left mouse click", AutoSize = true };
        readonly CheckBox wSpace = new CheckBox { Text = "Space bar", AutoSize = true };

        public SettingsForm()
        {
            Text = "Matrix Rain Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(330, 420);

            int y = 12;
            Controls.Add(new Label { Text = "Rain speed (slow to fast)", AutoSize = true, Left = 16, Top = y }); y += 20;
            speed.Left = 10; speed.Top = y; speed.Value = s.Speed; Controls.Add(speed); y += 52;
            Controls.Add(new Label { Text = "Character size (small to large)", AutoSize = true, Left = 16, Top = y }); y += 20;
            size.Left = 10; size.Top = y; size.Value = s.Size; Controls.Add(size); y += 56;

            glow.Left = 16; glow.Top = y; glow.Checked = s.Glow; Controls.Add(glow); y += 28;
            intro.Left = 16; intro.Top = y; intro.Checked = s.Intro; Controls.Add(intro); y += 38;

            Controls.Add(new Label { Text = "Wake the screen (exit the screensaver) on:", AutoSize = true, Left = 16, Top = y }); y += 24;
            wClick.Left = 30; wClick.Top = y; wClick.Checked = s.WakeClick; Controls.Add(wClick); y += 26;
            wSpace.Left = 30; wSpace.Top = y; wSpace.Checked = s.WakeSpace; Controls.Add(wSpace); y += 34;
            Controls.Add(new Label
            {
                Text = "Mouse movement and other keys never wake it. If both boxes are unticked, Alt+F4 still closes it. How long Windows waits before starting it, and whether it asks for a password afterwards, are set in Windows' own Screen Saver Settings.",
                Left = 16, Top = y, Width = 300, Height = 52
            });

            var ok = new Button { Text = "OK", Left = 150, Top = 380, Width = 80, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 240, Top = 380, Width = 80, DialogResult = DialogResult.Cancel };
            ok.Click += (a, b) =>
            {
                s.Speed = speed.Value; s.Size = size.Value; s.Glow = glow.Checked; s.Intro = intro.Checked;
                s.WakeClick = wClick.Checked; s.WakeSpace = wSpace.Checked;
                s.Save();
            };
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
        }
    }

    sealed class RainForm : Form
    {
        // Glyph set, following the film's code: mirrored half-width katakana, numerals,
        // capital Latin letters and a handful of symbols.
        const string Kana = "ｦｱｳｴｵｶｷｹｺｻｼｽｾｿﾀﾂﾃﾅﾆﾇﾈﾊﾋﾎﾏﾐﾑﾒﾓﾔﾕﾗﾘﾜ";
        const string Digits = "0123456789";
        const string Letters = "BCDEHIJLMPSTUWYZ";
        const string Symbols = ":.\"=*+-<>¦^_日";

        const int Levels = 24;        // brightness steps in the trail
        const int HeadLevel = Levels; // extra "white head" level

        readonly Settings cfg;
        readonly Rectangle bounds;
        readonly Random rng = new Random();
        readonly Timer timer = new Timer();

        int cellW, cellH, cols, rows;
        FontFamily matrixFamily;     // Matrix Code NFI, if found on this PC
        PrivateFontCollection privateFonts;
        string glyphs;
        int[] pick;                // weighted table of glyph indexes
        Bitmap[][] atlas;          // [level][glyph]
        int[] glyphGrid;
        float[] bright;
        float[] headY;
        float[] speed;
        int[] trailLen;
        bool[] active;
        int[] respawnWait;
        Bitmap frame;
        Bitmap desk;
        bool[] introDone;
        int introLeft;
        float speedMul;

        public RainForm(Settings settings, bool testMode)
        {
            cfg = settings;
            bounds = SystemInformation.VirtualScreen; // every monitor as one canvas
            if (cfg.Intro)
            {
                desk = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
                using (var dg = Graphics.FromImage(desk))
                    dg.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor.Hide();

            speedMul = 0.4f + cfg.Speed * 0.12f;
            BuildAtlas();
            InitGrid();

            frame = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(frame))
            {
                if (desk != null) g.DrawImageUnscaled(desk, 0, 0); else g.Clear(Color.Black);
            }

            timer.Interval = 33; // ~30 fps
            timer.Tick += (s, e) => Step();
            timer.Start();

            KeyDown += (s, e) => { if (cfg.WakeSpace && e.KeyCode == Keys.Space) Close(); };
            MouseDown += (s, e) => { if (cfg.WakeClick && e.Button == MouseButtons.Left) Close(); };
            Shown += (s, e) => { Activate(); Focus(); };   // so the space bar reaches this window
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            Cursor.Show();
            base.OnFormClosed(e);
        }

        int RandGlyph() { return pick[rng.Next(pick.Length)]; }

        bool IsMirrored(char ch) { return matrixFamily == null && ((ch >= '\uFF66' && ch <= '\uFF9D') || ch == '\u65E5'); }

        FontFamily FindMatrixFont()
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
            cellH = Math.Max(14, (int)Math.Round(minH / (double)targetRows));
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

            atlas = new Bitmap[Levels + 1][];
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
            float em = cellH * 0.80f;

            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
            {
                for (int lv = 0; lv <= Levels; lv++)
                {
                    atlas[lv] = new Bitmap[glyphs.Length];
                    Color c = LevelColor(lv);
                    Color halo = lv == HeadLevel ? Color.FromArgb(90, 255, 150) : c;
                    for (int gi = 0; gi < glyphs.Length; gi++)
                    {
                        var bmp = new Bitmap(cellW, cellH, PixelFormat.Format32bppPArgb);
                        using (var g = Graphics.FromImage(bmp))
                        using (var path = new GraphicsPath())
                        using (var fill = new SolidBrush(c))
                        {
                            g.Clear(Color.Black);
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            if (IsMirrored(glyphs[gi]))
                            {
                                g.TranslateTransform(cellW, 0);
                                g.ScaleTransform(-1, 1);
                            }
                            path.AddString(glyphs[gi].ToString(), fam, (int)style, em,
                                           new RectangleF(0, 0, cellW, cellH), sf);
                            if (cfg.Glow)
                            {
                                using (var p1 = new Pen(Color.FromArgb(lv == HeadLevel ? 55 : 38, halo), cellH * 0.20f) { LineJoin = LineJoin.Round })
                                using (var p2 = new Pen(Color.FromArgb(lv == HeadLevel ? 110 : 80, halo), cellH * 0.10f) { LineJoin = LineJoin.Round })
                                {
                                    g.DrawPath(p1, path);
                                    g.DrawPath(p2, path);
                                }
                            }
                            // slight outline in the same colour makes the strokes a little heavier, like the film
                            using (var p3 = new Pen(c, Math.Max(1f, cellH * 0.04f)) { LineJoin = LineJoin.Round })
                                g.DrawPath(p3, path);
                            g.FillPath(fill, path);
                        }
                        atlas[lv][gi] = bmp;
                    }
                }
            }
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
            headY = new float[cols];
            speed = new float[cols];
            trailLen = new int[cols];
            active = new bool[cols];
            respawnWait = new int[cols];
            introDone = new bool[cols];
            introLeft = cols;
            for (int c = 0; c < cols; c++)
            {
                Respawn(c, true);
                if (desk != null)
                {
                    // each column starts at its own moment (up to ~3 s) above the screen
                    headY[c] = -speed[c] * rng.Next(0, 90) - 1;
                }
                else
                {
                    headY[c] = -rng.Next(rows * 2);
                    introDone[c] = true;
                    introLeft--;
                }
                active[c] = true;
            }
        }

        void Respawn(int c, bool initial)
        {
            speed[c] = (0.30f + (float)rng.NextDouble() * 0.60f) * speedMul;   // rows per frame
            trailLen[c] = 12 + rng.Next(rows > 40 ? 40 : 24);
            if (initial)
            {
                headY[c] = -rng.Next(rows * 2);
                active[c] = true;
            }
            else
            {
                headY[c] = -rng.Next(4);
                respawnWait[c] = rng.Next(0, 45);
                active[c] = false;
            }
        }

        void Step()
        {
            for (int c = 0; c < cols; c++)
            {
                float decay = 1f / Math.Max(6, trailLen[c]);
                for (int r = 0; r < rows; r++)
                {
                    int i = r * cols + c;
                    if (bright[i] > 0f) { bright[i] -= decay * speed[c] * 2.0f; if (bright[i] < 0f) bright[i] = 0f; }
                }
            }
            for (int c = 0; c < cols; c++)
            {
                if (!active[c])
                {
                    if (--respawnWait[c] <= 0) active[c] = true;
                    continue;
                }
                int prev = (int)Math.Floor(headY[c]);
                headY[c] += speed[c];
                int cur = (int)Math.Floor(headY[c]);
                for (int r = prev + 1; r <= cur; r++)
                {
                    if (r >= 0 && r < rows)
                    {
                        int i = r * cols + c;
                        bright[i] = 1.5f; // >1 marks "head"
                        glyphGrid[i] = RandGlyph();
                    }
                }
                if (cur - trailLen[c] * 2 > rows && introDone[c]) Respawn(c, false);
            }
            int flickers = cols * rows / 300;
            for (int k = 0; k < flickers; k++)
            {
                int i = rng.Next(glyphGrid.Length);
                if (bright[i] > 0f) glyphGrid[i] = RandGlyph();
            }
            Draw();
            Invalidate();
        }

        void Draw()
        {
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
                        edge = Math.Max(0, Math.Min(rows, (int)Math.Floor(headY[c]) + 1));
                        if (edge >= rows)
                        {
                            introDone[c] = true;
                            introLeft--;
                        }
                        else if (desk != null)
                        {
                            int y0 = edge * cellH;
                            var src = new Rectangle(x, y0, Math.Min(cellW, bounds.Width - x), bounds.Height - y0);
                            if (src.Width > 0 && src.Height > 0)
                                g.DrawImage(desk, new Rectangle(x, y0, src.Width, src.Height), src, GraphicsUnit.Pixel);
                        }
                    }
                    for (int r = 0; r < edge; r++)
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
            if (introLeft <= 0 && desk != null) { desk.Dispose(); desk = null; }
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
