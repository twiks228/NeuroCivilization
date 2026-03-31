using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NeuroCivilization.Core;

namespace NeuroCivilization
{
    public partial class Form1 : Form
    {
        World world;
        bool isPaused;
        int simSpeed = 1;

        double camX, camY, zoom = 0.7;
        bool isDrag; Point dragS; double dragCX, dragCY;
        bool follow;

        Human selH; Animal selA;

        Dictionary<string, Bitmap> tex = new();
        const int TILE = 64;

        Font fT, fN, fS, fM;
        SolidBrush brP, brTx, brAc, brW, brD, brG, brDm;
        Pen penB;

        WorldStats st;
        int fps, fCnt; DateTime fpsT = DateTime.Now;
        List<double> popH = new(), fitH = new();

        // Toggles
        bool showTop = true, showLeft = true, showMap = true, showSel = true;
        bool showWeather = true, showRes = true, showTerr = true;
        bool showBld = true, showLbl = true, showGraph = true;
        bool showDead = true; // F12 — показ трупов

        static readonly Color CPB = Color.FromArgb(200, 15, 20, 32);
        static readonly Color CBR = Color.FromArgb(150, 50, 70, 110);

        public Form1() { InitializeComponent(); SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }

        void Form1_Load(object sender, EventArgs e)
        {
            fT = new Font("Segoe UI", 12, FontStyle.Bold);
            fN = new Font("Segoe UI", 9);
            fS = new Font("Segoe UI", 7.5f);
            fM = new Font("Consolas", 7);
            brP = new SolidBrush(CPB); brTx = new SolidBrush(Color.FromArgb(220, 230, 240));
            brAc = new SolidBrush(Color.FromArgb(90, 190, 255));
            brW = new SolidBrush(Color.FromArgb(255, 190, 70));
            brD = new SolidBrush(Color.FromArgb(255, 75, 75));
            brG = new SolidBrush(Color.FromArgb(70, 240, 110));
            brDm = new SolidBrush(Color.FromArgb(130, 160, 175));
            penB = new Pen(CBR);
            LoadTex();
            world = new World();
            world.SpawnHumans(30);
            world.SpawnAnimals(40, 12);
            camX = world.Width / 2 - ClientSize.Width / 2.0 / zoom;
            camY = world.Height / 2 - ClientSize.Height / 2.0 / zoom;
            simTimer.Start();
        }

        void LoadTex()
        {
            string[] ps = { Path.Combine(Application.StartupPath, "Assets"), @"C:\Users\Jake\source\repos\NeuroDots\NeuroDots\Assets" };
            string dir = null;
            foreach (var p in ps) if (Directory.Exists(p)) { dir = p; break; }
            foreach (var n in new[] { "grass", "forest", "sand", "snow", "water", "oasis" })
            {
                bool ok = false;
                if (dir != null) { string f = Path.Combine(dir, n + ".png"); if (File.Exists(f)) { tex[n] = new Bitmap(f); ok = true; } }
                if (!ok) tex[n] = MkTex(n);
            }
        }

        Bitmap MkTex(string n)
        {
            Color c = n switch { "grass" => Color.FromArgb(75, 135, 45), "forest" => Color.FromArgb(28, 90, 28), "sand" => Color.FromArgb(205, 185, 125), "snow" => Color.FromArgb(225, 235, 245), "water" => Color.FromArgb(35, 85, 175), _ => Color.FromArgb(55, 155, 95) };
            var b = new Bitmap(TILE, TILE); using var g = Graphics.FromImage(b); using var br = new SolidBrush(c); g.FillRectangle(br, 0, 0, TILE, TILE);
            var rn = new Random(c.GetHashCode());
            for (int i = 0; i < TILE * TILE / 5; i++) { int px = rn.Next(TILE), py = rn.Next(TILE); int d = rn.Next(-12, 12); b.SetPixel(px, py, Color.FromArgb(Math.Clamp(c.R + d, 0, 255), Math.Clamp(c.G + d, 0, 255), Math.Clamp(c.B + d, 0, 255))); }
            return b;
        }

        string B2T(Biome b) => b switch { Biome.Grassland => "grass", Biome.Forest or Biome.BorealForest or Biome.TropicalForest => "forest", Biome.Desert or Biome.Steppe => "sand", Biome.Tundra or Biome.Mountain => "snow", _ => "water" };

        void SimTimer_Tick(object sender, EventArgs e)
        {
            if (!isPaused && world != null)
            {
                for (int i = 0; i < simSpeed; i++) world.Update(1.0);
                st = world.GetStats();
                if (world.Tick % 30 == 0 && st != null) { popH.Add(st.Population); fitH.Add(st.AverageFitness); if (popH.Count > 200) { popH.RemoveAt(0); fitH.RemoveAt(0); } }
            }
            if (follow)
            {
                if (selH?.IsAlive == true) { camX += (selH.X - ClientSize.Width / 2.0 / zoom - camX) * 0.06; camY += (selH.Y - ClientSize.Height / 2.0 / zoom - camY) * 0.06; }
                else if (selA?.IsAlive == true) { camX += (selA.X - ClientSize.Width / 2.0 / zoom - camX) * 0.06; camY += (selA.Y - ClientSize.Height / 2.0 / zoom - camY) * 0.06; }
            }
            fCnt++; if ((DateTime.Now - fpsT).TotalSeconds >= 1) { fps = fCnt; fCnt = 0; fpsT = DateTime.Now; }
            Invalidate();
        }

