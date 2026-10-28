using System.Collections.Generic;
using UnityEngine;

namespace OmniMod.Ranching.Registries
{
    /// <summary>
    /// 全星系袋装小动物全局注册表：提供高效星球/格子索引与袋装动物检索
    /// </summary>
    public static class BaggedCreatureRegistry
    {
        private static readonly object _lock = new object();
        private static readonly HashSet<Baggable> _bagged = new HashSet<Baggable>();

        public static void Register(Baggable bag)
        {
            if (bag == null || bag.gameObject == null) return;
            lock (_lock)
            {
                _bagged.Add(bag);
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
            }
        }

        public static bool HasBaggedCreatureAtCell(int cell, int worldId)
        {
            if (!Grid.IsValidCell(cell)) return false;

            lock (_lock)
            {
                _bagged.RemoveWhere(b => b == null || b.gameObject == null || !b.wrangled);
                foreach (var b in _bagged)
                {
                    if (b.GetMyWorldId() == worldId)
                    {
                        if (Grid.PosToCell(b) == cell) return true;
                        var p = b.GetComponent<Pickupable>();
                        if (p != null)
                        {
                            if (p.cachedCell == cell || p.GetCell() == cell) return true;
                            var offsets = p.GetOffsets();
                            if (offsets != null)
                            {
                                int bCell = Grid.PosToCell(b);
                                for (int i = 0; i < offsets.Length; i++)
                                {
                                    if (Grid.OffsetCell(bCell, offsets[i]) == cell) return true;
                                }
                            }
                        }
                    }
                }
            }

            // Fallback: 检查该格子上是否有掉落的袋装小动物
            GameObject go = Grid.Objects[cell, (int)ObjectLayer.Pickupables];
            if (go != null)
            {
                var bag = go.GetComponent<Baggable>();
                if (bag != null && bag.wrangled && bag.GetMyWorldId() == worldId)
                {
                    Register(bag);
                    return true;
                }
            }

            return false;
        }

        public static List<Baggable> GetBaggedCreatures(int worldId)
        {
            lock (_lock)
            {
                _bagged.RemoveWhere(b => b == null || b.gameObject == null || !b.wrangled);
                var list = new List<Baggable>();
                foreach (var b in _bagged)
                {
                    if (b.GetMyWorldId() == worldId)
                    {
                        list.Add(b);
                    }
                }
                return list;
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _bagged.Clear();
            }
        }
    }
}
