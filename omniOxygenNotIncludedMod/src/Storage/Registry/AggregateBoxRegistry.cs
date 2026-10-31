using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using OmniMod.Buildings;
using OmniMod.Core.Logging;
using OmniMod.Logistics.RemoteFetch;
using OmniMod.Pool;
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
        internal static int BoxesCount => Boxes.Count;
        internal static GlobalItemCollector GetBox(int index) => (index >= 0 && index < Boxes.Count) ? Boxes[index] : null;

        /// <summary>所有聚合箱的 Storage → 所属聚合箱映射，供"隔空取物"补丁 O(1) 判断某物品是否在可访问的聚合箱里。</summary>
        private static readonly Dictionary<Storage, GlobalItemCollector> StorageToBox = new Dictionary<Storage, GlobalItemCollector>();

        /// <summary>所有生效聚合箱的根格子 → 所属聚合箱映射（仅单格，绝不污染周围挖掘格）。</summary>
        private static readonly Dictionary<int, GlobalItemCollector> ActiveBoxCellToBox = new Dictionary<int, GlobalItemCollector>();
        private static short[] _activeBoxCellWorld;
        private static readonly HashSet<int> _dirtyWorlds = new HashSet<int>();

        private static GlobalItemCollector _discreteHome;
        private static GlobalItemCollector _foodHome;

        internal static void MarkWorldDirty(int worldId)
        {
            lock (_dirtyWorlds)
            {
                _dirtyWorlds.Add(worldId);
            }
        }

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
            if (box.BoxStorage != null)
            {
                box.BoxStorage.ignoreSourcePriority = true;
                StorageToBox[box.BoxStorage] = box;
                RegisterRemoteStorage(box.BoxStorage);
            }
            MarkWorldDirty(box.GetMyWorldId());
            RebuildActiveBoxCells();
        }

        internal static void Unregister(GlobalItemCollector box)
        {
            if (box == null)
            {
                return;
            }
            Boxes.Remove(box);
            if (box.BoxStorage != null)
            {
                StorageToBox.Remove(box.BoxStorage);
            }
            if (_discreteHome == box)
            {
                _discreteHome = null;   // 下次访问 DiscreteHome 时重新选
            }
            if (_foodHome == box)
            {
                _foodHome = null;       // 下次访问 FoodHome 时重新选
            }
            MarkWorldDirty(box.GetMyWorldId());
            RebuildActiveBoxCells();
        }

        /// <summary>这个 Storage 是不是某个聚合箱的。</summary>
        internal static bool IsBoxStorage(Storage storage)
        {
            return storage != null && StorageToBox.ContainsKey(storage);
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

        /// <summary>当前生效的杂物箱总数。</summary>
        internal static int ActiveJunkBoxCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Boxes.Count; i++)
                {
                    GlobalItemCollector b = Boxes[i];
                    if (b != null && b.gameObject != null && !b.IsRedundant && b.mode == GlobalItemCollector.CollectMode.Debris)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        /// <summary>获取指定星球（或其所属母星）上生效的指定类型聚合箱。</summary>
        internal static GlobalItemCollector GetActiveBoxForWorld(int worldId, GlobalItemCollector.CollectMode mode)
        {
            if (worldId < 0 || worldId == 255)
            {
                return null;
            }

            // 1. 优先使用本世界（如火箭舱内部、或本星球自身）直接建造并生效的聚合箱！
            for (int i = 0; i < Boxes.Count; i++)
            {
                GlobalItemCollector b = Boxes[i];
                if (b != null && b.gameObject != null && !b.IsRedundant && b.mode == mode && b.GetMyWorldId() == worldId)
                {
                    return b;
                }
            }

            // 2. 本世界若没有，才回退到所属母星的聚合箱
            int parentWorldId = worldId;
            if (ClusterManager.Instance != null)
            {
                WorldContainer wc = ClusterManager.Instance.GetWorld(worldId);
                if (wc != null && wc.ParentWorldId >= 0 && wc.ParentWorldId != 255)
                {
                    parentWorldId = wc.ParentWorldId;
                }
            }

            if (parentWorldId != worldId)
            {
                for (int i = 0; i < Boxes.Count; i++)
                {
                    GlobalItemCollector b = Boxes[i];
                    if (b != null && b.gameObject != null && !b.IsRedundant && b.mode == mode)
                    {
                        int boxWorld = b.GetMyWorldId();
                        if (boxWorld == parentWorldId || b.GetMyParentWorldId() == parentWorldId)
                        {
                            return b;
                        }
                    }
                }
            }

            return null;
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

        // —— 机械臂"本星球隔空取物"用：主线程每秒刷新每个星球自身聚合箱内物品的快照，
        //    机械臂的扫描补丁使用基于 Tag 索引的字典进行 O(1) 匹配，杜绝全量线性扫描引起的卡顿。——
        private static readonly Dictionary<int, Dictionary<Tag, List<Pickupable>>> _worldBoxItemsByTag = new Dictionary<int, Dictionary<Tag, List<Pickupable>>>();
        private static readonly Dictionary<int, Dictionary<Tag, float>> _worldBoxMassByTag = new Dictionary<int, Dictionary<Tag, float>>();
        private sealed class WorldIndexBuild
        {
            internal int World;
            internal readonly List<GameObject> Items = new List<GameObject>();
            internal readonly HashSet<Storage> Sources = new HashSet<Storage>();
            internal readonly HashSet<Pickupable> Seen = new HashSet<Pickupable>();
            internal readonly Dictionary<Tag, List<Pickupable>> Tags = new Dictionary<Tag, List<Pickupable>>();
            internal readonly Dictionary<Tag, float> Mass = new Dictionary<Tag, float>();
            internal int Cursor;
        }

        private static readonly Queue<int> PendingWorlds = new Queue<int>();
        private static readonly HashSet<int> QueuedWorlds = new HashSet<int>();
        private static WorldIndexBuild _indexBuild;
        private static int _indexFrame = -1;

        // 来源按容器登记，库存变更时只遍历这些容器，不扫描整颗星球的 Pickupables。
        private static readonly Dictionary<int, List<Storage>> RemoteStorages = new Dictionary<int, List<Storage>>();
        private static readonly Dictionary<Storage, int> RemoteStorageWorlds = new Dictionary<Storage, int>();
        private static readonly Dictionary<int, float> LastWorldRefresh = new Dictionary<int, float>();

        internal static void RegisterRemoteStorage(Storage storage)
        {
            if (storage == null) return;
            int world = storage.GetMyWorldId();
            if (world < 0 || world == 255) return;
            bool registered = RemoteStorageWorlds.TryGetValue(storage, out int oldWorld);
            if (registered && oldWorld == world) return;
            if (registered) UnregisterRemoteStorage(storage);
            RemoteStorageWorlds[storage] = world;
            if (!RemoteStorages.TryGetValue(world, out List<Storage> list))
            {
                list = new List<Storage>();
                RemoteStorages[world] = list;
            }
            list.Add(storage);
            MarkWorldDirty(world);
            if (!registered)
            {
                storage.Subscribe((int)GameHashes.OnStorageChange, _ =>
                {
                    RegisterRemoteStorage(storage);
                    MarkWorldDirty(storage.GetMyWorldId());
                });
            }
        }

        internal static void UnregisterRemoteStorage(Storage storage)
        {
            if (ReferenceEquals(storage, null) || !RemoteStorageWorlds.TryGetValue(storage, out int world)) return;
            RemoteStorageWorlds.Remove(storage);
            if (RemoteStorages.TryGetValue(world, out List<Storage> list)) list.Remove(storage);
            MarkWorldDirty(world);
        }

        private static void AddTagToIndex(Dictionary<Tag, List<Pickupable>> index, Dictionary<Tag, float> massIndex, Tag tag, Pickupable p, float mass)
        {
            if (!index.TryGetValue(tag, out List<Pickupable> list))
            {
                list = new List<Pickupable>(4);
                index[tag] = list;
            }
            list.Add(p);

            if (massIndex.TryGetValue(tag, out float currentMass))
            {
                massIndex[tag] = currentMass + mass;
            }
            else
            {
                massIndex[tag] = mass;
            }
        }

        internal static void RefreshWorldBoxItems(int worldId, bool force = false)
        {
            if (worldId < 0 || worldId == 255 || !Game.IsOnMainThread() || QueuedWorlds.Contains(worldId)) return;
            bool exists;
            lock (_worldBoxItemsByTag) exists = _worldBoxItemsByTag.ContainsKey(worldId);
            lock (_dirtyWorlds)
            {
                if (!force && exists && !_dirtyWorlds.Contains(worldId)) return;
            }
            if (!force && exists && LastWorldRefresh.TryGetValue(worldId, out float last) &&
                Time.realtimeSinceStartup - last < 0.5f) return;
            QueuedWorlds.Add(worldId);
            PendingWorlds.Enqueue(worldId);
        }

        private static void CaptureIndexStorage(WorldIndexBuild build, Storage storage)
        {
            if (!SolidTransferArmRemotePickup.IsRemoteStorage(storage) || !build.Sources.Add(storage)) return;
            storage.ignoreSourcePriority = true;
            // 只复制引用；GetComponent、去重与逐标签建表分散到后续帧。
            build.Items.AddRange(storage.items);
        }

        internal static void AdvanceWorldIndexes()
        {
            if (_indexFrame == Time.frameCount || !Game.IsOnMainThread()) return;
            _indexFrame = Time.frameCount;
            if (_indexBuild == null && PendingWorlds.Count == 0) return;
            ModLog.CallTimer started = ModLog.StartCall();
            long deadline = started.Started + Stopwatch.Frequency / 500;
            if (_indexBuild == null)
            {
                int world = PendingWorlds.Dequeue();
                _indexBuild = new WorldIndexBuild { World = world };
                lock (_dirtyWorlds) _dirtyWorlds.Remove(world);
                CaptureIndexStorage(_indexBuild, GetActiveJunkBoxForWorld(world)?.BoxStorage);
                CaptureIndexStorage(_indexBuild, GetActiveBoxForWorld(world, GlobalItemCollector.CollectMode.Edibles)?.BoxStorage);
                if (RemoteStorages.TryGetValue(world, out List<Storage> storages))
                    for (int i = 0; i < storages.Count; i++) CaptureIndexStorage(_indexBuild, storages[i]);
            }
            WorldIndexBuild build = _indexBuild;
            int examined = 0;
            while (build.Cursor < build.Items.Count && examined++ < 256 && Stopwatch.GetTimestamp() < deadline)
            {
                GameObject item = build.Items[build.Cursor++];
                Pickupable pickup = item != null ? item.GetComponent<Pickupable>() : null;
                if (pickup == null || pickup.storage == null || !build.Sources.Contains(pickup.storage) ||
                    !build.Seen.Add(pickup)) continue;
                KPrefabID id = pickup.KPrefabID;
                if (id == null || id.HasTag(GameTags.StoredPrivate)) continue;
                float mass = pickup.TotalAmount;
                Tag prefab = id.PrefabTag;
                AddTagToIndex(build.Tags, build.Mass, prefab, pickup, mass);
                foreach (Tag tag in id.Tags)
                    if (tag != prefab) AddTagToIndex(build.Tags, build.Mass, tag, pickup, mass);
            }
            if (build.Cursor >= build.Items.Count)
            {
                // 后台查询继续读上一份完整快照，整批完成后才替换；绝不发布半张表。
                lock (_worldBoxItemsByTag)
                {
                    _worldBoxItemsByTag[build.World] = build.Tags;
                    _worldBoxMassByTag[build.World] = build.Mass;
                }
                LastWorldRefresh[build.World] = Time.realtimeSinceStartup;
                QueuedWorlds.Remove(build.World);
                _indexBuild = null;
            }
            ModLog.SlowCall("仓储索引分片", started, examined);
        }

        internal static void AbortWorldIndexBuild()
        {
            if (_indexBuild == null) return;
            MarkWorldDirty(_indexBuild.World);
            QueuedWorlds.Remove(_indexBuild.World);
            _indexBuild = null;
        }

        internal static void ClearWorldIndexes()
        {
            _indexBuild = null;
            _indexFrame = -1;
            PendingWorlds.Clear();
            QueuedWorlds.Clear();
            LastWorldRefresh.Clear();
            lock (_worldBoxItemsByTag)
            {
                _worldBoxItemsByTag.Clear();
                _worldBoxMassByTag.Clear();
            }
        }

        internal static bool TryGetWorldBoxMass(int worldId, Tag tag, out float mass)
        {
            Dictionary<Tag, float> massDict = null;
            lock (_worldBoxItemsByTag)
            {
                _worldBoxMassByTag.TryGetValue(worldId, out massDict);
            }
            if (massDict == null)
            {
                RefreshWorldBoxItems(worldId);
                lock (_worldBoxItemsByTag)
                {
                    _worldBoxMassByTag.TryGetValue(worldId, out massDict);
                }
            }
            if (massDict != null && massDict.TryGetValue(tag, out mass))
            {
                return true;
            }
            mass = 0f;
            return false;
        }

        internal static bool TryGetWorldBoxPickupables(int worldId, Tag tag, out List<Pickupable> list)
        {
            Dictionary<Tag, List<Pickupable>> tagDict = null;
            lock (_worldBoxItemsByTag)
            {
                _worldBoxItemsByTag.TryGetValue(worldId, out tagDict);
            }
            if (tagDict == null)
            {
                RefreshWorldBoxItems(worldId);
                lock (_worldBoxItemsByTag)
                {
                    _worldBoxItemsByTag.TryGetValue(worldId, out tagDict);
                }
            }
            if (tagDict != null && tagDict.TryGetValue(tag, out list))
            {
                return true;
            }
            list = null;
            return false;
        }

        internal static Pickupable FindMatchInWorldBox(int worldId, FetchChore chore, Storage destination, bool isCrossPlanetDiscrete = false)
        {
            // 快照存在也可能已脏；原先仅首次创建时刷新，普通箱新入库物品会长期不可见。
            RefreshWorldBoxItems(worldId);
            Dictionary<Tag, List<Pickupable>> tagIndex;
            lock (_worldBoxItemsByTag)
            {
                _worldBoxItemsByTag.TryGetValue(worldId, out tagIndex);
            }

            if (tagIndex == null)
            {
                return null;
            }

            if (chore.criteria == FetchChore.MatchCriteria.MatchID)
            {
                if (chore.tags != null)
                {
                    foreach (Tag t in chore.tags)
                    {
                        if (tagIndex.TryGetValue(t, out List<Pickupable> list) && list != null)
                        {
                            Pickupable p = CheckCandidateList(list, chore, destination, isCrossPlanetDiscrete, worldId);
                            if (p != null) return p;
                        }
                    }
                }
            }
            else if (chore.criteria == FetchChore.MatchCriteria.MatchTags)
            {
                if (chore.requiredTag.IsValid && tagIndex.TryGetValue(chore.requiredTag, out List<Pickupable> reqList) && reqList != null)
                {
                    Pickupable p = CheckCandidateList(reqList, chore, destination, isCrossPlanetDiscrete, worldId);
                    if (p != null) return p;
                }

                if (chore.tagsFirst.IsValid && tagIndex.TryGetValue(chore.tagsFirst, out List<Pickupable> firstList) && firstList != null)
                {
                    Pickupable p = CheckCandidateList(firstList, chore, destination, isCrossPlanetDiscrete, worldId);
                    if (p != null) return p;
                }

                if (chore.tags != null)
                {
                    foreach (Tag t in chore.tags)
                    {
                        if (t != chore.requiredTag && t != chore.tagsFirst && tagIndex.TryGetValue(t, out List<Pickupable> list) && list != null)
                        {
                            Pickupable p = CheckCandidateList(list, chore, destination, isCrossPlanetDiscrete, worldId);
                            if (p != null) return p;
                        }
                    }
                }
            }

            // 容错按需即时物化：如果箱内尚无该固体元素实体（如新炼成的浓缩铀尚未缓冲），直接从 OmniPool 即时物化
            if (!isCrossPlanetDiscrete)
            {
                Pickupable onDemand = TryMaterializeSolidFromPoolForChore(worldId, chore, destination);
                if (onDemand != null)
                {
                    return onDemand;
                }
            }

            return null;
        }

        private static bool ElementMatchesTag(Element e, Tag searchTag)
        {
            if (e == null || !searchTag.IsValid) return false;
            return e.HasTag(searchTag) || e.materialCategory == searchTag;
        }

        private static Pickupable TryMaterializeSolidFromPoolForChore(int worldId, FetchChore chore, Storage destination)
        {
            if (worldId < 0 || chore == null || destination == null) return null;
            if (OmniPool.Instance == null) return null;

            GlobalItemCollector localJunk = GetActiveBoxForWorld(worldId, GlobalItemCollector.CollectMode.Debris);
            if (localJunk == null || localJunk.BoxStorage == null)
            {
                if (ClusterManager.Instance != null)
                {
                    WorldContainer wc = ClusterManager.Instance.GetWorld(worldId);
                    if (wc != null && wc.ParentWorldId >= 0 && wc.ParentWorldId != 255 && wc.ParentWorldId != worldId)
                    {
                        localJunk = GetActiveBoxForWorld(wc.ParentWorldId, GlobalItemCollector.CollectMode.Debris);
                    }
                }
                if (localJunk == null || localJunk.BoxStorage == null)
                {
                    localJunk = DiscreteHome;
                }
            }
            if (localJunk == null || localJunk.BoxStorage == null) return null;

            float needed = chore.originalAmount > 0f ? chore.originalAmount : chore.amount;

            if (chore.criteria == FetchChore.MatchCriteria.MatchID)
            {
                if (chore.tags != null)
                {
                    foreach (Tag t in chore.tags)
                    {
                        Element e = ElementLoader.GetElement(t);
                        if (e != null && e.IsSolid && OmniPool.Instance.GetMass(e.id) > 0.001f)
                        {
                            Pickupable p = BulkBufferManager.MaterializeSolidFromPool(localJunk, e.id, needed);
                            if (p != null && FetchManager.IsFetchablePickup(p, chore, destination))
                            {
                                ModLog.Throttle($"mat_{e.name}", $"隔空取物即时物化固体: {e.name} (Chore需要 {needed:0.#} kg)", 5f);
                                return p;
                            }
                        }
                    }
                }
            }
            else if (chore.criteria == FetchChore.MatchCriteria.MatchTags)
            {
                // 先尝试具体的 requiredTag 或 tagsFirst
                if (chore.requiredTag.IsValid)
                {
                    Element e = ElementLoader.GetElement(chore.requiredTag);
                    if (e != null && e.IsSolid && OmniPool.Instance.GetMass(e.id) > 0.001f)
                    {
                        Pickupable p = BulkBufferManager.MaterializeSolidFromPool(localJunk, e.id, needed);
                        if (p != null && FetchManager.IsFetchablePickup(p, chore, destination))
                        {
                            ModLog.Throttle($"mat_req_{e.name}", $"隔空取物即时物化固体: {e.name} (Chore需要 {needed:0.#} kg)", 5f);
                            return p;
                        }
                    }
                }

                if (chore.tagsFirst.IsValid)
                {
                    Element e = ElementLoader.GetElement(chore.tagsFirst);
                    if (e != null && e.IsSolid && OmniPool.Instance.GetMass(e.id) > 0.001f)
                    {
                        Pickupable p = BulkBufferManager.MaterializeSolidFromPool(localJunk, e.id, needed);
                        if (p != null && FetchManager.IsFetchablePickup(p, chore, destination))
                        {
                            ModLog.Throttle($"mat_first_{e.name}", $"隔空取物即时物化固体: {e.name} (Chore需要 {needed:0.#} kg)", 5f);
                            return p;
                        }
                    }
                }

                if (chore.tags != null)
                {
                    foreach (Tag t in chore.tags)
                    {
                        Element e = ElementLoader.GetElement(t);
                        if (e != null && e.IsSolid && OmniPool.Instance.GetMass(e.id) > 0.001f)
                        {
                            Pickupable p = BulkBufferManager.MaterializeSolidFromPool(localJunk, e.id, needed);
                            if (p != null && FetchManager.IsFetchablePickup(p, chore, destination))
                            {
                                ModLog.Throttle($"mat_tag_{e.name}", $"隔空取物即时物化固体: {e.name} (Chore需要 {needed:0.#} kg)", 5f);
                                return p;
                            }
                        }
                    }
                }

                // 若 tags 属于大类（如 GameTags.RefinedMetal / GameTags.BuildableRaw 等），遍历池中可用固体
                foreach (OmniPool.PoolLine line in OmniPool.Instance.EnumerateJunk())
                {
                    if (line.MassKg <= 0.001f) continue;
                    Element e = ElementLoader.FindElementByHash(line.Element);
                    if (e == null || !e.IsSolid) continue;

                    if (chore.requiredTag.IsValid && !ElementMatchesTag(e, chore.requiredTag))
                    {
                        continue;
                    }

                    if (chore.forbiddenTags != null)
                    {
                        bool forbidden = false;
                        for (int f = 0; f < chore.forbiddenTags.Length; f++)
                        {
                            Tag forbid = chore.forbiddenTags[f];
                            if (ElementMatchesTag(e, forbid))
                            {
                                forbidden = true;
                                break;
                            }
                        }
                        if (forbidden) continue;
                    }

                    bool matches = false;
                    if (chore.tagsFirst.IsValid && ElementMatchesTag(e, chore.tagsFirst))
                    {
                        matches = true;
                    }
                    else if (chore.tags != null)
                    {
                        foreach (Tag t in chore.tags)
                        {
                            if (ElementMatchesTag(e, t))
                            {
                                matches = true;
                                break;
                            }
                        }
                    }

                    if (matches)
                    {
                        Pickupable p = BulkBufferManager.MaterializeSolidFromPool(localJunk, e.id, needed);
                        if (p != null && FetchManager.IsFetchablePickup(p, chore, destination))
                        {
                            ModLog.Throttle($"mat_tag_{e.name}", $"隔空取物按标签即时物化固体: {e.name} (Chore需要 {needed:0.#} kg)", 5f);
                            return p;
                        }
                    }
                }
            }

            return null;
        }

        private static Pickupable CheckCandidateList(List<Pickupable> list, FetchChore chore, Storage destination, bool isCrossPlanetDiscrete, int targetWorld)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Pickupable p = list[i];
                if (p == null || p.KPrefabID == null || p.UnreservedFetchAmount <= 0f)
                {
                    continue;
                }
                if (!SolidTransferArmRemotePickup.IsRemoteSource(p)) continue;
                if (p.storage != null)
                {
                    if (p.storage == destination)
                    {
                        continue;
                    }
                    p.storage.allowItemRemoval = true;
                    p.storage.ignoreSourcePriority = true;
                }
                if (isCrossPlanetDiscrete)
                {
                    if (!GlobalItemCollector.IsDiscreteManaged(p.gameObject))
                    {
                        continue;
                    }
                    Assignable assignable = p.GetComponent<Assignable>();
                    if (assignable != null && assignable.IsAssigned() && GetAssigneeWorldId(assignable.assignee) != targetWorld)
                    {
                        continue;
                    }
                }
                if (FetchManager.IsFetchablePickup(p, chore, destination))
                {
                    return p;
                }
            }
            return null;
        }

        /// <summary>统计除 self 以外所有同类型生效聚合箱 Storage 中某 tag 的可用质量（供侧边栏汇总全星系存量）。</summary>
        internal static float GetOtherBoxesMassAvailable(GlobalItemCollector self, Tag tag)
        {
            float total = 0f;
            if (self == null)
            {
                return total;
            }
            for (int i = 0; i < Boxes.Count; i++)
            {
                GlobalItemCollector b = Boxes[i];
                if (b != null && b != self && b.gameObject != null && !b.IsRedundant
                    && b.mode == self.mode && b.BoxStorage != null)
                {
                    total += b.BoxStorage.GetMassAvailable(tag);
                }
            }
            return total;
        }

        /// <summary>统计除 self 以外所有生效杂物箱中某散装元素的缓冲质量（供微量散装元素跨星球均衡缓冲）。</summary>
        internal static void CollectOtherBoxesBulkMass(GlobalItemCollector self, Dictionary<SimHashes, float> totals)
        {
            totals.Clear();
            for (int i = 0; i < Boxes.Count; i++)
            {
                GlobalItemCollector box = Boxes[i];
                if (box == null || box == self || box.IsRedundant ||
                    box.mode != GlobalItemCollector.CollectMode.Debris || box.BoxStorage == null) continue;
                List<GameObject> items = box.BoxStorage.items;
                for (int k = 0; k < items.Count; k++)
                {
                    GameObject go = items[k];
                    Pickupable pickup = go != null ? go.GetComponent<Pickupable>() : null;
                    if (pickup == null || !BulkBufferManager.IsBulkSolidElementChunk(pickup)) continue;
                    PrimaryElement element = pickup.PrimaryElement;
                    totals.TryGetValue(element.ElementID, out float mass);
                    totals[element.ElementID] = mass + element.Mass;
                }
            }
        }

        /// <summary>
        /// 供 WorldInventory.GetTotalAmount 补丁调用：当本星球建有杂物箱/食物箱时，
        /// 将其它星球生效聚合箱 Storage 内对应 tag 的存量计入本星球可用量，使全星系面板与选种/建造界面 100% 共享。
        /// 基于按星球 Tag 索引字典进行 O(1) 查询，彻底杜绝线性扫描成千上万件物品引起的严重卡顿！
        /// </summary>
        internal static float GetSharedRemoteBoxMass(int worldId, int parentWorldId, Tag tag)
        {
            bool hasJunk = WorldHasActiveBox(worldId, GlobalItemCollector.CollectMode.Debris);
            bool hasFood = WorldHasActiveBox(worldId, GlobalItemCollector.CollectMode.Edibles);
            if (!hasJunk && !hasFood)
            {
                return 0f;
            }

            float sum = 0f;
            for (int i = 0; i < Boxes.Count; i++)
            {
                GlobalItemCollector b = Boxes[i];
                if (b == null || b.gameObject == null || b.IsRedundant || b.BoxStorage == null)
                {
                    continue;
                }
                if (b.GetMyParentWorldId() == parentWorldId)
                {
                    continue;
                }
                if ((b.mode == GlobalItemCollector.CollectMode.Debris && hasJunk) ||
                    (b.mode == GlobalItemCollector.CollectMode.Edibles && hasFood))
                {
                    int boxWorld = b.GetMyWorldId();
                    if (TryGetWorldBoxMass(boxWorld, tag, out float m))
                    {
                        sum += m;
                    }
                }
            }
            return sum;
        }

        /// <summary>
        /// 供 WorldInventory.GetPickupables 补丁调用：当本星球建有杂物箱/食物箱时，
        /// 将其它星球生效聚合箱 Storage 内匹配 tag 的 Pickupable 列表追加给本星球，使种植砖选种、孵化器选蛋、展台选神器、
        /// 卡路里统计等界面在所有星球 100% 共享全部物品。
        /// 使用 O(1) Tag 索引字典以及 HashSet 去重合并，杜绝线性扫描与 O(N^2) 集合去重造成的界面冻结。
        /// </summary>
        internal static void AppendSharedRemoteBoxPickupables(int worldId, int parentWorldId, Tag tag, ref ICollection<Pickupable> result)
        {
            bool hasJunk = WorldHasActiveBox(worldId, GlobalItemCollector.CollectMode.Debris);
            bool hasFood = WorldHasActiveBox(worldId, GlobalItemCollector.CollectMode.Edibles);
            if (!hasJunk && !hasFood)
            {
                return;
            }

            List<Pickupable> extra = null;
            for (int i = 0; i < Boxes.Count; i++)
            {
                GlobalItemCollector b = Boxes[i];
                if (b == null || b.gameObject == null || b.IsRedundant || b.BoxStorage == null)
                {
                    continue;
                }
                if (b.GetMyParentWorldId() == parentWorldId)
                {
                    continue;
                }
                if ((b.mode == GlobalItemCollector.CollectMode.Debris && hasJunk) ||
                    (b.mode == GlobalItemCollector.CollectMode.Edibles && hasFood))
                {
                    int boxWorld = b.GetMyWorldId();
                    if (TryGetWorldBoxPickupables(boxWorld, tag, out List<Pickupable> list) && list != null && list.Count > 0)
                    {
                        for (int k = 0; k < list.Count; k++)
                        {
                            Pickupable p = list[k];
                            // 严密校验存活状态与组件完整性，彻底杜绝已销毁 Unity 对象的虚假指针传入下游界面导致 NullReferenceException
                            if (p != null && p.gameObject != null && p.KPrefabID != null && !p.KPrefabID.HasTag(GameTags.StoredPrivate))
                            {
                                if (extra == null)
                                {
                                    extra = new List<Pickupable>(list.Count);
                                }
                                extra.Add(p);
                            }
                        }
                    }
                }
            }

            if (extra == null || extra.Count == 0)
            {
                return;
            }

            HashSet<Pickupable> combined = new HashSet<Pickupable>();
            if (result != null)
            {
                foreach (Pickupable p in result)
                {
                    if (p != null && p.gameObject != null && p.KPrefabID != null)
                    {
                        combined.Add(p);
                    }
                }
            }
            for (int i = 0; i < extra.Count; i++)
            {
                Pickupable p = extra[i];
                if (p != null && p.gameObject != null && p.KPrefabID != null)
                {
                    combined.Add(p);
                }
            }
            result = combined;
        }

        /// <summary>解析 IAssignableIdentity 所属小人/目标所在的 WorldId。</summary>
        internal static int GetAssigneeWorldId(IAssignableIdentity assignee)
        {
            if (assignee == null || assignee.IsNull())
            {
                return -1;
            }
            if (assignee is MinionAssignablesProxy proxy)
            {
                GameObject targetGo = proxy.GetTargetGameObject();
                if (targetGo != null)
                {
                    return targetGo.GetMyWorldId();
                }
            }
            if (assignee is KMonoBehaviour kmb && kmb != null)
            {
                return kmb.GetMyWorldId();
            }
            List<Ownables> owners = assignee.GetOwners();
            if (owners != null && owners.Count > 0 && owners[0] != null)
            {
                MinionAssignablesProxy p2 = owners[0].GetComponent<MinionAssignablesProxy>();
                if (p2 != null && p2.GetTargetGameObject() != null)
                {
                    return p2.GetTargetGameObject().GetMyWorldId();
                }
                return owners[0].GetMyWorldId();
            }
            return -1;
        }

        /// <summary>
        /// 按需将指定的单个离散物品（如被分配给 targetWorldId 仿生人的增幅块、或被分配给小人的衣物）
        /// 立即调拨进 targetWorldId 星球的本地杂物箱，让该星球小人走去自己星球的杂物箱拾取安装。
        /// </summary>
        internal static bool TransferItemToWorldJunkBox(GameObject itemGo, int targetWorldId)
        {
            if (itemGo == null || targetWorldId < 0 || targetWorldId == 255)
            {
                return false;
            }

            bool isUpgrade = itemGo.GetComponent<BionicUpgradeComponent>() != null;
            GlobalItemCollector targetBox = isUpgrade
                ? BionicUpgradeRegistry.GetDestinationBox(targetWorldId) : GetActiveJunkBoxForWorld(targetWorldId);
            if (targetBox == null || targetBox.BoxStorage == null)
            {
                return false;
            }

            Pickupable p = itemGo.GetComponent<Pickupable>();
            // 旧版静默存储可能留下过期 StoredPrivate，仅在确认是可调拨容器后同步原版标签。
            if (isUpgrade && BionicUpgradeRegistry.IsDispatchSource(p) && p.storage != null)
                itemGo.Trigger((int)GameHashes.OnStorageInteracted, p.storage);
            if (p == null || p.ReservedAmount > 0f || itemGo.HasTag(GameTags.StoredPrivate))
            {
                return false;
            }

            if (p.storage == targetBox.BoxStorage)
            {
                return true;
            }

            if (p.storage != null)
            {
                if (!IsBoxStorage(p.storage) && !(isUpgrade && BionicUpgradeRegistry.IsDispatchSource(p)))
                {
                    return false;
                }
                int sourceWorld = p.storage.GetMyWorldId();
                if (!p.storage.Transfer(itemGo, targetBox.BoxStorage, block_events: false, hide_popups: true)) return false;
                MarkWorldDirty(sourceWorld);
                MarkWorldDirty(targetBox.GetMyWorldId());
                RefreshWorldBoxItems(targetWorldId);
                return p != null && p.storage == targetBox.BoxStorage;
            }

            int itemWorld = itemGo.GetMyWorldId();
            if (WorldHasActiveJunkBox(itemWorld) || (isUpgrade && BionicUpgradeRegistry.IsDispatchSource(p)))
            {
                targetBox.BoxStorage.Store(itemGo, hide_popups: true);
                MarkWorldDirty(itemWorld);
                MarkWorldDirty(targetBox.GetMyWorldId());
                RefreshWorldBoxItems(targetWorldId);
                return p != null && p.storage == targetBox.BoxStorage;
            }

            return false;
        }

        /// <summary>
        /// 按需确保 targetWorldId 星球的本地杂物箱中有至少 <paramref name="minUnreservedUnits"/> 个未预留的指定离散物品
        /// （如种植砖刚下单的 1 颗种子、孵化器下单的 1 枚蛋、仿生人需要的便携电池）。
        /// 仅按实际需求从其它星球的杂物箱调拨所需个数，绝不均分剩余库存。
        /// </summary>
        internal static bool EnsureDiscreteTagInWorldJunkBox(Tag itemTag, Tag additionalRequiredTag, int targetWorldId, float minUnreservedUnits = 1f)
        {
            if (!itemTag.IsValid || itemTag == GameTags.Empty || targetWorldId < 0 || targetWorldId == 255)
            {
                return false;
            }

            GlobalItemCollector targetBox = GetActiveJunkBoxForWorld(targetWorldId);
            if (targetBox == null || targetBox.BoxStorage == null || targetBox.BoxStorage.items == null)
            {
                return false;
            }

            bool checkAdditional = additionalRequiredTag.IsValid && additionalRequiredTag != GameTags.Empty;
            float haveUnits = 0f;

            List<GameObject> localItems = targetBox.BoxStorage.items;
            for (int i = 0; i < localItems.Count; i++)
            {
                GameObject go = localItems[i];
                if (go == null)
                {
                    continue;
                }
                KPrefabID id = go.GetComponent<KPrefabID>();
                Pickupable p = go.GetComponent<Pickupable>();
                if (id != null && p != null && p.ReservedAmount <= 0f && id.HasTag(itemTag)
                    && (!checkAdditional || id.HasTag(additionalRequiredTag)))
                {
                    haveUnits += p.PrimaryElement != null ? p.PrimaryElement.Units : p.TotalAmount;
                }
            }

            if (haveUnits >= minUnreservedUnits)
            {
                return true;
            }

            bool transferredAny = false;
            for (int bIdx = 0; bIdx < Boxes.Count && haveUnits < minUnreservedUnits; bIdx++)
            {
                GlobalItemCollector donor = Boxes[bIdx];
                if (donor == null || donor == targetBox || donor.gameObject == null || donor.IsRedundant
                    || donor.mode != GlobalItemCollector.CollectMode.Debris || donor.BoxStorage == null || donor.BoxStorage.items == null)
                {
                    continue;
                }

                List<GameObject> donorItems = donor.BoxStorage.items;
                for (int i = donorItems.Count - 1; i >= 0 && haveUnits < minUnreservedUnits; i--)
                {
                    GameObject go = donorItems[i];
                    if (go == null || !GlobalItemCollector.IsDiscreteManaged(go))
                    {
                        continue;
                    }
                    KPrefabID id = go.GetComponent<KPrefabID>();
                    Pickupable p = go.GetComponent<Pickupable>();
                    if (id == null || p == null || p.ReservedAmount > 0f || id.HasTag(GameTags.StoredPrivate))
                    {
                        continue;
                    }
                    if (!id.HasTag(itemTag) || (checkAdditional && !id.HasTag(additionalRequiredTag)))
                    {
                        continue;
                    }

                    // 跳过已分配给其它星球小人的专属物品
                    Assignable assignable = go.GetComponent<Assignable>();
                    if (assignable != null && assignable.IsAssigned() && GetAssigneeWorldId(assignable.assignee) != targetWorldId)
                    {
                        continue;
                    }

                    float needNow = Mathf.Ceil(minUnreservedUnits - haveUnits);
                    float itemUnits = p.PrimaryElement != null ? p.PrimaryElement.Units : 1f;
                    if (p.PrimaryElement != null && itemUnits > needNow && needNow >= 1f)
                    {
                        Pickupable split = p.TakeUnit(needNow);
                        if (split != null)
                        {
                            targetBox.BoxStorage.Store(split.gameObject, hide_popups: true);
                            haveUnits += needNow;
                            transferredAny = true;
                        }
                    }
                    else
                    {
                        donor.BoxStorage.Transfer(go, targetBox.BoxStorage, block_events: false, hide_popups: true);
                        haveUnits += itemUnits;
                        transferredAny = true;
                    }
                }
            }

            if (transferredAny)
            {
                RefreshWorldBoxItems(targetWorldId);
            }
            return haveUnits > 0f;
        }

        /// <summary>
        /// 「离散物品之家」——所有杂物箱收集到的闲置离散物品（增幅块/种子/电池/蛋/衣物/医疗/神器）
        /// 统一归集存放在这个箱子的 Storage，不按星球均分；当其它星球需要时按需调拨到目标星球的本地杂物箱。
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

        /// <summary>
        /// 「食物之家」——第一个生效的食物箱，负责协调各星球食物箱的库存平衡。
        /// </summary>
        internal static GlobalItemCollector FoodHome
        {
            get
            {
                if (_foodHome != null && _foodHome.gameObject != null && !_foodHome.IsRedundant)
                {
                    return _foodHome;
                }

                _foodHome = null;
                for (int i = 0; i < Boxes.Count; i++)
                {
                    GlobalItemCollector b = Boxes[i];
                    if (b != null && b.gameObject != null
                        && b.mode == GlobalItemCollector.CollectMode.Edibles && !b.IsRedundant)
                    {
                        _foodHome = b;
                        break;
                    }
                }
                return _foodHome;
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