        PointF W2S(double wx, double wy) => new((float)((wx - camX) * zoom), (float)((wy - camY) * zoom));
        (double x, double y) S2W(float sx, float sy) => (sx / zoom + camX, sy / zoom + camY);
        bool OnS(PointF p, float m = 40) => p.X > -m && p.X < ClientSize.Width + m && p.Y > -m && p.Y < ClientSize.Height + m;

        void Form1_Paint(object sender, PaintEventArgs e)
        {
            if (world == null) return;
            var g = e.Graphics;
            g.SmoothingMode = zoom > 0.4 ? SmoothingMode.AntiAlias : SmoothingMode.HighSpeed;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.Clear(Color.FromArgb(12, 16, 25));
            DrawTerrain(g); DrawDN(g);
            if (showTerr) DrawTerr(g);
            if (showRes) DrawRes(g);
            if (showBld) DrawBld(g);
            if (showDead) DrawDead(g);
            DrawAnimals(g);
            DrawHumans(g);
            if (showWeather) DrawWth(g);
            if (showTop) DrawTop(g);
            if (showLeft) DrawLP(g);
            if (showMap) DrawMM(g);
            if (showSel) DrawSel(g);
            if (showGraph) DrawGr(g);
            DrawHlp(g);
        }

        void DrawTerrain(Graphics g)
        {
            var (vx1, vy1) = S2W(0, 0); var (vx2, vy2) = S2W(ClientSize.Width, ClientSize.Height);
            int sx = Math.Max(0, (int)(vx1 / TILE) - 1), sy = Math.Max(0, (int)(vy1 / TILE) - 1);
            int ex = Math.Min((int)(world.Width / TILE) + 1, (int)(vx2 / TILE) + 2);
            int ey = Math.Min((int)(world.Height / TILE) + 1, (int)(vy2 / TILE) + 2);
            float ts = (float)(TILE * zoom);
            for (int tx = sx; tx < ex; tx++)
                for (int ty = sy; ty < ey; ty++)
                {
                    var bi = world.Env.GetBiome(tx * TILE, ty * TILE, world.Width, world.Height);
                    if (tex.TryGetValue(B2T(bi), out var t))
                    { var sp = W2S(tx * TILE, ty * TILE); g.DrawImage(t, sp.X, sp.Y, ts + 1, ts + 1); }
                }
        }

        void DrawDN(Graphics g)
        {
            float dl = (float)world.Env.DayLight;
            if (dl < 0.6) { using var br = new SolidBrush(Color.FromArgb((int)((1 - dl / 0.6) * 100), 3, 3, 25)); g.FillRectangle(br, ClientRectangle); }
        }

        void DrawRes(Graphics g)
        {
            foreach (var r in world.Resources)
            {
                if (r.Amount < 0.5) continue; var sp = W2S(r.X, r.Y); if (!OnS(sp)) continue;
                float sz = (float)(3 + r.Amount / r.MaxAmount * 5) * (float)zoom;
                Color c = r.Type switch { ResourceType.Berry => Color.FromArgb(200, 50, 140), ResourceType.Plant => Color.FromArgb(50, 170, 50), ResourceType.Stone => Color.FromArgb(150, 150, 140), _ => Color.FromArgb(139, 90, 43) };
                using var br = new SolidBrush(c); g.FillEllipse(br, sp.X - sz / 2, sp.Y - sz / 2, sz, sz);
            }
        }

        void DrawBld(Graphics g) { foreach (var b in world.Buildings) { var sp = W2S(b.X, b.Y); if (!OnS(sp)) continue; float sz = (float)(b.Size * zoom); using var br = new SolidBrush(Color.FromArgb(155, 115, 55)); g.FillRectangle(br, sp.X - sz / 2, sp.Y - sz / 2, sz, sz); } }
        void DrawTerr(Graphics g) { foreach (var t in world.Tribes) { if (t.MemberIds.Count == 0) continue; var sp = W2S(t.CenterX, t.CenterY); float r = (float)(t.TerritoryRadius * zoom); using var f = new SolidBrush(Color.FromArgb(10, t.BannerColor)); g.FillEllipse(f, sp.X - r, sp.Y - r, r * 2, r * 2); if (zoom < 1) { using var nb = new SolidBrush(Color.FromArgb(50, t.BannerColor)); var ns = g.MeasureString(t.Name, fS); g.DrawString(t.Name, fS, nb, sp.X - ns.Width / 2, sp.Y); } } }

        // ===== ТРУПЫ (показываются некоторое время) =====
        void DrawDead(Graphics g)
        {
            // Мёртвые люди
            foreach (var h in world.Humans)
            {
                if (h.IsAlive || h.State != HumanState.Dead) continue;
                var sp = W2S(h.X, h.Y);
                if (!OnS(sp)) continue;
                float sz = 5 * (float)zoom;
                using var br = new SolidBrush(Color.FromArgb(100, 80, 60, 50));
                g.FillEllipse(br, sp.X - sz, sp.Y - sz / 3, sz * 2, sz * 0.7f); // Лежащее тело
                if (zoom > 0.5)
                {
                    using var cross = new Pen(Color.FromArgb(120, 180, 50, 50), 1);
                    g.DrawLine(cross, sp.X - 3, sp.Y - 6 * (float)zoom, sp.X + 3, sp.Y - 6 * (float)zoom);
                    g.DrawLine(cross, sp.X, sp.Y - 9 * (float)zoom, sp.X, sp.Y - 3 * (float)zoom);
                }
            }
            // Мёртвые животные
            foreach (var a in world.Animals)
            {
                if (a.IsAlive) continue;
                var sp = W2S(a.X, a.Y);
                if (!OnS(sp)) continue;
                float sz = 4 * (float)zoom;
                using var br = new SolidBrush(Color.FromArgb(70, 60, 50, 40));
                g.FillEllipse(br, sp.X - sz, sp.Y - sz / 3, sz * 2, sz * 0.6f);
            }
        }

