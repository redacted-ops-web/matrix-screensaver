using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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
            {
                Application.Run(new RainForm());
            }
            else if (mode == "/p")
            {
                // Tiny preview pane in the Windows settings dialog: nothing to draw, just exit.
                return;
            }
            else
            {
                MessageBox.Show(
                    "Matrix Rain spans all of your monitors as one continuous field.\n\nPress any key or move the mouse to exit.",
                    "Matrix Rain", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }

    sealed class RainForm : Form
    {
        // Glyph set: mirrored half-width katakana (U+FF66..U+FF9D), numerals and a few symbols.
        const string Kana = "ｦｱｳｴｵｶｷｹｺｻｼｽｾｿﾀﾂﾃﾅﾆﾇﾈﾊﾋﾎﾏﾐﾑﾒﾓﾔﾕﾗﾘﾜ";
        const string Digits = "012345789";
        const string Symbols = ":.\"=*+-<>¦";
        const string Extra = "日Z";

        const int Levels = 24;      // brightness steps in the trail
        const int HeadLevel = Levels; // extra "white head" level

        readonly Rectangle bounds;
        readonly Random rng = new Random();
        readonly Timer timer = new Timer();

        int cell, cols, rows;
        string glyphs;
        Bitmap[][] atlas;          // [level][glyph]
        int[] glyphGrid;           // glyph index per cell
        float[] bright;            // brightness 0..1 per cell
        float[] headY;             // head row position per column
        float[] speed;             // rows per frame per column
        int[] trailLen;
        bool[] active;
        int[] respawnWait;
        Bitmap frame;
        Bitmap desk;          // screenshot of the desktop shown at start
        bool[] introDone;     // column has fully switched from desktop to rain
        int introLeft;
        Point startMouse;
        bool mouseSet;
        int ticks;

        public RainForm()
        {
            bounds = SystemInformation.VirtualScreen; // covers every monitor as one canvas
            desk = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
            using (var dg = Graphics.FromImage(desk))
                dg.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            DoubleBuffered = true;
            Cursor.Hide();

            BuildAtlas();
            InitGrid();

            frame = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(frame)) g.DrawImageUnscaled(desk, 0, 0);

            timer.Interval = 33; // ~30 fps
            timer.Tick += (s, e) => Step();
            timer.Start();

            KeyDown += (s, e) => Close();
            MouseDown += (s, e) => Close();
            MouseMove += OnMouseMove;
        }

        void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!mouseSet) { startMouse = e.Location; mouseSet = true; return; }
            if (Math.Abs(e.X - startMouse.X) > 8 || Math.Abs(e.Y - startMouse.Y) > 8) Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            Cursor.Show();
            base.OnFormClosed(e);
        }

        void BuildAtlas()
        {
            // Cell size scales with the tallest monitor so the look stays similar on 1080p and 4K.
            int minH = int.MaxValue;
            foreach (var s in Screen.AllScreens) minH = Math.Min(minH, s.Bounds.Height);
            cell = Math.Max(14, (int)Math.Round(minH / 54.0));

            glyphs = Kana + Digits + Symbols + Extra;
            atlas = new Bitmap[Levels + 1][];

            string fontName = FontFamilyExists("MS Gothic") ? "MS Gothic"
                            : FontFamilyExists("Yu Gothic") ? "Yu Gothic" : "Meiryo";
            using (var font = new Font(fontName, cell * 0.78f, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                for (int lv = 0; lv <= Levels; lv++)
                {
                    atlas[lv] = new Bitmap[glyphs.Length];
                    Color c = LevelColor(lv);
                    for (int gi = 0; gi < glyphs.Length; gi++)
                    {
                        var bmp = new Bitmap(cell, cell, PixelFormat.Format32bppPArgb);
                        using (var g = Graphics.FromImage(bmp))
                        using (var br = new SolidBrush(c))
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
                        {
                            g.Clear(Color.Black);
                            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                            bool isKana = gi < Kana.Length || glyphs[gi] == '日';
                            if (isKana)
                            {
                                // Mirror katakana horizontally, as in the film's typeface.
                                g.TranslateTransform(cell, 0);
                                g.ScaleTransform(-1, 1);
                            }
                            g.DrawString(glyphs[gi].ToString(), font, br, new RectangleF(0, 0, cell, cell), sf);
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
            if (lv == HeadLevel) return Color.FromArgb(215, 255, 225);
            float t = (lv + 1) / (float)Levels;       // 0..1
            t = (float)Math.Pow(t, 1.6);              // fade slowly then fall off
            int g = (int)(255 * t);
            int r = (int)(20 * t * t);
            int b = (int)(70 * t);
            return Color.FromArgb(Math.Max(r, 0), Math.Max(g, 6), Math.Max(b, 2));
        }

        void InitGrid()
        {
            cols = bounds.Width / cell + 1;
            rows = bounds.Height / cell + 1;
            glyphGrid = new int[cols * rows];
            bright = new float[cols * rows];
            for (int i = 0; i < glyphGrid.Length; i++) glyphGrid[i] = rng.Next(glyphs.Length);
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
                // Each column starts at its own moment (up to ~3 s), above the screen.
                headY[c] = -speed[c] * rng.Next(0, 90) - 1;
                active[c] = true;
            }
        }

        void Respawn(int c, bool initial)
        {
            speed[c] = 0.35f + (float)rng.NextDouble() * 0.55f;   // rows per frame
            trailLen[c] = 10 + rng.Next(rows > 40 ? 38 : 22);
            if (initial)
            {
                headY[c] = -rng.Next(rows * 2);
                active[c] = true;
            }
            else
            {
                headY[c] = -rng.Next(4);
                respawnWait[c] = rng.Next(0, 90);
                active[c] = false;
            }
        }

        void Step()
        {
            ticks++;
            // Fade existing cells; trail length controls the decay rate per column.
            for (int c = 0; c < cols; c++)
            {
                float decay = 1f / Math.Max(6, trailLen[c]);
                for (int r = 0; r < rows; r++)
                {
                    int i = r * cols + c;
                    if (bright[i] > 0f) { bright[i] -= decay * speed[c] * 2.2f; if (bright[i] < 0f) bright[i] = 0f; }
                }
            }
            // Advance heads.
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
                        glyphGrid[i] = rng.Next(glyphs.Length);
                    }
                }
                if (cur - trailLen[c] * 2 > rows && introDone[c]) Respawn(c, false);
            }
            // Random glyph flicker inside the trails.
            int flickers = cols * rows / 400;
            for (int k = 0; k < flickers; k++)
            {
                int i = rng.Next(glyphGrid.Length);
                if (bright[i] > 0f) glyphGrid[i] = rng.Next(glyphs.Length);
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
                    int x = c * cell;
                    int edge = rows; // rows [0, edge) show the rain, the rest still show the desktop
                    if (!introDone[c])
                    {
                        edge = Math.Max(0, Math.Min(rows, (int)Math.Floor(headY[c]) + 1));
                        if (edge >= rows)
                        {
                            introDone[c] = true;
                            introLeft--;
                        }
                        else if (desk != null && edge < rows)
                        {
                            int y0 = edge * cell;
                            var src = new Rectangle(x, y0, Math.Min(cell, bounds.Width - x), bounds.Height - y0);
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
                        if (lv >= 0) g.DrawImageUnscaled(atlas[lv][glyphGrid[i]], x, r * cell);
                        else g.FillRectangle(Brushes.Black, x, r * cell, cell, cell);
                    }
                }
            }
            if (introLeft <= 0 && desk != null) { desk.Dispose(); desk = null; }
            // Heads settle to full green on the next frame.
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
