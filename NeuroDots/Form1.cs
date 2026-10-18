using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using NeuroCivilization.Core;

namespace NeuroCivilization
{
    public partial class Form1 : Form
    {
        World world = null!;
        bool isPaused;
        int simSpeed = 1;

        double camX, camY, zoom;

        /// <summary>Optional startup zoom override (NCD_ZOOM env var, debug/QA hook).</summary>
        static readonly double? DebugZoom = double.TryParse(
            System.Environment.GetEnvironmentVariable("NCD_ZOOM"), out var z) && z is > 0.15 and < 8 ? z : null;
        bool isDrag; Point dragS; double dragCX, dragCY;
        bool follow;

        Human? selH; Animal? selA;

        Dictionary<string, Bitmap> tex = new();
        Dictionary<string, List<Bitmap>> texV = new();
        Bitmap? terrainBuffer;
        Bitmap? minimapBg;
        const int TILE = 64;

        Font fT = null!, fN = null!, fS = null!, fM = null!;
        SolidBrush brP = null!, brTx = null!, brAc = null!, brW = null!, brD = null!, brG = null!, brDm = null!;
        SolidBrush brBerry = null!, brPlant = null!, brStone = null!, brWood = null!, brHut = null!, brFire = null!, brWall = null!, brStorage = null!;
        SolidBrush brDeadH = null!, brDeadA = null!, brPredDot = null!, brHerbDot = null!;
        Pen penB = null!, penSelect = null!, penTargetLine = null!, penTargetCross = null!, penViewRect = null!, penPopGraph = null!, penFitGraph = null!;

        WorldStats? st;
        int fps, fCnt; DateTime fpsT = DateTime.Now;
        List<double> popH = new(), fitH = new();

        // Toggles
        bool showTop = true, showLeft = true, showMap = true, showSel = true;
        bool showWeather = true, showRes = true, showTerr = true;
        bool showBld = true, showLbl = true, showGraph = true;
        bool showLog = true, showDead = true, showPop = true, showSprites = true;
        bool drawerOpen = true;
        float drawerAnim;
        int drawerHover = -1;

        // ── Display filters (drawer rows) ──
        bool showPredOnly, showPregOnly;
        int tribeFilter = -1;          // -1 = all tribes

        // ── Tribe selection (click on a territory) ──
        Tribe? selT;

        // ── Particle system: smoke / sparks / dust / rain / snow / leaves / ash / ghosts / hearts / spirits ──
        struct Puff
        {
            public float X, Y, VX, VY, A, Size;
            public int Kind;   // 0 smoke,1 spark,2 dust,3 rain,4 snow,5 leaf,6 ash,7 ghost,8 heart,9 spirit,10 ripple
        }
        readonly List<Puff> _puffs = new();

        readonly HashSet<int> _aliveSeen = new();       // death detection → ghost puffs
        readonly HashSet<int> _newbornSeen = new();     // birth detection → heart puffs
        readonly Dictionary<int, List<double>> _tribeHist = new();   // per-tribe population history

        /// <summary>Left settings drawer rows. Get==null rows are action buttons drawn specially.</summary>
        public sealed class DrawerRow
        {
            public string Label { get; init; } = "";
            public string Section { get; init; } = "";   // section header shown above the row
            public Func<string>? DynLabel { get; init; }   // optional live label (filters etc.)
            public Func<bool?>? Get { get; init; }
            public Action Click { get; init; } = () => { };
        }

        readonly List<DrawerRow> drawerRows = new();

        /// <summary>Builds the toggle/action rows (must run in Load: references instance fields).</summary>
        void BuildDrawerRows()
        {
            drawerRows.Add(new DrawerRow { Section = "DISPLAY",  Label = "Top bar",            Get = () => showTop,     Click = () => showTop = !showTop });
            drawerRows.Add(new DrawerRow { Label = "Stats panel",                            Get = () => showLeft,    Click = () => showLeft = !showLeft });
            drawerRows.Add(new DrawerRow { Label = "Minimap",                                Get = () => showMap,     Click = () => showMap = !showMap });
            drawerRows.Add(new DrawerRow { Label = "Selection info",                         Get = () => showSel,     Click = () => showSel = !showSel });
            drawerRows.Add(new DrawerRow { Label = "Weather",                                Get = () => showWeather, Click = () => showWeather = !showWeather });
            drawerRows.Add(new DrawerRow { Label = "Resources",                              Get = () => showRes,     Click = () => showRes = !showRes });
            drawerRows.Add(new DrawerRow { Label = "Territories",                            Get = () => showTerr,    Click = () => showTerr = !showTerr });
            drawerRows.Add(new DrawerRow { Label = "Buildings",                              Get = () => showBld,     Click = () => showBld = !showBld });
            drawerRows.Add(new DrawerRow { Label = "Name labels",                            Get = () => showLbl,     Click = () => showLbl = !showLbl });
            drawerRows.Add(new DrawerRow { Label = "Population graph",                       Get = () => showGraph,   Click = () => showGraph = !showGraph });
            drawerRows.Add(new DrawerRow { Label = "Event log",                              Get = () => showLog,     Click = () => showLog = !showLog });
            drawerRows.Add(new DrawerRow { Label = "Graves",                                 Get = () => showDead,    Click = () => showDead = !showDead });
            drawerRows.Add(new DrawerRow { Label = "Floating popups",                        Get = () => showPop,     Click = () => showPop = !showPop });
            drawerRows.Add(new DrawerRow { Label = "Sprite packs (animals+humans)",          Get = () => showSprites, Click = () => showSprites = !showSprites });

            drawerRows.Add(new DrawerRow { Section = "FILTERS", Label = "Only predators",     Get = () => showPredOnly, Click = () => showPredOnly = !showPredOnly });
            drawerRows.Add(new DrawerRow { Label = "Only pregnant",                          Get = () => showPregOnly, Click = () => showPregOnly = !showPregOnly });
            drawerRows.Add(new DrawerRow
            {
                Label = "Tribe filter",
                DynLabel = () => tribeFilter < 0 ? "Tribe filter: ALL" : $"Tribe filter: #{tribeFilter}",
                Click = () =>
                {
                    var ids = world.Tribes.Where(t => t.Population > 0).Select(t => t.Id).OrderBy(t => t).ToList();
                    if (ids.Count == 0) { tribeFilter = -1; return; }
                    int idx = ids.IndexOf(tribeFilter);
                    tribeFilter = idx + 1 >= ids.Count ? -1 : ids[idx + 1];
                }
            });

            drawerRows.Add(new DrawerRow { Section = "SIMULATION", Label = "Speed −",        Click = () => { if (simSpeed > 1) simSpeed--; } });
            drawerRows.Add(new DrawerRow { Label = "Speed +",                                 Click = () => { if (simSpeed < 30) simSpeed++; } });
            drawerRows.Add(new DrawerRow { Label = "Pause / resume",                          Get = () => isPaused,    Click = () => isPaused = !isPaused });
            drawerRows.Add(new DrawerRow { Label = "New world",                               Click = () => { PluginHost.UnloadAll(world); InitWorld(); PluginHost.LoadAll(world); } });
            drawerRows.Add(new DrawerRow { Label = "Center on population",                    Click = () => Home() });

            drawerRows.Add(new DrawerRow { Section = "SYSTEM", Label = "Language: EN / Язык", Click = () => Lang.Toggle() });
        }
        const int DrawerW = 252, DrawerTop = 50, DrawerPad = 14, SectionH = 20, HeaderH = 74, FooterH = 30;

        int SectionCount
        {
            get
            {
                int n = 0; string last = "";
                foreach (var r in drawerRows)
                    if (!string.IsNullOrEmpty(r.Section) && r.Section != last) { n++; last = r.Section; }
                return n;
            }
        }

        /// <summary>Row height adapts so the whole sidebar always fits inside the client area.</summary>
        int RowH
        {
            get
            {
                int rows = Math.Max(1, drawerRows.Count);
                // panel top is DrawerTop - 8; keep an 8px bottom margin
                int budget = ClientSize.Height - (DrawerTop - 8) - 8 - HeaderH - FooterH - SectionCount * SectionH;
                return Math.Clamp(budget / rows, 18, 27);
            }
        }

        /// <summary>Total sidebar height: header + section headers + rows + footer.</summary>
        int DrawerHeight
        {
            get
            {
                int h = HeaderH + FooterH + 6;
                string last = "";
                foreach (var r in drawerRows)
                {
                    if (!string.IsNullOrEmpty(r.Section) && r.Section != last) { h += SectionH; last = r.Section; }
                    h += RowH;
                }
                return h;
            }
        }

        /// <summary>Left panels shift right while the sidebar is open (no overlap).</summary>
        int PanelX => drawerOpen ? DrawerW + 10 : 8;

        /// <summary>Row rectangles in sidebar coordinates (used by both hit-test and paint).</summary>
        List<(int idx, Rectangle rect)> DrawerRowRects()
        {
            var res = new List<(int, Rectangle)>();
            int y = DrawerTop + HeaderH;
            string last = "";
            for (int i = 0; i < drawerRows.Count; i++)
            {
                var r = drawerRows[i];
                if (!string.IsNullOrEmpty(r.Section) && r.Section != last) { y += SectionH; last = r.Section; }
                res.Add((i, new Rectangle(10, y, DrawerW - 20, RowH - 5)));
                y += RowH;
            }
            return res;
        }

        /// <summary>Camera to population center (Home key behavior).</summary>
        void Home()
        {
            var al = world.Humans.Where(h => h.IsAlive).ToList();
            if (al.Count > 0)
            {
                camX = al.Average(h => h.X) - ClientSize.Width / 2.0 / zoom;
                camY = al.Average(h => h.Y) - ClientSize.Height / 2.0 / zoom;
                follow = false;
            }
        }
        const int PopupLife = 40;   // ticks (≈ 2.2 сек при 18 тик/с)

        static readonly Color CPB = Color.FromArgb(205, 14, 19, 30);
        static readonly Color CBR = Color.FromArgb(160, 55, 80, 125);

        public Form1()
        {
            InitializeComponent();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        void Form1_Load(object sender, EventArgs e)
        {
            fT = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            fN = new Font("Segoe UI", 9f, FontStyle.Regular);
            fS = new Font("Segoe UI", 7.8f, FontStyle.Bold);
            fM = new Font("Consolas", 7.2f);

            brP = new SolidBrush(CPB);
            brTx = new SolidBrush(Color.FromArgb(225, 235, 245));
            brAc = new SolidBrush(Color.FromArgb(95, 195, 255));
            brW = new SolidBrush(Color.FromArgb(255, 195, 70));
            brD = new SolidBrush(Color.FromArgb(255, 80, 80));
            brG = new SolidBrush(Color.FromArgb(75, 240, 115));
            brDm = new SolidBrush(Color.FromArgb(135, 165, 185));

            brBerry = new SolidBrush(Color.FromArgb(215, 55, 145));
            brPlant = new SolidBrush(Color.FromArgb(55, 180, 55));
            brStone = new SolidBrush(Color.FromArgb(155, 155, 145));
            brWood = new SolidBrush(Color.FromArgb(145, 95, 45));

            brHut = new SolidBrush(Color.FromArgb(165, 120, 60));
            brFire = new SolidBrush(Color.FromArgb(255, 140, 30));
            brWall = new SolidBrush(Color.FromArgb(130, 135, 140));
            brStorage = new SolidBrush(Color.FromArgb(185, 145, 80));

            brDeadH = new SolidBrush(Color.FromArgb(110, 85, 65, 55));
            brDeadA = new SolidBrush(Color.FromArgb(80, 65, 55, 45));
            brPredDot = new SolidBrush(Color.IndianRed);
            brHerbDot = new SolidBrush(Color.OliveDrab);

            penB = new Pen(CBR, 1f);
            penSelect = new Pen(Color.FromArgb(220, 255, 235, 60), 2f);
            penTargetLine = new Pen(Color.FromArgb(100, 255, 210, 60), 1.2f) { DashStyle = DashStyle.Dot };
            penTargetCross = new Pen(Color.FromArgb(160, 255, 210, 60), 1.5f);
            penViewRect = new Pen(Color.Yellow, 1f);
            penPopGraph = new Pen(Color.FromArgb(90, 195, 255), 1.6f);
            penFitGraph = new Pen(Color.FromArgb(100, 240, 130), 1.4f);

            LoadTex();
            LoadAnimalSprites();
            LoadHumanSprites();
            BuildDrawerRows();
            InitWorld();
            PluginHost.LoadAll(world);
            simTimer.Start();
        }

        void InitWorld()
        {
            Human.ResetIds();
            Animal.ResetIds();
            Tribe.ResetIds();
            world = new World();
            world.SpawnHumans(32);
            world.SpawnAnimals(42, 12);
            zoom = DebugZoom ?? 0.7;
            BuildTerrainBuffers();
            camX = world.Width / 2 - ClientSize.Width / 2.0 / zoom;
            camY = world.Height / 2 - ClientSize.Height / 2.0 / zoom;
            st = world.GetStats();
            popH.Clear();
            fitH.Clear();
            selT = null;
            _puffs.Clear();
            _aliveSeen.Clear();
            _newbornSeen.Clear();
            _tribeHist.Clear();
        }

        void LoadTex()
        {
            string? dir = Path.Combine(Application.StartupPath, "Assets");
            if (!Directory.Exists(dir)) dir = null;

            foreach (var n in new[] { "grass", "forest", "sand", "snow", "water", "oasis" })
            {
                // Prefer 3 variant files (<name>_0..2.png) to avoid visible tiling repetition
                var list = new List<Bitmap>();
                if (dir != null)
                    for (int v = 0; v < 5; v++)
                    {
                        string fv = Path.Combine(dir, $"{n}_{v}.png");
                        if (File.Exists(fv)) list.Add(new Bitmap(fv));
                    }
                if (list.Count == 0 && dir != null)
                {
                    string f = Path.Combine(dir, n + ".png");
                    if (File.Exists(f)) list.Add(new Bitmap(f));
                }
                if (list.Count == 0) list.Add(MkTex(n));
                texV[n] = list;
                tex[n] = list[0];
            }
        }

        /// <summary>Seeded variant picker for a tile position (deterministic, no flicker).</summary>
        static int TileVariant(int tx, int ty, int count)
        {
            unchecked
            {
                uint h = (uint)(tx * 73856093) ^ (uint)(ty * 19349663) ^ 0x9E3779B9u;
                h = (h ^ (h >> 15)) * 2246822519u;
                h = (h ^ (h >> 13)) * 3266489917u;
                h ^= h >> 16;
                return (int)(h % (uint)count);
            }
        }

        /// <summary>Detailed deterministic tile textures (soft airbrush patches + fine grain + biome accents).</summary>
        Bitmap MkTex(string n)
        {
            var b = new Bitmap(TILE, TILE);
            using var g = Graphics.FromImage(b);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color baseC, darkC, liteC;
            switch (n)
            {
                case "grass":  baseC = Color.FromArgb(255, 92, 148, 56);  darkC = Color.FromArgb(255, 74, 126, 46);  liteC = Color.FromArgb(255, 116, 176, 70); break;
                case "forest": baseC = Color.FromArgb(255, 38, 96, 34);   darkC = Color.FromArgb(255, 24, 70, 26);   liteC = Color.FromArgb(255, 54, 118, 44); break;
                case "sand":   baseC = Color.FromArgb(255, 210, 192, 132); darkC = Color.FromArgb(255, 188, 170, 110); liteC = Color.FromArgb(255, 228, 212, 156); break;
                case "snow":   baseC = Color.FromArgb(255, 224, 234, 244); darkC = Color.FromArgb(255, 206, 218, 232); liteC = Color.FromArgb(255, 242, 248, 252); break;
                case "water":  baseC = Color.FromArgb(255, 34, 88, 158);  darkC = Color.FromArgb(255, 22, 66, 126);  liteC = Color.FromArgb(255, 62, 126, 198); break;
                default:       baseC = Color.FromArgb(255, 62, 160, 100); darkC = Color.FromArgb(255, 46, 130, 82);  liteC = Color.FromArgb(255, 88, 186, 122); break; // oasis
            }
            using (var br = new SolidBrush(baseC)) g.FillRectangle(br, 0, 0, TILE, TILE);

            var rn = new Random(n.GetHashCode());
            // Large soft patches (terrain macro variation, low alpha → hides tiling)
            for (int i = 0; i < 7; i++)
            {
                var c = rn.Next(3) == 0 ? liteC : darkC;
                using var pb = new SolidBrush(Color.FromArgb(34, c));
                int pd = TILE / 3 + rn.Next(TILE / 2);
                g.FillEllipse(pb, rn.Next(TILE) - pd / 2, rn.Next(TILE) - pd / 2, pd, (int)(pd * 0.75f));
            }
            // Medium mottling
            for (int i = 0; i < 26; i++)
            {
                var c = rn.Next(2) == 0 ? darkC : liteC;
                using var mb = new SolidBrush(Color.FromArgb(46, c));
                int md = 3 + rn.Next(7);
                g.FillEllipse(mb, rn.Next(TILE), rn.Next(TILE), md, md);
            }

            switch (n)
            {
                case "grass": // blades + few flowers
                    using (var blade = new Pen(Color.FromArgb(110, 42, 96, 30), 1.4f))
                        for (int i = 0; i < 15; i++)
                        {
                            float gx = rn.Next(TILE), gy = rn.Next(TILE);
                            g.DrawLine(blade, gx, gy, gx - 2 + rn.Next(5), gy - 3 - rn.Next(3));
                        }
                    using (var fl = new SolidBrush(Color.FromArgb(150, 248, 238, 130)))
                        for (int i = 0; i < 3; i++) g.FillRectangle(fl, rn.Next(TILE - 2), rn.Next(TILE - 2), 2, 2);
                    break;
                case "forest": // canopy blobs with rim highlight
                    for (int i = 0; i < 6; i++)
                    {
                        int cx = rn.Next(TILE - 14), cy = rn.Next(TILE - 14), cd = 10 + rn.Next(10);
                        using var cb = new SolidBrush(Color.FromArgb(80, 16, 54, 22));
                        g.FillEllipse(cb, cx, cy, cd, cd);
                        using var rim = new Pen(Color.FromArgb(60, 70, 130, 52), 2f);
                        g.DrawArc(rim, cx - 1, cy - 1, cd + 2, cd + 2, 200, 110);
                    }
                    break;
                case "sand": // wind ripples
                    using (var rp = new Pen(Color.FromArgb(55, 240, 224, 168), 1.6f))
                        for (int i = 0; i < 5; i++)
                        {
                            int yy = 4 + i * (TILE / 5) + rn.Next(4);
                            g.DrawArc(rp, -TILE / 4, yy, TILE * 1.5f, TILE / 3, 200, 140);
                        }
                    break;
                case "snow": // drift dots
                    using (var dr = new SolidBrush(Color.FromArgb(70, 250, 253, 255)))
                        for (int i = 0; i < 10; i++) g.FillEllipse(dr, rn.Next(TILE), rn.Next(TILE), 3 + rn.Next(4), 2 + rn.Next(3));
                    break;
                case "water": // horizontal wave bands + caustic glints
                    using (var wv = new Pen(Color.FromArgb(60, 80, 150, 214), 1.5f))
                        for (int i = 0; i < 5; i++)
                        {
                            int yy = 5 + i * (TILE / 5) + rn.Next(5);
                            g.DrawArc(wv, rn.Next(TILE / 2) - TILE / 4, yy, TILE / 2 + rn.Next(TILE / 2), 7, 190, 160);
                        }
                    using (var gl = new SolidBrush(Color.FromArgb(46, 214, 236, 250)))
                        for (int i = 0; i < 6; i++) g.FillEllipse(gl, rn.Next(TILE - 8), rn.Next(TILE - 4), 6 + rn.Next(5), 2);
                    break;
                default: // oasis: reeds + sparkle
                    using (var rd = new Pen(Color.FromArgb(120, 36, 110, 60), 1.6f))
                        for (int i = 0; i < 8; i++)
                        {
                            float rx = rn.Next(TILE), ry = TILE - 4 - rn.Next(TILE / 2);
                            g.DrawLine(rd, rx, ry, rx - 2 + rn.Next(5), ry - 4 - rn.Next(4));
                        }
                    using (var sp2 = new SolidBrush(Color.FromArgb(60, 230, 250, 240)))
                        for (int i = 0; i < 5; i++) g.FillEllipse(sp2, rn.Next(TILE), rn.Next(TILE), 3, 2);
                    break;
            }

            // Fine grain (low alpha to avoid salt-and-pepper)
            for (int i = 0; i < TILE * TILE / 14; i++)
            {
                int px = rn.Next(TILE), py = rn.Next(TILE);
                int d = rn.Next(-8, 9);
                using var fb = new SolidBrush(Color.FromArgb(40,
                    Math.Clamp(baseC.R + d, 0, 255),
                    Math.Clamp(baseC.G + d, 0, 255),
                    Math.Clamp(baseC.B + d, 0, 255)));
                b.SetPixel(px, py, fb.Color);
            }
            return b;
        }

        /// <summary>
        /// Предварительный рендеринг всей карты террейна и миникарты в битмап (1 вызов DrawImage вместо 1200 каждый кадр!)
        /// </summary>
        void BuildTerrainBuffers()
        {
            terrainBuffer?.Dispose();
            minimapBg?.Dispose();

            int w = (int)world.Width;
            int h = (int)world.Height;
            terrainBuffer = new Bitmap(w, h);
            using (var g = Graphics.FromImage(terrainBuffer))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                int cols = (w + TILE - 1) / TILE;
                int rows = (h + TILE - 1) / TILE;
                for (int tx = 0; tx < cols; tx++)
                {
                    for (int ty = 0; ty < rows; ty++)
                    {
                        double px = tx * TILE, py = ty * TILE;
                        var bi = world.Env.GetBiome(px, py, world.Width, world.Height);
                        if (texV.TryGetValue(B2T(bi), out var variants) && variants.Count > 0)
                            g.DrawImage(variants[TileVariant(tx, ty, variants.Count)], tx * TILE, ty * TILE, TILE, TILE);

                        bool water = world.Env.IsWater(px + TILE / 2, py + TILE / 2);
                        bool waterNear = water || world.Env.IsWater(px - TILE / 2, py + TILE / 2)
                                         || world.Env.IsWater(px + TILE * 1.5, py + TILE / 2)
                                         || world.Env.IsWater(px + TILE / 2, py - TILE / 2)
                                         || world.Env.IsWater(px + TILE / 2, py + TILE * 1.5);

                        // Sandy shore banks hug rivers/lakes
                        if (!water && waterNear)
                            using (var bank = new SolidBrush(Color.FromArgb(130, 200, 178, 118)))
                                g.FillRectangle(bank, tx * TILE, ty * TILE, TILE, TILE);

                        // Decorative nature sprites per tile (seeded → stable between rebuilds)
                        if (!water)
                        {
                            var rn = new Random(tx * 73856093 ^ ty * 19349663);
                            if ((bi == Biome.Forest || bi == Biome.BorealForest || bi == Biome.TropicalForest) && rn.NextDouble() < 0.75)
                                TileDecor(g, tx, ty, rn, bi == Biome.BorealForest ? "pine" : "tree");
                            else if ((bi == Biome.Grassland) && rn.NextDouble() < 0.16)
                                TileDecor(g, tx, ty, rn, "tree");
                            else if ((bi == Biome.Desert || bi == Biome.Steppe) && rn.NextDouble() < 0.45)
                                TileDecor(g, tx, ty, rn, rn.NextDouble() < 0.5 ? "rock" : "cactus");
                            else if ((bi == Biome.Tundra || bi == Biome.Mountain) && rn.NextDouble() < 0.4)
                                TileDecor(g, tx, ty, rn, "pine");
                            else if (bi == Biome.Swamp && rn.NextDouble() < 0.3)
                                TileDecor(g, tx, ty, rn, "reed");
                        }

                        // Macro lighting: smooth large-scale brightness variation hides the tile grid
                        double macro = 0.5 + 0.5 * Math.Sin(tx * 0.31 + ty * 0.17) * Math.Cos(tx * 0.11 - ty * 0.23);
                        int macroA = (int)(macro * 22) - 11;
                        if (macroA > 3)
                            using (var ov = new SolidBrush(Color.FromArgb(macroA, 255, 255, 255)))
                                g.FillRectangle(ov, tx * TILE, ty * TILE, TILE, TILE);
                        else if (macroA < -3)
                            using (var ov = new SolidBrush(Color.FromArgb(-macroA, 10, 14, 24)))
                                g.FillRectangle(ov, tx * TILE, ty * TILE, TILE, TILE);
                    }
                }
            }

            minimapBg = new Bitmap(160, 120);
            using (var mg = Graphics.FromImage(minimapBg))
            {
                mg.InterpolationMode = InterpolationMode.Bilinear;
                mg.DrawImage(terrainBuffer, 0, 0, 160, 120);
                using var dim = new SolidBrush(Color.FromArgb(75, 0, 0, 0));
                mg.FillRectangle(dim, 0, 0, 160, 120);
            }
        }