        // ===== ЖИВОТНЫЕ — 4 НОГИ + ТЕЛО =====
        void DrawAnimals(Graphics g)
        {
            foreach (var a in world.Animals)
            {
                if (!a.IsAlive) continue;
                var sp = W2S(a.X, a.Y);
                if (!OnS(sp)) continue;
                float sz = a.DrawSize * (float)zoom;
                if (sz < 2) { using var db = new SolidBrush(a.IsPredator ? Color.IndianRed : Color.OliveDrab); g.FillRectangle(db, sp.X, sp.Y, 2, 2); continue; }

                Color c = a.GetColor();
                float dx = (float)Math.Cos(a.Angle), dy = (float)Math.Sin(a.Angle);

                // Тело — горизонтальный овал (направленный)
                var state = g.Save();
                g.TranslateTransform(sp.X, sp.Y);
                g.RotateTransform((float)(a.Angle * 180 / Math.PI));

                // Тело
                using var bodyBr = new SolidBrush(c);
                g.FillEllipse(bodyBr, -sz * 0.6f, -sz * 0.35f, sz * 1.2f, sz * 0.7f);

                // Голова
                float headSz = sz * 0.35f;
                using var headBr = new SolidBrush(Color.FromArgb(
                    Math.Min(255, c.R + 20), Math.Min(255, c.G + 10), c.B));
                if (a.IsPredator)
                {
                    // Хищник — треугольная морда
                    var pts = new PointF[] {
                        new(sz * 0.6f + headSz * 0.8f, 0),
                        new(sz * 0.5f, -headSz * 0.4f),
                        new(sz * 0.5f, headSz * 0.4f)
                    };
                    g.FillPolygon(headBr, pts);
                    // Глаза красные
                    using var eyeBr = new SolidBrush(Color.FromArgb(220, 255, 60, 30));
                    float er = Math.Max(1, sz * 0.06f);
                    g.FillEllipse(eyeBr, sz * 0.45f, -headSz * 0.25f, er * 2, er * 2);
                    g.FillEllipse(eyeBr, sz * 0.45f, headSz * 0.1f, er * 2, er * 2);
                }
                else
                {
                    // Травоядное — круглая голова
                    g.FillEllipse(headBr, sz * 0.4f, -headSz / 2, headSz, headSz);
                    // Глаза
                    float er = Math.Max(1, sz * 0.05f);
                    g.FillEllipse(Brushes.Black, sz * 0.55f, -headSz * 0.2f, er * 2, er * 2);
                    g.FillEllipse(Brushes.Black, sz * 0.55f, headSz * 0.05f, er * 2, er * 2);
                }

                // 4 ноги
                float legLen = sz * 0.25f;
                using var legPen = new Pen(Color.FromArgb(180, Math.Max(0, c.R - 30), Math.Max(0, c.G - 30), Math.Max(0, c.B - 30)),
                    Math.Max(1, sz * 0.08f));
                float anim = (float)Math.Sin(world.Tick * 0.15) * legLen * 0.3f;
                // Передние
                g.DrawLine(legPen, sz * 0.2f, sz * 0.3f, sz * 0.2f + anim, sz * 0.3f + legLen);
                g.DrawLine(legPen, sz * 0.2f, -sz * 0.3f, sz * 0.2f - anim, -sz * 0.3f - legLen);
                // Задние
                g.DrawLine(legPen, -sz * 0.3f, sz * 0.3f, -sz * 0.3f - anim, sz * 0.3f + legLen);
                g.DrawLine(legPen, -sz * 0.3f, -sz * 0.3f, -sz * 0.3f + anim, -sz * 0.3f - legLen);

                g.Restore(state);

                // Обводка — тип
                Color oc = a.IsPredator ? Color.FromArgb(140, 200, 40, 40) :
                           a.IsDomesticated ? Color.FromArgb(140, 80, 130, 255) :
                           Color.FromArgb(60, 50, 100, 50);
                using var op = new Pen(oc, Math.Max(1, (float)(zoom * 0.8)));
                g.DrawEllipse(op, sp.X - sz * 0.6f, sp.Y - sz * 0.4f, sz * 1.2f, sz * 0.8f);

                if (showLbl && zoom > 0.5)
                {
                    // HP бар
                    float bw = sz * 1.3f, bh = Math.Max(2, 2 * (float)zoom);
                    float bx = sp.X - bw / 2, by = sp.Y - sz * 0.5f - bh - 2;
                    float hp = (float)Math.Clamp(a.Health / a.MaxHealth, 0, 1);
                    g.FillRectangle(Brushes.DarkRed, bx, by, bw, bh);
                    using var hpB = new SolidBrush(hp > 0.5 ? Color.LimeGreen : Color.Orange);
                    g.FillRectangle(hpB, bx, by, bw * hp, bh);
                    if (zoom > 0.7)
                    {
                        var ns = g.MeasureString(a.SpeciesName, fM);
                        g.DrawString(a.SpeciesName, fM, Brushes.LightGray, sp.X - ns.Width / 2, sp.Y + sz * 0.45f);
                    }
                }

                if (a == selA) { using var sel = new Pen(Color.Yellow, 2) { DashStyle = DashStyle.Dash }; g.DrawEllipse(sel, sp.X - sz, sp.Y - sz, sz * 2, sz * 2); }
            }
        }

