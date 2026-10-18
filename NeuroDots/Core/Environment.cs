using System;

namespace NeuroCivilization.Core
{
    public class Environment
    {
        public double TimeOfDay;
        public double DayOfYear;
        public int Year;
        public Season CurrentSeason;
        public double DayLight;

        public Weather CurrentWeather;
        public double Temperature;
        public double Humidity;
        public double WindSpeed;
        public double WindDirection;
        public double Precipitation;
        public double CloudCover;

        public bool IsThundering;
        public bool IsDrought;
        public bool IsFlood;
        public double DisasterTimer;

        public double BaseTemperature = 0.5;
        double weatherChangeTimer;
        Random rng;

        // ── Rivers & lakes: coarse water mask generated once per world ──
        const int RCell = 26;
        int _rCols, _rRows;
        bool[] _riverMask = Array.Empty<bool>();

        /// <summary>Winding random-walk rivers edge-to-edge + a few lakes stamped into a coarse static mask.</summary>
        public void GenerateRivers(double ww, double wh, Random random)
        {
            _rCols = (int)(ww / RCell) + 1;
            _rRows = (int)(wh / RCell) + 1;
            _riverMask = new bool[_rCols * _rRows];

            int rivers = 2 + random.Next(2);
            for (int r = 0; r < rivers; r++)
            {
                bool vertical = random.Next(2) == 0;
                // Start on a random edge, aim at the opposite edge
                double sx = 0.15 + random.NextDouble() * 0.7;
                float x, y, vx, vy;
                if (vertical)
                {
                    x = (float)(sx * ww); y = random.Next(2) == 0 ? 4f : (float)wh - 4f;
                    vy = y < wh / 2 ? 1f : -1f; vx = (float)(random.NextDouble() - 0.5);
                }
                else
                {
                    x = random.Next(2) == 0 ? 4f : (float)ww - 4f; y = (float)(sx * wh);
                    vx = x < ww / 2 ? 1f : -1f; vy = (float)(random.NextDouble() - 0.5);
                }

                int steps = (int)(Math.Max(ww, wh) / RCell) + 8;
                for (int s = 0; s < steps && x > -60 && y > -60 && x < ww + 60 && y < wh + 60; s++)
                {
                    StampWater(x, y, 0);
                    // Wander but keep flowing to the opposite edge
                    if (random.Next(5) == 0) { vx += (float)((random.NextDouble() - 0.5) * 0.7); vy += (float)((random.NextDouble() - 0.5) * 0.7); }
                    float len = Math.Max(0.4f, (float)Math.Sqrt(vx * vx + vy * vy));
                    vx /= len; vy /= len;
                    if (vertical) vy += (y < wh / 2 ? 0.10f : -0.10f); else vx += (x < ww / 2 ? 0.10f : -0.10f);
                    x += vx * RCell * 0.7f; y += vy * RCell * 0.7f;
                    if (x < 4) x = 4; if (x > ww - 4) x = (float)ww - 4;
                    if (y < 4) y = 4; if (y > wh - 4) y = (float)wh - 4;
                }
            }

            for (int l = 0; l < 2; l++)
            {
                double cx = ww * (0.2 + random.NextDouble() * 0.6), cy = wh * (0.2 + random.NextDouble() * 0.6), rad = 40 + random.Next(90);
                for (int dy = -4; dy <= 4; dy++)
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        double q = (dx * dx + dy * dy) / 18.0 + (random.NextDouble() - 0.5) * 0.16;
                        if (q < 1.0) StampWater(cx + dx * rad / 4f, cy + dy * rad / 4f, 1);
                    }
            }
        }

