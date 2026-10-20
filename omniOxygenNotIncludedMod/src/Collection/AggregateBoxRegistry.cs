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

        /// <summary>所有聚合箱的 Storage → 所属聚合箱映射，供"隔空取物"补丁 O(1) 判断某物品是否在可访问的聚合箱里。</summary>
        private static readonly Dictionary<Storage, GlobalItemCollector> StorageToBox = new Dictionary<Storage, GlobalItemCollector>();

        /// <summary>所有生效聚合箱的根格子 → 所属聚合箱映射（仅单格，绝不污染周围挖掘格）。</summary>
        private static readonly Dictionary<int, GlobalItemCollector> ActiveBoxCellToBox = new Dictionary<int, GlobalItemCollector>();

        private static GlobalItemCollector _discreteHome;
        private static GlobalItemCollector _foodHome;

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
                StorageToBox[box.BoxStorage] = box;
            }
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
            for (int i = 0; i < Boxes.Count; i++)
            {
                GlobalItemCollector b = Boxes[i];
                if (b == null || b.gameObject == null || b.IsRedundant)
                {
                    continue;
                }
                int bc = Grid.PosToCell(b.transform.GetPosition());
                if (Grid.IsValidCell(bc))
                {
                    ActiveBoxCellToBox[bc] = b;
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
        internal static bool IsSameWorldActiveBoxCell(int cell, int workerCell)
        {
            if (!Grid.IsValidCell(cell) || !Grid.IsValidCell(workerCell))
            {
                return false;
            }
            if (!ActiveBoxCellToBox.TryGetValue(cell, out GlobalItemCollector box) || box == null || box.IsRedundant)
            {
                return false;
            }
            int workerWorldId = Grid.WorldIdx[workerCell];
            int boxWorldId = box.GetMyWorldId();
            if (workerWorldId == boxWorldId)
            {
                return true;
            }
            return box.GetMyParentWorldId() == ClusterUtil.GetMyParentWorldId(box.gameObject) && Grid.WorldIdx[cell] == workerWorldId;
        }

        // —— 机械臂"本星球隔空取物"用：主线程每秒刷新每个星球自身聚合箱内物品的快照，
        //    机械臂的扫描补丁只读这个不可变数组。——
        private static readonly Dictionary<int, Pickupable[]> _worldBoxItems = new Dictionary<int, Pickupable[]>();
        private static readonly Dictionary<int, float> _rocketRefreshTime = new Dictionary<int, float>();
        private static readonly List<Pickupable> _refreshScratch = new List<Pickupable>(128);

        private static void AppendStoragePickupables(Storage storage, List<Pickupable> dst)
        {
            if (storage == null || storage.items == null)
            {
                return;
            }
            for (int k = 0; k < storage.items.Count; k++)
            {
                GameObject go = storage.items[k];
                Pickupable p = go != null ? go.GetComponent<Pickupable>() : null;
                if (p != null)
                {
                    dst.Add(p);
                }
            }
        }

        internal static void AppendRocketInteriorItems(int worldId, List<Pickupable> dst)
        {
            if (Components.Pickupables == null || worldId < 0 || worldId == 255)
            {
                return;
            }

            List<Pickupable> pickups = Components.Pickupables.GetWorldItems(worldId);
            if (pickups == null)
            {
                return;
            }

            for (int i = 0; i < pickups.Count; i++)
            {
                Pickupable p = pickups[i];
                if (p == null || p.gameObject == null || p.UnreservedFetchAmount <= 0f)
                {
                    continue;
                }

                // 如果物品在某个 Storage 内，检查是否允许取出（排除正在被消费的机器、以及运输装载机自身）
                if (p.storage != null)
                {
                    if (!p.storage.allowItemRemoval)
                    {
                        continue;
                    }
                    if (p.storage.GetComponent<SolidConduitInbox>() != null)
                    {
                        continue;
                    }
                }

                dst.Add(p);
            }
        }

        internal static void RefreshRocketInteriorIfNeeded(int worldId)
        {
            float now = Time.time;
            lock (_rocketRefreshTime)
            {
                if (_rocketRefreshTime.TryGetValue(worldId, out float lastTime) && now - lastTime < 1.0f)
                {
                    return;
                }
                _rocketRefreshTime[worldId] = now;
            }
            RefreshWorldBoxItems(worldId);
        }

        internal static void RefreshWorldBoxItems(int worldId)
        {
            _refreshScratch.Clear();

            GlobalItemCollector localJunk = GetActiveBoxForWorld(worldId, GlobalItemCollector.CollectMode.Debris);
            if (localJunk != null)
            {
                AppendStoragePickupables(localJunk.BoxStorage, _refreshScratch);
            }

            GlobalItemCollector localFood = GetActiveBoxForWorld(worldId, GlobalItemCollector.CollectMode.Edibles);
            if (localFood != null)
            {
                AppendStoragePickupables(localFood.BoxStorage, _refreshScratch);
            }

            // 火箭内部（IsModuleInterior）：自动收集舱内所有储物箱/冰箱/地面的物品，使机械手能隔空抓取舱内任意可用物品
            if (ClusterManager.Instance != null)
            {
                WorldContainer wc = ClusterManager.Instance.GetWorld(worldId);
                if (wc != null && wc.IsModuleInterior)
                {
                    AppendRocketInteriorItems(worldId, _refreshScratch);
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
        internal static float GetOtherBoxesBulkMass(GlobalItemCollector self, SimHashes element)
        {
            float total = 0f;
            for (int i = 0; i < Boxes.Count; i++)
            {
                GlobalItemCollector b = Boxes[i];
                if (b == null || b == self || b.gameObject == null || b.IsRedundant
                    || b.mode != GlobalItemCollector.CollectMode.Debris || b.BoxStorage == null || b.BoxStorage.items == null)
                {
                    continue;
                }
                List<GameObject> items = b.BoxStorage.items;
                for (int k = 0; k < items.Count; k++)
                {
                    GameObject go = items[k];
                    if (go == null)
                    {
                        continue;
                    }
                    Pickupable p = go.GetComponent<Pickupable>();
                    if (p != null && GlobalItemCollector.IsBulkSolidElementChunk(p) && p.PrimaryElement.ElementID == element)
                    {
                        total += p.PrimaryElement.Mass;
                    }
                }
            }
            return total;
        }

        private static float SumStorageAmountForTag(Storage storage, Tag tag)
        {
            if (storage == null || storage.items == null)
            {
                return 0f;
            }
            float sum = 0f;
            for (int i = 0; i < storage.items.Count; i++)
            {
                GameObject go = storage.items[i];
                if (go == null)
                {
                    continue;
                }
                KPrefabID id = go.GetComponent<KPrefabID>();
                Pickupable p = go.GetComponent<Pickupable>();
                if (id != null && p != null && id.HasTag(tag) && !id.HasTag(GameTags.StoredPrivate))
                {
                    sum += p.TotalAmount;
                }
            }
            return sum;
        }

        /// <summary>
        /// 供 WorldInventory.GetTotalAmount 补丁调用：当本星球建有杂物箱/食物箱时，
        /// 将其它星球生效聚合箱 Storage 内对应 tag 的存量计入本星球可用量，使全星系面板与选种/建造界面 100% 共享。
        /// </summary>
        internal static float GetSharedRemoteBoxMass(int worldId, int parentWorldId, Tag tag)
        {
            float sum = 0f;
            bool hasJunk = WorldHasActiveBox(worldId, GlobalItemCollector.CollectMode.Debris);
            bool hasFood = WorldHasActiveBox(worldId, GlobalItemCollector.CollectMode.Edibles);
            if (!hasJunk && !hasFood)
            {
                return 0f;
            }

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
                    sum += SumStorageAmountForTag(b.BoxStorage, tag);
                }
            }

            return sum;
        }

        private static void CollectMatchingFromStorage(Storage storage, Tag tag, ref List<Pickupable> added)
        {
            if (storage == null || storage.items == null)
            {
                return;
            }
            for (int i = 0; i < storage.items.Count; i++)
            {
                GameObject go = storage.items[i];
                if (go == null)
                {
                    continue;
                }
                KPrefabID id = go.GetComponent<KPrefabID>();
                Pickupable p = go.GetComponent<Pickupable>();
                if (id != null && p != null && id.HasTag(tag) && !id.HasTag(GameTags.StoredPrivate))
                {
                    if (added == null)
                    {
                        added = new List<Pickupable>();
                    }
                    added.Add(p);
                }
            }
        }

        /// <summary>
        /// 供 WorldInventory.GetPickupables 补丁调用：当本星球建有杂物箱/食物箱时，
        /// 将其它星球生效聚合箱 Storage 内匹配 tag 的 Pickupable 列表追加给本星球，使种植砖选种、孵化器选蛋、展台选神器、
        /// 卡路里统计等界面在所有星球 100% 共享全部物品。
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
                    CollectMatchingFromStorage(b.BoxStorage, tag, ref extra);
                }
            }

            if (extra == null || extra.Count == 0)
            {
                return;
            }

            List<Pickupable> combined = new List<Pickupable>((result != null ? result.Count : 0) + extra.Count);
            if (result != null)
            {
                foreach (Pickupable item in result)
                {
                    if (item != null)
                    {
                        combined.Add(item);
                    }
                }
            }
            for (int i = 0; i < extra.Count; i++)
            {
                Pickupable p = extra[i];
                if (!combined.Contains(p))
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

            GlobalItemCollector targetBox = GetActiveJunkBoxForWorld(targetWorldId);
            if (targetBox == null || targetBox.BoxStorage == null)
            {
                return false;
            }

            Pickupable p = itemGo.GetComponent<Pickupable>();
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
                if (!IsBoxStorage(p.storage))
                {
                    return false;
                }
                p.storage.Transfer(itemGo, targetBox.BoxStorage, block_events: false, hide_popups: true);
                RefreshWorldBoxItems(targetWorldId);
                return true;
            }

            int itemWorld = itemGo.GetMyWorldId();
            if (WorldHasActiveJunkBox(itemWorld))
            {
                targetBox.BoxStorage.Store(itemGo, hide_popups: true);
                RefreshWorldBoxItems(targetWorldId);
                return true;
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