        // ===== ЛЮДИ — ФИГУРКА (голова + тело + ноги) =====
        void DrawHumans(Graphics g)
        {
            foreach (var h in world.Humans)
            {
                if (!h.IsAlive) continue;
                var sp = W2S(h.X, h.Y);
                if (!OnS(sp)) continue;
                float sz = h.DrawSize * (float)zoom;
                if (sz < 3) { using var db = new SolidBrush(h.GetColor()); g.FillRectangle(db, sp.X - 1, sp.Y - 1, 3, 3); continue; }

                Color skinC = h.GetColor();
                Color outC = Color.FromArgb(150, 80, 80, 80);
                if (h.TribeId >= 0) { var tribe = world.Tribes.FirstOrDefault(t => t.Id == h.TribeId); if (tribe != null) outC = tribe.BannerColor; }

                float headR = sz * 0.32f;
                float bodyH = sz * 0.5f;
                float legLen = sz * 0.35f;
                float bodyW = sz * 0.4f;

                // Направление движения для анимации ног
                float anim = (float)Math.Sin(world.Tick * 0.12 + h.Id) * legLen * 0.4f;
                bool moving = h.State == HumanState.Walking || h.State == HumanState.Running;

                // НОГИ
                using var legPen = new Pen(Color.FromArgb(
                    Math.Max(0, skinC.R - 40), Math.Max(0, skinC.G - 30), Math.Max(0, skinC.B - 20)),
                    Math.Max(1.5f, sz * 0.1f));
                float legY = sp.Y + bodyH * 0.3f;
                float legSpread = bodyW * 0.25f;
                if (moving)
                {
                    g.DrawLine(legPen, sp.X - legSpread, legY, sp.X - legSpread + anim, legY + legLen);
                    g.DrawLine(legPen, sp.X + legSpread, legY, sp.X + legSpread - anim, legY + legLen);
                }
                else
                {
                    g.DrawLine(legPen, sp.X - legSpread, legY, sp.X - legSpread, legY + legLen);
                    g.DrawLine(legPen, sp.X + legSpread, legY, sp.X + legSpread, legY + legLen);
                }

                // ТЕЛО (вертикальный овал)
                using var bodyBr = new SolidBrush(Color.FromArgb(
                    outC.R, outC.G, outC.B)); // Одежда = цвет племени
                g.FillEllipse(bodyBr, sp.X - bodyW / 2, sp.Y - bodyH * 0.15f, bodyW, bodyH * 0.6f);

                // РУКИ
                using var armPen = new Pen(skinC, Math.Max(1, sz * 0.08f));
                float armY = sp.Y + bodyH * 0.05f;
                float armLen = sz * 0.3f;
                if (moving)
                {
                    g.DrawLine(armPen, sp.X - bodyW / 2, armY, sp.X - bodyW / 2 - anim * 0.5f, armY + armLen);
                    g.DrawLine(armPen, sp.X + bodyW / 2, armY, sp.X + bodyW / 2 + anim * 0.5f, armY + armLen);
                }
                else
                {
                    g.DrawLine(armPen, sp.X - bodyW / 2, armY, sp.X - bodyW * 0.6f, armY + armLen);
                    g.DrawLine(armPen, sp.X + bodyW / 2, armY, sp.X + bodyW * 0.6f, armY + armLen);
                }

                // ГОЛОВА
                float headY = sp.Y - bodyH * 0.15f - headR;
                using var headBr = new SolidBrush(skinC);
                g.FillEllipse(headBr, sp.X - headR, headY, headR * 2, headR * 2);
                using var headPen = new Pen(Color.FromArgb(100, 50, 40, 30), Math.Max(0.5f, sz * 0.04f));
                g.DrawEllipse(headPen, sp.X - headR, headY, headR * 2, headR * 2);

                // Глаза (смотрят в направлении)
                if (sz > 6)
                {
                    float eyeR = Math.Max(1, headR * 0.2f);
                    float eyeCX = sp.X + (float)Math.Cos(h.Angle) * headR * 0.3f;
                    float eyeCY = headY + headR + (float)Math.Sin(h.Angle) * headR * 0.2f;
                    float eyeOff = headR * 0.25f;
                    float perpX = -(float)Math.Sin(h.Angle) * eyeOff;
                    float perpY = (float)Math.Cos(h.Angle) * eyeOff;
                    g.FillEllipse(Brushes.White, eyeCX + perpX - eyeR, eyeCY + perpY - eyeR, eyeR * 2, eyeR * 2);
                    g.FillEllipse(Brushes.White, eyeCX - perpX - eyeR, eyeCY - perpY - eyeR, eyeR * 2, eyeR * 2);
                    float pupR = eyeR * 0.5f;
                    g.FillEllipse(Brushes.Black, eyeCX + perpX - pupR, eyeCY + perpY - pupR, pupR * 2, pupR * 2);
                    g.FillEllipse(Brushes.Black, eyeCX - perpX - pupR, eyeCY - perpY - pupR, pupR * 2, pupR * 2);
                }

                // Корона лидера
                if (h.IsLeader && zoom > 0.4)
                {
                    float crY = headY - 3 * (float)zoom;
                    using var crBr = new SolidBrush(Color.Gold);
                    var pts = new PointF[] {
                        new(sp.X - headR * 0.8f, crY + 3*(float)zoom),
                        new(sp.X - headR * 0.6f, crY),
                        new(sp.X, crY - 1.5f*(float)zoom),
                        new(sp.X + headR * 0.6f, crY),
                        new(sp.X + headR * 0.8f, crY + 3*(float)zoom)
                    };
                    g.FillPolygon(crBr, pts);
                }

                // Беременная
                if (h.IsPregnant && zoom > 0.5)
                {
                    using var prBr = new SolidBrush(Color.FromArgb(80, 255, 200, 220));
                    g.FillEllipse(prBr, sp.X - bodyW * 0.3f, sp.Y + bodyH * 0.1f, bodyW * 0.6f, bodyH * 0.3f);
                }

                if (showLbl && zoom > 0.5)
                {
                    // HP бар
                    float bw = sz * 1.5f, bh = Math.Max(2, 2.5f * (float)zoom);
                    float bx = sp.X - bw / 2, by = headY - bh - 3;
                    float hp = (float)Math.Clamp(h.Health / h.MaxHealth, 0, 1);
                    g.FillRectangle(Brushes.DarkRed, bx, by, bw, bh);
                    using var hpB = new SolidBrush(hp > 0.6 ? Color.LimeGreen : hp > 0.3 ? Color.Orange : Color.Red);
                    g.FillRectangle(hpB, bx, by, bw * hp, bh);

                    if (zoom > 0.65)
                    {
                        // Имя
                        var ns = g.MeasureString(h.Name, fM);
                        g.DrawString(h.Name, fM, Brushes.WhiteSmoke, sp.X - ns.Width / 2, sp.Y + bodyH * 0.3f + legLen + 1);

                        // Иконка состояния
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
                            _ => ""
                        };
                        if (icon != "") g.DrawString(icon, fM, Brushes.Yellow, sp.X + bodyW, headY);
                    }
                }

                if (h == selH)
                {
                    float selR = sz * 1.1f;
                    using var sel = new Pen(Color.FromArgb(200, 255, 255, 50), 2);
                    g.DrawEllipse(sel, sp.X - selR, headY - 3, selR * 2, sz * 1.5f + legLen);
                }
                if (h == selH)
                {
                    float selR = sz * 1.1f;
                    using var sel = new Pen(Color.FromArgb(200, 255, 255, 50), 2);
                    g.DrawEllipse(sel, sp.X - selR, headY - 3, selR * 2, sz * 1.5f + legLen);

                    // Линия к цели
                    if (h.HasTarget)
                    {
                        var tp = W2S(h.TargetX, h.TargetY);
                        using var targetPen = new Pen(Color.FromArgb(80, 255, 200, 50), 1)
                        { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
                        g.DrawLine(targetPen, sp.X, sp.Y, tp.X, tp.Y);

                        // Крестик на цели
                        using var crossPen = new Pen(Color.FromArgb(120, 255, 200, 50), 1.5f);
                        g.DrawLine(crossPen, tp.X - 4, tp.Y - 4, tp.X + 4, tp.Y + 4);
                        g.DrawLine(crossPen, tp.X - 4, tp.Y + 4, tp.X + 4, tp.Y - 4);
                    }
                }
            }
        }