        // ── Decorative nature sprites baked into the terrain buffer (zero runtime cost) ──
        readonly Dictionary<string, Bitmap> _tileSprites = new();

        void TileDecor(Graphics g, int tx, int ty, Random rn, string kind)
        {
            var bmp = TileSprite(kind);
            if (bmp == null) return;
            int ox = 4 + rn.Next(TILE - bmp.Width - 8), oy = 4 + rn.Next(TILE - bmp.Height - 8);
            g.DrawImage(bmp, tx * TILE + ox, ty * TILE + oy);
        }

        Bitmap? TileSprite(string kind)
        {
            if (_tileSprites.TryGetValue(kind, out var b)) return b;
            var bmp = TileSpriteFactory(kind);
            if (bmp != null) _tileSprites[kind] = bmp;
            return bmp;
        }

        static Bitmap? TileSpriteFactory(string kind)
        {
            var bmp = new Bitmap(40, 40);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            switch (kind)
            {
                case "tree":
                    using (var sh = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillEllipse(sh, 8, 30, 24, 6);
                    using (var tr = new SolidBrush(Color.FromArgb(255, 104, 72, 36))) g.FillRectangle(tr, 17, 20, 6, 12);
                    using (var l1 = new SolidBrush(Color.FromArgb(255, 52, 108, 44))) g.FillEllipse(l1, 8, 8, 24, 18);
                    using (var l2 = new SolidBrush(Color.FromArgb(255, 66, 130, 52))) g.FillEllipse(l2, 4, 13, 32, 16);
                    using (var hl = new SolidBrush(Color.FromArgb(70, 140, 200, 110))) g.FillEllipse(hl, 12, 10, 10, 7);
                    break;
                case "pine":
                    using (var sh = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillEllipse(sh, 10, 31, 20, 5);
                    using (var tr2 = new SolidBrush(Color.FromArgb(255, 92, 64, 34))) g.FillRectangle(tr2, 17, 26, 6, 8);
                    using (var tri = new SolidBrush(Color.FromArgb(255, 34, 84, 46)))
                    {
                        var p1 = new PointF[] { new(20, 4), new(8, 16), new(32, 16) };
                        var p2 = new PointF[] { new(20, 11), new(7, 24), new(33, 24) };
                        var p3 = new PointF[] { new(20, 18), new(8, 31), new(32, 31) };
                        g.FillPolygon(tri, p1); g.FillPolygon(tri, p2); g.FillPolygon(tri, p3);
                    }
                    using (var sn = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                    { g.FillEllipse(sn, 14, 6, 6, 3); g.FillEllipse(sn, 10, 16, 5, 3); }
                    break;
                case "rock":
                    using (var sh = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillEllipse(sh, 8, 27, 24, 6);
                    using (var rb = new SolidBrush(Color.FromArgb(255, 116, 118, 126)))
                        g.FillEllipse(rb, 7, 15, 26, 15);
                    using (var rb2 = new SolidBrush(Color.FromArgb(255, 146, 148, 156)))
                        g.FillEllipse(rb2, 10, 17, 10, 6);
                    break;
                case "cactus":
                    using (var sh = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillEllipse(sh, 13, 31, 14, 5);
                    using (var cb = new SolidBrush(Color.FromArgb(255, 62, 128, 56)))
                    {
                        g.FillRectangle(cb, 16, 8, 8, 26);
                        g.FillRectangle(cb, 10, 14, 8, 5); g.FillRectangle(cb, 10, 10, 4, 9);
                        g.FillRectangle(cb, 22, 18, 8, 5); g.FillRectangle(cb, 26, 14, 4, 9);
                    }
                    using (var cbh = new SolidBrush(Color.FromArgb(255, 96, 160, 80))) g.FillRectangle(cbh, 17, 9, 3, 24);
                    break;
                case "reed":
                    using (var rd = new Pen(Color.FromArgb(255, 56, 122, 66), 2f))
                    {
                        g.DrawLine(rd, 12, 32, 10, 10); g.DrawLine(rd, 18, 32, 18, 6); g.DrawLine(rd, 24, 32, 27, 12);
                    }
                    using (var rd2 = new SolidBrush(Color.FromArgb(255, 140, 96, 40)))
                    { g.FillEllipse(rd2, 16, 4, 5, 8); g.FillEllipse(rd2, 25, 10, 4, 7); }
                    break;
            }
            return bmp;
        }

        string B2T(Biome b) => b switch
        {
            Biome.Grassland => "grass",
            Biome.Forest or Biome.BorealForest or Biome.TropicalForest => "forest",
            Biome.Desert or Biome.Steppe => "sand",
            Biome.Tundra or Biome.Mountain => "snow",
            _ => "water"
        };

        void SimTimer_Tick(object sender, EventArgs e)
        {
            if (!isPaused && world != null)
            {
                for (int i = 0; i < simSpeed; i++) world.Update(1.0);
                PluginHost.TickAll(world);
                st = world.GetStats();
                TrackBirthsDeaths();
                SpawnAmbientPuffs();
                UpdateTribeHistory();
                if (world.Tick == 260 && System.Environment.GetEnvironmentVariable("NCD_SHOT") is string shotPath)
                {
                    // headless self-screenshot: render one frame offscreen in true client pixels
                    using var bmp = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
                    using var bg = Graphics.FromImage(bmp);
                    RenderFrame(bg);
                    bmp.Save(shotPath, System.Drawing.Imaging.ImageFormat.Png);
                    System.IO.File.WriteAllText(shotPath + ".txt", $"client={ClientSize.Width}x{ClientSize.Height}");
                }
                if (world.Tick % 25 == 0 && st != null)
                {
                    popH.Add(st.Population);
                    fitH.Add(st.AverageFitness);
                    if (popH.Count > 180) { popH.RemoveAt(0); fitH.RemoveAt(0); }
                }
            }
            UpdatePuffs();
            if (follow)
            {
                if (selH?.IsAlive == true)
                {
                    camX += (selH.X - ClientSize.Width / 2.0 / zoom - camX) * 0.12;
                    camY += (selH.Y - ClientSize.Height / 2.0 / zoom - camY) * 0.12;
                }
                else if (selA?.IsAlive == true)
                {
                    camX += (selA.X - ClientSize.Width / 2.0 / zoom - camX) * 0.12;
                    camY += (selA.Y - ClientSize.Height / 2.0 / zoom - camY) * 0.12;
                }
            }
            fCnt++;
            if ((DateTime.Now - fpsT).TotalSeconds >= 1)
            {
                fps = fCnt;
                fCnt = 0;
                fpsT = DateTime.Now;
            }
            Invalidate();
        }

        PointF W2S(double wx, double wy) => new((float)((wx - camX) * zoom), (float)((wy - camY) * zoom));
        (double x, double y) S2W(float sx, float sy) => (sx / zoom + camX, sy / zoom + camY);
        bool OnS(PointF p, float m = 40) => p.X > -m && p.X < ClientSize.Width + m && p.Y > -m && p.Y < ClientSize.Height + m;

        /// <summary>Rising world-anchored event labels — can be disabled in the settings drawer (F11 row / popup toggle).</summary>
        void DrawPopups(Graphics g)
        {
            if (!showPop) return;
            var src = world.Popups;
            if (src.Count == 0) return;
            for (int i = 0; i < src.Count; i++)
            {
                var p = src[i];
                int age = world.Tick - p.TickSpawned;
                if (age > PopupLife) continue;
                float rise = age * 0.010f;
                var scr = W2S(p.X, p.Y - rise * 8);
                if (!OnS(scr)) continue;
                float alpha = age < PopupLife * 0.70f ? 255f : 255f * (1f - (age - PopupLife * 0.70f) / (PopupLife * 0.30f));
                // Outline (dark) for readability in any background
                using var sh = new SolidBrush(Color.FromArgb((int)(alpha * 0.85f), 10, 10, 14));
                var szT = g.MeasureString(p.Text, fM);
                using var pill = RoundedPath((int)(scr.X - szT.Width / 2f - 3f), (int)(scr.Y - szT.Height / 2f - 2f),
                                             (int)(szT.Width + 6f), (int)(szT.Height + 3f), 5);
                g.FillPath(sh, pill);
                using var br = new SolidBrush(Color.FromArgb((int)alpha, (p.Argb >> 16) & 255, (p.Argb >> 8) & 255, p.Argb & 255));
                g.DrawString(p.Text, fM, br, scr.X - szT.Width / 2f, scr.Y - szT.Height / 2f);
            }
        }

        void Form1_Paint(object sender, PaintEventArgs e)
        {
            if (world == null) return;
            RenderFrame(e.Graphics);
        }

        /// <summary>Full frame render (used by both the Paint event and the offscreen self-screenshot).</summary>
        void RenderFrame(Graphics g)
        {
            g.SmoothingMode = zoom > 0.4 ? SmoothingMode.AntiAlias : SmoothingMode.HighSpeed;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.Clear(Color.FromArgb(12, 16, 25));

            DrawTerrain(g);
            DrawDN(g);
            if (showTerr) DrawTerr(g);
            if (showRes) DrawRes(g);
            if (showBld) DrawBld(g);
            if (showDead) DrawDead(g);
            DrawAnimals(g);
            DrawHumans(g);
            DrawParticles(g);
            DrawPopups(g);
            if (showWeather) DrawWth(g);
            if (showTop) DrawTop(g);
            if (showLeft) DrawLP(g);
            if (showLog) DrawEventLog(g);
            if (showMap) DrawMM(g);
            if (showSel) DrawSel(g);
            if (showGraph) DrawGr(g);
            DrawHlp(g);
            DrawDrawer(g);
        }

        void DrawTerrain(Graphics g)
        {
            if (terrainBuffer == null) return;
            var p0 = W2S(0, 0);
            float dw = (float)(world.Width * zoom);
            float dh = (float)(world.Height * zoom);
            g.DrawImage(terrainBuffer, p0.X, p0.Y, dw, dh);
        }

        void DrawDN(Graphics g)
        {
            float dl = (float)world.Env.DayLight;
            if (dl < 0.6f)
            {
                int alpha = Math.Clamp((int)((1f - dl / 0.6f) * 118), 0, 88);   // softer night: actions stay readable
                using var br = new SolidBrush(Color.FromArgb(alpha, 8, 12, 34));
                g.FillRectangle(br, ClientRectangle);
            }
        }

        void DrawRes(Graphics g)
        {
            float z = (float)zoom;
            var oldMode = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            for (int i = 0; i < world.Resources.Count; i++)
            {
                var r = world.Resources[i];
                if (r.Amount < 0.5) continue;
                var sp = W2S(r.X, r.Y);
                if (!OnS(sp)) continue;
                float sz = (float)(8 + r.Amount / r.MaxAmount * 8) * z;
                var bmp = ObjSprite(r.Type switch
                {
                    ResourceType.Berry => "res_berries",
                    ResourceType.Plant => "res_plant",
                    ResourceType.Stone => "res_stone",
                    ResourceType.Wood => "res_wood",
                    ResourceType.Water => "res_water",
                    ResourceType.Ore => "res_ore",
                    _ => "res_wood"
                });
                if (bmp is null)
                {
                    SolidBrush br = r.Type switch
                    {
                        ResourceType.Berry => brBerry,
                        ResourceType.Plant => brPlant,
                        ResourceType.Stone => brStone,
                        _ => brWood
                    };
                    g.FillEllipse(br, sp.X - sz / 2, sp.Y - sz / 2, sz, sz);
                    continue;
                }
                g.DrawImage(bmp, sp.X - sz / 2, sp.Y - sz / 2, sz, sz);
            }
            g.InterpolationMode = oldMode;
        }

        void DrawBld(Graphics g)
        {
            float z = (float)zoom;
            var oldMode = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            for (int i = 0; i < world.Buildings.Count; i++)
            {
                var b = world.Buildings[i];
                var sp = W2S(b.X, b.Y);
                if (!OnS(sp)) continue;
                float sz = (float)(b.Size * z);

                string key = b.Type switch
                {
                    BuildingType.Hut => "bld_hut",
                    BuildingType.StorageHut => "bld_storage",
                    BuildingType.Wall => "bld_wall",
                    BuildingType.Pen => "bld_pen",
                    BuildingType.Market => "bld_market",
                    BuildingType.Totem => "bld_totem",
                    _ => "bld_fire"
                };
                var bmp = ObjSprite(key);
                if (bmp is null)
                {
                    // Vector fallback (matches the legacy look)
                    if (b.Type == BuildingType.Firepit)
                        g.FillEllipse(brFire, sp.X - sz * 0.35f, sp.Y - sz * 0.35f, sz * 0.7f, sz * 0.7f);
                    else
                    {
                        var br = b.Type == BuildingType.StorageHut ? brStorage : brHut;
                        g.FillRectangle(br, sp.X - sz / 2, sp.Y - sz / 2, sz, sz);
                    }
                    continue;
                }
                g.DrawImage(bmp, sp.X - sz / 2, sp.Y - sz / 2, sz, sz);

                if (b.Type == BuildingType.Firepit)
                {
                    // Warm glow + alive flame on top of the sprite
                    float glow = sz * 2.2f;
                    using var glowBr = new SolidBrush(Color.FromArgb(40, 255, 160, 40));
                    g.FillEllipse(glowBr, sp.X - glow / 2, sp.Y - glow / 2, glow, glow);
                    float fl = (float)(world.Tick % 9) * 0.06f;
                    using (var flBr = new SolidBrush(Color.FromArgb(140, 255, 140 - (int)(fl * 40), 40)))
                    g.FillEllipse(flBr, sp.X - sz * 0.16f, sp.Y - sz * 0.24f - fl * sz * 0.3f, sz * 0.32f, sz * 0.40f + fl * sz * 0.25f);
                }
                else if (b.Type == BuildingType.Totem && !isPaused && world.Tick % 2 == 0 && Random.Shared.NextDouble() < 0.3)
                {
                    // Spirit wisps around the ritual pole
                    AddPuff((float)b.X, (float)b.Y - 6f, (float)(Random.Shared.NextDouble() - 0.5) * 0.6f, -0.2f, 9, 2.4f);
                }
            }
            g.InterpolationMode = oldMode;
        }

        void DrawTerr(Graphics g)
        {
            for (int i = 0; i < world.Tribes.Count; i++)
            {
                var t = world.Tribes[i];
                if (t.MemberIds.Count == 0) continue;
                var sp = W2S(t.CenterX, t.CenterY);
                float r = (float)(t.TerritoryRadius * zoom);
                using var f = new SolidBrush(Color.FromArgb(14, t.BannerColor));
                using var border = new Pen(Color.FromArgb(55, t.BannerColor), 1.2f) { DashStyle = DashStyle.Dash };
                g.FillEllipse(f, sp.X - r, sp.Y - r, r * 2, r * 2);
                g.DrawEllipse(border, sp.X - r, sp.Y - r, r * 2, r * 2);

                using var nb = new SolidBrush(Color.FromArgb(140, t.BannerColor));
                string label = $"{t.Name} ({t.Population})";
                var ns = g.MeasureString(label, fS);
                g.DrawString(label, fS, nb, sp.X - ns.Width / 2, sp.Y - r + 6);
            }
        }

        void DrawDead(Graphics g)
        {
            float z = (float)zoom;
            for (int i = 0; i < world.Humans.Count; i++)
            {
                var h = world.Humans[i];
                if (h.IsAlive || h.State != HumanState.Dead) continue;
                var sp = W2S(h.X, h.Y);
                if (!OnS(sp)) continue;
                float sz = 5 * z;
                g.FillEllipse(brDeadH, sp.X - sz, sp.Y - sz / 3, sz * 2, sz * 0.7f);
                if (zoom > 0.5)
                {
                    using var cross = new Pen(Color.FromArgb(120, 180, 50, 50), 1);
                    g.DrawLine(cross, sp.X - 3, sp.Y - 6 * z, sp.X + 3, sp.Y - 6 * z);
                    g.DrawLine(cross, sp.X, sp.Y - 9 * z, sp.X, sp.Y - 3 * z);
                }
            }
            for (int i = 0; i < world.Animals.Count; i++)
            {
                var a = world.Animals[i];
                if (a.IsAlive) continue;
                var sp = W2S(a.X, a.Y);
                if (!OnS(sp)) continue;
                float sz = 4 * z;
                g.FillEllipse(brDeadA, sp.X - sz, sp.Y - sz / 3, sz * 2, sz * 0.6f);
            }
        }

        // ── CC0 animal sprite sets (inkBubi "Seasons of Forest" sample pack, Creative Commons Zero) ──
        // Key: "<folder>:<idle|run>:<back|front|left|right>" -> 4 frames.
        readonly Dictionary<string, Bitmap?[]> spriteAnim = new(StringComparer.Ordinal);
        const string SPR_ROOT = "Assets/Animals";

        /// <summary>Game species -> sprite folder mapping (CC0 pack contents).</summary>
        static string SpeciesFolder(bool pred, string species) => pred
            ? "fox"                                        // Wolf/Bear/Lion/Hyena -> fox frames
            : species switch
            {
                "Rabbit" => "squirle",
                "Deer" or "Goat" or "Bison" or "Horse" => "doe",
                _ => "doe"
            };

        void LoadAnimalSprites()
        {
            string root = Path.Combine(AppContext.BaseDirectory, SPR_ROOT.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(root)) return;
            foreach (var an in Directory.GetDirectories(root))
            {
                var files = Directory.GetFiles(an, "*.png");
                foreach (var fn in files)
                {
                    // frame file pattern: <idle|run>_<back|front|left|right>_frame_%d.png
                    var m = Regex.Match(Path.GetFileName(fn), @"^(idle|run)_(back|front|left|right)_frame_(\d)\.png$",
                        RegexOptions.IgnoreCase);
                    if (!m.Success) continue;
                    string key = $"{Path.GetFileName(an).ToLowerInvariant()}:{m.Groups[1].Value.ToLowerInvariant()}:{m.Groups[2].Value.ToLowerInvariant()}";
                    int idx = m.Groups[3].Value[0] - '0';
                    if (!spriteAnim.TryGetValue(key, out var arr))
                        spriteAnim[key] = arr = new Bitmap[4];
                    try { arr[idx] = new Bitmap(fn, false); } catch { arr[idx] = null; }
                }
            }
            UpscaleSprites();
            BuildSpeciesLookSets();
        }

        /// <summary>Per-species visual identity: CC0 base frames + unique coat tint + body scale. Every species looks different.</summary>
        static (string folder, float r, float g, float b, float scale) SpeciesLook(string species) => species switch
        {
            "Rabbit" => ("squirle", 0.98f, 0.96f, 0.90f, 0.78f),   // small, pale
            "Deer"   => ("doe",     1.00f, 1.00f, 1.00f, 1.00f),   // reference
            "Goat"   => ("doe",     1.16f, 1.16f, 1.12f, 0.88f),   // whitish, compact
            "Bison"  => ("doe",     0.72f, 0.66f, 0.60f, 1.38f),   // dark, massive
            "Horse"  => ("doe",     1.10f, 0.94f, 0.74f, 1.12f),   // caramel, tall
            "Wolf"   => ("fox",     0.94f, 0.94f, 1.04f, 1.00f),   // cool grey
            "Bear"   => ("fox",     0.74f, 0.66f, 0.58f, 1.34f),   // dark brown, huge
            "Lion"   => ("fox",     1.18f, 1.02f, 0.68f, 1.06f),   // golden
            "Hyena"  => ("fox",     0.92f, 0.86f, 0.72f, 0.94f),   // tawny, lanky
            _ => ("", 1f, 1f, 1f, 1f)
        };

        /// <summary>Bakes a tinted + rescaled sprite set for each known species (run once after pack load).</summary>
        void BuildSpeciesLookSets()
        {
            foreach (var spec in new[] { "Rabbit", "Deer", "Goat", "Bison", "Horse", "Wolf", "Bear", "Lion", "Hyena" })
            {
                var (folder, cr, cg, cb, sc) = SpeciesLook(spec);
                if (folder.Length == 0) continue;
                string spKey = spec.ToLowerInvariant();
                foreach (var mode in new[] { "idle", "run" })
                    foreach (var dir in new[] { "back", "front", "left", "right" })
                        if (spriteAnim.TryGetValue($"{folder}:{mode}:{dir}", out var src) && src[0] != null)
                            spriteAnim[$"{spKey}:{mode}:{dir}"] = TintFrames(src, cr, cg, cb, sc);
            }
        }

        /// <summary>Channel-scaled color tint + integer-ish rescale of a 4-frame set (no per-frame allocation in app loop — cache only).</summary>
        static Bitmap?[] TintFrames(Bitmap?[] src, float cr, float cg, float cb, float scale)
        {
            var res = new Bitmap?[4];
            var cm = new System.Drawing.Imaging.ColorMatrix
            {
                Matrix00 = cr, Matrix11 = cg, Matrix22 = cb,
                Matrix33 = 1f, Matrix44 = 1f
            };
            for (int i = 0; i < 4; i++)
            {
                var s = src[i];
                if (s is null) continue;
                int w = Math.Max(2, (int)(s.Width * scale)), h = Math.Max(2, (int)(s.Height * scale));
                var d = new Bitmap(w, h);
                using var gg = Graphics.FromImage(d);
                gg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using var ia = new System.Drawing.Imaging.ImageAttributes();
                ia.SetColorMatrix(cm);
                gg.DrawImage(s, new Rectangle(0, 0, w, h), 0, 0, s.Width, s.Height, GraphicsUnit.Pixel, ia);
                res[i] = d;
            }
            return res;
        }

        /// <summary>2x NearestNeighbor pre-upscale: crisp pixel-art without runtime blur (CC0 pack frames are 41x33).</summary>
        void UpscaleSprites()
        {
            foreach (var kv in spriteAnim)
                for (int i = 0; i < 4; i++)
                {
                    var b = kv.Value[i];
                    if (b is null) continue;
                    var up = new Bitmap(b.Width * 2, b.Height * 2);
                    using (var g2 = Graphics.FromImage(up))
                    {
                        g2.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g2.DrawImage(b, 0, 0, up.Width, up.Height);
                    }
                    b.Dispose();
                    kv.Value[i] = up;
                }
        }

        /// <summary>Returns the 4-frame array for the animal, or null when the pack is missing (vector fallback).</summary>
        /// <summary>Returns the 4-frame array for the animal: species-specific look first, base folder fallback.</summary>
        Bitmap?[]? SpriteFrames(Animal a, int tick)
        {
            string folder = SpeciesFolder(a.IsPredator, a.SpeciesName);
            string mode = a.State == AnimalState.Sleeping || (Math.Abs(a.Vx) + Math.Abs(a.Vy)) < 0.06 ? "idle" : "run";
            double c = Math.Cos(a.Angle), s = Math.Sin(a.Angle);
            string dir = Math.Abs(c) > 0.55 ? (c > 0 ? "right" : "left") : (s < 0 ? "back" : "front");
            string key = $"{a.SpeciesName.ToLowerInvariant()}:{mode}:{dir}";
            return spriteAnim.TryGetValue(key, out var sp0) && sp0[0] != null ? sp0
                 : spriteAnim.TryGetValue($"{folder}:{mode}:{dir}", out var fr) && fr[0] != null ? fr : null;
        }

        /// <summary>Dashboard sidebar: rounded tab + slide-out panel with sections, pills and switches.</summary>
        void DrawDrawer(Graphics g)
        {
            // Smooth slide-in / slide-out
            drawerAnim = Math.Clamp(drawerAnim + (drawerOpen ? 0.18f : -0.22f), 0f, 1f);
            if (drawerAnim <= 0.001f && !drawerOpen) { DrawTabButton(g); return; }

            DrawTabButton(g);
            if (!drawerOpen) return;

            int animW = Math.Max(28, (int)(DrawerW * drawerAnim));
            int h = DrawerHeight;
            var st = g.Save();
            g.SetClip(new Rectangle(0, DrawerTop - 8, animW, h + 10));

            // Panel: dark gradient card with soft blue rim
            using var path = RoundedPath(-8, DrawerTop - 8, DrawerW + 8, h + 10, 16);
            using (var fill = new LinearGradientBrush(new Rectangle(0, DrawerTop, DrawerW, h),
                Color.FromArgb(236, 16, 21, 34), Color.FromArgb(236, 10, 13, 23), 90f))
                g.FillPath(fill, path);
            using (var rim = new Pen(Color.FromArgb(122, 96, 132, 186), 1f))
                g.DrawPath(rim, path);
            using (var acc = new LinearGradientBrush(new Rectangle(0, DrawerTop, 4, h),
                Color.FromArgb(220, 70, 150, 235), Color.FromArgb(60, 70, 150, 235), 90f))
                g.FillRectangle(acc, 2, DrawerTop + 8, 3, h - 16);

            // Header: live dot + title + subtitle
            using (var live = new SolidBrush(Color.FromArgb(120, 90, 225, 150)))
                g.FillEllipse(live, DrawerPad, DrawerTop + 14, 7, 7);
            g.DrawString("NeuroCivilization", fT, brTx, DrawerPad + 14, DrawerTop + 7);
            using (var sub = new SolidBrush(Color.FromArgb(150, 165, 190)))
                g.DrawString("Evolution Simulator", fM, sub, DrawerPad + 14, DrawerTop + 25);
            using (var dv = new Pen(Color.FromArgb(64, 92, 122, 172), 1f))
                g.DrawLine(dv, DrawerPad, DrawerTop + 42, DrawerW - DrawerPad, DrawerTop + 42);

            // Sections + rows
            string lastSection = "";
            int y = DrawerTop + HeaderH;
            for (int i = 0; i < drawerRows.Count; i++)
            {
                var row = drawerRows[i];
                if (!string.IsNullOrEmpty(row.Section) && row.Section != lastSection)
                {
                    lastSection = row.Section;
                    using var sh = new SolidBrush(Color.FromArgb(112, 152, 184));
                    g.DrawString(row.Section, fM, sh, DrawerPad, y + 6);
                    y += SectionH;
                }

                var rect = new Rectangle(10, y, DrawerW - 20, RowH - 5);
                bool? on = row.Get?.Invoke();
                string name = row.DynLabel?.Invoke() ?? row.Label;
                bool isAction = on is null;
                bool hovered = (i == drawerHover);

                if (on == true)
                {
                    using var pill = RoundedPath(rect.X, rect.Y, rect.Width, rect.Height, 12);
                    using (var pb = new LinearGradientBrush(rect, Color.FromArgb(205, 48, 96, 156), Color.FromArgb(205, 30, 60, 106), 90f))
                        g.FillPath(pb, pill);
                    using (var rb = new Pen(Color.FromArgb(160, 116, 168, 226), 1f))
                        g.DrawPath(rb, pill);
                }
                else if (hovered)
                {
                    using var hb = new SolidBrush(Color.FromArgb(58, 255, 255, 255));
                    FillRounded(g, hb, rect.X, rect.Y, rect.Width, rect.Height, 12);
                }

                // accent dot icon
                using (var dot = new SolidBrush(
                    on == true ? Color.FromArgb(220, 235, 248) :
                    isAction ? Color.FromArgb(130, 165, 205) :
                    hovered ? Color.FromArgb(150, 205, 245) : Color.FromArgb(80, 140, 195)))
                    g.FillEllipse(dot, rect.X + 13, rect.Y + (rect.Height - 7) / 2, 7, 7);

                using (var fg = new SolidBrush(on == true ? Color.White : hovered ? Color.FromArgb(235, 242, 250) : Color.FromArgb(170, 182, 198)))
                    g.DrawString(name, fM, fg, rect.X + 30, rect.Y + 8);

                if (isAction)
                {
                    using var ch = new SolidBrush(hovered ? Color.FromArgb(200, 225, 245) : Color.FromArgb(90, 115, 145));
                    g.DrawString("›", fN, ch, rect.X + rect.Width - 22, rect.Y + 6);
                }
                else
                {
                    DrawSwitch(g, rect.X + rect.Width - 36, rect.Y + (rect.Height - 15) / 2, 26, 15, on == true);
                }
                y += RowH;
            }

            // Footer
            using (var hint = new SolidBrush(Color.FromArgb(112, 142, 168)))
                g.DrawString("v2.0  •  ESC — закрыть", fM, hint, DrawerPad, DrawerTop + h - FooterH + 12);
            g.Restore(st);
        }

        /// <summary>Rounded menu tab in the top-left corner.</summary>
        void DrawTabButton(Graphics g)
        {
            int bx = 8, by = 6, bw = 42, bh = 32;
            using var p = RoundedPath(bx, by, bw, bh, 11);
            using (var fill = new SolidBrush(drawerOpen ? Color.FromArgb(226, 42, 62, 98) : Color.FromArgb(214, 24, 32, 54)))
                g.FillPath(fill, p);
            using (var rim = new Pen(Color.FromArgb(122, 112, 152, 206), 1f))
                g.DrawPath(rim, p);
            using var br = new SolidBrush(Color.FromArgb(226, 236, 246));
            g.DrawString(drawerOpen ? "✕" : "≡", fN, br, bx + (drawerOpen ? 12 : 11), by + 6);
        }

        /// <summary>Capsule on/off switch (dashboard style).</summary>
        void DrawSwitch(Graphics g, int x, int y, int w, int h, bool on)
        {
            using var p = RoundedPath(x, y, w, h, h / 2);
            using (var fill = new SolidBrush(on ? Color.FromArgb(205, 74, 196, 126) : Color.FromArgb(122, 62, 74, 92)))
                g.FillPath(fill, p);
            using (var rim = new Pen(on ? Color.FromArgb(120, 130, 245, 170) : Color.FromArgb(90, 102, 124), 1f))
                g.DrawPath(rim, p);
            float kr = h / 2f - 3f;
            float cx = on ? x + w - kr - 2f : x + kr + 2f;
            using (var knob = new SolidBrush(Color.White))
                g.FillEllipse(knob, cx - kr, y + 3f, kr * 2f, kr * 2f);
        }

        void FillRounded(Graphics g, Brush br, int x, int y, int w, int h, int r)
        {
            using var p = RoundedPath(x, y, w, h, r);
            g.FillPath(br, p);
        }

        void DrawRoundedBox(Graphics g, Color color, int x, int y, int w, int h, int r)
        {
            using var p = RoundedPath(x, y, w, h, r);
            using var br = new SolidBrush(color);
            g.FillPath(br, p);
        }

        int DrawerHit(Point p)
        {
            if (p.X >= 8 && p.X <= 50 && p.Y >= 6 && p.Y <= 38) return -2;   // tab toggles
            if (!drawerOpen || p.X > DrawerW || p.Y < DrawerTop) return -1;
            foreach (var (idx, rect) in DrawerRowRects())
                if (rect.Contains(p)) return idx;
            return -1;
        }

        /// <summary>Renders an animal using the CC0 sprite pack (crisp walking animation, 4 directions).</summary>
        void DrawAnimalSprite(Graphics g, Animal a, PointF sp, float sz, Bitmap?[] fr)
        {
            int tick = world.Tick;
            // Ground shadow
            using (var shBr = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
                g.FillEllipse(shBr, sp.X - sz * 0.60f, sp.Y - sz * 0.28f, sz * 1.20f, sz * 0.56f);

            // Sprite (failsafe: skip missing frames gracefully)
            var bmp = fr[((tick / 6 + a.Id) % 4 + 4) % 4] ?? fr[0];
            if (bmp != null)
            {
                float dh = sz * 2.05f;                       // 33px tall frame -> scale to ~2x sz
                float dw = dh * bmp.Width / Math.Max(1, bmp.Height);
                var st = g.Save();
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                if (a.State == AnimalState.Sleeping) g.RotateTransform(-90f);   // lying on side
                g.DrawImage(bmp, sp.X - dw / 2, sp.Y - dh * 0.62f, dw, dh);
                g.Restore(st);
            }

            // State rings / auras (unchanged semantics)
            Color oc = a.IsPredator ? Color.FromArgb(150, 215, 45, 45) :
                       a.IsDomesticated ? Color.FromArgb(180, 80, 165, 255) :
                       Color.FromArgb(66, 58, 108, 55);
            using (var op = new Pen(oc, (float)Math.Max(1f, zoom * 0.8f)))
                g.DrawEllipse(op, sp.X - sz * 0.75f, sp.Y - sz * 0.42f, sz * 1.5f, sz * 0.9f);

            if (a.State == AnimalState.Hunting && zoom > 0.4f)
            {
                float pulse = 1f + 0.10f * (float)Math.Sin(tick * 0.22 + a.Id);
                using var alertPen = new Pen(Color.FromArgb(150, 255, 85, 85), (float)Math.Max(1f, zoom * 0.8f));
                g.DrawEllipse(alertPen, sp.X - sz * 0.95f * pulse, sp.Y - sz * 0.95f * pulse, sz * 1.9f * pulse, sz * 1.9f * pulse);
            }

            if (a.State == AnimalState.Sleeping && zoom > 0.55f)
            {
                float zb = ((tick * 0.55f + a.Id * 3.1f) % 100f) / 100f;
                using var zBr = new SolidBrush(Color.FromArgb((int)(175 * (1f - zb)), 205, 225, 255));
                g.DrawString("z", fM, zBr, sp.X + sz * 0.5f + zb * sz * 0.7f, sp.Y - sz * 0.9f - zb * sz * 1.3f);
            }

            if (showLbl && zoom > 0.5f)
            {
                float bw = sz * 1.4f, bh = Math.Max(2f, 2f * (float)zoom);
                float bx = sp.X - bw / 2f, by = sp.Y - sz * 1.28f - bh - 3f;
                float hp = (float)Math.Clamp(a.Health / a.MaxHealth, 0, 1);
                g.FillRectangle(Brushes.DarkRed, bx, by, bw, bh);
                g.FillRectangle(hp > 0.5f ? Brushes.LimeGreen : hp > 0.25f ? Brushes.Orange : Brushes.Red, bx, by, bw * hp, bh);
                using (var hgBr = new SolidBrush(Color.FromArgb(130, 255, 195, 45)))
                    g.FillRectangle(hgBr, bx, by + bh + 1f, bw * (float)Math.Clamp(a.Hunger, 0, 1), 1.5f);
                if (zoom > 0.7f)
                {
                    var ns = g.MeasureString(a.SpeciesName, fM);
                    g.DrawString(a.SpeciesName, fM, Brushes.LightGray, sp.X - ns.Width / 2f, sp.Y + sz * 0.62f);
                }
            }

            if (a == selA)
                g.DrawEllipse(penSelect, sp.X - sz * 1.25f, sp.Y - sz * 1.25f, sz * 2.5f, sz * 2.5f);
        }

        /// <summary>Returns a cached detailed sprite for a resource/building kind (rendered once at 72 px).</summary>
        readonly Dictionary<string, Bitmap> objSprites = new(StringComparer.Ordinal);
        Bitmap? ObjSprite(string key)
        {
            if (objSprites.TryGetValue(key, out var cached)) return cached;
            var bmp = BuildObjSprite(key);
            objSprites[key] = bmp;                       // may be a transparent empty bitmap
            return bmp.Width > 2 ? bmp : null;
        }

        /// <summary>Draws one 72x72 sprite from code — crisp, deterministic, no external files needed.</summary>
        Bitmap BuildObjSprite(string key)
        {
            var bmp = new Bitmap(72, 72);
            using var sm = new SolidBrush(Color.FromArgb(70, 20, 26, 12));
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            switch (key)
            {
                case "res_berries":
                    g.FillEllipse(sm, 12, 22, 48, 40);
                    using (var lb = new SolidBrush(Color.FromArgb(255, 42, 118, 30))) g.FillEllipse(lb, 8, 18, 56, 42);
                    using (var rb = new SolidBrush(Color.FromArgb(235, 205, 40, 110)))
                    {
                        g.FillEllipse(rb, 22, 30, 10, 10); g.FillEllipse(rb, 38, 26, 10, 10);
                        g.FillEllipse(rb, 30, 42, 10, 10); g.FillEllipse(rb, 46, 38, 9, 9);
                        g.FillEllipse(rb, 18, 44, 8, 8);
                    }
                    break;
                case "res_plant":
                    using (var lb2 = new SolidBrush(Color.FromArgb(255, 60, 150, 40)))
                    {
                        g.FillEllipse(lb2, 14, 26, 44, 30);
                        g.FillEllipse(lb2, 24, 18, 24, 22);
                    }
                    using (var hl = new SolidBrush(Color.FromArgb(60, 255, 255, 255))) g.FillEllipse(hl, 18, 30, 18, 9);
                    break;
                case "res_stone":
                    using (var st = new SolidBrush(Color.FromArgb(255, 148, 148, 140)))
                    {
                        var shp = new PointF[] { new(14, 52), new(18, 30), new(34, 18), new(56, 28), new(60, 48), new(40, 56) };
                        g.FillPolygon(st, shp);
                    }
                    using (var sd = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillEllipse(sd, 24, 40, 30, 14);
                    break;
                case "res_wood":
                    using (var wd = new SolidBrush(Color.FromArgb(255, 140, 90, 40)))
                    {
                        g.FillEllipse(wd, 10, 30, 52, 20);
                        g.FillEllipse(wd, 16, 22, 44, 20);
                    }
                    using (var wc = new SolidBrush(Color.FromArgb(255, 190, 148, 96)))
                    {
                        g.FillEllipse(wc, 54, 34, 10, 14); g.FillEllipse(wc, 50, 26, 10, 14);
                    }
                    break;
                case "res_water":
                    using (var wt = new SolidBrush(Color.FromArgb(255, 60, 140, 210))) g.FillEllipse(wt, 10, 24, 52, 40);
                    using (var wg = new SolidBrush(Color.FromArgb(90, 255, 255, 255))) g.FillEllipse(wg, 18, 30, 20, 8);
                    break;
                case "res_ore":
                    using (var orB = new SolidBrush(Color.FromArgb(255, 120, 118, 130)))
                    {
                        var oct = new PointF[] { new(14, 52), new(22, 28), new(40, 16), new(58, 32), new(54, 54), new(34, 58) };
                        g.FillPolygon(orB, oct);
                    }
                    using (var md = new SolidBrush(Color.FromArgb(230, 120, 210, 255))) { g.FillEllipse(md, 28, 34, 10, 8); g.FillEllipse(md, 40, 42, 8, 7); }
                    break;
                case "bld_hut":
                    using (var wallB = new SolidBrush(Color.FromArgb(255, 150, 112, 66))) g.FillRectangle(wallB, 16, 34, 40, 26);
                    using (var roofB = new SolidBrush(Color.FromArgb(255, 105, 70, 34)))
                    {
                        var rf = new PointF[] { new(10, 36), new(36, 10), new(62, 36) };
                        g.FillPolygon(roofB, rf);
                    }
                    using (var dr = new SolidBrush(Color.FromArgb(255, 86, 58, 26))) g.FillRectangle(dr, 32, 42, 10, 18);
                    using (var ven = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillRectangle(ven, 18, 38, 38, 4);
                    break;
                case "bld_storage":
                    using (var cr = new SolidBrush(Color.FromArgb(255, 176, 134, 74))) g.FillRectangle(cr, 14, 30, 44, 30);
                    using (var cs = new SolidBrush(Color.FromArgb(255, 122, 88, 44)))
                    { g.FillRectangle(cs, 14, 40, 44, 4); g.FillRectangle(cs, 22, 30, 4, 30); g.FillRectangle(cs, 46, 30, 4, 30); }
                    using (var lid = new SolidBrush(Color.FromArgb(255, 96, 66, 30))) g.FillRectangle(lid, 10, 24, 52, 8);
                    break;
                case "bld_wall":
                    using (var wb = new SolidBrush(Color.FromArgb(255, 135, 138, 144))) g.FillRectangle(wb, 12, 22, 48, 40);
                    using (var wg2 = new SolidBrush(Color.FromArgb(80, 30, 32, 36)))
                    { for (int r2 = 0; r2 < 4; r2++) g.FillRectangle(wg2, 12 + (r2 % 2) * 12, 28 + r2 * 9, 6, 3); }
                    using (var wl = new SolidBrush(Color.FromArgb(50, 255, 255, 255))) g.FillRectangle(wl, 14, 24, 44, 4);
                    break;
                case "bld_pen":
                    using (var fc = new SolidBrush(Color.FromArgb(255, 150, 108, 52)))
                    {
                        for (int x = 10; x <= 58; x += 16) g.FillRectangle(fc, x, 26, 6, 38);
                        g.FillRectangle(fc, 10, 30, 52, 5); g.FillRectangle(fc, 10, 52, 52, 5);
                    }
                    break;
                case "bld_totem":
                    using (var pole = new SolidBrush(Color.FromArgb(255, 122, 84, 46))) g.FillRectangle(pole, 30, 14, 8, 54);
                    using (var carve = new SolidBrush(Color.FromArgb(255, 200, 160, 90)))
                    {
                        g.FillEllipse(carve, 26, 16, 16, 12);   // spirit face 1
                        g.FillEllipse(carve, 26, 32, 16, 12);   // spirit face 2
                        g.FillEllipse(carve, 28, 48, 12, 10);   // spirit face 3
                    }
                    using (var dark = new SolidBrush(Color.FromArgb(255, 60, 40, 20)))
                    {
                        g.FillRectangle(dark, 30, 20, 8, 3);
                        g.FillRectangle(dark, 30, 36, 8, 3);
                        g.FillEllipse(dark, 31, 50, 6, 3);
                    }
                    using (var wingB = new SolidBrush(Color.FromArgb(255, 96, 64, 32)))
                    { g.FillRectangle(wingB, 18, 24, 12, 4); g.FillRectangle(wingB, 36, 24, 12, 4); }
                    break;
                case "bld_market":
                    using (var canopy = new SolidBrush(Color.FromArgb(255, 216, 140, 66)))
                    {
                        var cf = new PointF[] { new(8, 30), new(60, 30), new(56, 20), new(12, 20) };
                        g.FillPolygon(canopy, cf);
                    }
                    using (var canopyB = new SolidBrush(Color.FromArgb(255, 170, 100, 40)))
                        g.FillRectangle(canopyB, 8, 30, 52, 4);
                    using (var mpostB = new SolidBrush(Color.FromArgb(255, 120, 84, 44)))
                    { g.FillRectangle(mpostB, 10, 34, 5, 32); g.FillRectangle(mpostB, 53, 34, 5, 32); }
                    using (var crateB = new SolidBrush(Color.FromArgb(255, 190, 140, 70)))
                    { g.FillRectangle(crateB, 16, 46, 14, 12); g.FillRectangle(crateB, 40, 42, 12, 16); }
                    using (var wareB = new SolidBrush(Color.FromArgb(255, 220, 200, 120)))
                    { g.FillRectangle(wareB, 18, 44, 10, 3); g.FillRectangle(wareB, 42, 40, 8, 3); }
                    break;
                case "bld_fire":
                    using (var rings = new SolidBrush(Color.FromArgb(255, 105, 105, 112)))
                    { for (int i = 0; i < 7; i++) { double a2 = i * Math.PI * 2 / 7; g.FillEllipse(rings, 28 + (float)Math.Cos(a2) * 18, 34 + (float)Math.Sin(a2) * 18, 9, 9); } }
                    using (var logs = new SolidBrush(Color.FromArgb(255, 92, 60, 30))) { g.FillRectangle(logs, 24, 36, 26, 6); g.FillRectangle(logs, 30, 42, 22, 5); }
                    using (var fl2 = new SolidBrush(Color.FromArgb(230, 255, 140, 20))) g.FillEllipse(fl2, 28, 22, 16, 22);
                    using (var fl3 = new SolidBrush(Color.FromArgb(220, 255, 210, 80))) g.FillEllipse(fl3, 32, 26, 8, 12);
                    break;
                default: return new Bitmap(2, 2);
            }
            // Shared ground contact shadow
            using (var gs = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillEllipse(gs, 22, 60, 28, 8);
            return bmp;
        }

        void DrawAnimals(Graphics g)
        {
            float z = (float)zoom;
            float shadowFade = zoom > 0.35f ? 1f : 0.55f;
            int tick = world.Tick;

            for (int i = 0; i < world.Animals.Count; i++)
            {
                var a = world.Animals[i];
                if (!a.IsAlive) continue;
                if (showPredOnly && a.IsHerbivore) continue;   // drawer filter: predators only
                var sp = W2S(a.X, a.Y);
                if (!OnS(sp)) continue;
                float sz = a.DrawSize * z;

                if (sz < 2.4f)
                {
                    g.FillRectangle(a.IsPredator ? brPredDot : brHerbDot, sp.X, sp.Y, 2, 2);
                    continue;
                }

                Color c = a.GetColor();
                var frames = SpriteFrames(a, world.Tick);
                if (showSprites && frames != null) { DrawAnimalSprite(g, a, sp, sz, frames); continue; }

                bool sleeping = a.State == AnimalState.Sleeping;
                bool moving = !sleeping && (Math.Abs(a.Vx) + Math.Abs(a.Vy)) > 0.02;
                double spd = Math.Sqrt(a.Vx * a.Vx + a.Vy * a.Vy);

                // ---- Ground shadow ----
                float shW = sz * 1.15f, shH = sz * 0.60f;
                using (var shBr = new SolidBrush(Color.FromArgb((int)(52 * shadowFade), 0, 0, 0)))
                    g.FillEllipse(shBr, sp.X - shW / 2 + sz * 0.12f, sp.Y - shH / 2 + sz * 0.24f, shW, shH);

                // ---- Gait: frequency & amplitude scale with real speed ----
                float gaitAmp = moving ? (float)Math.Min(1.0, 0.30 + spd * 45.0) : 0f;
                float gaitPhase = (tick * 0.16f + a.Id * 0.7f);
                float swing = (float)Math.Sin(gaitPhase) * gaitAmp;
                float lift = Math.Abs((float)Math.Cos(gaitPhase)) * gaitAmp;

                var gs = g.Save();
                g.TranslateTransform(sp.X, sp.Y);
                g.RotateTransform((float)(a.Angle * 180.0 / Math.PI));

                // ---- Tail ----
                float tailLen = sz * (a.IsPredator ? 0.78f : 0.44f);
                Color tailC = Color.FromArgb(215, Math.Max(0, c.R - 28), Math.Max(0, c.G - 28), Math.Max(0, c.B - 28));
                using (var tailBr = new SolidBrush(tailC))
                {
                    float tipY = swing * tailLen * 0.55f;
                    if (a.IsPredator)
                    {
                        using var tp = new Pen(tailC, Math.Max(1f, sz * 0.11f));
                        g.DrawLine(tp, -sz * 0.55f, -sz * 0.08f, -sz * 0.55f - tailLen, tipY);
                    }
                    else
                    {
                        g.FillEllipse(tailBr, -sz * 0.58f - tailLen * 0.7f, -sz * 0.09f + tipY, tailLen, sz * 0.19f);
                    }
                }

                // ---- Legs (diagonal trot) ----
                float legLen = sz * 0.27f;
                using (var legPen = new Pen(Color.FromArgb(215, Math.Max(0, c.R - 40), Math.Max(0, c.G - 36), Math.Max(0, c.B - 32)),
                    Math.Max(1f, sz * 0.09f)))
                {
                    float ls = legLen * (0.78f + lift * 0.34f);
                    g.DrawLine(legPen, sz * 0.25f, sz * 0.25f, sz * 0.25f + swing * sz * 0.15f, sz * 0.25f + ls);
                    g.DrawLine(legPen, sz * 0.25f, -sz * 0.25f, sz * 0.25f - swing * sz * 0.15f, -sz * 0.25f - ls);
                    g.DrawLine(legPen, -sz * 0.30f, sz * 0.25f, -sz * 0.30f - swing * sz * 0.17f, sz * 0.25f + ls);
                    g.DrawLine(legPen, -sz * 0.30f, -sz * 0.25f, -sz * 0.30f + swing * sz * 0.17f, -sz * 0.25f - ls);
                }

                // ---- Body ----
                float bodyW = sz * 1.26f, bodyH = sz * 0.68f;
                using (var bodyBr = new SolidBrush(c))
                    g.FillEllipse(bodyBr, -bodyW / 2, -bodyH / 2, bodyW, bodyH);
                using (var dorsalBr = new SolidBrush(Color.FromArgb(62, 0, 0, 0)))
                    g.FillEllipse(dorsalBr, -bodyW * 0.40f, -bodyH * 0.46f, bodyW * 0.80f, bodyH * 0.46f);
                if (sz > 6f)
                {
                    using var bellyBr = new SolidBrush(Color.FromArgb(42, 255, 255, 255));
                    g.FillEllipse(bellyBr, -bodyW * 0.35f, bodyH * 0.04f, bodyW * 0.70f, bodyH * 0.38f);
                }

                // ---- Coat pattern ----
                if (sz > 9f && zoom > 0.55f)
                {
                    float pat = (float)a.Genes.ColorB;
                    using var patPen = new Pen(Color.FromArgb(46, 0, 0, 0), Math.Max(0.6f, sz * 0.025f));
                    if (pat < 0.34f)
                    {
                        // Stripes
                        for (int f = 0; f < 4; f++)
                        {
                            float fx = -sz * 0.34f + f * sz * 0.24f;
                            g.DrawLine(patPen, fx, -bodyH * 0.40f, fx - sz * 0.06f, bodyH * 0.22f);
                        }
                    }
                    else if (pat < 0.67f)
                    {
                        // Spots
                        using var spotBr = new SolidBrush(Color.FromArgb(56, 0, 0, 0));
                        g.FillEllipse(spotBr, -sz * 0.30f, -bodyH * 0.22f, sz * 0.15f, sz * 0.10f);
                        g.FillEllipse(spotBr, sz * 0.02f, bodyH * 0.02f, sz * 0.12f, sz * 0.09f);
                    }
                    else
                    {
                        // Fur strokes
                        for (int f = 0; f < 5; f++)
                        {
                            float fx = -sz * 0.42f + f * sz * 0.22f;
                            g.DrawLine(patPen, fx, -bodyH * 0.34f, fx - sz * 0.05f, bodyH * 0.10f);
                        }
                    }
                }

                // ---- Neck + Head ----
                float headSz = sz * 0.33f;
                float headX = sz * 0.56f;
                float bob = sleeping ? 0f : lift * sz * 0.045f;
                Color headC = Color.FromArgb(255, Math.Min(255, c.R + 22), Math.Min(255, c.G + 12), Math.Max(0, c.B - 8));

                using (var neckPen = new Pen(headC, Math.Max(1.5f, sz * 0.22f)))
                    g.DrawLine(neckPen, sz * 0.34f, 0, headX - headSz * 0.35f, bob * 0.5f);

                using (var headBr = new SolidBrush(headC))
                {
                    if (a.IsPredator)
                    {
                        var snout = new PointF[]
                        {
                            new(headX + headSz * 1.20f, bob),
                            new(headX + headSz * 0.10f, bob - headSz * 0.40f),
                            new(headX + headSz * 0.10f, bob + headSz * 0.40f)
                        };
                        g.FillPolygon(headBr, snout);
                    }
                    else
                    {
                        g.FillEllipse(headBr, headX - headSz * 0.55f, bob - headSz * 0.46f, headSz * 1.30f, headSz * 0.92f);
                    }
                    g.FillEllipse(headBr, headX - headSz * 0.58f, bob - headSz * 0.52f, headSz * 1.10f, headSz * 1.04f);
                }

                // Nose
                if (a.IsPredator && sz > 4f)
                {
                    g.FillEllipse(Brushes.Black, headX + headSz * 1.02f, bob - headSz * 0.11f, headSz * 0.22f, headSz * 0.22f);
                }

                // ---- Ears (perked when alert, flat when scared/sleeping) ----
                if (sz > 4.5f)
                {
                    float perk = sleeping ? -0.30f : a.Brain.Fear > 0.45f ? -0.18f : 0.14f;
                    using var earBr = new SolidBrush(Color.FromArgb(225, Math.Max(0, c.R - 20), Math.Max(0, c.G - 20), Math.Max(0, c.B - 20)));
                    g.FillPolygon(earBr, new PointF[]
                    {
                        new(headX - headSz * 0.42f, bob - headSz * 0.36f),
                        new(headX - headSz * 0.10f, bob - headSz * (0.92f + perk)),
                        new(headX + headSz * 0.06f, bob - headSz * 0.38f)
                    });
                    g.FillPolygon(earBr, new PointF[]
                    {
                        new(headX - headSz * 0.38f, bob + headSz * 0.38f),
                        new(headX - headSz * 0.04f, bob + headSz * (0.92f + perk)),
                        new(headX + headSz * 0.12f, bob + headSz * 0.40f)
                    });
                }

                // ---- Eyes + blink ----
                float eyeR = Math.Max(1f, headSz * 0.21f);
                float eyeCx = headX + headSz * 0.30f;
                float blinkT = ((tick + a.Id * 41) % 130) / 130f;
                float eyeRy = blinkT > 0.945f ? eyeR * 0.16f : eyeR;

                if (sleeping)
                {
                    using var slPen = new Pen(Color.FromArgb(170, 20, 15, 12), Math.Max(0.7f, sz * 0.028f));
                    g.DrawArc(slPen, eyeCx - eyeR, bob - headSz * 0.26f - eyeR, eyeR * 2f, eyeR * 2f, 205, 130);
                    g.DrawArc(slPen, eyeCx - eyeR, bob + headSz * 0.14f - eyeR, eyeR * 2f, eyeR * 2f, 205, 130);
                }
                else if (a.IsPredator)
                {
                    using var irisBr = new SolidBrush(Color.FromArgb(240, 255, 210, 80));
                    g.FillEllipse(irisBr, eyeCx - eyeR, bob - headSz * 0.27f - eyeRy, eyeR * 2f, eyeRy * 2f);
                    g.FillEllipse(irisBr, eyeCx - eyeR, bob + headSz * 0.13f - eyeRy, eyeR * 2f, eyeRy * 2f);
                    g.FillEllipse(Brushes.Black, eyeCx - eyeR * 0.22f, bob - headSz * 0.27f - eyeRy * 0.92f, eyeR * 0.44f, eyeRy * 1.84f);
                    g.FillEllipse(Brushes.Black, eyeCx - eyeR * 0.22f, bob + headSz * 0.13f - eyeRy * 0.92f, eyeR * 0.44f, eyeRy * 1.84f);
                }
                else
                {
                    using var eyeBr = new SolidBrush(Color.FromArgb(235, 28, 20, 14));
                    g.FillEllipse(eyeBr, eyeCx - eyeR, bob - headSz * 0.28f - eyeRy, eyeR * 2f, eyeRy * 2f);
                    g.FillEllipse(eyeBr, eyeCx - eyeR, bob + headSz * 0.14f - eyeRy, eyeR * 2f, eyeRy * 2f);
                    if (eyeRy > eyeR * 0.5f)
                    {
                        using var glintBr = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
                        g.FillEllipse(glintBr, eyeCx - eyeR * 0.42f, bob - headSz * 0.26f, eyeR * 0.5f, eyeRy * 0.55f);
                        g.FillEllipse(glintBr, eyeCx - eyeR * 0.42f, bob + headSz * 0.16f, eyeR * 0.5f, eyeRy * 0.55f);
                    }
                }

                g.Restore(gs);

                // ---- Sleep Zzz ----
                if (sleeping && zoom > 0.55f)
                {
                    float zb = ((tick * 0.55f + a.Id * 3.1f) % 100f) / 100f;
                    using var zBr = new SolidBrush(Color.FromArgb((int)(175 * (1f - zb)), 205, 225, 255));
                    g.DrawString("z", fM, zBr, sp.X + sz * 0.5f + zb * sz * 0.7f, sp.Y - sz * 0.9f - zb * sz * 1.3f);
                }

                // ---- Outline / state rings ----
                Color oc = a.IsPredator ? Color.FromArgb(150, 215, 45, 45) :
                           a.IsDomesticated ? Color.FromArgb(180, 80, 165, 255) :
                           Color.FromArgb(66, 58, 108, 55);
                using (var op = new Pen(oc, Math.Max(1f, z * 0.8f)))
                    g.DrawEllipse(op, sp.X - bodyW / 2, sp.Y - bodyH / 2, bodyW, bodyH);

                if (a.State == AnimalState.Hunting && zoom > 0.4f)
                {
                    float pulse = 1f + 0.10f * (float)Math.Sin(tick * 0.22 + a.Id);
                    using var alertPen = new Pen(Color.FromArgb(150, 255, 85, 85), Math.Max(1f, z * 0.8f));
                    g.DrawEllipse(alertPen, sp.X - sz * 0.95f * pulse, sp.Y - sz * 0.95f * pulse, sz * 1.9f * pulse, sz * 1.9f * pulse);
                }
                if (a.IsDomesticated && zoom > 0.35f)
                {
                    float pulse = 1f + 0.10f * (float)Math.Sin(tick * 0.10 + a.Id);
                    using var bondPen = new Pen(Color.FromArgb(200, 125, 205, 255), Math.Max(1f, z * 0.9f));
                    g.DrawEllipse(bondPen, sp.X - bodyW * 0.5f * pulse, sp.Y - bodyH * 0.5f * pulse, bodyW * pulse, bodyH * pulse);
                }
                if (a.IsPregnant && zoom > 0.5f)
                {
                    using var prBr = new SolidBrush(Color.FromArgb(120, 255, 190, 220));
                    g.FillEllipse(prBr, sp.X - sz * 0.16f, sp.Y - sz * 0.10f, sz * 0.36f, sz * 0.28f);
                }

                // ---- Health / hunger bars ----
                if (showLbl && zoom > 0.5f)
                {
                    float bw = sz * 1.4f, bh = Math.Max(2f, 2f * z);
                    float bx = sp.X - bw / 2f, by = sp.Y - sz * 0.60f - bh - 4f;
                    float hp = (float)Math.Clamp(a.Health / a.MaxHealth, 0, 1);
                    g.FillRectangle(Brushes.DarkRed, bx, by, bw, bh);
                    g.FillRectangle(hp > 0.5f ? Brushes.LimeGreen : hp > 0.25f ? Brushes.Orange : Brushes.Red, bx, by, bw * hp, bh);
                    using (var hgBr = new SolidBrush(Color.FromArgb(130, 255, 195, 45))) g.FillRectangle(hgBr, bx, by + bh + 1f, bw * (float)Math.Clamp(a.Hunger, 0, 1), 1.5f);

                    if (zoom > 0.7f)
                    {
                        var ns = g.MeasureString(a.SpeciesName, fM);
                        g.DrawString(a.SpeciesName, fM, Brushes.LightGray, sp.X - ns.Width / 2f, sp.Y + sz * 0.42f);
                    }
                }

                if (a == selA)
                    g.DrawEllipse(penSelect, sp.X - sz * 1.25f, sp.Y - sz * 1.25f, sz * 2.5f, sz * 2.5f);
            }
        }

                // ── CC0 human sprite LOD (villager.png by dutzy, CC0) — far zoom sprites / close zoom detailed vector ──
        readonly Bitmap?[] _hMan = new Bitmap?[20];   // row-major [row(0..3), col(0..4)]
        bool _humanSheetOk;

        /// <summary>Slices Assets/Humans/villager.png: row0 walk, row1 run, row2 work, row3 idle/blink.</summary>
        void LoadHumanSprites()
        {
            try
            {
                string f = Path.Combine(AppContext.BaseDirectory, "Assets", "Humans", "villager.png");
                if (!File.Exists(f)) return;
                using var sheet = new Bitmap(f);
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 5; c++)
                    {
                        var t = sheet.Clone(new Rectangle(c * 16, r * 16, 16, 16), sheet.PixelFormat);
                        if (TileEmpty(t)) { t.Dispose(); continue; }
                        _hMan[r * 5 + c] = t;
                    }
                _humanSheetOk = _hMan[0] != null && _hMan[1] != null && _hMan[2] != null && _hMan[15] != null;
            }
            catch { _humanSheetOk = false; }
        }

        static bool TileEmpty(Bitmap b)
        {
            for (int y = 0; y < b.Height; y++)
                for (int x = 0; x < b.Width; x++)
                    if (b.GetPixel(x, y).A > 0) return false;
            return true;
        }

        /// <summary>Picks the sprite frame for the human's live state (walk/run/work/idle+blink), or null.</summary>
        Bitmap? HumanSpriteFrame(Human h, int tick)
        {
            if (!_humanSheetOk) return null;
            bool moving = Math.Abs(h.Vx) + Math.Abs(h.Vy) > 0.02;
            int row = h.State switch
            {
                HumanState.Sleeping or HumanState.Resting => 3,
                HumanState.Gathering or HumanState.Building or
                HumanState.Crafting or HumanState.Hunting => 2,
                HumanState.Running => 1,
                _ => moving ? 0 : 3
            };
            int col = row switch
            {
                0 => (tick / 6) % 4,          // walk cycle
                1 => (tick / 4) % 4,          // run cycle
                2 => (tick / 8) % 4,          // work swing
                3 => (tick % 160) < 10 ? 2    // occasional eye blink
                                   : 0,
                _ => 0
            };
            return _hMan[row * 5 + col] ?? _hMan[row * 5];
        }

        /// <summary>Far-view human render: CC0 sprite + tribe ring + leader dot + health bar + selection.</summary>
        void DrawHumanLOD(Graphics g, Human h, PointF sp, float sz, Bitmap fr)
        {
            using (var sh = new SolidBrush(Color.FromArgb(45, 0, 0, 0)))
                g.FillEllipse(sh, sp.X - sz * 0.5f, sp.Y - sz * 0.20f, sz, sz * 0.45f);
            var st = g.Save();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            float dh = sz * 1.9f, dw = dh * fr.Width / Math.Max(1, fr.Height);
            g.DrawImage(fr, sp.X - dw / 2, sp.Y + sz * 0.45f - dh, dw, dh);
            g.Restore(st);

            var tr = h.TribeId >= 0 ? world.GetTribeById(h.TribeId) : null;
            Color oc = tr?.BannerColor ?? Color.FromArgb(150, 90, 90, 95);

            // Tribe-colored tunic overlay on the sprite torso
            using (var tunic = new SolidBrush(Color.FromArgb(120, oc)))
                g.FillEllipse(tunic, sp.X - dw * 0.28f, sp.Y + sz * 0.45f - dh * 0.52f, dw * 0.56f, dh * 0.34f);
            using (var tunicHi = new SolidBrush(Color.FromArgb(45, 255, 255, 255)))
                g.FillEllipse(tunicHi, sp.X - dw * 0.18f, sp.Y + sz * 0.45f - dh * 0.48f, dw * 0.36f, dh * 0.14f);

            using (var ring = new Pen(Color.FromArgb(170, oc), Math.Max(1f, (float)zoom)))
                g.DrawEllipse(ring, sp.X - sz * 0.62f, sp.Y - sz * 0.55f, sz * 1.24f, sz * 1.10f);
            if (h.IsLeader && zoom > 0.45f)
                g.FillEllipse(Brushes.Gold, sp.X - sz * 0.13f, sp.Y - sz * 1.30f, sz * 0.26f, sz * 0.26f);
            if (h.IsPregnant && zoom > 0.55f)
            {
                using var prBr = new SolidBrush(Color.FromArgb(120, 255, 190, 220));
                g.FillEllipse(prBr, sp.X - sz * 0.14f, sp.Y - sz * 0.10f, sz * 0.32f, sz * 0.24f);
            }
            if (showLbl && zoom > 0.45f)
            {
                float bw = sz * 1.2f, bh = Math.Max(1.6f, 1.8f * (float)zoom), bx = sp.X - bw / 2, by = sp.Y - sz * 1.50f;
                float hp = (float)Math.Clamp(h.Health / h.MaxHealth, 0, 1);
                g.FillRectangle(Brushes.DarkRed, bx, by, bw, bh);
                g.FillRectangle(hp > 0.6f ? Brushes.LimeGreen : hp > 0.3f ? Brushes.Orange : Brushes.Red, bx, by, bw * hp, bh);
            }
            if (h == selH)
                g.DrawEllipse(penSelect, sp.X - sz * 1.15f, sp.Y - sz * 1.30f, sz * 2.3f, sz * 2.2f);
        }

        void DrawHumans(Graphics g)
        {
            float z = (float)zoom;
            int tick = world.Tick;

            for (int i = 0; i < world.Humans.Count; i++)
            {
                var h = world.Humans[i];
                if (!h.IsAlive) continue;
                if (showPregOnly && !h.IsPregnant) continue;                 // drawer filter: pregnant only
                if (tribeFilter >= 0 && h.TribeId != tribeFilter) continue;  // drawer filter: one tribe
                var sp = W2S(h.X, h.Y);
                if (!OnS(sp)) continue;
                float sz = h.DrawSize * z;

                if (sz < 3.2f)
                {
                    using var db = new SolidBrush(h.GetColor());
                    g.FillRectangle(db, sp.X - 1.5f, sp.Y - 1.5f, 3, 3);
                    continue;
                }

                Color skinC = h.GetColor();

                // Far view: CC0 sprite LOD instead of detailed vector art
                if (showSprites && zoom < 0.9)
                {
                    var hf = HumanSpriteFrame(h, world.Tick);
                    if (hf != null) { DrawHumanLOD(g, h, sp, sz, hf); continue; }
                }
                Color outC = Color.FromArgb(155, 78, 78, 82);
                if (h.TribeId >= 0)
                {
                    var tribe = world.GetTribeById(h.TribeId);
                    if (tribe != null) outC = tribe.BannerColor;
                }

                bool sleeping = h.State == HumanState.Sleeping;
                bool working = h.State == HumanState.Gathering || h.State == HumanState.Building ||
                               h.State == HumanState.Crafting || h.State == HumanState.Hunting;
                bool moving = h.State == HumanState.Walking || h.State == HumanState.Running;
                double spd = Math.Sqrt(h.Vx * h.Vx + h.Vy * h.Vy);

                // ---- Gait: run cycle scales with speed ----
                float stepAmp = moving ? (float)Math.Min(1.0, 0.18 + spd * 26.0) : 0f;
                float bobAmp = moving ? (float)Math.Min(1.0, 0.12 + spd * 18.0) : 0f;
                float runRate = h.State == HumanState.Running ? 0.24f : 0.14f;
                float ph = tick * runRate + h.Id * 0.9f;
                float legSwing = (float)Math.Sin(ph) * stepAmp;
                float armSwing = (float)Math.Sin(ph + Math.PI) * stepAmp * 0.55f;
                float bob = sleeping ? 0f : -(float)Math.Abs(Math.Cos(ph)) * sz * 0.018f * bobAmp;

                float headR = sz * 0.22f;
                float bodyH = sz * 0.56f;
                float legLen = sz * 0.40f;
                float bodyW = sz * 0.36f;
                float footY = sp.Y + bodyH * 0.28f + legLen;

                // ---- Ground shadow ----
                using (var shBr = new SolidBrush(Color.FromArgb(52, 0, 0, 0)))
                    g.FillEllipse(shBr, sp.X - bodyW * 0.85f + sz * 0.10f, footY - sz * 0.09f,
                                  bodyW * 1.7f, sz * 0.20f);

                var gs = g.Save();
                g.TranslateTransform(0, bob);

                // ---- Legs ----
                Color legC = Color.FromArgb(230, Math.Max(0, skinC.R - 46), Math.Max(0, skinC.G - 36), Math.Max(0, skinC.B - 26));
                using (var legPen = new Pen(legC, Math.Max(1.6f, sz * 0.105f)))
                {
                    float legSpread = bodyW * 0.28f;
                    float hipY = sp.Y + bodyH * 0.26f;
                    // Left leg (forward phase)
                    g.DrawLine(legPen, sp.X - legSpread, hipY, sp.X - legSpread + legSwing * sz * 0.13f, hipY + legLen);
                    // Right leg (opposite phase)
                    g.DrawLine(legPen, sp.X + legSpread, hipY, sp.X + legSpread - legSwing * sz * 0.13f, hipY + legLen);
                    // Feet
                    using var footPen = new Pen(Color.FromArgb(210, 55, 42, 32), Math.Max(1.2f, sz * 0.07f));
                    g.DrawLine(footPen, sp.X - legSpread + legSwing * sz * 0.13f, hipY + legLen,
                                      sp.X - legSpread + legSwing * sz * 0.13f + sz * 0.06f, hipY + legLen);
                    g.DrawLine(footPen, sp.X + legSpread - legSwing * sz * 0.13f, hipY + legLen,
                                      sp.X + legSpread - legSwing * sz * 0.13f + sz * 0.06f, hipY + legLen);
                }

                // ---- Torso ----
                float torsoTop = sp.Y - bodyH * 0.22f;
                using (var bodyBr = new SolidBrush(Color.FromArgb(255, outC.R, outC.G, outC.B)))
                {
                    g.FillEllipse(bodyBr, sp.X - bodyW / 2, torsoTop, bodyW, bodyH * 0.60f);
                }
                // Clothing shading (darker hem) + dark outline for sprite-like readability
                using (var shadeBr = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                    g.FillEllipse(shadeBr, sp.X - bodyW / 2, torsoTop + bodyH * 0.34f, bodyW, bodyH * 0.26f);
                using (var outlPen = new Pen(Color.FromArgb(120, 12, 14, 18), Math.Max(0.8f, sz * 0.035f)))
                    g.DrawEllipse(outlPen, sp.X - bodyW / 2, torsoTop, bodyW, bodyH * 0.60f);
                // Chest highlight + tribe emblem stripe
                using (var hiBr = new SolidBrush(Color.FromArgb(46, 255, 255, 255)))
                    g.FillEllipse(hiBr, sp.X - bodyW * 0.34f, torsoTop + bodyH * 0.03f, bodyW * 0.68f, bodyH * 0.22f);
                if (h.TribeId >= 0 && bodyW > 4f)
                {
                    using var embBr = new SolidBrush(Color.FromArgb(190, 255, 255, 255));
                    g.FillEllipse(embBr, sp.X - bodyW * 0.10f, torsoTop + bodyH * 0.10f, bodyW * 0.20f, bodyH * 0.16f);
                    // Tribe sash
                    using var sashPen = new Pen(Color.FromArgb(230, Math.Min(255, outC.R + 40), Math.Min(255, outC.G + 40), Math.Min(255, outC.B + 40)),
                        Math.Max(1f, sz * 0.06f));
                    g.DrawLine(sashPen, sp.X - bodyW * 0.46f, torsoTop + bodyH * 0.24f, sp.X + bodyW * 0.46f, torsoTop + bodyH * 0.30f);
                }
                // Clothing hem
                using (var hemPen = new Pen(Color.FromArgb(120, 30, 25, 22), Math.Max(0.8f, sz * 0.035f)))
                    g.DrawLine(hemPen, sp.X - bodyW * 0.48f, sp.Y + bodyH * 0.36f, sp.X + bodyW * 0.48f, sp.Y + bodyH * 0.36f);

                // ---- Arms ----
                float shoulderY = sp.Y - bodyH * 0.14f;
                float armLen = sz * 0.30f;
                using (var armPen = new Pen(Color.FromArgb(235, skinC.R, skinC.G, skinC.B), Math.Max(1.3f, sz * 0.075f)))
                {
                    float handLX = sp.X - bodyW * 0.52f, handRX = sp.X + bodyW * 0.52f;
                    float handLY, handRY;

                    if (working)
                    {
                        // Working swing (chopping / gathering motion)
                        float wk = (float)Math.Sin(tick * 0.30f + h.Id) * armLen * 0.38f;
                        handLY = shoulderY + armLen * 0.5f - wk;
                        handRY = shoulderY + armLen * 0.5f + wk;
                    }
                    else if (sleeping)
                    {
                        handLY = handRY = shoulderY + armLen * 0.85f;
                    }
                    else
                    {
                        handLY = shoulderY + armLen + armSwing * sz * 0.13f;
                        handRY = shoulderY + armLen - armSwing * sz * 0.13f;
                    }

                    g.DrawLine(armPen, sp.X - bodyW * 0.44f, shoulderY, handLX, handLY);
                    g.DrawLine(armPen, sp.X + bodyW * 0.44f, shoulderY, handRX, handRY);

                    // Hands
                    using var handBr = new SolidBrush(Color.FromArgb(240, skinC.R, skinC.G, skinC.B));
                    float hr2 = Math.Max(1f, sz * 0.045f);
                    g.FillEllipse(handBr, handLX - hr2, handLY - hr2, hr2 * 2, hr2 * 2);
                    g.FillEllipse(handBr, handRX - hr2, handRY - hr2, hr2 * 2, hr2 * 2);

                    // Tool / weapon in right hand
                    if (working && h.EquippedTool != null && zoom > 0.55f)
                    {
                        using var toolPen = new Pen(Color.FromArgb(230, 150, 120, 85), Math.Max(1f, sz * 0.06f));
                        g.DrawLine(toolPen, handRX, handRY, handRX + sz * 0.14f, handRY - sz * 0.20f);
                    }
                }

                // ---- Weapon (drawn in hand, sheathed on back when walking) ----
                if (h.EquippedWeapon != null && zoom > 0.45f)
                {
                    using var wpPen = new Pen(Color.FromArgb(240, 190, 195, 205), Math.Max(1f, sz * 0.065f));
                    float wx = sp.X + bodyW * 0.55f, wy = shoulderY + armLen * 0.55f;
                    g.DrawLine(wpPen, wx, wy, wx + sz * 0.13f, wy - sz * 0.30f);
                    g.FillEllipse(Brushes.SaddleBrown, wx + sz * 0.11f, wy - sz * 0.36f, sz * 0.07f, sz * 0.07f);
                }

                // ---- Head ----
                float headY = torsoTop - headR * 0.95f;
                // Neck
                using (var neckBr = new SolidBrush(Color.FromArgb(240, Math.Max(0, skinC.R - 18), Math.Max(0, skinC.G - 16), Math.Max(0, skinC.B - 14))))
                    g.FillRectangle(neckBr, sp.X - headR * 0.34f, headY + headR * 0.72f, headR * 0.68f, headR * 0.5f);

                using (var headBr = new SolidBrush(Color.FromArgb(255, skinC.R, skinC.G, skinC.B)))
                    g.FillEllipse(headBr, sp.X - headR, headY, headR * 2, headR * 2);
                // Skull outline for crisp sprite-like silhouette
                using (var skullPen = new Pen(Color.FromArgb(130, 16, 14, 14), Math.Max(0.7f, sz * 0.03f)))
                    g.DrawEllipse(skullPen, sp.X - headR, headY, headR * 2, headR * 2);

                // Hair (genome-driven, greys with age)
                Color hairC = h.Genes.Hair;
                using (var hairBr = new SolidBrush(Color.FromArgb(240, hairC.R, hairC.G, hairC.B)))
                {
                    // Hair cap: full ellipse over the skull, then face is re-exposed below
                    g.FillEllipse(hairBr, sp.X - headR * 1.05f, headY - headR * 1.05f, headR * 2.10f, headR * 2.10f);
                    if (h.Genes.HairStyle > 0.5f)
                    {
                        // Longer hair - side locks framing the face
                        g.FillEllipse(hairBr, sp.X - headR * 1.20f, headY - headR * 0.10f, headR * 0.58f, headR * 1.50f);
                        g.FillEllipse(hairBr, sp.X + headR * 0.62f, headY - headR * 0.10f, headR * 0.58f, headR * 1.50f);
                    }
                    // Re-expose the face (skin) over the lower part of the skull
                    using var faceBr = new SolidBrush(Color.FromArgb(255, skinC.R, skinC.G, skinC.B));
                    g.FillEllipse(faceBr, sp.X - headR * 0.90f, headY + headR * 0.14f, headR * 1.80f, headR * 1.82f);
                }

                // Beard — adult males, soft arc under the chin (looks more primitive-realistic)
                if (h.Sex == Gender.Male && h.Age > 16 && headR > 3f)
                {
                    float beardA = (float)Math.Min(1.0, 0.25 + h.Genes.HairStyle * 0.65);
                    using (var brr = new SolidBrush(Color.FromArgb((int)(215 * beardA), hairC.R, hairC.G, hairC.B)))
                        g.FillEllipse(brr, sp.X - headR * 0.62f, headY + headR * 0.95f, headR * 1.24f, headR * 0.85f);
                }

                // Cold-weather fur cloak collar (realistic adaptation to climate)
                if (h.IsAlive && world.Env.Temperature < 6 && bodyW > 4f)
                {
                    using (var fur = new SolidBrush(Color.FromArgb(200, 128, 112, 96)))
                        g.FillEllipse(fur, sp.X - bodyW * 0.72f, torsoTop - sz * 0.03f, bodyW * 1.44f, bodyH * 0.24f);
                    using (var furHi = new SolidBrush(Color.FromArgb(80, 190, 175, 155)))
                        g.FillEllipse(furHi, sp.X - bodyW * 0.60f, torsoTop, bodyW * 1.2f, bodyH * 0.10f);
                }

                // Face — eyes track movement direction
                if (sz > 6f && !sleeping)
                {
                    float eyeR = Math.Max(1f, headR * 0.17f);
                    float ex = sp.X + (float)Math.Cos(h.Angle) * headR * 0.26f;
                    float ey = headY + headR * 0.12f + (float)Math.Sin(h.Angle) * headR * 0.14f;
                    float perpX = -(float)Math.Sin(h.Angle) * headR * 0.30f;
                    float perpY = (float)Math.Cos(h.Angle) * headR * 0.30f;
                    float blinkT = ((tick * 2 + h.Id * 53) % 170) / 170f;
                    float eyeRy = blinkT > 0.955f ? eyeR * 0.18f : eyeR;

                    using var scleraBr = new SolidBrush(Color.FromArgb(240, 245, 248, 252));
                    g.FillEllipse(scleraBr, ex + perpX - eyeR, ey + perpY - eyeRy, eyeR * 2, eyeRy * 2);
                    g.FillEllipse(scleraBr, ex - perpX - eyeR, ey - perpY - eyeRy, eyeR * 2, eyeRy * 2);
                    if (eyeRy > eyeR * 0.5f)
                    {
                        float pupR = eyeR * 0.52f;
                        using var pupBr = new SolidBrush(Color.FromArgb(250, 24, 20, 16));
                        g.FillEllipse(pupBr, ex + perpX - pupR, ey + perpY - pupR, pupR * 2, pupR * 2);
                        g.FillEllipse(pupBr, ex - perpX - pupR, ey - perpY - pupR, pupR * 2, pupR * 2);
                    }
                    // Brow (anger / fear)
                    if (h.Brain.Cortisol > 0.35f && headR > 3.5f)
                    {
                        using var browPen = new Pen(Color.FromArgb(170, 90, 40, 30), Math.Max(0.8f, sz * 0.025f));
                        g.DrawLine(browPen, ex + perpX - eyeR, ey + perpY - eyeR * 1.6f, ex + perpX + eyeR, ey + perpY - eyeR * 0.7f);
                        g.DrawLine(browPen, ex - perpX - eyeR, ey - perpY - eyeR * 1.6f, ex - perpX + eyeR, ey - perpY - eyeR * 0.7f);
                    }
                }
                else if (sleeping && sz > 4f)
                {
                    using var slPen = new Pen(Color.FromArgb(180, 60, 40, 32), Math.Max(0.7f, sz * 0.025f));
                    float eyeR2 = Math.Max(1f, headR * 0.18f);
                    g.DrawArc(slPen, sp.X - headR * 0.52f, headY + headR * 0.06f, eyeR2 * 2, eyeR2 * 2, 200, 140);
                    g.DrawArc(slPen, sp.X + headR * 0.10f, headY + headR * 0.06f, eyeR2 * 2, eyeR2 * 2, 200, 140);
                }

                g.Restore(gs);

                // ---- Leader crown ----
                if (h.IsLeader && zoom > 0.4f)
                {
                    float crY = headY - sz * 0.06f;
                    float cw = headR * 0.95f, ch = sz * 0.13f;
                    var crown = new PointF[]
                    {
                        new(sp.X - cw, crY + ch),
                        new(sp.X - cw * 0.62f, crY),
                        new(sp.X - cw * 0.30f, crY + ch * 0.55f),
                        new(sp.X, crY - ch * 0.30f),
                        new(sp.X + cw * 0.30f, crY + ch * 0.55f),
                        new(sp.X + cw * 0.62f, crY),
                        new(sp.X + cw, crY + ch)
                    };
                    g.FillPolygon(Brushes.Gold, crown);
                    using var gemBr = new SolidBrush(Color.FromArgb(220, 220, 60, 60));
                    g.FillEllipse(gemBr, sp.X - headR * 0.12f, crY + ch * 0.15f, headR * 0.24f, headR * 0.24f);
                }

                // ---- Pregnancy ----
                if (h.IsPregnant && zoom > 0.45f)
                {
                    using var prBr = new SolidBrush(Color.FromArgb(110, 255, 195, 220));
                    g.FillEllipse(prBr, sp.X - bodyW * 0.36f, sp.Y + bodyH * 0.04f, bodyW * 0.72f, bodyH * 0.34f);
                }

                // ---- Sleep Zzz ----
                if (sleeping && zoom > 0.55f)
                {
                    float zb = ((tick * 0.5f + h.Id * 2.3f) % 100f) / 100f;
                    using var zBr = new SolidBrush(Color.FromArgb((int)(180 * (1f - zb)), 205, 225, 255));
                    g.DrawString("z", fM, zBr, sp.X + headR + zb * sz * 0.6f, headY - sz * 0.35f - zb * sz * 1.2f);
                }

                // ---- Name tag ----
                if (showLbl && zoom > 0.55f)
                {
                    var ns = g.MeasureString(h.Name, fM);
                    g.DrawString(h.Name, fM, Brushes.WhiteSmoke,
                        sp.X - ns.Width / 2f, footY + 2f);
                }

                // ---- Vitals bars ----
                if (showLbl && zoom > 0.5f)
                {
                    float bw = sz * 1.55f, bh = Math.Max(2.2f, 2.6f * z);
                    float bx = sp.X - bw / 2f, by = headY - bh - 4f;

                    g.FillRectangle(Brushes.DarkRed, bx, by, bw, bh);
                    float hp = (float)Math.Clamp(h.Health / h.MaxHealth, 0, 1);
                    g.FillRectangle(hp > 0.6f ? Brushes.LimeGreen : hp > 0.3f ? Brushes.Orange : Brushes.Red, bx, by, bw * hp, bh);

                    // Hunger (amber) + Energy (blue) micro-bars
                    float sub = bh * 0.34f;
                    using (var huBr = new SolidBrush(Color.FromArgb(150, 255, 190, 40))) g.FillRectangle(huBr, bx, by + bh + 1f, bw * (float)Math.Clamp(h.Hunger, 0, 1), sub);
                    using (var enBr = new SolidBrush(Color.FromArgb(150, 90, 180, 255))) g.FillRectangle(enBr, bx, by + bh + 1f + sub + 1f,
                        bw * (float)Math.Clamp(h.Energy / h.MaxEnergy, 0, 1), sub);

                    // Neurochemistry strip
                    if (zoom > 0.7f)
                    {
                        float nY = by + bh + 3f + sub * 2f;
                        using (var csBr = new SolidBrush(Color.FromArgb(190, 255, 90, 140))) g.FillRectangle(csBr, bx, nY, bw * (float)Math.Clamp(h.Brain.Cortisol, 0, 1), sub * 0.8f);
                        using (var dpBr = new SolidBrush(Color.FromArgb(190, 120, 230, 255))) g.FillRectangle(dpBr, bx + bw * (float)Math.Clamp(h.Brain.Cortisol, 0, 1), nY,
                            bw * (float)Math.Clamp(h.Brain.Dopamine, 0, 1), sub * 0.8f);
                    }

                    // State icon
                    if (zoom > 0.65f)
                    {
                        string icon = h.State switch
                        {
                            HumanState.Eating => "🍖",
                            HumanState.Sleeping => "💤",
                            HumanState.Fighting => "⚔",
                            HumanState.Building => "🔨",
                            HumanState.Hunting => "🏹",
                            HumanState.Running => "💨",
                            HumanState.Resting => "~",
                            HumanState.Crafting => "🔧",
                            HumanState.Trading => "⚖",
                            HumanState.Teaching => "📖",
                            HumanState.Fleeing => "!",
                            _ => ""
                        };
                        if (icon != "") g.DrawString(icon, fM, Brushes.Gold, sp.X + bodyW * 0.62f, headY - sz * 0.06f);
                    }

                    // Selection + target line
                    if (h == selH)
                    {
                        float selR = sz * 1.15f;
                        g.DrawEllipse(penSelect, sp.X - selR, headY - sz * 0.18f, selR * 2, sz * 1.55f + legLen);

                        if (h.HasTarget)
                        {
                            var tp = W2S(h.TargetX, h.TargetY);
                            g.DrawLine(penTargetLine, sp.X, sp.Y, tp.X, tp.Y);
                            g.DrawLine(penTargetCross, tp.X - 4, tp.Y - 4, tp.X + 4, tp.Y + 4);
                            g.DrawLine(penTargetCross, tp.X - 4, tp.Y + 4, tp.X + 4, tp.Y - 4);
                        }
                    }
                }
                else if (h == selH)
                {
                    float selR = sz * 1.15f;
                    g.DrawEllipse(penSelect, sp.X - selR, headY - sz * 0.18f, selR * 2, sz * 1.55f + legLen);
                }
            }
        }
        void DrawWth(Graphics g)
        {
            // Rain / storm / snow / leaves / ash are now PARTICLES (See SpawnAmbientPuffs + DrawParticles).
            switch (world.Env.CurrentWeather)
            {
                case Weather.Fog:
                    using (var b = new SolidBrush(Color.FromArgb(30, 190, 195, 200)))
                        g.FillRectangle(b, ClientRectangle);
                    break;
                case Weather.HeatWave:
                    using (var b = new SolidBrush(Color.FromArgb(15, 255, 140, 0)))
                        g.FillRectangle(b, ClientRectangle);
                    break;
            }
        }

        /// <summary>Per-tick emitters: firepit smoke & sparks, running dust, weather precipitation.</summary>
        void SpawnAmbientPuffs()
        {
            double vw = ClientSize.Width / zoom, vh = ClientSize.Height / zoom;
            double vx0 = camX, vy0 = camY;

            for (int i = 0; i < world.Buildings.Count; i++)
            {
                var b = world.Buildings[i];
                if (b.Type != BuildingType.Firepit) continue;
                if (b.X < vx0 - 40 || b.X > vx0 + vw + 40 || b.Y < vy0 - 40 || b.Y > vy0 + vh + 40) continue;
                if (world.Tick % 2 == 0)
                    AddPuff((float)b.X, (float)b.Y - 4f, (float)(Random.Shared.NextDouble() - 0.5) * 0.4f, -0.55f, 0, 2.4f);
                if (Random.Shared.NextDouble() < 0.10)
                    AddPuff((float)b.X, (float)b.Y - 6f, (float)(Random.Shared.NextDouble() - 0.5) * 1.8f, -0.9f, 1, 1.6f);
            }

            if ((world.Tick & 3) == 0)
                for (int i = 0; i < world.Humans.Count; i += 2)
                {
                    var h = world.Humans[i];
                    if (!h.IsAlive) continue;
                    if (h.State != HumanState.Running && h.State != HumanState.Fighting && h.State != HumanState.Hunting) continue;
                    if (h.X < vx0 || h.X > vx0 + vw || h.Y < vy0 || h.Y > vy0 + vh) continue;
                    AddPuff((float)h.X, (float)h.Y + 2f, (float)(Random.Shared.NextDouble() - 0.5) * 0.6f, 0.15f, 2, 2.0f);
                }

            // Ripples while wading / swimming
            if ((world.Tick & 5) == 0)
            {
                for (int i = 0; i < world.Humans.Count; i++)
                {
                    var h = world.Humans[i];
                    if (!h.IsAlive || Math.Abs(h.Vx) + Math.Abs(h.Vy) < 0.25) continue;
                    if (!world.Env.IsWater(h.X, h.Y)) continue;
                    if (h.X < vx0 || h.X > vx0 + vw || h.Y < vy0 || h.Y > vy0 + vh) continue;
                    AddPuff((float)h.X, (float)h.Y + 2f, 0, 0, 10, 2.0f);
                }
                for (int i = 0; i < world.Animals.Count; i += 2)
                {
                    var a = world.Animals[i];
                    if (a == null || !a.IsAlive || Math.Abs(a.Vx) + Math.Abs(a.Vy) < 0.25) continue;
                    if (!world.Env.IsWater(a.X, a.Y)) continue;
                    if (a.X < vx0 || a.X > vx0 + vw || a.Y < vy0 || a.Y > vy0 + vh) continue;
                    AddPuff((float)a.X, (float)a.Y + 2f, 0, 0, 10, 2.0f);
                }
            }

            switch (world.Env.CurrentWeather)
            {
                case Weather.Rain:
                    for (int i = 0; i < 22; i++) AddPuff((float)(vx0 + Random.Shared.NextDouble() * vw), (float)(vy0 - 10), 0, 0, 3, 2f);
                    break;
                case Weather.Storm:
                    for (int i = 0; i < 34; i++) AddPuff((float)(vx0 + Random.Shared.NextDouble() * vw), (float)(vy0 - 12), -0.8f, 0, 3, 2.2f);
                    break;
                case Weather.Snow:
                    for (int i = 0; i < 10; i++) AddPuff((float)(vx0 + Random.Shared.NextDouble() * vw), (float)(vy0 - 6), 0, 0, 4, 2.6f);
                    break;
                default:
                    if (world.Env.CurrentSeason == Season.Autumn)
                        for (int i = 0; i < 2; i++)
                            if (Random.Shared.NextDouble() < 0.6)
                                AddPuff((float)(vx0 + Random.Shared.NextDouble() * vw), (float)(vy0 - 4), (float)(Random.Shared.NextDouble() - 0.5) * 0.8f, 0, 5, 2.6f);
                    break;
            }
            if (world.Env.IsThundering && Random.Shared.NextDouble() < 0.2)
                AddPuff((float)(vx0 + Random.Shared.NextDouble() * vw), (float)(vy0 + Random.Shared.NextDouble() * vh), 0, 0.4f, 6, 2f);
        }

        void AddPuff(float x, float y, float vx, float vy, int kind, float size)
        {
            if (_puffs.Count > 1000) return;
            _puffs.Add(new Puff { X = x, Y = y, VX = vx, VY = vy, A = 1f, Size = size, Kind = kind });
        }

        void UpdatePuffs()
        {
            for (int i = _puffs.Count - 1; i >= 0; i--)
            {
                var p = _puffs[i];
                p.X += p.VX; p.Y += p.VY;
                switch (p.Kind)
                {
                    case 0: p.VY = -0.35f; p.A -= 0.012f; p.Size += 0.05f; break;    // smoke
                    case 1: p.A -= 0.05f; p.VY = -0.4f; break;                      // spark
                    case 2: p.A -= 0.06f; break;                                    // dust
                    case 3: p.A -= 0.04f; p.VY = 2.2f; break;                       // rain drop
                    case 4: p.A -= 0.006f; p.VY = 0.4f; p.X += (float)Math.Sin(p.Y * 0.05) * 0.3f; break; // snow drift
                    case 5: p.A -= 0.008f; p.VY = 0.3f; p.X += (float)Math.Sin(p.Y * 0.07) * 0.5f; break; // falling leaf
                    case 6: p.A -= 0.01f; p.VY = 0.5f; break;                       // ash
                    case 7: p.A -= 0.008f; p.Y -= 0.25f; break;                     // ghost rising
                    case 8: p.A -= 0.045f; p.Y -= 0.35f; break;                     // birth heart
                    case 9: p.A -= 0.01f; p.Y -= 0.15f; break;                      // totem spirit
                    case 10: p.Size += 0.22f; p.A -= 0.055f; break;                  // water ripple
                }
                if (p.A <= 0f) _puffs.RemoveAt(i);
                else _puffs[i] = p;
            }
        }

        void DrawParticles(Graphics g)
        {
            if (_puffs.Count == 0) return;
            for (int i = 0; i < _puffs.Count; i++)
            {
                var p = _puffs[i];
                var sp = W2S(p.X, p.Y);
                if (sp.X < -20 || sp.Y < -30 || sp.X > ClientSize.Width + 20 || sp.Y > ClientSize.Height + 20) continue;
                int a = Math.Clamp((int)(p.A * 255), 0, 255);
                float s = Math.Max(1f, p.Size * (float)zoom);
                switch (p.Kind)
                {
                    case 0:
                        using (var br = new SolidBrush(Color.FromArgb(a * 50 / 100, 122, 122, 130)))
                            g.FillEllipse(br, sp.X - s, sp.Y - s, s * 2, s * 2);
                        break;
                    case 1:
                        using (var br = new SolidBrush(Color.FromArgb(a, 255, 190, 60)))
                            g.FillEllipse(br, sp.X - s / 2, sp.Y - s / 2, s, s);
                        break;
                    case 2:
                        using (var br = new SolidBrush(Color.FromArgb(a * 60 / 100, 192, 172, 132)))
                            g.FillEllipse(br, sp.X - s, sp.Y - s, s * 2, s * 2);
                        break;
                    case 3:
                        using (var pen = new Pen(Color.FromArgb(a, 130, 180, 255), 1f))
                            g.DrawLine(pen, sp.X, sp.Y, sp.X - 1.5f, sp.Y + 5f);
                        break;
                    case 4:
                        using (var br = new SolidBrush(Color.FromArgb(a, 240, 246, 252)))
                            g.FillEllipse(br, sp.X - s / 2, sp.Y - s / 2, s, s);
                        break;
                    case 5:
                        using (var br = new SolidBrush(Color.FromArgb(a, 214, 150, 60)))
                            g.FillRectangle(br, sp.X - s / 2, sp.Y - s / 2, s, s * 0.7f);
                        break;
                    case 6:
                        using (var br = new SolidBrush(Color.FromArgb(a * 70 / 100, 70, 70, 74)))
                            g.FillEllipse(br, sp.X - s / 2, sp.Y - s / 2, s, s);
                        break;
                    case 7: // ghost
                        using (var br = new SolidBrush(Color.FromArgb(a * 60 / 100, 220, 235, 255)))
                            g.FillEllipse(br, sp.X - s, sp.Y - s * 1.4f, s * 2, s * 2.4f);
                        using (var eye = new SolidBrush(Color.FromArgb(a, 30, 40, 60)))
                        {
                            g.FillEllipse(eye, sp.X - s * 0.5f, sp.Y - s * 0.8f, s * 0.4f, s * 0.5f);
                            g.FillEllipse(eye, sp.X + s * 0.1f, sp.Y - s * 0.8f, s * 0.4f, s * 0.5f);
                        }
                        break;
                    case 8:
                        using (var br = new SolidBrush(Color.FromArgb(a, 255, 120, 170)))
                            g.FillEllipse(br, sp.X - s / 2, sp.Y - s / 3, s / 2, s / 2);
                        break;
                    case 9:
                        using (var br = new SolidBrush(Color.FromArgb(a * 60 / 100, 190, 140, 255)))
                            g.FillEllipse(br, sp.X - s / 2, sp.Y - s / 2, s, s);
                        break;
                    case 10: // ripple ring on the water
                        using (var rp = new Pen(Color.FromArgb(a, 200, 230, 255), Math.Max(1f, 0.8f * (float)zoom)))
                            g.DrawEllipse(rp, sp.X - s, sp.Y - s * 0.45f, s * 2, s * 0.9f);
                        break;
                }
            }
        }

        void TrackBirthsDeaths()
        {
            for (int i = 0; i < world.Humans.Count; i++)
            {
                var h = world.Humans[i];
                if (h.IsAlive)
                {
                    _aliveSeen.Add(h.Id);
                    if (h.Age < 1.5 && _newbornSeen.Add(h.Id))
                        for (int k = 0; k < 6; k++)
                            AddPuff((float)h.X, (float)h.Y, (float)(Random.Shared.NextDouble() - 0.5) * 1.2f, -0.6f, 8, 3.2f);
                }
                else if (_aliveSeen.Remove(h.Id) && h.State == HumanState.Dead)
                    AddPuff((float)h.X, (float)h.Y - 6f, 0, -0.3f, 7, 5f);   // soul leaves the body
            }
        }

        void UpdateTribeHistory()
        {
            if (world.Tick % 25 != 0) return;
            for (int i = 0; i < world.Tribes.Count; i++)
            {
                var t = world.Tribes[i];
                if (!_tribeHist.TryGetValue(t.Id, out var list))
                {
                    list = new List<double>();
                    _tribeHist[t.Id] = list;
                }
                list.Add(t.Population);
                if (list.Count > 180) list.RemoveAt(0);
            }
        }

        void DrawTop(Graphics g)
        {
            using (var grad = new LinearGradientBrush(new Rectangle(0, 0, ClientSize.Width, 44),
                Color.FromArgb(235, 26, 36, 58), Color.FromArgb(235, 14, 20, 34), 90f))
                g.FillRectangle(grad, 0, 0, ClientSize.Width, 42);
            using (var edge = new Pen(Color.FromArgb(110, 110, 150, 205), 1f))
                g.DrawLine(edge, 0, 42.5f, ClientSize.Width, 42.5f);
            if (st == null) return;

            // Title block (right of the menu tab)
            g.DrawString("NeuroCivilization", fT, brTx, 60, 2);
            g.DrawString(
                $"{world.Env.CurrentSeason}  •  {world.Env.TimeOfDay:F0}:00  •  {world.Env.CurrentWeather}  •  T: {world.Env.Temperature * 40 - 5:F0}°C",
                fM, brDm, 60, 22);

            string line2 = $"Year {st.Year} (Gen {world.Evolution.Generation})   Pop {st.Population}   Animals {st.AnimalCount} (H{st.HerbivoreCount}/P{st.PredatorCount})   Tribes {st.TribeCount}   Techs {st.TotalTechs}   Words {st.WordsInLanguage}   B/D {st.TotalBirths}/{st.TotalDeaths}";
            using (var l2 = new SolidBrush(Color.FromArgb(150, 168, 186)))
                g.DrawString(line2, fM, l2, 300, 22);

            // Right status pill (rounded)
            string r = isPaused ? "⏸ PAUSED" : $"▶ x{simSpeed}";
            var rs = g.MeasureString(r, fN);
            int pw = (int)rs.Width + 34, px = ClientSize.Width - pw - 10, py = 5, ph2 = 30;
            using (var pill = RoundedPath(px, py, pw, ph2, 15))
            {
                using (var fill = new SolidBrush(isPaused ? Color.FromArgb(205, 96, 84, 60) : Color.FromArgb(205, 34, 84, 74)))
                    g.FillPath(fill, pill);
                using (var rim = new Pen(isPaused ? Color.FromArgb(160, 220, 180, 120) : Color.FromArgb(160, 110, 215, 160), 1f))
                    g.DrawPath(rim, pill);
            }
            using (var dot = new SolidBrush(isPaused ? Color.FromArgb(240, 220, 160) : Color.FromArgb(140, 255, 185)))
                g.FillEllipse(dot, px + 11, py + 11, 8, 8);
            using (var tx = new SolidBrush(isPaused ? Color.FromArgb(255, 238, 214) : Color.FromArgb(220, 250, 232)))
                g.DrawString(r, fN, tx, px + 24, py + 8);
            using (var fpsBr = new SolidBrush(Color.FromArgb(140, 165, 190)))
                g.DrawString($"FPS {fps}", fM, fpsBr, ClientSize.Width - 70, py + 2);
        }

        void DrawLP(Graphics g)
        {
            if (st == null) return;
            int pw = 216, py = 48, ph = 228;
            Pnl(g, PanelX, py, pw, ph);
            // card header
            using (var hd = new LinearGradientBrush(new Rectangle(PanelX + 2, py + 4, pw - 16, 26),
                Color.FromArgb(70, 62, 108, 168), Color.FromArgb(10, 62, 108, 168), 90f))
                g.FillRectangle(hd, PanelX + 2, py + 4, pw - 16, 26);
            g.DrawString("Civilization Overview", fS, brAc, PanelX + 8, py + 10);
            int y = py + 34;
            int rx = PanelX + 8;
            Rw(g, rx, ref y, "Population", $"{st.Population} (Peak {st.PeakPopulation})");
            Rw(g, rx, ref y, "Births / Deaths", $"{st.TotalBirths} / {st.TotalDeaths}");
            Rw(g, rx, ref y, "Avg Fitness", $"{st.AverageFitness:F1}");
            Rw(g, rx, ref y, "Avg Age", $"{st.AverageAge:F1} y");
            Rw(g, rx, ref y, "Active Words", $"{st.WordsInLanguage}");
            Rw(g, rx, ref y, "Buildings", $"{world.Buildings.Count}");

            y += 4;
            g.DrawString("Major Tribes", fS, brAc, rx, y); y += 15;
            int shown = 0;
            for (int i = 0; i < world.Tribes.Count && shown < 5; i++)
            {
                var t = world.Tribes[i];
                if (t.MemberIds.Count == 0) continue;
                shown++;
                using (var cb = new SolidBrush(t.BannerColor))
                    FillRounded(g, cb, rx, y + 1, 9, 9, 3);
                using (var nm = new SolidBrush(Color.FromArgb(205, 216, 230)))
                    g.DrawString($"{t.Name} ({t.Population})  T{t.Technologies.Count}", fM, nm, rx + 15, y);
                y += 13;
            }
        }

        void DrawEventLog(Graphics g)
        {
            if (world.EventLog.Count == 0) return;
            int pw = 280, ph = 135, px = PanelX, py = showLeft ? 280 : 48;
            Pnl(g, px, py, pw, ph);
            int y = py + 5;
            g.DrawString("World Chronicle", fS, brAc, px + 6, y); y += 15;
            int count = Math.Min(9, world.EventLog.Count);
            for (int i = 0; i < count; i++)
            {
                string msg = world.EventLog[i];
                if (msg.Length > 40) msg = msg[..40] + "…";
                using var mb = new SolidBrush(i == 0 ? Color.FromArgb(225, 240, 248) : Color.FromArgb(150, 168, 182));
                g.DrawString(msg, fM, mb, px + 6, y);
                y += 12;
            }
            using var hint = new SolidBrush(Color.FromArgb(120, 150, 165, 180));
            g.DrawString("newest first", fM, hint, px + pw - 62, py + 5);
        }

        void Rw(Graphics g, int x, ref int y, string l, string v)
        {
            g.DrawString(l, fM, brDm, x, y);
            g.DrawString(v, fM, brTx, x + 82, y);
            y += 13;
        }

        void Bar(Graphics g, int x, ref int y, string n, double v, int w)
        {
            using (var nm = new SolidBrush(Color.FromArgb(168, 180, 196)))
                g.DrawString(n, fM, nm, x, y);
            int bx = x + 38, bw = w - 44, bh = 5;
            FillCapsule(g, new SolidBrush(Color.FromArgb(90, 34, 42, 58)), bx, y + 2, bw, bh);
            using var vb = new SolidBrush(v > 0.6 ? Color.LimeGreen : v > 0.3 ? Color.Goldenrod : Color.IndianRed);
            FillCapsule(g, vb, bx, y + 2, (int)(bw * Math.Clamp(v, 0, 1)), bh);
            y += 11;
        }

        void DrawMM(Graphics g)
        {
            int mw = 160, mh = 120, mx = ClientSize.Width - mw - 6, my = ClientSize.Height - mh - 6;
            if (minimapBg != null)
                g.DrawImage(minimapBg, mx, my, mw, mh);
            else
            {
                using var fb2 = new SolidBrush(CPB);
                g.FillRectangle(fb2, mx, my, mw, mh);
            }

            using (var path = RoundedPath(mx - 1, my - 1, mw + 2, mh + 2, 7))
            {
                using (var edge = new Pen(Color.FromArgb(120, 110, 150, 205), 1.2f))
                    g.DrawPath(edge, path);
            }
            double sx = mw / world.Width, sy = mh / world.Height;

            for (int i = 0; i < world.Animals.Count; i++)
            {
                var a = world.Animals[i];
                if (!a.IsAlive) continue;
                g.FillRectangle(a.IsPredator ? Brushes.Red : Brushes.OliveDrab,
                    mx + (int)(a.X * sx), my + (int)(a.Y * sy), 2, 2);
            }

            for (int i = 0; i < world.Humans.Count; i++)
            {
                var h = world.Humans[i];
                if (!h.IsAlive) continue;
                Color hc = Color.White;
                if (h.TribeId >= 0)
                {
                    var t = world.GetTribeById(h.TribeId);
                    if (t != null) hc = t.BannerColor;
                }
                using var hb = new SolidBrush(hc);
                g.FillRectangle(hb, mx + (int)(h.X * sx) - 1, my + (int)(h.Y * sy) - 1, 3, 3);
            }

            g.DrawRectangle(penViewRect,
                mx + (int)(camX * sx),
                my + (int)(camY * sy),
                Math.Max(2, (int)(ClientSize.Width / zoom * sx)),
                Math.Max(2, (int)(ClientSize.Height / zoom * sy)));
        }

        void DrawSel(Graphics g)
        {
            if (isPaused && selH is { IsAlive: true })
            {
                DrawMindPanel(g, selH);
            }

            // Tribe summary panel (click on a territory)
            if (selA == null && selH == null && selT != null)
            {
                DrawTribePanel(g);
            }

            if (selH == null && selA == null) return;
            int pw = 230, px = ClientSize.Width - pw - 6, py = 48;

            if (selH != null)
            {
                if (!selH.IsAlive) { selH = null; return; }
                var h = selH;
                int ph = 470;
                Pnl(g, px, py, pw, ph);
                int y = py + 5;

                string sex = h.Sex == Gender.Male ? "♂" : "♀";
                g.DrawString($"{h.Name} {sex} {h.Age:F1}y", fT, brAc, px + 6, y); y += 21;

                using var goalBr = new SolidBrush(
                    h.CurrentGoal == Goal.Flee ? Color.Red :
                    h.CurrentGoal == Goal.FindFood || h.CurrentGoal == Goal.EatFood || h.CurrentGoal == Goal.Cook ? Color.Orange :
                    h.CurrentGoal == Goal.HuntAnimal || h.CurrentGoal == Goal.DefendTerritory ? Color.IndianRed :
                    h.CurrentGoal == Goal.Socialize || h.CurrentGoal == Goal.Mate || h.CurrentGoal == Goal.Trade ? Color.LightSkyBlue :
                    h.CurrentGoal == Goal.Rest || h.CurrentGoal == Goal.Sleep || h.CurrentGoal == Goal.Heal ? Color.LightGreen :
                    Color.LightGray);
                g.DrawString($"→ {h.GoalDebug}", fN, goalBr, px + 6, y); y += 17;

                Rw(g, px + 6, ref y, "State", h.State.ToString());
                if (h.TribeId >= 0)
                {
                    var tr = world.GetTribeById(h.TribeId);
                    Rw(g, px + 6, ref y, "Tribe", tr != null ? $"{tr.Name}{(h.IsLeader ? " ★" : "")}" : "None");
                }

                y += 3;
                g.DrawString("Needs & Vitals", fS, brAc, px + 6, y); y += 13;
                NeedBar(g, px + 6, ref y, "Health", h.Health / h.MaxHealth, pw - 16);
                NeedBar(g, px + 6, ref y, "Satiety", 1 - h.Hunger, pw - 16);
                NeedBar(g, px + 6, ref y, "Energy", h.Energy / h.MaxEnergy, pw - 16);
                NeedBar(g, px + 6, ref y, "Stamina", 1 - h.Fatigue, pw - 16);
                NeedBar(g, px + 6, ref y, "Happy", h.Happiness, pw - 16);

                y += 3;
                g.DrawString("Genetics & Epigenetics", fS, brAc, px + 6, y); y += 13;
                Rw(g, px + 6, ref y, "Intel / Speed", $"{h.Genes.EffIntelligence:F2} / {h.Genes.EffMaxSpeed:F0}");
                Rw(g, px + 6, ref y, "Strength", $"{h.Genes.PhysicalStrength:F2}");
                Rw(g, px + 6, ref y, "Telomeres", $"{h.Genes.TeloLength:P0} (Stress {h.Genes.StressEpigenetic:P0})");

                y += 3;
                g.DrawString("Skills & Gear", fS, brAc, px + 6, y); y += 13;
                Bar(g, px + 6, ref y, "Hunt", h.SkillHunting, pw - 16);
                Bar(g, px + 6, ref y, "Build", h.SkillBuilding, pw - 16);
                Bar(g, px + 6, ref y, "Fight", h.SkillFighting, pw - 16);
                Bar(g, px + 6, ref y, "Craft", h.SkillCrafting, pw - 16);
                Bar(g, px + 6, ref y, "Speak", h.SkillSpeaking, pw - 16);

                Rw(g, px + 6, ref y, "Weapon", h.EquippedWeapon != null ? h.EquippedWeapon.Name : "Unarmed");
                Rw(g, px + 6, ref y, "Tool", h.EquippedTool != null ? h.EquippedTool.Name : "Hands");

                y += 3;
                g.DrawString("Achievements", fS, brAc, px + 6, y); y += 13;
                Rw(g, px + 6, ref y, "Meat / Food", $"{h.MeatStored:F0} / {h.FoodEaten}");
                Rw(g, px + 6, ref y, "Kills / Kids", $"{h.AnimalsKilled} / {h.ChildrenBorn}");
                Rw(g, px + 6, ref y, "Techs / Words", $"{h.KnownTechnologies.Count} / {h.VocabularySize}");
                Rw(g, px + 6, ref y, "Fitness", $"{h.CalculateFitness():F1}");
                if (h.IsPregnant) Rw(g, px + 6, ref y, "Pregnancy", $"{h.PregnancyProgress:P0}");

                y += 3;
                g.DrawString("Neurochemistry", fS, brAc, px + 6, y); y += 13;
                Bar(g, px + 6, ref y, "Dopa", h.Brain.Dopamine, pw - 16);
                Bar(g, px + 6, ref y, "Sero", h.Brain.Serotonin, pw - 16);
                Bar(g, px + 6, ref y, "Cort", h.Brain.Cortisol, pw - 16);
                Bar(g, px + 6, ref y, "Oxyt", h.Brain.Oxytocin, pw - 16);
            }
            else if (selA != null)
            {
                if (!selA.IsAlive) { selA = null; return; }
                var a = selA;
                Pnl(g, px, py, pw, 225);
                int y = py + 5;
                g.DrawString(a.SpeciesName, fT, brAc, px + 6, y); y += 22;

                g.DrawString($"→ {a.GoalDebug}", fN, brTx, px + 6, y); y += 17;

                Rw(g, px + 6, ref y, "Diet", a.IsPredator ? "Predator" : a.IsHerbivore ? "Herbivore" : "Omnivore");
                Rw(g, px + 6, ref y, "State", a.State.ToString());
                NeedBar(g, px + 6, ref y, "HP", a.Health / a.MaxHealth, pw - 16);
                NeedBar(g, px + 6, ref y, "Satiety", 1 - a.Hunger, pw - 16);
                Rw(g, px + 6, ref y, "Age", $"{a.Age:F1} y");
                Rw(g, px + 6, ref y, "Speed", $"{a.Genes.Speed:F0}");
                Rw(g, px + 6, ref y, "Eaten / Kids", $"{a.FoodEaten} / {a.OffspringCount}");
                Rw(g, px + 6, ref y, "Tameness", $"{a.Tameness:P0}");
            }
        }

        /// <summary>OBSERVE: when paused, show the recent decision stream of the selected human.</summary>
        void DrawMindPanel(Graphics g, Human h)
        {
            if (h.MindLog.Count == 0) return;
            int mw = 190, rows = Math.Min(6, h.MindLog.Count), mh = 16 + rows * 13 + 6;
            int mx = 6, my = 48;
            Pnl(g, mx, my, mw, mh);
            g.DrawString("OBSERVE — thoughts", fS, brAc, mx + 6, my + 3);
            int y2 = my + 20;
            for (int i = h.MindLog.Count - 1, k = 0; i >= 0 && k < rows; i--, k++)
            {
                using var mb = new SolidBrush(Color.FromArgb(200, 210, 225, 240));
                g.DrawString("• " + h.MindLog[i], fM, mb, mx + 6, y2);
                y2 += 13;
            }
        }

        /// <summary>Tribe info panel: leader, techs, diplomacy stages, storage, lineage.</summary>
        void DrawTribePanel(Graphics g)
        {
            var t = selT!;
            if (t.Population == 0) { selT = null; return; }

            int pw = 250, px = ClientSize.Width - pw - 6, py = 48;
            float scale = Math.Min(9, t.Technologies.Count * 2 + 16);   // grows with tech
            int ph = 150 + (int)scale;
            Pnl(g, px, py, pw, ph);
            int y = py + 5;

            using (var tb = new SolidBrush(t.BannerColor))
                g.FillRectangle(tb, px + 6, y, 14, 14);
            g.DrawString($"{t.Name}", fT, brAc, px + 24, y); y += 20;

            var leader = world.GetTribeById(t.Id) != null && world.HumanById.TryGetValue(t.LeaderId, out var lh) && lh.IsAlive ? lh : null;
            Rw(g, px + 6, ref y, "Leader", leader != null ? $"{leader.Name}{(t.HeirId > 0 ? " (heir named)" : "")}" : "electing…");
            Rw(g, px + 6, ref y, "Members", $"{t.Population} (fits {t.AverageFitness:F0})");
            Rw(g, px + 6, ref y, "Births/Deaths", $"{t.TotalBirths} / {t.TotalDeaths}");
            Rw(g, px + 6, ref y, "Storage", $"food {t.FoodStorage:F0} | mat {t.MaterialStorage:F0}");
            Rw(g, px + 6, ref y, "Culture", $"{t.CultureLevel:F2} | coop {t.CooperationNorm:F2}");

            y += 3;
            g.DrawString("Technologies", fS, brAc, px + 6, y); y += 13;
            if (t.Technologies.Count == 0) Rw(g, px + 6, ref y, "—", "none yet");
            else
            {
                int shown = 0;
                foreach (var tech in t.Technologies)
                {
                    if (shown++ >= 5) { Rw(g, px + 6, ref y, "…", $"+{t.Technologies.Count - shown + 1} more"); break; }
                    Rw(g, px + 6, ref y, "•", tech);
                }
            }

            y += 3;
            g.DrawString("Diplomacy", fS, brAc, px + 6, y); y += 13;
            bool any = false;
            for (int i = 0; i < world.Tribes.Count; i++)
            {
                var o = world.Tribes[i];
                if (o.Id == t.Id || o.Population == 0) continue;
                var d = t.GetDiplomacy(o.Id);
                using var sb = new SolidBrush(TribeDiplomacy.StageColor(d.Stage));
                g.FillRectangle(sb, px + 8, y + 2, 9, 9);
                g.DrawString($"{o.Name}: {d.Stage}", fM, brTx, px + 22, y);
                any = true;
                y += 13;
            }
            if (!any) { g.DrawString("no neighbors yet", fM, brDm, px + 22, y); y += 13; }
            y += 2;
            using var hintB = new SolidBrush(Color.FromArgb(130, 160, 175, 190));
            g.DrawString("ESC — close", fM, hintB, px + 6, y);
        }

        void NeedBar(Graphics g, int x, ref int y, string name, double val, int maxW)
        {
            using (var nm = new SolidBrush(Color.FromArgb(168, 180, 196)))
                g.DrawString(name, fM, nm, x, y);
            int bx = x + 46, bw = maxW - 54, bh = 7;
            FillCapsule(g, new SolidBrush(Color.FromArgb(90, 34, 42, 58)), bx, y + 1, bw, bh);

            int r = (int)(255 * Math.Clamp(1 - val, 0, 1) * 1.5);
            int gr = (int)(255 * Math.Clamp(val, 0, 1) * 1.2);
            Color c = Color.FromArgb(Math.Min(255, r), Math.Min(255, gr), 30);
            FillCapsule(g, new SolidBrush(c), bx, y + 1, (int)(bw * Math.Clamp(val, 0, 1)), bh);

            using (var vl = new SolidBrush(Color.FromArgb(180, 192, 208)))
                g.DrawString($"{val:P0}", fM, vl, bx + bw + 2, y);
            y += 12;
        }

        void DrawGr(Graphics g)
        {
            if (popH.Count < 3) return;
            int gw = 220, gh = 58, gx = PanelX, gy = ClientSize.Height - gh - 34;
            Pnl(g, gx, gy, gw, gh);
            g.DrawString("Pop (Blue) / Per-tribe / Fitness (Green)", fM, brDm, gx + 4, gy + 2);
            double mxPop = Math.Max(1, popH.Max());
            double mxFit = Math.Max(1, fitH.Max());
            float step = (float)(gw - 8) / (popH.Count - 1);
            for (int i = 1; i < popH.Count; i++)
            {
                float x1 = gx + 4 + (i - 1) * step;
                float x2 = gx + 4 + i * step;
                g.DrawLine(penPopGraph,
                    x1, gy + gh - 4 - (float)(popH[i - 1] / mxPop * (gh - 18)),
                    x2, gy + gh - 4 - (float)(popH[i] / mxPop * (gh - 18)));
                g.DrawLine(penFitGraph,
                    x1, gy + gh - 4 - (float)(fitH[i - 1] / mxFit * (gh - 18)),
                    x2, gy + gh - 4 - (float)(fitH[i] / mxFit * (gh - 18)));
            }

            // Per-tribe population lines in each tribe's banner color
            foreach (var (tribeId, hist) in _tribeHist)
            {
                var tr = world.GetTribeById(tribeId);
                if (tr == null || hist.Count < 2) continue;
                float tstep = (float)(gw - 8) / (hist.Count - 1);
                using var tp = new Pen(Color.FromArgb(200, tr.BannerColor), 1.1f);
                for (int i = 1; i < hist.Count; i++)
                    g.DrawLine(tp,
                        gx + 4 + (i - 1) * tstep, gy + gh - 4 - (float)(hist[i - 1] / Math.Max(1, hist.Max()) * (gh - 18)),
                        gx + 4 + i * tstep, gy + gh - 4 - (float)(hist[i] / Math.Max(1, hist.Max()) * (gh - 18)));
            }
        }

        void DrawHlp(Graphics g)
        {
            // ── Legend (localized, EN by default / RU via "T") ──
            int ly = ClientSize.Height - 78;
            int lx = PanelX, lw = 640;
            using (var pill = RoundedPath(lx, ly, lw, 40, 8))
            using (var lb = new SolidBrush(Color.FromArgb(170, 10, 14, 24)))
                g.FillPath(lb, pill);
            g.DrawString("• " + Lang.T("legend"), fM, brDm, lx + 6, ly + 4);
            g.DrawString("• " + Lang.T("legend2"), fM, brDm, lx + 6, ly + 15);
            g.DrawString("• " + Lang.T("legend3"), fM, brDm, lx + 6, ly + 26);
            g.DrawString("SPACE=Pause  +/-=Speed  WASD=Pan  Scroll=Zoom  Click=Select  TAB=Next  F=Follow  N=New World  T=RU/EN  ESC=Deselect",
                fM, brDm, 215, ClientSize.Height - 15);
            g.DrawString($"F1:Top{O(showTop)} F2:Stats{O(showLeft)} F3:Map{O(showMap)} F4:Info{O(showSel)} F5:Wth{O(showWeather)} F6:Res{O(showRes)} F7:Terr{O(showTerr)} F8:Bld{O(showBld)} F9:Lbl{O(showLbl)} F10:Graph{O(showGraph)} F11:Log{O(showLog)} F12:Dead{O(showDead)}",
                fM, brDm, 6, ClientSize.Height - 28);
        }

        string O(bool v) => v ? "+" : "-";
        /// <summary>Frosted-glass style rounded card: soft vertical gradient + subtle blue rim light.</summary>
        void Rounded(Graphics g, int x, int y, int w, int h, int r = 14, int a = 228)
        {
            using var path = RoundedPath(x, y, w, h, r);
            using (var fill = new LinearGradientBrush(new Rectangle(x, y, w, h),
                Color.FromArgb(a, 21, 27, 41), Color.FromArgb(a, 13, 17, 28), 90f))
                g.FillPath(fill, path);
            using (var rim = new Pen(Color.FromArgb(98, 98, 134, 186), 1f))
                g.DrawPath(rim, path);
        }

        /// <summary>Standard dashboard card.</summary>
        void Pnl(Graphics g, int x, int y, int w, int h) => Rounded(g, x, y, w, h, 14, 228);

        /// <summary>Capsule fill used by progress bars.</summary>
        void FillCapsule(Graphics g, Brush br, int x, int y, int w, int h)
        {
            if (w < 1) w = 1;
            using var p = RoundedPath(x, y, w, h, h / 2);
            g.FillPath(br, p);
        }

        static GraphicsPath RoundedPath(int x, int y, int w, int h, int r)
        {
            var p = new GraphicsPath();
            int d = r * 2;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void Form1_MouseDown(object sender, MouseEventArgs e)
        {
            // Settings drawer has priority over world interaction
            int hit = DrawerHit(e.Location);
            if (e.Button == MouseButtons.Left && hit != -1)
            {
                if (hit == -2) drawerOpen = !drawerOpen;
                else drawerRows[hit].Click();
                Invalidate();
                return;
            }

            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle)
            {
                isDrag = true;
                dragS = e.Location;
                dragCX = camX;
                dragCY = camY;
                Cursor = Cursors.SizeAll;
                follow = false;
            }
            else if (e.Button == MouseButtons.Left)
            {
                TrySel(e.Location);
            }
        }

        void Form1_MouseMove(object sender, MouseEventArgs e)
        {
            if (drawerOpen)
            {
                int hov = DrawerHit(e.Location);
                if (hov != drawerHover) { drawerHover = hov; Invalidate(); }
            }
            if (isDrag)
            {
                camX = dragCX - (e.X - dragS.X) / zoom;
                camY = dragCY - (e.Y - dragS.Y) / zoom;
            }
        }

        void Form1_MouseUp(object sender, MouseEventArgs e)
        {
            if (isDrag)
            {
                isDrag = false;
                Cursor = Cursors.Default;
            }
        }

        void Form1_MouseWheel(object sender, MouseEventArgs e)
        {
            var (wx, wy) = S2W(e.X, e.Y);
            zoom = e.Delta > 0 ? Math.Min(5.0, zoom * 1.15) : Math.Max(0.15, zoom / 1.15);
            camX = wx - e.X / zoom;
            camY = wy - e.Y / zoom;
        }

        void TrySel(Point sp)
        {
            // Клик по миникарте перемещает камеру
            int mw = 160, mh = 120, mx = ClientSize.Width - mw - 6, my = ClientSize.Height - mh - 6;
            if (showMap && sp.X >= mx && sp.X <= mx + mw && sp.Y >= my && sp.Y <= my + mh)
            {
                double wx = (sp.X - mx) / (double)mw * world.Width;
                double wy = (sp.Y - my) / (double)mh * world.Height;
                camX = wx - ClientSize.Width / 2.0 / zoom;
                camY = wy - ClientSize.Height / 2.0 / zoom;
                follow = false;
                return;
            }

            var (worldX, worldY) = S2W(sp.X, sp.Y);
            double best = 22 / zoom;
            selH = null;
            selA = null;
            selT = null;
            for (int i = 0; i < world.Humans.Count; i++)
            {
                var h = world.Humans[i];
                if (!h.IsAlive) continue;
                double d = Math.Sqrt(Sq(h.X - worldX) + Sq(h.Y - worldY));
                if (d < best) { best = d; selH = h; selA = null; }
            }
            if (selH == null)
            {
                for (int i = 0; i < world.Animals.Count; i++)
                {
                    var a = world.Animals[i];
                    if (!a.IsAlive) continue;
                    double d = Math.Sqrt(Sq(a.X - worldX) + Sq(a.Y - worldY));
                    if (d < best) { best = d; selA = a; }
                }
            }
            // Fallback: click inside a territory → tribe summary panel
            if (selH == null && selA == null)
            {
                for (int i = 0; i < world.Tribes.Count; i++)
                {
                    var t = world.Tribes[i];
                    if (t.Population == 0) continue;
                    double dx = t.CenterX - worldX, dy = t.CenterY - worldY;
                    if (dx * dx + dy * dy < t.TerritoryRadius * t.TerritoryRadius) { selT = t; break; }
                }
            }
            follow = selH != null || selA != null;
        }

        void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            double mv = 35 / zoom;
            switch (e.KeyCode)
            {
                case Keys.Space: isPaused = !isPaused; break;
                case Keys.Oemplus: case Keys.Add: simSpeed = Math.Min(30, simSpeed + 1); break;
                case Keys.OemMinus: case Keys.Subtract: simSpeed = Math.Max(1, simSpeed - 1); break;
                case Keys.W: case Keys.Up: camY -= mv; follow = false; break;
                case Keys.S: case Keys.Down: camY += mv; follow = false; break;
                case Keys.A: case Keys.Left: camX -= mv; follow = false; break;
                case Keys.D: case Keys.Right: camX += mv; follow = false; break;
                case Keys.Escape: selH = null; selA = null; selT = null; follow = false; break;
                case Keys.F: follow = !follow; break;
                case Keys.R: zoom = DebugZoom ?? 0.7; break;
                case Keys.N: PluginHost.UnloadAll(world); InitWorld(); PluginHost.LoadAll(world); break;
                case Keys.T: Lang.Toggle(); break;
                case Keys.Home:
                    var al = world.Humans.Where(h => h.IsAlive).ToList();
                    if (al.Count > 0)
                    {
                        camX = al.Average(h => h.X) - ClientSize.Width / 2.0 / zoom;
                        camY = al.Average(h => h.Y) - ClientSize.Height / 2.0 / zoom;
                    }
                    break;
                case Keys.Tab: Cyc(); e.SuppressKeyPress = true; break;
                case Keys.F1: showTop = !showTop; break;
                case Keys.F2: showLeft = !showLeft; break;
                case Keys.F3: showMap = !showMap; break;
                case Keys.F4: showSel = !showSel; break;
                case Keys.F5: showWeather = !showWeather; break;
                case Keys.F6: showRes = !showRes; break;
                case Keys.F7: showTerr = !showTerr; break;
                case Keys.F8: showBld = !showBld; break;
                case Keys.F9: showLbl = !showLbl; break;
                case Keys.F10: showGraph = !showGraph; break;
                case Keys.F11: showLog = !showLog; break;
                case Keys.F12: showDead = !showDead; break;
            }
        }

        void Cyc()
        {
            var al = world.Humans.Where(h => h.IsAlive).ToList();
            if (al.Count == 0) return;
            int i = selH != null ? al.IndexOf(selH) : -1;
            selH = al[(i + 1) % al.Count];
            selA = null;
            follow = true;
        }

        void Form1_Resize(object sender, EventArgs e) => Invalidate();
        static double Sq(double x) => x * x;
    }
}
