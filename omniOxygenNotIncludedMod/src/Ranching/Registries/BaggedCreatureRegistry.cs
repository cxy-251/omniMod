using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace OmniMod.Ranching.Registries
{
    /// <summary>
    /// 全星系袋装小动物全局注册表：
    /// 采用直接整型数组索引与按需星球快照，彻底实现 0 GC、0 锁竞争、O(1) 纳秒级检索。
    /// 确保机械臂多线程更新（SolidTransferArmBatchUpdater）中 IsCellReachable 零压力。
    /// </summary>
    public static class BaggedCreatureRegistry
    {
        private static readonly object _lock = new object();
        private static int[] _baggedCellCount;
        private static readonly Dictionary<Baggable, int> _baggedToCell = new Dictionary<Baggable, int>();
        private static readonly Dictionary<int, List<Baggable>> _worldBagged = new Dictionary<int, List<Baggable>>();
        private static readonly Dictionary<int, Baggable[]> _worldBaggedArray = new Dictionary<int, Baggable[]>();
        private static readonly List<Baggable> _allBaggedList = new List<Baggable>();
        private static Baggable[] _allBaggedArray = Array.Empty<Baggable>();

        public static int TotalCount
        {
            get
            {
                var arr = _allBaggedArray;
                return arr != null ? arr.Length : 0;
            }
        }

        private static void EnsureCellArray()
        {
            if (_baggedCellCount == null || _baggedCellCount.Length < Grid.CellCount)
            {
                int count = Math.Max(Grid.CellCount, 1);
                _baggedCellCount = new int[count];
            }
        }

        public static void Register(Baggable bag)
        {
            if (bag == null || bag.gameObject == null) return;
            if (!bag.wrangled && !bag.HasTag(GameTags.Creatures.Bagged) && !bag.HasTag(GameTags.Trapped) && !bag.HasTag(GameTags.Creatures.Deliverable))
            {
                return;
            }

            int cell = Grid.PosToCell(bag);
            if (!Grid.IsValidCell(cell)) return;

            int world = bag.GetMyWorldId();

            lock (_lock)
            {
                EnsureCellArray();

                if (_baggedToCell.TryGetValue(bag, out int oldCell))
                {
                    if (oldCell == cell) return;
                    if ((uint)oldCell < (uint)_baggedCellCount.Length && _baggedCellCount[oldCell] > 0)
                    {
                        _baggedCellCount[oldCell]--;
                    }
                }

                _baggedToCell[bag] = cell;
                if ((uint)cell < (uint)_baggedCellCount.Length)
                {
                    _baggedCellCount[cell]++;
                }

                if (!_worldBagged.TryGetValue(world, out var list))
                {
                    list = new List<Baggable>();
                    _worldBagged[world] = list;
                }

                if (!list.Contains(bag))
                {
                    list.Add(bag);
                }
                _worldBaggedArray[world] = list.ToArray();

                if (!_allBaggedList.Contains(bag))
                {
                    _allBaggedList.Add(bag);
                    _allBaggedArray = _allBaggedList.ToArray();
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
                if (_baggedToCell.TryGetValue(bag, out int cell))
                {
                    _baggedToCell.Remove(bag);
                    if (_baggedCellCount != null && (uint)cell < (uint)_baggedCellCount.Length && _baggedCellCount[cell] > 0)
                    {
                        _baggedCellCount[cell]--;
                    }
                }

                int world = bag.GetMyWorldId();
                if (_worldBagged.TryGetValue(world, out var list) && list.Remove(bag))
                {
                    _worldBaggedArray[world] = list.ToArray();
                }
                else
                {
                    foreach (var kvp in _worldBagged)
                    {
                        if (kvp.Value.Remove(bag))
                        {
                            _worldBaggedArray[kvp.Key] = kvp.Value.ToArray();
                            break;
                        }
                    }
                }

                if (_allBaggedList.Remove(bag))
                {
                    _allBaggedArray = _allBaggedList.ToArray();
                }
            }
        }

        public static void OnCellChanged(Baggable bag, int oldCell, int newCell)
        {
            if (bag == null || oldCell == newCell) return;

            lock (_lock)
            {
                EnsureCellArray();
                if (_baggedToCell.ContainsKey(bag))
                {
                    if ((uint)oldCell < (uint)_baggedCellCount.Length && _baggedCellCount[oldCell] > 0)
                    {
                        _baggedCellCount[oldCell]--;
                    }
                    if ((uint)newCell < (uint)_baggedCellCount.Length)
                    {
                        _baggedCellCount[newCell]++;
                    }
                    _baggedToCell[bag] = newCell;

                    int oldWorld = (Grid.WorldIdx != null && (uint)oldCell < (uint)Grid.WorldIdx.Length) ? Grid.WorldIdx[oldCell] : -1;
                    int newWorld = (Grid.WorldIdx != null && (uint)newCell < (uint)Grid.WorldIdx.Length) ? Grid.WorldIdx[newCell] : -1;
                    if (oldWorld != newWorld)
                    {
                        if (_worldBagged.TryGetValue(oldWorld, out var oldList))
                        {
                            oldList.Remove(bag);
                            _worldBaggedArray[oldWorld] = oldList.ToArray();
                        }
                        if (newWorld >= 0)
                        {
                            if (!_worldBagged.TryGetValue(newWorld, out var newList))
                            {
                                newList = new List<Baggable>();
                                _worldBagged[newWorld] = newList;
                            }
                            if (!newList.Contains(bag))
                            {
                                newList.Add(bag);
                            }
                            _worldBaggedArray[newWorld] = newList.ToArray();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 全星系（跨星球与火箭舱内）极速判断目标格子是否有袋装小动物
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasBaggedCreatureAtCell(int cell)
        {
            var counts = _baggedCellCount;
            return counts != null && (uint)cell < (uint)counts.Length && counts[cell] > 0;
        }

        /// <summary>
        /// 供 SolidTransferArm.IsCellReachable 高频调用：
        /// 无锁、无 GC 分配、无 LINQ、无 Native 调用，单个数组寻址 O(1) 立即返回。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasBaggedCreatureAtCell(int cell, int worldId)
        {
            var counts = _baggedCellCount;
            if (counts != null && (uint)cell < (uint)counts.Length && counts[cell] > 0)
            {
                return Grid.WorldIdx != null && (uint)cell < (uint)Grid.WorldIdx.Length && Grid.WorldIdx[cell] == worldId;
            }
            return false;
        }

        /// <summary>
        /// 供机械臂 FindFetchTarget 在主线程匹配袋装动物时获取指定星球袋装动物快照。
        /// </summary>
        public static Baggable[] GetBaggedCreatures(int worldId)
        {
            lock (_lock)
            {
                if (_worldBaggedArray.TryGetValue(worldId, out var arr))
                {
                    return arr;
                }
                return Array.Empty<Baggable>();
            }
        }

        /// <summary>
        /// 获取全星系所有星球/火箭舱内的袋装小动物快照（0 GC、O(1) 数组返回）。
        /// </summary>
        public static Baggable[] GetAllBaggedCreatures()
        {
            return _allBaggedArray;
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _baggedToCell.Clear();
                _worldBagged.Clear();
                _worldBaggedArray.Clear();
                _allBaggedList.Clear();
                _allBaggedArray = Array.Empty<Baggable>();
                if (_baggedCellCount != null)
                {
                    Array.Clear(_baggedCellCount, 0, _baggedCellCount.Length);
                }
            }
        }
    }
}
