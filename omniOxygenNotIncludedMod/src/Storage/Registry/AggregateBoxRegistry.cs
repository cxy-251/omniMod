using System;
using System.Collections.Generic;
using UnityEngine;

namespace OmniMod.Collection
{
    /// <summary>
    /// 所有已生成的聚合箱（杂物箱 / 食物箱）的全局登记表。
    /// 用于"每颗星球每种箱子只能有一个"的校验，以及全星系 100% 共享库存（不均分）。
    /// </summary>
    internal static class AggregateBoxRegistry
    {
        private static readonly List<GlobalItemCollector> Boxes = new List<GlobalItemCollector>();
        private static readonly Dictionary<int, GlobalItemCollector[]> BoxesByWorld = new Dictionary<int, GlobalItemCollector[]>();
        private static readonly Dictionary<GlobalItemCollector, int> BoxWorlds = new Dictionary<GlobalItemCollector, int>();
        internal static int BoxesCount => Boxes.Count;
        internal static GlobalItemCollector GetBox(int index) => (index >= 0 && index < Boxes.Count) ? Boxes[index] : null;

        /// <summary>所有聚合箱的 Storage → 所属聚合箱映射，供"隔空取物"补丁 O(1) 判断某物品是否在可访问的聚合箱里。</summary>
        private static readonly Dictionary<Storage, GlobalItemCollector> StorageToBox = new Dictionary<Storage, GlobalItemCollector>();

        /// <summary>所有生效聚合箱的根格子 → 所属聚合箱映射（仅单格，绝不污染周围挖掘格）。</summary>
        private static readonly Dictionary<int, GlobalItemCollector> ActiveBoxCellToBox = new Dictionary<int, GlobalItemCollector>();
        private static short[] _activeBoxCellWorld;


        private static void EnsureCellArray()
        {
            if (_activeBoxCellWorld == null || _activeBoxCellWorld.Length < Grid.CellCount)
            {
                int count = Mathf.Max(Grid.CellCount, 1);
                _activeBoxCellWorld = new short[count];
                for (int i = 0; i < count; i++) _activeBoxCellWorld[i] = -1;
            }
        }

        internal static void Register(GlobalItemCollector box)
        {
            if (box == null)
            {
                return;
            }
            if (!Boxes.Contains(box))
            {
                Boxes.Add(box);
            }
            int world = box.GetMyWorldId();
            BoxWorlds[box] = world;
            if (!box.IsRedundant && world >= 0 && world != 255)
            {
                if (!BoxesByWorld.TryGetValue(world, out var boxes))
                    BoxesByWorld[world] = boxes = new GlobalItemCollector[2];
                boxes[(int)box.mode] = box;
            }
            if (box.BoxStorage != null)
            {
                box.BoxStorage.ignoreSourcePriority = true;
                StorageToBox[box.BoxStorage] = box;
                WorldStorageIndex.Register(box.BoxStorage);
            }
            WorldStorageIndex.MarkDirty(box.GetMyWorldId());
            SharedBoxInventory.Invalidate();
            RebuildActiveBoxCells();
        }

        internal static void Unregister(GlobalItemCollector box)
        {
            if (box == null)
            {
                return;
            }
            if (!Boxes.Remove(box)) return;
            if (BoxWorlds.TryGetValue(box, out int world))
            {
                BoxWorlds.Remove(box);
                if (BoxesByWorld.TryGetValue(world, out var boxes) && ReferenceEquals(boxes[(int)box.mode], box))
                    boxes[(int)box.mode] = null;
            }
            if (box.BoxStorage != null)
            {
                StorageToBox.Remove(box.BoxStorage);
            }
            WorldStorageIndex.MarkDirty(box.GetMyWorldId());
            SharedBoxInventory.Invalidate();
            RebuildActiveBoxCells();
        }

        /// <summary>这个 Storage 是不是某个聚合箱的。</summary>
        internal static bool IsBoxStorage(Storage storage)
        {
            return storage != null && StorageToBox.ContainsKey(storage);
        }

        internal static bool TryGetBox(Storage storage, out GlobalItemCollector box)
        {
            box = null;
            return storage != null && StorageToBox.TryGetValue(storage, out box) && box != null && !box.IsRedundant;
        }

        internal static void Clear()
        {
            Boxes.Clear();
            BoxesByWorld.Clear();
            BoxWorlds.Clear();
            StorageToBox.Clear();
            ActiveBoxCellToBox.Clear();
            _activeBoxCellWorld = null;
            SharedBoxInventory.Invalidate();
        }

