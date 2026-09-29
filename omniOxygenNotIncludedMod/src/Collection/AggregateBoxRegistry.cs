using System.Collections.Generic;
using UnityEngine;

namespace OmniMod.Collection
{
    /// <summary>
    /// 所有已生成的聚合箱（杂物箱 / 食物箱）的全局登记表。
    /// 用于"每颗星球每种箱子只能有一个"的校验，以及后续的全局共享库存。
    /// </summary>
    internal static class AggregateBoxRegistry
    {
        private static readonly List<GlobalItemCollector> Boxes = new List<GlobalItemCollector>();

        /// <summary>所有聚合箱的 Storage 组件，供"隔空取物"补丁 O(1) 判断某物品是否在箱里。</summary>
        private static readonly HashSet<Storage> BoxStorages = new HashSet<Storage>();

        private static GlobalItemCollector _discreteHome;

        internal static void Register(GlobalItemCollector box)
        {
            if (!Boxes.Contains(box))
            {
                Boxes.Add(box);
            }
            if (box != null && box.BoxStorage != null)
            {
                BoxStorages.Add(box.BoxStorage);
            }
        }

        internal static void Unregister(GlobalItemCollector box)
        {
            Boxes.Remove(box);
            if (box != null && box.BoxStorage != null)
            {
                BoxStorages.Remove(box.BoxStorage);
            }
            if (_discreteHome == box)
            {
                _discreteHome = null;   // 下次访问 DiscreteHome 时重新选
            }
        }

        /// <summary>这个 Storage 是不是某个聚合箱的。</summary>
        internal static bool IsBoxStorage(Storage storage)
        {
            if (storage == null)
            {
                return false;
            }
            if (BoxStorages.Contains(storage))
            {
                return true;
            }
            return storage.GetComponent<GlobalItemCollector>() != null;
        }

        // —— 机械臂"隔空取物"用：主线程每秒刷新每个星球箱内物品的快照，
        //    机械臂的（工作线程）扫描补丁只读这个不可变数组。——
        private static readonly Dictionary<int, Pickupable[]> _worldBoxItems = new Dictionary<int, Pickupable[]>();
        private static readonly List<Pickupable> _refreshScratch = new List<Pickupable>(64);

        internal static void RefreshWorldBoxItems(int worldId)
        {
            _refreshScratch.Clear();
            for (int i = 0; i < Boxes.Count; i++)
            {
                GlobalItemCollector b = Boxes[i];
                if (b == null || b.gameObject == null || b.IsRedundant || b.BoxStorage == null
                    || b.BoxStorage.items == null || b.GetMyWorldId() != worldId)
                {
                    continue;
                }
                for (int k = 0; k < b.BoxStorage.items.Count; k++)
                {
                    GameObject go = b.BoxStorage.items[k];
                    Pickupable p = go != null ? go.GetComponent<Pickupable>() : null;
                    if (p != null)
                    {
                        _refreshScratch.Add(p);
                    }
                }
            }

            Pickupable[] arr = _refreshScratch.Count > 0 ? _refreshScratch.ToArray() : System.Array.Empty<Pickupable>();
            lock (_worldBoxItems)
            {
                _worldBoxItems[worldId] = arr;
            }
        }

        internal static Pickupable[] GetWorldBoxItems(int worldId)
        {
            lock (_worldBoxItems)
            {
                return _worldBoxItems.TryGetValue(worldId, out Pickupable[] a) ? a : System.Array.Empty<Pickupable>();
            }
        }

        /// <summary>这个格子上是否有生效的聚合箱（供机械臂"可达"判断）。</summary>
        internal static bool IsActiveBoxCell(int cell)
        {
            if (!Grid.IsValidCell(cell))
            {
                return false;
            }
            for (int i = 0; i < Boxes.Count; i++)
            {
                GlobalItemCollector b = Boxes[i];
                if (b == null || b.gameObject == null || b.IsRedundant)
                {
                    continue;
                }
                int bc = Grid.PosToCell(b.transform.GetPosition());
                if (Grid.IsValidCell(bc) && Grid.GetCellRange(cell, bc) <= 2)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>这个格子上是否有生效的杂物箱（兼容保留）。</summary>
        internal static bool IsActiveJunkBoxCell(int cell)
        {
            return IsActiveBoxCell(cell);
        }

        /// <summary>
        /// 「离散物品之家」——所有杂物箱收集到的离散物品（种子/电池/可选蛋衣物）
        /// 统一存进这个箱子的 Storage，实现星系共享。第一个生效的杂物箱担任。
        /// </summary>
        internal static GlobalItemCollector DiscreteHome
        {
            get
            {
                if (_discreteHome != null && _discreteHome.gameObject != null && !_discreteHome.IsRedundant)
                {
                    return _discreteHome;
                }

                _discreteHome = null;
                for (int i = 0; i < Boxes.Count; i++)
                {
                    GlobalItemCollector b = Boxes[i];
                    if (b != null && b.gameObject != null
                        && b.mode == GlobalItemCollector.CollectMode.Debris && !b.IsRedundant)
                    {
                        _discreteHome = b;
                        break;
                    }
                }
                return _discreteHome;
            }
        }

        /// <summary>指定类型的箱子在指定星球上是否已存在（可排除某个自身）。</summary>
        internal static bool ExistsOnWorld(GlobalItemCollector.CollectMode mode, int worldId, GlobalItemCollector except = null)
        {
            for (int i = 0; i < Boxes.Count; i++)
            {
                GlobalItemCollector b = Boxes[i];
                if (b == null || b == except)
                {
                    continue;
                }

                if (b.mode == mode && b.GetMyWorldId() == worldId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>指定星球上是否有（生效的）杂物箱。</summary>
        internal static bool WorldHasActiveJunkBox(int worldId)
        {
            for (int i = 0; i < Boxes.Count; i++)
            {
                GlobalItemCollector b = Boxes[i];
                if (b != null && b.mode == GlobalItemCollector.CollectMode.Debris
                    && !b.IsRedundant && b.GetMyWorldId() == worldId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>指定建筑 ID 的箱子在指定星球上是否已存在。</summary>
        internal static bool ExistsOnWorld(string buildingId, int worldId)
        {
            if (buildingId == Buildings.JunkBoxConfig.Id)
            {
                return ExistsOnWorld(GlobalItemCollector.CollectMode.Debris, worldId);
            }

            if (buildingId == Buildings.FoodBoxConfig.Id)
            {
                return ExistsOnWorld(GlobalItemCollector.CollectMode.Edibles, worldId);
            }

            return false;
        }
    }
}
