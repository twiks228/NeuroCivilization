// Generates seamless tileable terrain textures for NeuroCivilization.
// Deterministic (fixed seeds) -> stable across regenerations.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

string outDir = @"D:\PROJECT\NeuroCivilization-main\NeuroDots\Assets";
int N = 128;

static double[,] Noise(int size, int freq, int seed)
{
    var rnd = new Random(seed);
    int gw = freq + 1;
    var grid = new double[gw, gw];
    for (int i = 0; i < freq; i++)
        for (int j = 0; j < freq; j++) grid[i, j] = rnd.NextDouble();
    for (int i = 0; i < freq; i++) grid[i, freq] = grid[i, 0];
    for (int j = 0; j < freq; j++) grid[freq, j] = grid[0, j];
    grid[freq, freq] = grid[0, 0];

    var res = new double[size, size];
    for (int y = 0; y < size; y++)
    {
        double fy = y / (double)size * freq;
        int y0 = (int)fy;
        double ty = fy - y0, sy = ty * ty * (3 - 2 * ty);
        for (int x = 0; x < size; x++)
        {
            double fx = x / (double)size * freq;
            int x0 = (int)fx;
            double tx = fx - x0, sx = tx * tx * (3 - 2 * tx);
            double a = grid[x0, y0], b = grid[x0 + 1, y0], c = grid[x0, y0 + 1], d = grid[x0 + 1, y0 + 1];
            res[x, y] = (a * (1 - sx) + b * sx) * (1 - sy) + (c * (1 - sx) + d * sx) * sy;
        }
    }
    return res;
}

static Color Lerp(Color c1, Color c2, double t) =>
    Color.FromArgb((int)(c1.R + (c2.R - c1.R) * t),
                   (int)(c1.G + (c2.G - c1.G) * t),
                   (int)(c1.B + (c2.B - c1.B) * t));

var biomes = new (string Name, Color Dark, Color Lite, string Det)[]
{
    ("grass",  ColorTranslator.FromHtml("#639C38"), ColorTranslator.FromHtml("#70AC44"), "grass"),
    ("forest", ColorTranslator.FromHtml("#2A632C"), ColorTranslator.FromHtml("#33702F"), "forest"),
    ("sand",   ColorTranslator.FromHtml("#D6BE8A"), ColorTranslator.FromHtml("#E2CC9C"), "sand"),
    ("snow",   ColorTranslator.FromHtml("#D3DEEE"), ColorTranslator.FromHtml("#FAFDFF"), "snow"),
    ("water",  ColorTranslator.FromHtml("#22609F"), ColorTranslator.FromHtml("#4A8ED4"), "water"),
    ("oasis",  ColorTranslator.FromHtml("#33855A"), ColorTranslator.FromHtml("#59B87E"), "oasis"),
};

foreach (var (name, dark, lite, det) in biomes)
{
    for (int v = 0; v < 5; v++)
    {
        using var bmp = new Bitmap(N, N);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var n1 = Noise(N, 4, 1000 + v * 7 + name.Length);
        var n2 = Noise(N, 10, 2000 + v * 13 + name.Length * 3);

        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                double t = n1[x, y] * 0.68 + n2[x, y] * 0.32;
                bmp.SetPixel(x, y, Lerp(dark, lite, t));
            }

        var rnd = new Random(500 + v * 31 + name.Length);

        switch (det)
        {
            case "grass":
                using (var pen = new Pen(Color.FromArgb(120, 30, 70, 22), 1.4f))
                    for (int i = 0; i < 160; i++)
                    {
                        int gx = rnd.Next(N), gy = rnd.Next(N);
                        g.DrawLine(pen, gx, gy, gx - 2 + rnd.Next(5), gy - 3 - rnd.Next(3));
                    }
                using (var fl = new SolidBrush(Color.FromArgb(170, 250, 240, 140)))
                    for (int i = 0; i < 10; i++) g.FillRectangle(fl, rnd.Next(N - 2), rnd.Next(N - 2), 2, 2);
                break;

            case "forest":
                for (int i = 0; i < 26; i++)
                {
                    int cx = rnd.Next(N - 16), cy = rnd.Next(N - 16), cd = 12 + rnd.Next(12);
                    using var br = new SolidBrush(Color.FromArgb(60, 10, 42, 16));
                    g.FillEllipse(br, cx, cy, cd, cd);
                    using var rm = new Pen(Color.FromArgb(55, 60, 128, 46), 2f);
                    g.DrawArc(rm, cx - 1, cy - 1, cd + 2, cd + 2, 200, 110);
                }
                break;

            case "sand":
                using (var rp = new Pen(Color.FromArgb(60, 248, 232, 176), 1.6f))
                    for (int i = 0; i < 14; i++) { int yy = rnd.Next(N); g.DrawArc(rp, -40, yy, N + 80, 60, 200, 140); }
                using (var sp = new SolidBrush(Color.FromArgb(70, 180, 160, 110)))
                    for (int i = 0; i < 60; i++) g.FillRectangle(sp, rnd.Next(N), rnd.Next(N), 2, 1);
                break;

            case "snow":
                using (var dr = new SolidBrush(Color.FromArgb(55, 255, 255, 255)))
                    for (int i = 0; i < 22; i++) g.FillEllipse(dr, rnd.Next(N), rnd.Next(N), 4 + rnd.Next(8), 3 + rnd.Next(5));
                using (var sh = new SolidBrush(Color.FromArgb(45, 170, 195, 225)))
                    for (int i = 0; i < 14; i++) g.FillEllipse(sh, rnd.Next(N), rnd.Next(N), 5 + rnd.Next(7), 3 + rnd.Next(4));
                break;

            case "water":
                using (var wv = new Pen(Color.FromArgb(55, 105, 170, 235), 1.6f))
                    for (int i = 0; i < 16; i++)
                    {
                        int yy = rnd.Next(N);
                        g.DrawArc(wv, rnd.Next(N / 2) - 60, yy, 90 + rnd.Next(80), 10, 190, 160);
                    }
                using (var gl = new SolidBrush(Color.FromArgb(45, 215, 238, 252)))
                    for (int i = 0; i < 26; i++) g.FillEllipse(gl, rnd.Next(N - 8), rnd.Next(N - 4), 5 + rnd.Next(6), 2);
                break;

            case "oasis":
                using (var rd = new Pen(Color.FromArgb(130, 26, 96, 48), 1.6f))
                    for (int i = 0; i < 18; i++)
                    {
                        int rx = rnd.Next(N), ry = rnd.Next(N);
                        g.DrawLine(rd, rx, ry, rx - 2 + rnd.Next(5), ry - 4 - rnd.Next(4));
                    }
                using (var sp = new SolidBrush(Color.FromArgb(55, 230, 250, 240)))
                    for (int i = 0; i < 14; i++) g.FillEllipse(sp, rnd.Next(N), rnd.Next(N), 3, 2);
                break;
        }

        bmp.Save(Path.Combine(outDir, $"{name}_{v}.png"), ImageFormat.Png);
        Console.WriteLine($"saved {name}_{v}.png");
    }
}