        internal static void RebuildActiveBoxCells()
        {
            ActiveBoxCellToBox.Clear();
            EnsureCellArray();
            if (_activeBoxCellWorld != null)
            {
                for (int i = 0; i < _activeBoxCellWorld.Length; i++) _activeBoxCellWorld[i] = -1;
            }

            for (int i = 0; i < Boxes.Count; i++)
            {
                GlobalItemCollector b = Boxes[i];
                if (b == null || b.gameObject == null || b.IsRedundant)
                {
                    continue;
                }
                short wid = (short)b.GetMyWorldId();
                OccupyArea area = b.GetComponent<OccupyArea>();
                int[] cells = area != null ? area.GetOccupiedGridCells() : null;
                if (cells != null && cells.Length > 0)
                {
                    for (int c = 0; c < cells.Length; c++)
                    {
                        int cell = cells[c];
                        if (Grid.IsValidCell(cell))
                        {
                            ActiveBoxCellToBox[cell] = b;
                            if ((uint)cell < (uint)_activeBoxCellWorld.Length)
                            {
                                _activeBoxCellWorld[cell] = wid;
                            }
                        }
                    }
                }
                else
                {
                    int bc = Grid.PosToCell(b.transform.GetPosition());
                    if (Grid.IsValidCell(bc))
                    {
                        ActiveBoxCellToBox[bc] = b;
                        if ((uint)bc < (uint)_activeBoxCellWorld.Length)
                        {
                            _activeBoxCellWorld[bc] = wid;
                        }
                    }
                }
            }
        }

        /// <summary>只返回当前世界的真实入口；母星回退由具体功能显式选择。</summary>
        internal static GlobalItemCollector GetActiveBoxForWorld(int worldId, GlobalItemCollector.CollectMode mode)
        {
            if (worldId < 0 || worldId == 255 || !BoxesByWorld.TryGetValue(worldId, out var boxes)) return null;
            GlobalItemCollector box = boxes[(int)mode];
            return box != null && !box.IsRedundant && box.GetMyWorldId() == worldId ? box : null;
        }

        internal static GlobalItemCollector GetActiveJunkBoxForWorld(int worldId)
        {
            return GetActiveBoxForWorld(worldId, GlobalItemCollector.CollectMode.Debris);
        }

        internal static void GetActiveBoxes(GlobalItemCollector.CollectMode mode, List<GlobalItemCollector> dst)
        {
            dst.Clear();
            for (int i = 0; i < Boxes.Count; i++)
            {
                GlobalItemCollector b = Boxes[i];
                if (b != null && b.gameObject != null && !b.IsRedundant && b.mode == mode && b.BoxStorage != null)
                {
                    dst.Add(b);
                }
            }
        }

        /// <summary>指定星球（或其所属母星）上是否有生效的指定类型聚合箱。</summary>
        internal static bool WorldHasActiveBox(int worldId, GlobalItemCollector.CollectMode mode)
        {
            return GetActiveBoxForWorld(worldId, mode) != null;
        }

        /// <summary>
        /// 判断某个 Storage 是否是 workerCell 所在星球可访问的聚合箱（供 UI 跨星选增幅块/选装备校验）。
        /// </summary>
        internal static bool IsAccessibleBoxStorage(Storage storage, int workerCell)
        {
            if (storage == null || !Grid.IsValidCell(workerCell))
            {
                return false;
            }
            if (!StorageToBox.TryGetValue(storage, out GlobalItemCollector box) || box == null || box.IsRedundant)
            {
                return false;
            }
            int workerWorldId = Grid.WorldIdx[workerCell];
            return WorldHasActiveBox(workerWorldId, box.mode);
        }

        /// <summary>
        /// 判断某个格子是否是与 workerCell 位于【同一星球】的生效聚合箱根格（供本星机械手无视距离隔空取物）。
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static bool IsSameWorldActiveBoxCell(int cell, int workerCell)
        {
            if (!Grid.IsValidCell(cell) || !Grid.IsValidCell(workerCell))
            {
                return false;
            }
            short[] arr = _activeBoxCellWorld;
            if (arr == null || (uint)cell >= (uint)arr.Length)
            {
                return false;
            }
            short boxWorld = arr[cell];
            if (boxWorld < 0)
            {
                return false;
            }
            return (uint)workerCell < (uint)Grid.WorldIdx.Length && Grid.WorldIdx[workerCell] == boxWorld;
        }

        /// <summary>
        /// 全星系判断目标格是否为生效聚合箱所在格（供全星系机械臂隔空取物，0 GC、O(1) 极速判定）。
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static bool IsActiveBoxCell(int cell)
        {
            short[] arr = _activeBoxCellWorld;
            return arr != null && (uint)cell < (uint)arr.Length && arr[cell] >= 0;
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
            return WorldHasActiveBox(worldId, GlobalItemCollector.CollectMode.Debris);
        }

        /// <summary>指定星球上是否有（生效的）食物箱。</summary>
        internal static bool WorldHasActiveFoodBox(int worldId)
        {
            return WorldHasActiveBox(worldId, GlobalItemCollector.CollectMode.Edibles);
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
