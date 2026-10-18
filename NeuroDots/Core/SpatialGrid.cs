using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace NeuroCivilization.Core
{
    /// <summary>
    /// High-performance zero-allocation Spatial Hash Grid for O(1) cell neighborhood queries.
    /// Avoids LINQ and GC pressure on hot paths.
    /// </summary>
    public sealed class SpatialGrid<T> where T : class
    {
        private readonly int _cols;
        private readonly int _rows;
        private readonly double _invCellSize;
        private readonly List<Entry>[] _buckets;

        public struct Entry
        {
            public T Item;
            public double X;
            public double Y;
        }

        public SpatialGrid(double worldWidth, double worldHeight, double cellSize)
        {
            _invCellSize = 1.0 / cellSize;
            _cols = Math.Max(1, (int)Math.Ceiling(worldWidth * _invCellSize) + 1);
            _rows = Math.Max(1, (int)Math.Ceiling(worldHeight * _invCellSize) + 1);
            _buckets = new List<Entry>[_cols * _rows];
            for (int i = 0; i < _buckets.Length; i++)
                _buckets[i] = new List<Entry>(8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            for (int i = 0; i < _buckets.Length; i++)
                _buckets[i].Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Insert(T item, double x, double y)
        {
            int cx = Math.Clamp((int)(x * _invCellSize), 0, _cols - 1);
            int cy = Math.Clamp((int)(y * _invCellSize), 0, _rows - 1);
            _buckets[cy * _cols + cx].Add(new Entry { Item = item, X = x, Y = y });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetCellBounds(double x, double y, double radius, out int minX, out int maxX, out int minY, out int maxY)
        {
            minX = Math.Clamp((int)((x - radius) * _invCellSize), 0, _cols - 1);
            maxX = Math.Clamp((int)((x + radius) * _invCellSize), 0, _cols - 1);
            minY = Math.Clamp((int)((y - radius) * _invCellSize), 0, _rows - 1);
            maxY = Math.Clamp((int)((y + radius) * _invCellSize), 0, _rows - 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<Entry> GetBucket(int cx, int cy) => _buckets[cy * _cols + cx];
    }
}
