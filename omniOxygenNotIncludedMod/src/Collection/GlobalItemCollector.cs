using System.Collections.Generic;
using KSerialization;
using OmniMod.Pool;
using UnityEngine;

namespace OmniMod.Collection
{
    /// <summary>
    /// 全星球物品收集器。挂在聚合箱（杂物箱 / 食物箱）上，
    /// 每隔几秒扫描"箱子所在星球"范围内散落在地面的物品，直接吸进主箱 Storage 或全局杂物池。
    ///
    /// 【全星系 100% 共享、绝不均分架构】
    /// - 散装固体材料归入 OmniPool，仅在「离散物品之家」（DiscreteHome，第一个生效的杂物箱）物化最多 1000 kg 常备缓冲；
    /// - 所有以"个"为单位的离散物品（增幅块、种子、移动电源电池、衣服/气压服、蛋、医药、神器等）100% 存放在 DiscreteHome.BoxStorage；
    /// - 所有食物与烹饪原料 100% 存放在「食物之家」（FoodHome，第一个生效的食物箱）的 BoxStorage；
    /// - 其它星球只要建有对应类型的聚合箱，即可通过 RemoteFetchPatch 与 WorldInventoryPatch 直接看到并隔空取用主箱中的全部物品，
    ///   不再按星球数量均分任何物品或材料！
    /// </summary>
    public class GlobalItemCollector : KMonoBehaviour, ISim1000ms
    {
        public enum CollectMode
        {
            /// <summary>收集带 Clearable 的固体碎片与离散物品（杂物箱用）。</summary>
            Debris,

            /// <summary>收集可食用物与烹饪原料（食物箱用）。</summary>
            Edibles,
        }

        /// <summary>收集模式。在建筑配置里设置到预制体上，实例化时随预制体克隆。</summary>
        public CollectMode mode = CollectMode.Debris;

        /// <summary>是否已根据当前已发现资源初始化过筛选集合（只做一次，之后玩家的勾选靠存档保存）。</summary>
        [Serialize]
        private bool _filterInitialized;

        /// <summary>
        /// 是否已做过一次"补勾后来发现的资源"。老存档里箱子建好之后才发现的资源（冰、烹饪原料等）
        /// 可能从来没被勾上：游戏自带的 TreeFilterable.OnDiscover 只在"其它类别全勾"时才自动补勾，
        /// 而杂物箱默认故意不勾蛋/衣物，这个条件永远不成立。（V1 那次把烹饪原料类的种子漏了，
        /// 换个新字段让老存档再补一次。）
        /// </summary>
        [Serialize]
        private bool _backfillDiscoveredV3;

        private const float ScanIntervalSeconds = 4f;
        /// <summary>高于 400°C 的不收（避免过热熔化箱体或物品），低温不限——冰之类照收。</summary>
        private const float MaxCollectTemperatureK = 673.15f;   // 400°C

        /// <summary>缓冲区里某元素超出目标这么多（kg）才退回池，避免抖动。</summary>
        private const float ReturnToPoolThresholdKg = 200f;

        /// <summary>主杂物箱（DiscreteHome）常备散装材料缓冲的目标质量（kg），供全星系所有建有杂物箱的星球直接隔空取用。</summary>
        private const float LocalBufferTargetPerElementKg = 1000f;

        /// <summary>"无疾病"的 disease 索引哨兵值。</summary>
        private const byte NoDisease = byte.MaxValue;

        private float _timer;
        private Storage _storage;
        private TreeFilterable _treeFilterable;

        /// <summary>本星球已有同类型箱子时，这个箱子不生效（只留兜底放生逻辑）。</summary>
        private bool _isRedundant;

        /// <summary>本星球已有同类型箱子、此箱不生效。</summary>
        internal bool IsRedundant => _isRedundant;

        /// <summary>本箱子的 Storage。</summary>
        internal Storage BoxStorage => _storage;

        private readonly List<GameObject> _bufferScratch = new List<GameObject>(16);
        private readonly List<Pickupable> _candidates = new List<Pickupable>(64);
        private readonly HashSet<SimHashes> _elementScratch = new HashSet<SimHashes>();
        private readonly Dictionary<SimHashes, float> _localBulkMass = new Dictionary<SimHashes, float>(64);
        private readonly Dictionary<SimHashes, PrimaryElement> _localBulkChunk = new Dictionary<SimHashes, PrimaryElement>(64);
        private readonly List<KeyValuePair<Tag, int>> _seedScratch = new List<KeyValuePair<Tag, int>>(32);
        private readonly List<GlobalItemCollector> _boxesScratch = new List<GlobalItemCollector>(8);
        private readonly HashSet<Tag> _foodTagsScratch = new HashSet<Tag>();

        protected override void OnSpawn()
        {
            base.OnSpawn();
            _storage = GetComponent<Storage>();
            _treeFilterable = GetComponent<TreeFilterable>();

            AggregateBoxRegistry.Register(this);
            _isRedundant = AggregateBoxRegistry.ExistsOnWorld(mode, this.GetMyWorldId(), except: this);
            if (_isRedundant)
            {
                Debug.LogWarning("[OmniMod] 本星球已有同类型聚合箱，此箱不生效（仅保留安全放生）。");
            }

            InitFilterIfNeeded();
            BackfillDiscoveredOnce();
            if (DiscoveredResources.Instance != null)
            {
                DiscoveredResources.Instance.OnDiscover += OnResourceDiscovered;
            }
            EjectLivingThings();
            InsulateStoredItems();
            MigrateOwnStorageToPool();
        }

        /// <summary>
        /// 箱内物品不跟箱子/环境交换热量（什么温度进去就什么温度出来）。新存进来的靠建筑配置里的
        /// Insulate 修饰；这里给老存档里已经在箱子里的物品补上。
        /// </summary>
        private void InsulateStoredItems()
        {
            if (_storage == null || _storage.items == null)
            {
                return;
            }
            foreach (GameObject go in _storage.items)
            {
                if (go != null)
                {
                    Storage.MakeItemTemperatureInsulated(go, is_stored: true, is_initializing: false);
                }
            }
        }