        void DrawWth(Graphics g)
        {
            var rn = new Random(world.Tick / 5);
            switch (world.Env.CurrentWeather)
            {
                case Weather.Rain:
                    using (var p = new Pen(Color.FromArgb(40, 100, 150, 255), 1))
                        for (int i = 0; i < 60; i++) g.DrawLine(p, rn.Next(ClientSize.Width), rn.Next(ClientSize.Height), rn.Next(ClientSize.Width) - 2, rn.Next(ClientSize.Height) + 6);
                    break;
                case Weather.Storm:
                    using (var p = new Pen(Color.FromArgb(60, 70, 120, 255), 1.5f))
                        for (int i = 0; i < 100; i++) { int x = rn.Next(ClientSize.Width); g.DrawLine(p, x, rn.Next(ClientSize.Height), x - 3, rn.Next(ClientSize.Height) + 12); }
                    if (world.Env.IsThundering) using (var f = new SolidBrush(Color.FromArgb(20, 255, 255, 200))) g.FillRectangle(f, ClientRectangle);
                    break;
                case Weather.Snow:
                    using (var b = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                        for (int i = 0; i < 40; i++) g.FillEllipse(b, rn.Next(ClientSize.Width), rn.Next(ClientSize.Height), 2, 2);
                    break;
                case Weather.Fog:
                    using (var b = new SolidBrush(Color.FromArgb(30, 190, 195, 200))) g.FillRectangle(b, ClientRectangle);
                    break;
                case Weather.HeatWave:
                    using (var b = new SolidBrush(Color.FromArgb(15, 255, 140, 0))) g.FillRectangle(b, ClientRectangle);
                    break;
            }
        }

        void DrawTop(Graphics g)
        {
            g.FillRectangle(brP, 0, 0, ClientSize.Width, 42); g.DrawLine(penB, 0, 42, ClientSize.Width, 42);
            if (st == null) return;
            g.DrawString($"Year {st.Year} | {world.Env.CurrentSeason} | {world.Env.TimeOfDay:F0}h | {world.Env.CurrentWeather} | T:{world.Env.Temperature * 40 - 5:F0}C", fN, brTx, 10, 2);
            g.DrawString($"Pop:{st.Population} Anim:{st.AnimalCount}(H{st.HerbivoreCount}/P{st.PredatorCount}) Tribes:{st.TribeCount} Tech:{st.TotalTechs} B:{st.TotalBirths} D:{st.TotalDeaths} Res:{st.ResourceCount}", fS, brAc, 10, 22);
            string r = isPaused ? "PAUSED" : $"x{simSpeed} FPS:{fps} T:{world.Tick}";
            var rs = g.MeasureString(r, fN);
            g.DrawString(r, fN, isPaused ? brW : brDm, ClientSize.Width - rs.Width - 10, 12);
            string line2 = $"Pop:{st.Population} Preg:{st.PregnantCount} " +
    $"Anim:{st.AnimalCount}(H{st.HerbivoreCount}/P{st.PredatorCount}) " +
    $"Tribes:{st.TribeCount} Tech:{st.TotalTechs} " +
    $"B:{st.TotalBirths} D:{st.TotalDeaths} Res:{st.ResourceCount}";
        }

        void DrawLP(Graphics g)
        {
            if (st == null) return;
            int pw = 185, py = 48, ph = 200;
            Pnl(g, 5, py, pw, ph); int y = py + 5;
            g.DrawString("Stats", fN, brAc, 12, y); y += 18;
            Rw(g, 12, ref y, "Pop", $"{st.Population}"); Rw(g, 12, ref y, "Peak", $"{st.PeakPopulation}");
            Rw(g, 12, ref y, "B/D", $"{st.TotalBirths}/{st.TotalDeaths}");
            Rw(g, 12, ref y, "Fitness", $"{st.AverageFitness:F1}"); Rw(g, 12, ref y, "Age", $"{st.AverageAge:F1}y");
            y += 4; g.DrawString("Tribes", fS, brAc, 12, y); y += 13;
            foreach (var t in world.Tribes.Where(t => t.MemberIds.Count > 0).Take(4))
            {
                using var cb = new SolidBrush(t.BannerColor); g.FillRectangle(cb, 12, y + 1, 6, 6);
                g.DrawString($"{t.Name}({t.Population})T{t.Technologies.Count}", fM, brTx, 22, y); y += 12;
            }
        }

        void Rw(Graphics g, int x, ref int y, string l, string v)
        { g.DrawString(l, fM, brDm, x, y); g.DrawString(v, fM, brTx, x + 80, y); y += 13; }
        void Bar(Graphics g, int x, ref int y, string n, double v, int w)
        {
            g.DrawString(n, fM, brDm, x, y); int bx = x + 36, bw = w - 42, bh = 5;
            g.FillRectangle(Brushes.DarkSlateGray, bx, y + 2, bw, bh);
            using var vb = new SolidBrush(v > 0.6 ? Color.LimeGreen : v > 0.3 ? Color.Goldenrod : Color.IndianRed);
            g.FillRectangle(vb, bx, y + 2, (int)(bw * Math.Clamp(v, 0, 1)), bh); y += 11;
        }

        void DrawMM(Graphics g)
        {
            int mw = 160, mh = 120, mx = ClientSize.Width - mw - 6, my = ClientSize.Height - mh - 6;
            Pnl(g, mx, my, mw, mh); double sx = mw / world.Width, sy = mh / world.Height;
            foreach (var a in world.Animals.Where(a => a.IsAlive))
            { using var ab = new SolidBrush(a.IsPredator ? Color.Red : Color.OliveDrab); g.FillRectangle(ab, mx + (int)(a.X * sx), my + (int)(a.Y * sy), 2, 2); }
            foreach (var h in world.Humans.Where(h => h.IsAlive))
            { Color hc = Color.White; if (h.TribeId >= 0) { var t = world.Tribes.FirstOrDefault(t => t.Id == h.TribeId); if (t != null) hc = t.BannerColor; } using var hb = new SolidBrush(hc); g.FillRectangle(hb, mx + (int)(h.X * sx) - 1, my + (int)(h.Y * sy) - 1, 3, 3); }
            using var vp = new Pen(Color.Yellow, 1); g.DrawRectangle(vp, mx + (int)(camX * sx), my + (int)(camY * sy), Math.Max(1, (int)(ClientSize.Width / zoom * sx)), Math.Max(1, (int)(ClientSize.Height / zoom * sy)));
        }

        void DrawSel(Graphics g)
        {
            if (selH == null && selA == null) return;
            int pw = 220, px = ClientSize.Width - pw - 6, py = 48;

            if (selH != null)
            {
                if (!selH.IsAlive) { selH = null; return; }
                var h = selH;
                int ph = 430;
                Pnl(g, px, py, pw, ph);
                int y = py + 5;

                string sex = h.Sex == Gender.Male ? "♂" : "♀";
                g.DrawString($"{h.Name} {sex} {h.Age:F1}y", fT, brAc, px + 6, y); y += 22;

                // Текущая цель — КРУПНО
                using var goalBr = new SolidBrush(
                    h.CurrentGoal == Goal.Flee ? Color.Red :
                    h.CurrentGoal == Goal.FindFood || h.CurrentGoal == Goal.EatFood ? Color.Orange :
                    h.CurrentGoal == Goal.HuntAnimal ? Color.IndianRed :
                    h.CurrentGoal == Goal.Socialize || h.CurrentGoal == Goal.Mate ? Color.LightBlue :
                    h.CurrentGoal == Goal.Rest || h.CurrentGoal == Goal.Sleep ? Color.LightGreen :
                    Color.LightGray);
                g.DrawString($"→ {h.GoalDebug}", fN, goalBr, px + 6, y); y += 18;

                Rw(g, px + 6, ref y, "State", h.State.ToString());

                // Потребности с цветовой индикацией
                y += 4;
                g.DrawString("Needs", fS, brAc, px + 6, y); y += 12;
                NeedBar(g, px + 6, ref y, "Health", h.Health / h.MaxHealth, pw - 16);
                NeedBar(g, px + 6, ref y, "Hunger", 1 - h.Hunger, pw - 16); // Инвертируем
                NeedBar(g, px + 6, ref y, "Energy", h.Energy / h.MaxEnergy, pw - 16);
                NeedBar(g, px + 6, ref y, "Fatigue", 1 - h.Fatigue, pw - 16);
                NeedBar(g, px + 6, ref y, "Happy", h.Happiness, pw - 16);

                // Генетика
                y += 4;
                g.DrawString("Genes", fS, brAc, px + 6, y); y += 12;
                Rw(g, px + 6, ref y, "Intel", $"{h.Genes.EffIntelligence:F2}");
                Rw(g, px + 6, ref y, "Speed", $"{h.Genes.EffMaxSpeed:F0}");
                Rw(g, px + 6, ref y, "Strength", $"{h.Genes.PhysicalStrength:F2}");
                Rw(g, px + 6, ref y, "Telomeres", $"{h.Genes.TeloLength:P0}");

                // Навыки
                y += 4;
                g.DrawString("Skills", fS, brAc, px + 6, y); y += 12;
                Bar(g, px + 6, ref y, "Hunt", h.SkillHunting, pw - 16);
                Bar(g, px + 6, ref y, "Build", h.SkillBuilding, pw - 16);
                Bar(g, px + 6, ref y, "Fight", h.SkillFighting, pw - 16);
                Bar(g, px + 6, ref y, "Speak", h.SkillSpeaking, pw - 16);
                Bar(g, px + 6, ref y, "Gather", h.SkillGathering, pw - 16);

                // Статистика
                y += 4;
                g.DrawString("Stats", fS, brAc, px + 6, y); y += 12;
                Rw(g, px + 6, ref y, "Food", $"{h.FoodEaten}");
                Rw(g, px + 6, ref y, "Kills", $"{h.AnimalsKilled}");
                Rw(g, px + 6, ref y, "Children", $"{h.ChildrenBorn}");
                Rw(g, px + 6, ref y, "Techs", $"{h.KnownTechnologies.Count}");
                Rw(g, px + 6, ref y, "Friends", $"{h.Relationships.Count}");
                Rw(g, px + 6, ref y, "Fitness", $"{h.CalculateFitness():F1}");
                if (h.IsPregnant) Rw(g, px + 6, ref y, "Pregnant", $"{h.PregnancyProgress:P0}");

                // Мозг
                y += 4;
                g.DrawString("Brain", fS, brAc, px + 6, y); y += 12;
                Bar(g, px + 6, ref y, "Dopa", h.Brain.Dopamine, pw - 16);
                Bar(g, px + 6, ref y, "Sero", h.Brain.Serotonin, pw - 16);
                Bar(g, px + 6, ref y, "Cort", h.Brain.Cortisol, pw - 16);
                Bar(g, px + 6, ref y, "Oxyt", h.Brain.Oxytocin, pw - 16);
            }
            else if (selA != null)
            {
                if (!selA.IsAlive) { selA = null; return; }
                var a = selA;
                Pnl(g, px, py, pw, 220);
                int y = py + 5;
                g.DrawString(a.SpeciesName, fT, brAc, px + 6, y); y += 22;

                using var goalBr = new SolidBrush(Color.LightGray);
                g.DrawString($"→ {a.GoalDebug}", fN, goalBr, px + 6, y); y += 16;

                Rw(g, px + 6, ref y, "Type", a.IsPredator ? "Predator" : "Herbivore");
                Rw(g, px + 6, ref y, "State", a.State.ToString());
                NeedBar(g, px + 6, ref y, "HP", a.Health / a.MaxHealth, pw - 16);
                NeedBar(g, px + 6, ref y, "Hunger", 1 - a.Hunger, pw - 16);
                Rw(g, px + 6, ref y, "Age", $"{a.Age:F1}");
                Rw(g, px + 6, ref y, "Speed", $"{a.Genes.Speed:F0}");
                Rw(g, px + 6, ref y, "Eaten", $"{a.FoodEaten}");
                Rw(g, px + 6, ref y, "Tame", $"{a.Tameness:P0}");
            }
        }

        void NeedBar(Graphics g, int x, ref int y, string name, double val, int maxW)
        {
            g.DrawString(name, fM, brDm, x, y);
            int bx = x + 45, bw = maxW - 52, bh = 7;
            g.FillRectangle(Brushes.DarkSlateGray, bx, y + 1, bw, bh);

            // Цвет: зелёный → жёлтый → красный
            int r = (int)(255 * Math.Clamp(1 - val, 0, 1) * 1.5);
            int gr = (int)(255 * Math.Clamp(val, 0, 1) * 1.2);
            Color c = Color.FromArgb(Math.Min(255, r), Math.Min(255, gr), 30);
            using var vb = new SolidBrush(c);
            g.FillRectangle(vb, bx, y + 1, (int)(bw * Math.Clamp(val, 0, 1)), bh);

            // Значение
            using var vtx = new SolidBrush(Color.FromArgb(180, 255, 255, 255));
            g.DrawString($"{val:P0}", fM, vtx, bx + bw + 2, y);
            y += 12;
        }

        void DrawGr(Graphics g)
        {
            if (popH.Count < 3) return;
            int gw = 160, gh = 45, gx = 5, gy = ClientSize.Height - gh - 6;
            Pnl(g, gx, gy, gw, gh); g.DrawString("Pop", fM, brDm, gx + 2, gy + 1);
            double mx = Math.Max(1, popH.Max()); float step = (float)gw / popH.Count;
            using var pen = new Pen(Color.FromArgb(90, 190, 255), 1.5f);
            for (int i = 1; i < popH.Count; i++)
                g.DrawLine(pen, gx + (i - 1) * step, gy + gh - (float)(popH[i - 1] / mx * (gh - 10)),
                    gx + i * step, gy + gh - (float)(popH[i] / mx * (gh - 10)));
        }

        void DrawHlp(Graphics g)
        {
            g.DrawString("SPACE=Pause +/-=Speed WASD=Move Scroll=Zoom Click=Select TAB=Next F=Follow ESC=Deselect", fM, brDm, 170, ClientSize.Height - 14);
            g.DrawString($"F1:Top{O(showTop)} F2:Panel{O(showLeft)} F3:Map{O(showMap)} F4:Info{O(showSel)} F5:Wth{O(showWeather)} F6:Res{O(showRes)} F7:Terr{O(showTerr)} F8:Bld{O(showBld)} F9:Lbl{O(showLbl)} F12:Dead{O(showDead)}", fM, brDm, 5, ClientSize.Height - 28);
        }

        string O(bool v) => v ? "+" : "-";
        void Pnl(Graphics g, int x, int y, int w, int h) { g.FillRectangle(brP, x, y, w, h); g.DrawRectangle(penB, x, y, w, h); }

        // === ВВОД ===
        void Form1_MouseDown(object sender, MouseEventArgs e)
        { if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle) { isDrag = true; dragS = e.Location; dragCX = camX; dragCY = camY; Cursor = Cursors.SizeAll; follow = false; } else if (e.Button == MouseButtons.Left) TrySel(e.Location); }
        void Form1_MouseMove(object sender, MouseEventArgs e) { if (isDrag) { camX = dragCX - (e.X - dragS.X) / zoom; camY = dragCY - (e.Y - dragS.Y) / zoom; } }
        void Form1_MouseUp(object sender, MouseEventArgs e) { if (isDrag) { isDrag = false; Cursor = Cursors.Default; } }
        void Form1_MouseWheel(object sender, MouseEventArgs e) { var (wx, wy) = S2W(e.X, e.Y); zoom = e.Delta > 0 ? Math.Min(5, zoom * 1.15) : Math.Max(0.1, zoom / 1.15); camX = wx - e.X / zoom; camY = wy - e.Y / zoom; }