        void StampWater(double x, double y, int r)
        {
            int cx = (int)(x / RCell), cy = (int)(y / RCell);
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int tx = cx + dx, ty = cy + dy;
                    if (tx < 0 || ty < 0 || tx >= _rCols || ty >= _rRows) continue;
                    _riverMask[ty * _rCols + tx] = true;
                }
        }

        /// <summary>True if the world point lies on a river/lake cell (biome override + movement slow-down).</summary>
        public bool IsWater(double x, double y)
        {
            if (_riverMask.Length == 0) return false;
            int cx = (int)(x / RCell), cy = (int)(y / RCell);
            if (cx < 0 || cy < 0 || cx >= _rCols || cy >= _rRows) return false;
            return _riverMask[cy * _rCols + cx];
        }

        public Environment(Random? random = null)
        {
            rng = random ?? new Random();
            TimeOfDay = 8;
            CurrentWeather = Weather.Clear;
            Temperature = 0.5;
            Humidity = 0.4;
        }

        public void Update(double dt)
        {
            TimeOfDay += dt * 0.06;
            if (TimeOfDay >= 24) TimeOfDay -= 24;

            double sunAngle = (TimeOfDay - 6) / 12.0 * Math.PI;
            DayLight = Math.Clamp(Math.Sin(sunAngle) * 0.6 + 0.4, 0, 1);

            DayOfYear += dt * 0.003;
            if (DayOfYear >= 365) { DayOfYear -= 365; Year++; }

            if (DayOfYear < 91) CurrentSeason = Season.Spring;
            else if (DayOfYear < 182) CurrentSeason = Season.Summer;
            else if (DayOfYear < 273) CurrentSeason = Season.Autumn;
            else CurrentSeason = Season.Winter;

            double seasonTemp = CurrentSeason switch
            {
                Season.Spring => 0.5,
                Season.Summer => 0.75,
                Season.Autumn => 0.45,
                Season.Winter => 0.25,
                _ => 0.5
            };
            double targetTemp = BaseTemperature * 0.3 + seasonTemp * 0.7 +
                                DayLight * 0.1 - 0.03;
            Temperature += (targetTemp - Temperature) * dt * 0.01;
            Temperature = Math.Clamp(Temperature, 0, 1);

            weatherChangeTimer += dt;
            if (weatherChangeTimer > 600 + rng.NextDouble() * 800)
            {
                ChangeWeather();
                weatherChangeTimer = 0;
            }

            UpdateWeatherEffects(dt);
            UpdateDisasters(dt);
        }

        void ChangeWeather()
        {
            double r = rng.NextDouble();
            CurrentWeather = CurrentSeason switch
            {
                Season.Winter => r < 0.2 ? Weather.Snow : r < 0.3 ? Weather.Cloudy : Weather.Clear,
                Season.Summer => r < 0.08 ? Weather.Storm : r < 0.15 ? Weather.Rain :
                                 r < 0.25 ? Weather.Cloudy : r < 0.3 ? Weather.HeatWave : Weather.Clear,
                _ => r < 0.15 ? Weather.Rain : r < 0.25 ? Weather.Cloudy :
                     r < 0.3 ? Weather.Fog : Weather.Clear
            };
        }

        void UpdateWeatherEffects(double dt)
        {
            Precipitation = Math.Max(0, Precipitation - dt * 0.008);
            IsThundering = false;

            switch (CurrentWeather)
            {
                case Weather.Rain:
                    Precipitation = Math.Min(0.5, Precipitation + dt * 0.015);
                    Humidity = Math.Min(1, Humidity + dt * 0.005);
                    CloudCover = 0.7; WindSpeed = 0.3; break;
                case Weather.Storm:
                    Precipitation = Math.Min(1, Precipitation + dt * 0.03);
                    Humidity = 0.9; CloudCover = 0.95;
                    WindSpeed = Math.Min(1, WindSpeed + dt * 0.01);
                    IsThundering = rng.NextDouble() < 0.01; break;
                case Weather.Snow:
                    Precipitation = Math.Min(0.4, Precipitation + dt * 0.01);
                    Temperature = Math.Max(0, Temperature - dt * 0.003);
                    CloudCover = 0.6; break;
                case Weather.Fog:
                    Humidity = Math.Min(1, Humidity + dt * 0.005);
                    CloudCover = 0.4; WindSpeed = 0.05; break;
                case Weather.HeatWave:
                    Temperature = Math.Min(1, Temperature + dt * 0.002);
                    Humidity = Math.Max(0, Humidity - dt * 0.005);
                    CloudCover = 0.1; break;
                default:
                    Humidity += (0.4 - Humidity) * dt * 0.003;
                    CloudCover = Math.Max(0, CloudCover - dt * 0.01);
                    WindSpeed += (0.2 - WindSpeed) * dt * 0.005; break;
            }
        }

        void UpdateDisasters(double dt)
        {
            DisasterTimer += dt;
            IsDrought = CurrentSeason == Season.Summer && Humidity < 0.2 &&
                        Precipitation < 0.1 && DisasterTimer > 500;
            IsFlood = Precipitation > 0.8 && Humidity > 0.9 && DisasterTimer > 400;
            if (IsDrought || IsFlood) DisasterTimer = 0;
        }

        /// <summary>FIX: Биомы СТАТИЧНЫЕ — зависят ТОЛЬКО от позиции, НЕ от погоды</summary>
        public Biome GetBiome(double x, double y, double ww, double wh)
        {
            if (IsWater(x, y)) return Biome.River;
            double nx = x / ww;
            double ny = y / wh;

            // Статичный шум на основе позиции
            double moisture = 0.5
                + Math.Sin(nx * Math.PI * 3.7) * 0.2
                + Math.Cos(ny * Math.PI * 2.3) * 0.15
                + Math.Sin((nx + ny) * Math.PI * 5.1) * 0.1;

            double heat = 0.3
                + (1 - ny) * 0.5 // Юг теплее
                + Math.Sin(nx * Math.PI * 2.1) * 0.08
                + Math.Cos(ny * Math.PI * 4.3) * 0.05;

            moisture = Math.Clamp(moisture, 0, 1);
            heat = Math.Clamp(heat, 0, 1);

            if (heat > 0.78 && moisture < 0.35) return Biome.Desert;
            if (heat > 0.7 && moisture > 0.6) return Biome.TropicalForest;
            if (heat < 0.22) return Biome.Tundra;
            if (heat < 0.38 && moisture > 0.45) return Biome.BorealForest;
            if (moisture > 0.72) return Biome.Swamp;
            if (moisture > 0.45) return Biome.Forest;
            if (moisture < 0.3) return Biome.Steppe;
            return Biome.Grassland;
        }

        public double PlantGrowthRate
        {
            get
            {
                double g = 0.5;
                g *= CurrentSeason switch
                {
                    Season.Spring => 1.5,
                    Season.Summer => 1.3,
                    Season.Autumn => 0.7,
                    Season.Winter => 0.2,
                    _ => 1
                };
                g *= (0.5 + DayLight * 0.5);
                g *= (0.3 + Humidity * 0.7);
                if (Temperature < 0.15 || Temperature > 0.9) g *= 0.2;
                return g;
            }
        }

        public double EnvironmentalDanger
        {
            get
            {
                double d = 0;
                if (IsThundering) d += 0.3;
                if (IsDrought) d += 0.4;
                if (IsFlood) d += 0.5;
                if (Temperature < 0.15 || Temperature > 0.85) d += 0.3;
                if (CurrentWeather == Weather.Storm) d += 0.2;
                return Math.Clamp(d, 0, 1);
            }
        }
    }

    public enum Season { Spring, Summer, Autumn, Winter }
    public enum Weather { Clear, Cloudy, Rain, Storm, Snow, Fog, HeatWave }
    public enum Biome
    {
        Grassland, Forest, Desert, Tundra, Swamp,
        BorealForest, TropicalForest, Steppe, Mountain, River
    }
}