        /// <summary>
        /// 生成时把自己 Storage 里的散装固体元素迁移进全局杂物池、销毁 GameObject；
        /// 非散装（离散物品，含种子/增幅块/电池等）保留为真实 GameObject。
        /// </summary>
        private void MigrateOwnStorageToPool()
        {
            if (mode != CollectMode.Debris || OmniPool.Instance == null || _storage == null || _storage.items == null)
            {
                return;
            }

            for (int i = _storage.items.Count - 1; i >= 0; i--)
            {
                GameObject go = _storage.items[i];
                if (go == null)
                {
                    continue;
                }

                Pickupable p = go.GetComponent<Pickupable>();
                if (p != null && !IsLivingThing(go) && IsBulkSolidElementChunk(p))
                {
                    PrimaryElement pe = p.PrimaryElement;
                    OmniPool.Instance.AddSolid(pe.ElementID, pe.Mass, pe.Temperature);
                    _storage.Remove(go);
                    Util.KDestroyGameObject(go);
                }
            }
        }

        protected override void OnCleanUp()
        {
            if (DiscoveredResources.Instance != null)
            {
                DiscoveredResources.Instance.OnDiscover -= OnResourceDiscovered;
            }
            AggregateBoxRegistry.Unregister(this);
            base.OnCleanUp();
        }

        public void Sim1000ms(float dt)
        {
            if (_storage == null)
            {
                return;
            }

            if (!_isRedundant && mode == CollectMode.Debris)
            {
                // 每个星球的生效杂物箱都在自己箱内物化散装固体材料缓冲（最高 1000 kg/种），
                // 让本星球小人走去自己星球的箱子取材料，跨星球箱子绝不为空！
                RestockFromPool();

                if (AggregateBoxRegistry.DiscreteHome == this)
                {
                    MigratePoolSeedsToHomeStorage();
                    ReleaseUncheckedDiscrete();
                }
                else
                {
                    // 非主杂物箱：按本星球实际需求调拨离散物品（已分配增幅块/衣服、种植下单种子、仿生人电池等），
                    // 闲置未用完的离散物品归还 DiscreteHome，实现全星系共享且绝不均分！
                    ServeDiscreteDemand();
                    ReturnIdleDiscrete();
                }
            }
            else if (!_isRedundant && mode == CollectMode.Edibles)
            {
                ReleaseUncheckedFood();
                if (AggregateBoxRegistry.FoodHome == this)
                {
                    RebalanceFoodAcrossBoxes();
                }
            }

            // 主线程刷新"本星球自身聚合箱内物品"快照，供本星球机械臂无视距离隔空取用
            if (!_isRedundant)
            {
                AggregateBoxRegistry.RefreshWorldBoxItems(this.GetMyWorldId());
            }

            _timer += dt;
            if (_timer < ScanIntervalSeconds)
            {
                return;
            }
            _timer = 0f;

            EjectLivingThings();   // 兜底：任何时候都不允许活物留在箱子里

            if (!_isRedundant)
            {
                Collect();
            }
        }

        /// <summary>
        /// 兼容旧存档：如果 OmniPool 的旧种子池里还存有种子数据，一次性还原为真实种子实体存入主杂物箱（DiscreteHome），
        /// 这样全星系所有星球都能 100% 看到并共享全部种子，不再按每星球 2 颗拆分。
        /// </summary>
        private void MigratePoolSeedsToHomeStorage()
        {
            OmniPool pool = OmniPool.Instance;
            if (pool == null || _storage == null)
            {
                return;
            }

            _seedScratch.Clear();
            foreach (KeyValuePair<Tag, int> pair in pool.EnumerateSeeds())
            {
                if (pair.Value > 0)
                {
                    _seedScratch.Add(pair);
                }
            }
            if (_seedScratch.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _seedScratch.Count; i++)
            {
                Tag seedTag = _seedScratch[i].Key;
                int count = _seedScratch[i].Value;
                if (count <= 0 || !pool.RemoveSeed(seedTag, count))
                {
                    continue;
                }

                GameObject prefab = Assets.TryGetPrefab(seedTag);
                if (prefab == null)
                {
                    continue;
                }

                GameObject inst = GameUtil.KInstantiate(prefab, transform.GetPosition(), Grid.SceneLayer.Ore);
                inst.SetActive(true);
                PrimaryElement pe = inst.GetComponent<PrimaryElement>();
                if (pe != null && count > 1)
                {
                    pe.Units = count;
                }
                _storage.Store(inst, hide_popups: true);
            }
        }

