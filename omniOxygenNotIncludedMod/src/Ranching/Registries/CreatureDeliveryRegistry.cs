using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace OmniMod.Ranching.Registries
{
    /// <summary>
    /// 放生点全局注册表：采用整型数组索引记录有效释放格子，实现无锁 0 GC O(1) 判定
    /// </summary>
    public static class CreatureDeliveryRegistry
    {
        private static readonly object _lock = new object();
        private static int[] _deliveryPointCells;
        private static readonly Dictionary<CreatureDeliveryPoint, List<int>> _pointCells = new Dictionary<CreatureDeliveryPoint, List<int>>();

        private static void EnsureCellArray()
        {
            if (_deliveryPointCells == null || _deliveryPointCells.Length < Grid.CellCount)
            {
                int count = Math.Max(Grid.CellCount, 1);
                _deliveryPointCells = new int[count];
            }
        }

        public static void Register(CreatureDeliveryPoint point)
        {
            if (point == null) return;
            int baseCell = Grid.PosToCell(point.transform.GetPosition());
            if (!Grid.IsValidCell(baseCell)) return;

            lock (_lock)
            {
                EnsureCellArray();

                if (_pointCells.ContainsKey(point)) return;

                var cells = new List<int>();
                _pointCells[point] = cells;

                void AddCell(int c)
                {
                    if (Grid.IsValidCell(c) && (uint)c < (uint)_deliveryPointCells.Length)
                    {
                        cells.Add(c);
                        _deliveryPointCells[c]++;
                    }
                }

                AddCell(baseCell);
                if (point.deliveryOffsets != null)
                {
                    for (int i = 0; i < point.deliveryOffsets.Length; i++)
                    {
                        AddCell(Grid.OffsetCell(baseCell, point.deliveryOffsets[i]));
                    }
                }
                AddCell(Grid.OffsetCell(baseCell, point.spawnOffset));
            }
        }

        public static void Unregister(CreatureDeliveryPoint point)
        {
            if (point == null) return;

            lock (_lock)
            {
                if (_pointCells.TryGetValue(point, out var cells))
                {
                    _pointCells.Remove(point);
                    if (_deliveryPointCells != null)
                    {
                        for (int i = 0; i < cells.Count; i++)
                        {
                            int c = cells[i];
                            if ((uint)c < (uint)_deliveryPointCells.Length && _deliveryPointCells[c] > 0)
                            {
                                _deliveryPointCells[c]--;
                            }
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDeliveryCell(int cell)
        {
            var cells = _deliveryPointCells;
            return cells != null && (uint)cell < (uint)cells.Length && cells[cell] > 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDeliveryCell(int cell, int worldId)
        {
            var cells = _deliveryPointCells;
            if (cells != null && (uint)cell < (uint)cells.Length && cells[cell] > 0)
            {
                return Grid.WorldIdx != null && (uint)cell < (uint)Grid.WorldIdx.Length && Grid.WorldIdx[cell] == worldId;
            }
            return false;
        }

        internal static CreatureDeliveryPoint[] Snapshot()
        {
            lock (_lock)
            {
                var points = new CreatureDeliveryPoint[_pointCells.Count];
                _pointCells.Keys.CopyTo(points, 0);
                return points;
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _pointCells.Clear();
                if (_deliveryPointCells != null)
                {
                    Array.Clear(_deliveryPointCells, 0, _deliveryPointCells.Length);
                }
            }
        }
    }
}