        void TrySel(Point sp)
        {
            var (wx, wy) = S2W(sp.X, sp.Y); double best = 20 / zoom; selH = null; selA = null;
            foreach (var h in world.Humans) { if (!h.IsAlive) continue; double d = Math.Sqrt(Sq(h.X - wx) + Sq(h.Y - wy)); if (d < best) { best = d; selH = h; selA = null; } }
            if (selH == null) foreach (var a in world.Animals) { if (!a.IsAlive) continue; double d = Math.Sqrt(Sq(a.X - wx) + Sq(a.Y - wy)); if (d < best) { best = d; selA = a; } }
            follow = true;
        }

        void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            double mv = 30 / zoom;
            switch (e.KeyCode)
            {
                case Keys.Space: isPaused = !isPaused; break;
                case Keys.Oemplus: case Keys.Add: simSpeed = Math.Min(20, simSpeed + 1); break;
                case Keys.OemMinus: case Keys.Subtract: simSpeed = Math.Max(1, simSpeed - 1); break;
                case Keys.W: case Keys.Up: camY -= mv; follow = false; break;
                case Keys.S: case Keys.Down: camY += mv; follow = false; break;
                case Keys.A: case Keys.Left: camX -= mv; follow = false; break;
                case Keys.D: case Keys.Right: camX += mv; follow = false; break;
                case Keys.Escape: selH = null; selA = null; follow = false; break;
                case Keys.F: follow = !follow; break;
                case Keys.R: zoom = 0.7; break;
                case Keys.Home: var al = world.Humans.Where(h => h.IsAlive).ToList(); if (al.Count > 0) { camX = al.Average(h => h.X) - ClientSize.Width / 2 / zoom; camY = al.Average(h => h.Y) - ClientSize.Height / 2 / zoom; } break;
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
                case Keys.F12: showDead = !showDead; break;
            }
        }

        void Cyc() { var al = world.Humans.Where(h => h.IsAlive).ToList(); if (al.Count == 0) return; int i = selH != null ? al.IndexOf(selH) : -1; selH = al[(i + 1) % al.Count]; selA = null; follow = true; }
        void Form1_Resize(object sender, EventArgs e) => Invalidate();
        static double Sq(double x) => x * x;
    }
}