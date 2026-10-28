using System.Collections.Generic;

namespace OmniMod.Ranching.Registries
{
    /// <summary>
    /// 放生点全局注册表：按星球记录全星系所有放生点及其有效释放格子，实现 O(1) 判定
    /// </summary>
    public static class CreatureDeliveryRegistry
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<int, HashSet<int>> _worldDeliveryCells = new Dictionary<int, HashSet<int>>();

        public static void Register(CreatureDeliveryPoint point)
        {
            if (point == null) return;
            int world = point.GetMyWorldId();
            int baseCell = Grid.PosToCell(point.transform.GetPosition());

            lock (_lock)
            {
                if (!_worldDeliveryCells.TryGetValue(world, out var cells))
                {
                    cells = new HashSet<int>();
                    _worldDeliveryCells[world] = cells;
                }

                if (Grid.IsValidCell(baseCell))
                {
                    cells.Add(baseCell);
                }
                if (point.deliveryOffsets != null)
                {
                    for (int i = 0; i < point.deliveryOffsets.Length; i++)
                    {
                        int offsetCell = Grid.OffsetCell(baseCell, point.deliveryOffsets[i]);
                        if (Grid.IsValidCell(offsetCell))
                        {
                            cells.Add(offsetCell);
                        }
                    }
                }
                int spawnCell = Grid.OffsetCell(baseCell, point.spawnOffset);
                if (Grid.IsValidCell(spawnCell))
                {
                    cells.Add(spawnCell);
                }
            }
        }

        public static void Unregister(CreatureDeliveryPoint point)
        {
            if (point == null) return;
            int world = point.GetMyWorldId();
            int baseCell = Grid.PosToCell(point.transform.GetPosition());

            lock (_lock)
            {
                if (_worldDeliveryCells.TryGetValue(world, out var cells))
                {
                    cells.Remove(baseCell);
                    if (point.deliveryOffsets != null)
                    {
                        for (int i = 0; i < point.deliveryOffsets.Length; i++)
                        {
                            cells.Remove(Grid.OffsetCell(baseCell, point.deliveryOffsets[i]));
                        }
                    }
                    cells.Remove(Grid.OffsetCell(baseCell, point.spawnOffset));
                }
            }
        }

        public static bool IsDeliveryCell(int cell, int worldId)
        {
            lock (_lock)
            {
                if (_worldDeliveryCells.TryGetValue(worldId, out var cells))
                {
                    return cells.Contains(cell);
                }
                return false;
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _worldDeliveryCells.Clear();
            }
        }
    }
}
