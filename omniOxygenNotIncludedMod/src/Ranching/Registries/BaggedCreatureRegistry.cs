using System.Collections.Generic;
using UnityEngine;

namespace OmniMod.Ranching.Registries
{
    /// <summary>
    /// 全星系袋装小动物全局注册表：提供 O(1) 格子索引与实时袋装动物检索
    /// </summary>
    public static class BaggedCreatureRegistry
    {
        private static readonly object _lock = new object();
        private static readonly HashSet<Baggable> _bagged = new HashSet<Baggable>();
        private static readonly Dictionary<Baggable, int> _bagToCell = new Dictionary<Baggable, int>();
        private static readonly HashSet<int> _baggedCells = new HashSet<int>();

        public static void Register(Baggable bag)
        {
            if (bag == null || bag.gameObject == null) return;
            lock (_lock)
            {
                _bagged.Add(bag);
                int cell = Grid.PosToCell(bag.transform.GetPosition());
                if (Grid.IsValidCell(cell))
                {
                    _bagToCell[bag] = cell;
                    _baggedCells.Add(cell);
                }
            }

            bag.Subscribe((int)GameHashes.ObjectDestroyed, (data) =>
            {
                Unregister(bag);
            });
        }

        public static void Unregister(Baggable bag)
        {
            if (bag == null) return;
            lock (_lock)
            {
                _bagged.Remove(bag);
                if (_bagToCell.TryGetValue(bag, out int oldCell))
                {
                    _bagToCell.Remove(bag);
                    if (!_bagToCell.ContainsValue(oldCell))
                    {
                        _baggedCells.Remove(oldCell);
                    }
                }
            }
        }

        public static bool HasBaggedCreatureAtCell(int cell)
        {
            lock (_lock)
            {
                return _baggedCells.Contains(cell);
            }
        }

        public static List<Baggable> GetBaggedCreatures()
        {
            lock (_lock)
            {
                return new List<Baggable>(_bagged);
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _bagged.Clear();
                _bagToCell.Clear();
                _baggedCells.Clear();
            }
        }
    }
}