        private static bool WorldHasLiveBionicMinion(int worldId)
        {
            if (worldId < 0 || worldId == 255 || Components.LiveMinionIdentities == null)
            {
                return false;
            }
            List<MinionIdentity> minions = Components.LiveMinionIdentities.GetWorldItems(worldId, checkChildWorlds: true);
            for (int i = 0; i < minions.Count; i++)
            {
                MinionIdentity m = minions[i];
                if (m != null && (m.model == BionicMinionConfig.MODEL || m.HasTag(GameTags.Minions.Models.Bionic)))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsStorageToStorageDestination(Storage dest)
        {
            if (dest == null)
            {
                return true;
            }
            return dest.GetComponent<StorageLocker>() != null ||
                   dest.GetComponent<ObjectDispenser>() != null ||
                   AggregateBoxRegistry.IsBoxStorage(dest);
        }

        /// <summary>
        /// 非主杂物箱：按需把本星球需要的离散物品调拨到本箱，供本星球小人走去本箱拾取（不隔空取物、不均分）：
        /// 1. 分配给本星球小人的增幅块（BionicUpgradeComponent）与衣物装备（Equippable）；
        /// 2. 若本星球有仿生人，常备最多 2 块充满电的移动电源（ChargedPortableBattery）；
        /// 3. 本星球当前活跃 FetchChore 所需的种子、蛋、医药、展台神器等离散物品。
        /// </summary>
        private void ServeDiscreteDemand()
        {
            if (_storage == null || _storage.items == null)
            {
                return;
            }

            int myWorld = this.GetMyWorldId();
            int myParentWorld = this.GetMyParentWorldId();
            if (myWorld < 0 || myWorld == 255)
            {
                return;
            }

            // 1. 调拨已分配给本星球小人的增幅块与衣物装备
            if (Components.AssignableItems != null)
            {
                List<Assignable> assignables = Components.AssignableItems.Items;
                for (int i = 0; i < assignables.Count; i++)
                {
                    Assignable a = assignables[i];
                    if (a == null || a.gameObject == null || !a.IsAssigned())
                    {
                        continue;
                    }
                    if (a is Equippable eq && eq.isEquipped)
                    {
                        continue;
                    }
                    if (a.HasTag(GameTags.StoredPrivate))
                    {
                        continue;
                    }
                    int targetWorld = AggregateBoxRegistry.GetAssigneeWorldId(a.assignee);
                    if (targetWorld == myWorld && a.GetMyWorldId() != myWorld)
                    {
                        AggregateBoxRegistry.TransferItemToWorldJunkBox(a.gameObject, myWorld);
                    }
                }
            }

            // 2. 若本星球有仿生人，按需保留最多 2 块便携电池供其去本星箱子换电
            if (WorldHasLiveBionicMinion(myWorld))
            {
                AggregateBoxRegistry.EnsureDiscreteTagInWorldJunkBox(GameTags.ChargedPortableBattery, Tag.Invalid, myWorld, 2f);
            }

            // 3. 响应本星球活跃的 FetchChore（种植砖选种、孵化器选蛋、制药台、展台等）
            if (GlobalChoreProvider.Instance == null ||
                !GlobalChoreProvider.Instance.fetchMap.TryGetValue(myParentWorld, out List<FetchChore> chores) ||
                chores == null)
            {
                return;
            }

            _boxesScratch.Clear();
            AggregateBoxRegistry.GetActiveBoxes(CollectMode.Debris, _boxesScratch);

            for (int cIdx = 0; cIdx < chores.Count; cIdx++)
            {
                FetchChore fc = chores[cIdx];
                if (fc == null || fc.destination == null || fc.fetchTarget != null || fc.driver != null || fc.amount <= 0f)
                {
                    continue;
                }
                if (IsStorageToStorageDestination(fc.destination))
                {
                    continue;
                }

                // 检查本箱是否已有满足该任务的未预留物品
                bool alreadyHave = false;
                for (int i = 0; i < _storage.items.Count; i++)
                {
                    GameObject localGo = _storage.items[i];
                    Pickupable localP = localGo != null ? localGo.GetComponent<Pickupable>() : null;
                    if (localP != null && localP.UnreservedFetchAmount > 0f && FetchManager.IsFetchablePickup(localP, fc, fc.destination))
                    {
                        alreadyHave = true;
                        break;
                    }
                }
                if (alreadyHave)
                {
                    continue;
                }

                // 从其它星球的杂物箱按需调拨 1 份匹配的离散物品到本箱
                bool fulfilled = false;
                for (int bIdx = 0; bIdx < _boxesScratch.Count && !fulfilled; bIdx++)
                {
                    GlobalItemCollector donor = _boxesScratch[bIdx];
                    if (donor == null || donor == this || donor.BoxStorage == null || donor.BoxStorage.items == null)
                    {
                        continue;
                    }

                    List<GameObject> donorItems = donor.BoxStorage.items;
                    for (int i = donorItems.Count - 1; i >= 0; i--)
                    {
                        GameObject go = donorItems[i];
                        if (go == null || !IsDiscreteManaged(go))
                        {
                            continue;
                        }
                        Pickupable p = go.GetComponent<Pickupable>();
                        if (p == null || p.ReservedAmount > 0f || p.UnreservedFetchAmount <= 0f)
                        {
                            continue;
                        }
                        Assignable assignable = go.GetComponent<Assignable>();
                        if (assignable != null && assignable.IsAssigned() && AggregateBoxRegistry.GetAssigneeWorldId(assignable.assignee) != myWorld)
                        {
                            continue;
                        }
                        if (!FetchManager.IsFetchablePickup(p, fc, fc.destination))
                        {
                            continue;
                        }

                        PrimaryElement pe = p.PrimaryElement;
                        float massPerUnit = pe != null && pe.MassPerUnit > 0f ? pe.MassPerUnit : 1f;
                        float unitsNeeded = Mathf.Max(1f, Mathf.Ceil(fc.amount / massPerUnit));
                        if (pe != null && pe.Units > unitsNeeded)
                        {
                            Pickupable split = p.TakeUnit(unitsNeeded);
                            if (split != null)
                            {
                                _storage.Store(split.gameObject, hide_popups: true);
                            }
                        }
                        else
                        {
                            donor.BoxStorage.Transfer(go, _storage, block_events: false, hide_popups: true);
                        }
                        fulfilled = true;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 非主杂物箱：将本星球不再需要的闲置离散物品归还主杂物箱（DiscreteHome），确保离散物品不均分。
        /// 已预留、已分配给本星小人、本星仿生人常备的 2 块电池、或本星 FetchChore 正在请求的离散物品留在本地箱内等待小人来取。
        /// </summary>
        private void ReturnIdleDiscrete()
        {
            if (_storage == null || _storage.items == null || _storage.items.Count == 0)
            {
                return;
            }

            GlobalItemCollector home = AggregateBoxRegistry.DiscreteHome;
            Storage homeStore = home != null && home != this ? home.BoxStorage : null;
            if (homeStore == null)
            {
                return;
            }

            int myWorld = this.GetMyWorldId();
            int myParentWorld = this.GetMyParentWorldId();
            bool hasBionic = WorldHasLiveBionicMinion(myWorld);
            float keptBatteries = 0f;

            List<FetchChore> chores = null;
            if (GlobalChoreProvider.Instance != null)
            {
                GlobalChoreProvider.Instance.fetchMap.TryGetValue(myParentWorld, out chores);
            }

            for (int i = _storage.items.Count - 1; i >= 0; i--)
            {
                GameObject go = _storage.items[i];
                if (go == null || !IsDiscreteManaged(go))
                {
                    continue;
                }

                Pickupable p = go.GetComponent<Pickupable>();
                KPrefabID id = go.GetComponent<KPrefabID>();
                if (p == null || id == null || p.ReservedAmount > 0f)
                {
                    continue;
                }

                // 1. 已分配给本星球小人的增幅块或装备：留在本地杂物箱等待小人走过来拿
                Assignable assignable = go.GetComponent<Assignable>();
                if (assignable != null && assignable.IsAssigned() && AggregateBoxRegistry.GetAssigneeWorldId(assignable.assignee) == myWorld)
                {
                    continue;
                }

                // 2. 本星球有仿生人时，保留最多 2 块便携电池
                if (hasBionic && id.HasTag(GameTags.ChargedPortableBattery) && keptBatteries < 2f)
                {
                    keptBatteries += p.PrimaryElement != null ? p.PrimaryElement.Units : 1f;
                    continue;
                }

                // 3. 本星球有活跃 FetchChore 需要该离散物品（如刚下单的种子/蛋）时，留在本地箱内
                bool wantedByChore = false;
                if (chores != null)
                {
                    for (int cIdx = 0; cIdx < chores.Count; cIdx++)
                    {
                        FetchChore fc = chores[cIdx];
                        if (fc != null && fc.destination != null && fc.driver == null && fc.amount > 0f
                            && !IsStorageToStorageDestination(fc.destination)
                            && FetchManager.IsFetchablePickup(p, fc, fc.destination))
                        {
                            wantedByChore = true;
                            break;
                        }
                    }
                }
                if (wantedByChore)
                {
                    continue;
                }

                _storage.Transfer(go, homeStore, block_events: false, hide_popups: true);
            }
        }

        private static float GetUnreservedTagMass(Storage storage, Tag prefabTag)
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
                if (id != null && p != null && p.ReservedAmount <= 0f && id.PrefabTag == prefabTag)
                {
                    sum += p.TotalAmount;
                }
            }
            return sum;
        }

        /// <summary>
        /// 协调所有生效的食物箱：将可拆分食物均衡调配到各个星球的食物箱中，确保跨星球的食物箱绝不为空，
        /// 让每个星球的小人都能走到自己星球的食物箱取食。
        /// </summary>
        private void RebalanceFoodAcrossBoxes()
        {
            _boxesScratch.Clear();
            AggregateBoxRegistry.GetActiveBoxes(CollectMode.Edibles, _boxesScratch);
            int boxCount = _boxesScratch.Count;
            if (boxCount <= 1)
            {
                return;
            }

            _foodTagsScratch.Clear();
            for (int b = 0; b < boxCount; b++)
            {
                Storage st = _boxesScratch[b].BoxStorage;
                if (st == null || st.items == null)
                {
                    continue;
                }
                for (int i = 0; i < st.items.Count; i++)
                {
                    GameObject go = st.items[i];
                    KPrefabID id = go != null ? go.GetComponent<KPrefabID>() : null;
                    if (id != null && !IsLivingThing(go))
                    {
                        _foodTagsScratch.Add(id.PrefabTag);
                    }
                }
            }

            foreach (Tag foodTag in _foodTagsScratch)
            {
                float totalMass = 0f;
                for (int b = 0; b < boxCount; b++)
                {
                    totalMass += GetUnreservedTagMass(_boxesScratch[b].BoxStorage, foodTag);
                }

                float targetPerBox = totalMass / boxCount;
                if (targetPerBox < 0.2f)
                {
                    continue;
                }

                for (int r = 0; r < boxCount; r++)
                {
                    Storage recipient = _boxesScratch[r].BoxStorage;
                    float rHave = GetUnreservedTagMass(recipient, foodTag);
                    if (rHave >= targetPerBox * 0.65f)
                    {
                        continue;
                    }

                    float need = targetPerBox - rHave;
                    for (int d = 0; d < boxCount && need > 0.05f; d++)
                    {
                        if (d == r)
                        {
                            continue;
                        }
                        Storage donor = _boxesScratch[d].BoxStorage;
                        float dHave = GetUnreservedTagMass(donor, foodTag);
                        float dSurplus = dHave - targetPerBox;
                        if (dSurplus <= 0.05f)
                        {
                            continue;
                        }

                        float toMove = Mathf.Min(need, dSurplus);
                        for (int i = donor.items.Count - 1; i >= 0 && toMove > 0.05f; i--)
                        {
                            GameObject go = donor.items[i];
                            if (go == null)
                            {
                                continue;
                            }
                            KPrefabID id = go.GetComponent<KPrefabID>();
                            Pickupable p = go.GetComponent<Pickupable>();
                            if (id == null || p == null || p.ReservedAmount > 0f || id.PrefabTag != foodTag)
                            {
                                continue;
                            }

                            float avail = p.TotalAmount;
                            if (avail > toMove + 0.05f)
                            {
                                Pickupable split = p.Take(toMove);
                                if (split != null)
                                {
                                    recipient.Store(split.gameObject, hide_popups: true);
                                    need -= toMove;
                                    toMove = 0f;
                                }
                            }
                            else
                            {
                                donor.Transfer(go, recipient, block_events: false, hide_popups: true);
                                need -= avail;
                                toMove -= avail;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>把食物箱里"已被取消勾选"的食物放回地面。</summary>
        private void ReleaseUncheckedFood()
        {
            if (_treeFilterable == null || _storage == null || _storage.items == null)
            {
                return;
            }

            for (int i = _storage.items.Count - 1; i >= 0; i--)
            {
                GameObject go = _storage.items[i];
                KPrefabID id = go != null ? go.GetComponent<KPrefabID>() : null;
                Pickupable p = go != null ? go.GetComponent<Pickupable>() : null;
                if (id != null && p != null && p.ReservedAmount <= 0f && !IsLivingThing(go) && !IsAcceptedByFilter(id))
                {
                    _storage.Drop(go);
                }
            }
        }

        /// <summary>
        /// 每个星球的生效杂物箱都在自己内部物化散装固体材料缓冲（单箱单元素封顶 <see cref="LocalBufferTargetPerElementKg"/>），
        /// 保证每个星球的杂物箱都有实物材料可供本星球小人走过来取用、本星球机械手隔空取用。
        /// </summary>
        private void RestockFromPool()
        {
            OmniPool pool = OmniPool.Instance;
            if (pool == null || _treeFilterable == null || _storage.items == null)
            {
                return;
            }

            int worldId = this.GetMyWorldId();
            if (worldId < 0)
            {
                return;
            }

            int boxCount = Mathf.Max(1, AggregateBoxRegistry.ActiveJunkBoxCount);
            bool isHome = AggregateBoxRegistry.DiscreteHome == this;

            // 单次遍历收集池内容 + 缓冲区已有散装块的质量与首块引用（避免 O(E*N) 反复遍历 Storage，且绝不误触同材质离散物品）
            _elementScratch.Clear();
            _localBulkMass.Clear();
            _localBulkChunk.Clear();

            foreach (OmniPool.PoolLine line in pool.EnumerateJunk())
            {
                _elementScratch.Add(line.Element);
            }
            for (int i = 0; i < _storage.items.Count; i++)
            {
                GameObject go = _storage.items[i];
                if (go == null)
                {
                    continue;
                }
                Pickupable p0 = go.GetComponent<Pickupable>();
                if (p0 != null && IsBulkSolidElementChunk(p0))   // 只管散装元素缓冲，别碰离散物品（金属移动电源/宇航服/种子/增幅块等）
                {
                    PrimaryElement pe = p0.PrimaryElement;
                    SimHashes elemId = pe.ElementID;
                    _elementScratch.Add(elemId);
                    _localBulkMass[elemId] = _localBulkMass.TryGetValue(elemId, out float m) ? m + pe.Mass : pe.Mass;
                    if (!_localBulkChunk.ContainsKey(elemId) && pe.Mass > 0.001f)
                    {
                        _localBulkChunk[elemId] = pe;
                    }
                }
            }

            foreach (SimHashes element in _elementScratch)
            {
                Element e = ElementLoader.FindElementByHash(element);
                if (e == null)
                {
                    continue;
                }

                Tag tag = e.tag;
                float have = _localBulkMass.TryGetValue(element, out float hm) ? hm : 0f;

                if (!_treeFilterable.ContainsTag(tag))
                {
                    // 已取消勾选：把缓冲区里的这种材料退回池
                    if (have > 0f)
                    {
                        MoveBufferElementToPool(element, have);
                        _localBulkMass[element] = 0f;
                    }
                    // 由主杂物箱把池里这种元素逐步吐到箱子脚下，让玩家能把它彻底拿出 OmniMod 系统
                    if (isHome)
                    {
                        DrainUncheckedElementToFloor(element, e);
                    }
                    continue;
                }

                float inPool = pool.GetMass(element);
                float target = Mathf.Min(inPool + have, LocalBufferTargetPerElementKg);
                if (boxCount > 1 && inPool + have < LocalBufferTargetPerElementKg)
                {
                    float otherHave = AggregateBoxRegistry.GetOtherBoxesBulkMass(this, element);
                    float fairShare = (inPool + have + otherHave) / boxCount;
                    target = Mathf.Min(fairShare, LocalBufferTargetPerElementKg);
                }

                if (target > have + 1f && inPool > 0.001f)
                {
                    // 池里同种材料按温度分档存放。缓冲区有料时只从"同一温度档"补；那一档没了就等小人把缓冲区这块用完，
                    // 缓冲区空了再从别的温度档补。
                    PrimaryElement chunk = _localBulkChunk.TryGetValue(element, out PrimaryElement peChunk) ? peChunk : null;
                    float taken;
                    float tempK;
                    if (chunk != null && chunk.Mass > 0.001f)
                    {
                        taken = pool.RemoveSolidNear(element, chunk.Temperature, target - have, out tempK);
                    }
                    else
                    {
                        taken = pool.RemoveSolid(element, target - have, out tempK);
                    }
                    if (taken > 0f)
                    {
                        if (chunk != null)
                        {
                            float finalTemp = GameUtil.GetFinalTemperature(chunk.Temperature, chunk.Mass, tempK, taken);
                            chunk.Mass += taken;
                            chunk.Temperature = finalTemp;
                            _storage.Trigger(-1697596308, chunk.gameObject);
                            _storage.OnStorageChange?.Invoke(chunk.gameObject);
                        }
                        else if (e.substance != null)
                        {
                            GameObject go = e.substance.SpawnResource(transform.GetPosition(), taken, tempK, NoDisease, 0, prevent_merge: true, forceTemperature: false, manual_activation: true);
                            Pickupable p = go.GetComponent<Pickupable>();
                            if (p != null)
                            {
                                p.prevent_absorb_until_stored = true;
                            }
                            e.substance.ActivateSubstanceGameObject(go, NoDisease, 0);
                            _storage.Store(go, hide_popups: true, block_events: false, do_disease_transfer: false);
                        }
                        _localBulkMass[element] = have + taken;
                    }
                }
                else if (have > target + ReturnToPoolThresholdKg ||
                         (boxCount > 1 && target < LocalBufferTargetPerElementKg - 1f && have > target + 2f))
                {
                    float ret = have - target;
                    MoveBufferElementToPool(element, ret);
                    _localBulkMass[element] = Mathf.Max(0f, have - ret);
                }
            }
        }

        /// <summary>每次扫描最多把这么多质量（kg）的"已取消勾选"元素从池里吐到箱子脚下。节流，避免一帧刷爆。</summary>
        private const float DrainUncheckedPerTickKg = 20000f;

        /// <summary>
        /// 某散装元素已被取消勾选：把它从全局杂物池里逐步吐成真实碎块、落在箱子脚下，
        /// 让玩家能把这种材料彻底拿出 OmniMod 系统。池里没有了就自然停。
        /// </summary>
        private void DrainUncheckedElementToFloor(SimHashes element, Element e)
        {
            OmniPool pool = OmniPool.Instance;
            if (pool == null || e == null || e.substance == null)
            {
                return;
            }

            float inPool = pool.GetMass(element);
            if (inPool <= 0.01f)
            {
                return;
            }

            float taken = pool.RemoveSolid(element, Mathf.Min(inPool, DrainUncheckedPerTickKg), out float tempK);
            if (taken <= 0f)
            {
                return;
            }

            e.substance.SpawnResource(transform.GetPosition(), taken, tempK, 0, 0);
            Debug.Log($"[OmniMod] 取消勾选 {e.name}：吐出 {taken:0.#} kg 到箱子脚下（池中剩 {inPool - taken:0.#} kg）");
        }

        /// <summary>把缓冲区里某元素的一部分质量退回全局池。</summary>
        private void MoveBufferElementToPool(SimHashes element, float amount)
        {
            OmniPool pool = OmniPool.Instance;
            if (pool == null || amount <= 0f)
            {
                return;
            }

            _bufferScratch.Clear();
            _bufferScratch.AddRange(_storage.items);

            float remaining = amount;
            foreach (GameObject go in _bufferScratch)
            {
                if (remaining <= 0f)
                {
                    break;
                }
                if (go == null)
                {
                    continue;
                }

                Pickupable p = go.GetComponent<Pickupable>();
                if (p == null || !IsBulkSolidElementChunk(p))
                {
                    continue;   // 别碰离散物品
                }

                PrimaryElement pe = go.GetComponent<PrimaryElement>();
                if (pe == null || pe.ElementID != element)
                {
                    continue;
                }

                float move = Mathf.Min(remaining, pe.Mass);
                pool.AddSolid(element, move, pe.Temperature);
                pe.Mass -= move;
                remaining -= move;

                if (pe.Mass <= 0.001f)
                {
                    _storage.Remove(go);
                    Util.KDestroyGameObject(go);
                }
            }
        }

        /// <summary>
        /// 首次生成时，把"当前已发现、且属于本箱子存储类别"的资源全部勾上，
        /// 让箱子默认收集所有已知固体。之后新发现的资源由 TreeFilterable.OnDiscover 自动补勾；
        /// 玩家手动取消的勾选会随存档保存，不会被这里覆盖。
        /// </summary>
        private void InitFilterIfNeeded()
        {
            if (_treeFilterable == null || _filterInitialized)
            {
                return;
            }
            _filterInitialized = true;

            // 默认勾上 storageFilters 里所有已发现的资源……
            var accepted = new HashSet<Tag>(_treeFilterable.GetTags());
            if (_storage != null && _storage.storageFilters != null && DiscoveredResources.Instance != null)
            {
                foreach (Tag category in _storage.storageFilters)
                {
                    foreach (Tag resource in DiscoveredResources.Instance.GetDiscoveredResourcesFromTag(category))
                    {
                        accepted.Add(resource);
                    }
                }

                // ……但蛋、衣物默认不勾（玩家想收再自己勾）
                if (mode == CollectMode.Debris)
                {
                    // 修正原版 BionicUpgrade 同时带有 MiscPickupable 导致被归入 MiscPickupable 类别的问题
                    foreach (Tag resource in new List<Tag>(DiscoveredResources.Instance.GetDiscoveredResourcesFromTag(GameTags.MiscPickupable)))
                    {
                        GameObject prefab = Assets.TryGetPrefab(resource);
                        if (prefab != null && prefab.HasTag(GameTags.BionicUpgrade))
                        {
                            DiscoveredResources.Instance.Discover(resource, GameTags.BionicUpgrade);
                            accepted.Add(resource);
                        }
                    }

                    foreach (Tag category in new[] { GameTags.Egg, GameTags.Clothes })
                    {
                        accepted.Remove(category);
                        foreach (Tag resource in DiscoveredResources.Instance.GetDiscoveredResourcesFromTag(category))
                        {
                            accepted.Remove(resource);
                        }
                    }
                }
            }
            _treeFilterable.UpdateFilters(accepted);
        }

        /// <summary>这个资源该不该默认进本箱筛选：属于本箱存储类别；杂物箱不默认收蛋/衣物；
        /// 不属于任何类别的种子（筛选界面里没有它）由杂物箱收。</summary>
        private bool ShouldAutoAccept(Tag category, Tag resource)
        {
            if (_storage == null || _storage.storageFilters == null)
            {
                return false;
            }
            GameObject prefab = Assets.TryGetPrefab(resource);
            KPrefabID pid = prefab != null ? prefab.GetComponent<KPrefabID>() : null;
            bool isSeed = pid != null && IsSeedItem(pid);
            bool isBionicUpgrade = pid != null && pid.HasTag(GameTags.BionicUpgrade);
            if (mode == CollectMode.Debris)
            {
                if (category == GameTags.Egg || category == GameTags.Clothes)
                {
                    return false;
                }
                if (isBionicUpgrade)
                {
                    DiscoveredResources.Instance?.Discover(resource, GameTags.BionicUpgrade);
                    return true;
                }
                return _storage.storageFilters.Contains(category) || (isSeed && !category.IsValid);
            }
            return _storage.storageFilters.Contains(category);
        }

        /// <summary>新发现资源时直接补勾（不走游戏"其它类别全勾"那套条件）。</summary>
        private void OnResourceDiscovered(Tag category, Tag resource)
        {
            if (_treeFilterable != null && ShouldAutoAccept(category, resource))
            {
                _treeFilterable.AddTagToFilter(resource);
            }
        }

        /// <summary>老存档一次性补勾：把所有已发现、本该收的资源勾上。</summary>
        private void BackfillDiscoveredOnce()
        {
            if (_backfillDiscoveredV3 || _treeFilterable == null || DiscoveredResources.Instance == null
                || _storage == null || _storage.storageFilters == null)
            {
                return;
            }
            _backfillDiscoveredV3 = true;

            var accepted = new HashSet<Tag>(_treeFilterable.GetTags());
            var categories = new List<Tag>(_storage.storageFilters);
            if (mode == CollectMode.Debris && !categories.Contains(GameTags.MiscPickupable))
            {
                categories.Add(GameTags.MiscPickupable);
            }
            int added = 0;
            foreach (Tag category in categories)
            {
                foreach (Tag resource in new List<Tag>(DiscoveredResources.Instance.GetDiscoveredResourcesFromTag(category)))
                {
                    if (!accepted.Contains(resource) && ShouldAutoAccept(category, resource))
                    {
                        accepted.Add(resource);
                        added++;
                    }
                }
            }
            if (added > 0)
            {
                _treeFilterable.UpdateFilters(accepted);
                Debug.Log($"[OmniMod] 聚合箱补勾了 {added} 种后来发现的资源");
            }
        }

        /// <summary>种子（含"兼作食物的种子"，如拟种）。</summary>
        private static bool IsSeedItem(KPrefabID id)
        {
            return id.HasTag(GameTags.Seed) || id.HasTag(GameTags.CropSeed);
        }

        private bool PassesFilter(Tag prefabTag)
        {
            return _treeFilterable == null || _treeFilterable.ContainsTag(prefabTag);
        }

        /// <summary>物品是否被本箱筛选接受——勾了它自己的资源 tag 或它所属类别 tag 都算。</summary>
        private bool IsAcceptedByFilter(KPrefabID id)
        {
            if (_treeFilterable == null || id == null)
            {
                return true;
            }
            if (_treeFilterable.ContainsTag(id.PrefabTag))
            {
                return true;
            }
            if (mode == CollectMode.Debris && id.HasTag(GameTags.BionicUpgrade))
            {
                // 原版增幅块同时带有 BionicUpgrade 与 MiscPickupable 两个类别标签，
                // DiscoveredResources.GetCategoryForEntity 可能因 HashSet 顺序返回 MiscPickupable，
                // 导致增幅块未注册到 BionicUpgrade 类别下。首次遇到时将其补注册并默认勾选。
                if (DiscoveredResources.Instance != null
                    && !DiscoveredResources.Instance.GetDiscoveredResourcesFromTag(GameTags.BionicUpgrade).Contains(id.PrefabTag))
                {
                    DiscoveredResources.Instance.Discover(id.PrefabTag, GameTags.BionicUpgrade);
                    _treeFilterable.AddTagToFilter(id.PrefabTag);
                    return true;
                }
                return _treeFilterable.ContainsTag(GameTags.BionicUpgrade);
            }
            Tag category = DiscoveredResources.GetCategoryForEntity(id);
            if (!category.IsValid)
            {
                // 不属于任何可勾选类别的种子，筛选界面里根本没有它、玩家也勾不了，由杂物箱照收，
                // 否则永远散落在地上。
                return mode == CollectMode.Debris && IsSeedItem(id);
            }
            return _treeFilterable.ContainsTag(category);
        }

        /// <summary>把箱子里任何"活的/有生命的"东西（小人、装袋小动物等）立刻放出来。</summary>
        private void EjectLivingThings()
        {
            if (_storage == null || _storage.items == null)
            {
                return;
            }

            for (int i = _storage.items.Count - 1; i >= 0; i--)
            {
                GameObject go = _storage.items[i];
                if (go == null)
                {
                    continue;
                }

                if (IsLivingThing(go))
                {
                    Debug.LogWarning($"[OmniMod] 从聚合箱中放出了不该被收进来的对象: {go.name}");
                    _storage.Drop(go);
                    continue;
                }

                PrimaryElement pe = go.GetComponent<PrimaryElement>();
                if (pe != null && pe.Element != null && (pe.Element.IsLiquid || pe.Element.IsGas))
                {
                    Debug.LogWarning($"[OmniMod] 从聚合箱中放出了瓶装流体: {go.name}");
                    _storage.Drop(go);
                }
            }
        }

        private static bool IsLivingThing(GameObject go)
        {
            KPrefabID id = go.GetComponent<KPrefabID>();
            if (id != null)
            {
                // 蛋 / 衣物 / 食物 / 种子 / 便携电池 / 增幅块 是安全的可收纳物品，无需后续 GetComponent 检查
                if (id.HasTag(GameTags.Egg) ||
                    id.HasTag(GameTags.Clothes) ||
                    id.HasTag(GameTags.Edible) ||
                    id.HasTag(GameTags.ChargedPortableBattery) ||
                    id.HasTag(GameTags.BionicUpgrade) ||
                    IsSeedItem(id))
                {
                    return false;
                }

                if (id.HasTag(GameTags.DupeBrain) ||
                    id.HasTag(GameTags.BaseMinion) ||
                    id.HasTag(GameTags.Creature) ||
                    id.HasTag(GameTags.BagableCreature))
                {
                    return true;
                }
            }

            Pickupable p = go.GetComponent<Pickupable>();
            if (p != null && IsBulkSolidElementChunk(p))
            {
                return false;
            }

            // 会移动 / 有身份的一律视为活物（小人、动物）
            return go.GetComponent<MinionIdentity>() != null ||
                   go.GetComponent<Navigator>() != null ||
                   go.GetComponent<Health>() != null;
        }

        private void Collect()
        {
            int worldId = this.GetMyWorldId();
            if (worldId < 0)
            {
                return;
            }

            _candidates.Clear();

            if (mode == CollectMode.Edibles)
            {
                foreach (Edible edible in Components.Edibles.WorldItemsEnumerate(worldId, false))
                {
                    if (edible == null)
                    {
                        continue;
                    }

                    Pickupable p = edible.GetComponent<Pickupable>();
                    if (IsLooseInertPickupable(p) && IsAcceptedByFilter(p.KPrefabID))
                    {
                        _candidates.Add(p);
                    }
                }

                // 0 卡路里的烹饪原料（海苔、拟种等）：游戏不给它 Edible 组件、只打 CookingIngredient 标签，
                // 上面的 Components.Edibles 扫不到；杂物箱的类别里又没有"烹饪原料"。单独扫一遍。
                // 这类东西大多会腐烂，正好放在防腐的食物箱里。
                foreach (Pickupable p in Components.Pickupables.WorldItemsEnumerate(worldId, false))
                {
                    KPrefabID id = p != null ? p.KPrefabID : null;
                    if (id != null && id.HasTag(GameTags.CookingIngredient) && !id.HasTag(GameTags.Edible)
                        && IsLooseInertPickupable(p) && IsAcceptedByFilter(id))
                    {
                        _candidates.Add(p);
                    }
                }
            }
            else
            {
                if (OmniPool.Instance == null)
                {
                    return;   // 全局杂物池还没就绪，本次跳过
                }

                foreach (Pickupable p in Components.Pickupables.WorldItemsEnumerate(worldId, false))
                {
                    if (!IsLooseInertPickupable(p))
                    {
                        continue;
                    }

                    KPrefabID id = p.KPrefabID;

                    // 食用物交给食物箱
                    if (id.HasTag(GameTags.Edible))
                    {
                        continue;
                    }

                    if (!IsAcceptedByFilter(id))
                    {
                        continue;
                    }

                    if (IsBulkSolidElementChunk(p))
                    {
                        // 散装固体：只认"精确资源勾选"（不走类别兜底），
                        // 和 RestockFromPool / DrainUncheckedElementToFloor 一致，
                        // 否则取消勾选后吐出的碎块会被按类别 tag 又收回去 → 死循环。
                        if (!PassesFilter(p.KPrefabID.PrefabTag))
                        {
                            continue;
                        }

                        _candidates.Add(p);
                    }
                    else
                    {
                        // 其它一切通过安全 + 筛选的固体（增幅块/种子/电池/神器/医疗/衣物/蛋…）当离散物品收
                        _candidates.Add(p);
                    }
                }
            }

            for (int i = 0; i < _candidates.Count; i++)
            {
                Pickupable p = _candidates[i];
                if (p == null || p.storage != null || p.ReservedAmount > 0f)
                {
                    continue;
                }

                if (mode == CollectMode.Debris && IsBulkSolidElementChunk(p))
                {
                    // 散装固体（散落在地面）→ 并入全局杂物池，销毁 GameObject
                    PrimaryElement pe = p.PrimaryElement;
                    OmniPool.Instance.AddSolid(pe.ElementID, pe.Mass, pe.Temperature);
                    Util.KDestroyGameObject(p.gameObject);
                }
                else if (mode == CollectMode.Debris)
                {
                    // 已分配给本星球小人的增幅块/衣物直接留在本地杂物箱等待小人走来取；
                    // 其它闲置离散物品（含种子、增幅块、便携电池、衣物、蛋、神器、医药等）统一收进 DiscreteHome，
                    // 按需调拨、绝不均分！
                    Assignable assignable = p.GetComponent<Assignable>();
                    if (assignable != null && assignable.IsAssigned()
                        && AggregateBoxRegistry.GetAssigneeWorldId(assignable.assignee) == worldId)
                    {
                        _storage.Store(p.gameObject, hide_popups: true);
                    }
                    else
                    {
                        GlobalItemCollector home = AggregateBoxRegistry.DiscreteHome;
                        Storage dest = (home != null ? home.BoxStorage : null) ?? _storage;
                        dest.Store(p.gameObject, hide_popups: true);
                    }
                }
                else
                {
                    // 食物箱：直接收进本星球食物箱，并由 RebalanceFoodAcrossBoxes 跨星球平衡库存，保证每个星球食物箱都有食物！
                    _storage.Store(p.gameObject, hide_popups: true);
                }
            }

            _candidates.Clear();
        }

        /// <summary>预制体本身就是一块元素（预制体 tag == 元素 tag）的固体碎片。</summary>
        internal static bool IsBulkSolidElementChunk(Pickupable p)
        {
            PrimaryElement pe = p.PrimaryElement;
            Element e = pe != null ? pe.Element : null;
            return e != null && e.IsSolid && p.KPrefabID != null && p.KPrefabID.PrefabTag == e.tag;
        }

        /// <summary>
        /// 这个已入库物品是不是"由我们当离散物品管理"的——即：不是散装元素缓冲块、
        /// 也不是活物。散装缓冲块归 RestockFromPool 管，不能在这里被当离散物品挪动。
        /// </summary>
        internal static bool IsDiscreteManaged(GameObject go)
        {
            if (go == null || IsLivingThing(go))
            {
                return false;
            }
            Pickupable p = go.GetComponent<Pickupable>();
            if (p == null || IsBulkSolidElementChunk(p))
            {
                return false;
            }
            PrimaryElement pe = p.PrimaryElement;
            if (pe != null && pe.Element != null && (pe.Element.IsLiquid || pe.Element.IsGas))
            {
                return false; // 瓶装液体/气体不进箱子，留在原地供机械手或小人取用
            }
            return true;
        }

        /// <summary>把「家」箱里"已被取消勾选"的离散物品放回地面。</summary>
        private void ReleaseUncheckedDiscrete()
        {
            if (AggregateBoxRegistry.DiscreteHome != this
                || _treeFilterable == null || _storage == null || _storage.items == null)
            {
                return;
            }

            for (int i = _storage.items.Count - 1; i >= 0; i--)
            {
                GameObject go = _storage.items[i];
                KPrefabID id = go != null ? go.GetComponent<KPrefabID>() : null;
                Pickupable p = go != null ? go.GetComponent<Pickupable>() : null;
                if (id != null && p != null && p.ReservedAmount <= 0f && IsDiscreteManaged(go) && !IsAcceptedByFilter(id))
                {
                    _storage.Drop(go);
                }
            }
        }

        /// <summary>
        /// 物品是否是"散落在地面、可安全吸取的惰性碎片"。
        /// 关键正向门槛：必须带 Clearable 组件且 isClearable（清扫工具认可的碎片）；
        /// 关键排除：任何带 Health / Navigator / MinionIdentity 的对象一律不碰。
        /// </summary>
        private static bool IsLooseInertPickupable(Pickupable p)
        {
            if (p == null || p.storage != null)
            {
                return false;   // 已在存储里，或正被搬运（O(1) 指针判断，极速剔除绝大多数物品）
            }

            KPrefabID id = p.KPrefabID;
            if (id == null || id.HasTag(GameTags.Stored) || p.ReservedAmount > 0f)
            {
                return false;
            }

            // —— 极速排除生命/移动体（小人、动物、装袋动物、脑体）——
            // 先通过 Tag 快速拦截，避免调用极慢的 Unity GetComponent
            if (id.HasTag(GameTags.Creature) ||
                id.HasTag(GameTags.BaseMinion) ||
                id.HasTag(GameTags.DupeBrain) ||
                id.HasTag(GameTags.BagableCreature))
            {
                return false;
            }

            PrimaryElement pe = p.PrimaryElement;
            // 高于 400°C 的不收，其余（含冰等低温物品）都收
            if (pe == null || pe.Mass <= 0f || pe.Temperature > MaxCollectTemperatureK)
            {
                return false;
            }

            Element e = pe.Element;
            // 瓶装水/瓶装液体/气体绝不进箱子，留在原地供机械手或小人取用！
            if (e != null && (e.IsLiquid || e.IsGas))
            {
                return false;
            }

            // 绝大多数地面杂物都是矿石/冰等散装固体碎片（PrefabTag == e.tag）。
            // 这类碎片绝无可能具有生命/导航/小人组件，直接放行，跳过 4 次极昂贵的 GetComponent！
            if (e != null && e.IsSolid && id.PrefabTag == e.tag)
            {
                Clearable clearableOre = p.GetComponent<Clearable>();
                return clearableOre == null || clearableOre.isClearable;
            }

            // 离散物品（蛋、衣物、装备、医疗、种子等）走常规安全检查
            bool safeDiscrete = id.HasTag(GameTags.Egg) || id.HasTag(GameTags.Clothes);
            if ((p.GetComponent<Health>() != null && !safeDiscrete) ||
                p.GetComponent<Navigator>() != null ||
                p.GetComponent<MinionIdentity>() != null)
            {
                return false;
            }

            // 游戏明确标记"别自动搬我"的（Clearable.isClearable == false）→ 跳过；
            // 没有 Clearable 组件的（很多离散物品如科技组件/医疗物资）仍然收——靠上面的硬排除保命。
            Clearable clearable = p.GetComponent<Clearable>();
            return clearable == null || clearable.isClearable;
        }
    }
}
