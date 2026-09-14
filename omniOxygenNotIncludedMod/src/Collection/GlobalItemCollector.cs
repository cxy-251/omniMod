using System.Collections.Generic;
using KSerialization;
using OmniMod.Pool;
using UnityEngine;

namespace OmniMod.Collection
{
    /// <summary>
    /// 全星球物品收集器。挂在聚合箱（杂物箱 / 食物箱）上，
    /// 每隔几秒扫描"箱子所在星球"范围内散落在地面的物品，直接吸进箱子的 Storage。
    ///
    /// 【安全教训】小人（Duplicant）身上也挂着 Pickupable + PrimaryElement 组件！
    /// 早期版本用"是可拾取的固体"这种宽松判断，结果把小人也收进了箱子（拆箱=殉爆）。
    /// 现在改成严格白名单：必须是带 Clearable（清扫工具认可的碎片）、且没有 Health /
    /// Navigator / MinionIdentity 的东西才收。并且每次扫描前先把箱子里任何"活物"放出来兜底。
    ///
    /// 其它要点：
    /// - 实现 ISim1000ms，游戏每 ~1 秒自动回调，无需手动注册定时器。
    /// - 直接 Storage.Store（不派小人搬），跟自动清扫机同理。
    /// - 必须先收集候选到 List、枚举结束后再 Store：Store 会改动 Components 全局列表。
    /// - 收集受箱子的资源筛选（TreeFilterable）控制：没勾选的类别不收；取消勾选时
    ///   TreeFilterable 自带 dropIncorrectOnFilterChange，会把已存的该类物品倒到箱子脚下。
    /// - 极端温度固体（岩浆、熔融金属、极低温固体）默认不收，避免取出时炸锅。
    /// </summary>
    public class GlobalItemCollector : KMonoBehaviour, ISim1000ms
    {
        public enum CollectMode
        {
            /// <summary>收集带 Clearable 的固体碎片（杂物箱用）。</summary>
            Debris,

            /// <summary>收集可食用物（食物箱用）。</summary>
            Edibles,
        }

        /// <summary>收集模式。在建筑配置里设置到预制体上，实例化时随预制体克隆。</summary>
        public CollectMode mode = CollectMode.Debris;

        /// <summary>是否已根据当前已发现资源初始化过筛选集合（只做一次，之后玩家的勾选靠存档保存）。</summary>
        [Serialize]
        private bool _filterInitialized;

        private const float ScanIntervalSeconds = 2f;
        private const float MaxCollectTemperatureK = 673.15f;   // 400°C
        private const float MinCollectTemperatureK = 173.15f;   // -100°C

        /// <summary>补货时在"本星球需求量"之上多备的余量（kg），避免刚好卡在边界反复搬运。</summary>
        private const float RestockHeadroomKg = 200f;

        /// <summary>缓冲区里某元素超出目标这么多（kg）才退回池，避免抖动。</summary>
        private const float ReturnToPoolThresholdKg = 500f;

        /// <summary>缓冲区里每种元素的上限（kg）。单箱不囤太多，剩下的留在池里。</summary>
        private const float BufferCapPerElementKg = 100_000f;

        /// <summary>"无疾病"的 disease 索引哨兵值。</summary>
        private const byte NoDisease = byte.MaxValue;

        /// <summary>
        /// 会驱动"从池里物化到箱子"的消耗型搬运任务类型（建造、制造、发电补料、机器上料、维修、研究、农业、医疗）。
        /// 不含 StorageFetch / Fetch（往储物柜里塞东西的），避免把池子倒进一堆箱子。
        /// </summary>
        private static readonly HashSet<string> ConsumerFetchTypeIds = new HashSet<string>
        {
            "BuildFetch", "FabricateFetch", "PowerFetch", "MachineFetch",
            "RepairFetch", "ResearchFetch", "FarmFetch", "DoctorFetch",
        };

        private float _timer;
        private Storage _storage;
        private TreeFilterable _treeFilterable;

        /// <summary>本星球已有同类型箱子时，这个箱子不生效（只留兜底放生逻辑）。</summary>
        private bool _isRedundant;

        /// <summary>本星球已有同类型箱子、此箱不生效。</summary>
        internal bool IsRedundant => _isRedundant;

        /// <summary>本箱子的 Storage（离散物品之家用来存放共享的种子/电池等）。</summary>
        internal Storage BoxStorage => _storage;

        private readonly List<GameObject> _bufferScratch = new List<GameObject>(16);
        private readonly List<Pickupable> _candidates = new List<Pickupable>(64);
        private readonly HashSet<SimHashes> _elementScratch = new HashSet<SimHashes>();

        /// <summary>每次补货时重算的"本星球各元素还差多少"（key = (int)SimHashes）。</summary>
        private readonly Dictionary<int, float> _demandScratch = new Dictionary<int, float>();

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
            EjectLivingThings();
            MigrateOwnStorageToPool();
        }

        /// <summary>
        /// 旧版本里杂物箱把碎片存进了自己的 Storage。3b 起碎片统一进全局杂物池，
        /// 所以生成时把自己 Storage 里的散装固体元素迁移进池、销毁 GameObject；
        /// 非散装（离散物品）留在原地不动。
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
                if (p != null && IsBulkSolidElementChunk(p) && !IsLivingThing(go))
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
            AggregateBoxRegistry.Unregister(this);
            base.OnCleanUp();
        }

        public void Sim1000ms(float dt)
        {
            if (_storage == null)
            {
                return;
            }

            EjectLivingThings();   // 兜底：任何时候都不允许活物留在箱子里

            // 主线程刷新"本星球箱内物品"快照，供机械臂（工作线程）无视距离取用
            if (!_isRedundant)
            {
                AggregateBoxRegistry.RefreshWorldBoxItems(this.GetMyWorldId());
            }

            // 隔空取物 = 把池里的材料"物化"成箱子里的真实碎块（按本星球实际搬运需求），
            // 之后由小人/机械臂/机器人从箱子正常取用、运进工地。中间有真实搬运者。
            if (!_isRedundant && mode == CollectMode.Debris)
            {
                RestockFromPool();
                ReleaseUncheckedDiscrete();

                // 离散物品（种子等）现在靠「隔空取物」补丁——「家」箱对全星球任意位置都算"就在旁边"，
                // 小人/机械臂直接从「家」箱取，不再需要 box→box 的调货搬运。
            }

            _timer += dt;
            if (_timer < ScanIntervalSeconds)
            {
                return;
            }
            _timer = 0f;

            if (!_isRedundant)
            {
                Collect();
            }
        }

        /// <summary>
        /// 把全局杂物池里的材料"物化"成缓冲区里的真实碎块，数量按本星球当前需求 + 常备量，
        /// 每种元素封顶。多出来的退回池。池与缓冲区质量互斥（RemoveSolid 是搬移不是复制），
        /// 所以总量守恒，不会凭空多出材料。
        /// </summary>
        private void RestockFromPool()
        {
            OmniPool pool = OmniPool.Instance;
            if (pool == null || _treeFilterable == null)
            {
                return;
            }

            int worldId = this.GetMyWorldId();
            if (worldId < 0)
            {
                return;
            }

            ComputeBulkDemand();

            // 遍历池内容 + 缓冲区已有内容 + 有需求的元素的并集（复用 scratch，避免每秒分配）
            _elementScratch.Clear();
            foreach (KeyValuePair<int, float> kv in _demandScratch)
            {
                _elementScratch.Add((SimHashes)kv.Key);
            }
            foreach (OmniPool.PoolLine line in pool.EnumerateJunk())
            {
                _elementScratch.Add(line.Element);
            }
            foreach (GameObject go in _storage.items)
            {
                Pickupable p0 = go != null ? go.GetComponent<Pickupable>() : null;
                if (p0 != null && IsBulkSolidElementChunk(p0))   // 只管散装元素缓冲，别碰离散物品（种子等）
                {
                    _elementScratch.Add(p0.PrimaryElement.ElementID);
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
                float have = _storage.GetMassAvailable(element);

                if (!_treeFilterable.ContainsTag(tag))
                {
                    // 已取消勾选：把缓冲区里的这种材料退回池
                    if (have > 0f)
                    {
                        MoveBufferElementToPool(element, have);
                    }
                    // 再把池里这种元素逐步吐到箱子脚下，让玩家能把它彻底拿出 OmniMod 系统
                    DrainUncheckedElementToFloor(element, e);
                    continue;
                }

                // 按需物化：本星球有实际搬运需求才把材料搬进缓冲区；没需求就清空（材料以纯数据留在池里）
                _demandScratch.TryGetValue((int)element, out float need);
                float inPool = pool.GetMass(element);
                float target = need > 0f
                    ? Mathf.Min(inPool + have, Mathf.Min(need + RestockHeadroomKg, BufferCapPerElementKg))
                    : 0f;

                if (target > have + 1f)
                {
                    float taken = pool.RemoveSolid(element, target - have, out float tempK);
                    if (taken > 0f)
                    {
                        _storage.AddOre(element, taken, tempK, NoDisease, 0);
                        Debug.Log($"[OmniMod] 物化 {taken:0.#} kg {e.name} 进箱子（本星球需求 {need:0.#} kg）");
                    }
                }
                else if (have > target + ReturnToPoolThresholdKg)
                {
                    MoveBufferElementToPool(element, have - target);
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
            Tag category = DiscoveredResources.GetCategoryForEntity(id);
            return category.IsValid && _treeFilterable.ContainsTag(category);
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
                }
            }
        }

        private static bool IsLivingThing(GameObject go)
        {
            KPrefabID id = go.GetComponent<KPrefabID>();

            // 蛋 / 衣物是安全的可收纳离散物品（它们可能带 Health 组件，但不是"活物"）
            if (id != null && (id.HasTag(GameTags.Egg) || id.HasTag(GameTags.Clothes)))
            {
                return false;
            }

            // 会移动 / 有身份的一律视为活物（小人、动物）
            if (go.GetComponent<MinionIdentity>() != null ||
                go.GetComponent<Navigator>() != null ||
                go.GetComponent<Health>() != null)
            {
                return true;
            }

            return id != null &&
                   (id.HasTag(GameTags.DupeBrain) ||
                    id.HasTag(GameTags.BaseMinion) ||
                    id.HasTag(GameTags.Creature) ||
                    id.HasTag(GameTags.BagableCreature));
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

                        // 极端温度跳过
                        PrimaryElement pe = p.PrimaryElement;
                        if (pe.Temperature > MaxCollectTemperatureK || pe.Temperature < MinCollectTemperatureK)
                        {
                            continue;
                        }
                        _candidates.Add(p);
                    }
                    else
                    {
                        // 其它一切通过安全 + 筛选的固体（种子/电池/神器/医疗/衣物…）当离散物品收
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
                    // 离散物品（种子/电池/可选蛋衣物）→ 收进「离散物品之家」箱子的 Storage，实现星系共享
                    GlobalItemCollector home = AggregateBoxRegistry.DiscreteHome;
                    Storage dest = (home != null ? home.BoxStorage : null) ?? _storage;
                    dest.Store(p.gameObject, hide_popups: true);
                }
                else
                {
                    // 食物箱：作为真实物品收进自己的 Storage
                    _storage.Store(p.gameObject, hide_popups: true);
                }
            }

            _candidates.Clear();
        }

        /// <summary>
        /// 扫描本星球所有消耗型搬运任务（建造/制造/补料/上料/维修/研究/农业/医疗），
        /// 算出每种散装固体元素"还差多少还没到位"，填进 <see cref="_demandScratch"/>。
        /// RestockFromPool 据此把材料从池里物化进箱子缓冲区。
        /// </summary>
        private void ComputeBulkDemand()
        {
            _demandScratch.Clear();

            GlobalChoreProvider gcp = GlobalChoreProvider.Instance;
            if (gcp == null
                || !gcp.fetchMap.TryGetValue(this.GetMyParentWorldId(), out List<FetchChore> fetches)
                || fetches == null)
            {
                return;
            }

            for (int i = 0; i < fetches.Count; i++)
            {
                FetchChore fc = fetches[i];
                if (fc == null || fc.destination == null || fc.tags == null)
                {
                    continue;
                }
                if (fc.choreType == null || !ConsumerFetchTypeIds.Contains(fc.choreType.Id))
                {
                    continue;
                }

                foreach (Tag want in fc.tags)
                {
                    Element e = ElementLoader.GetElement(want);
                    if (e == null || !e.IsSolid)
                    {
                        continue;
                    }
                    float remaining = fc.originalAmount - fc.destination.GetMassAvailable(want);
                    if (remaining <= 0f)
                    {
                        continue;
                    }
                    _demandScratch.TryGetValue((int)e.id, out float d);
                    _demandScratch[(int)e.id] = d + remaining;
                }
            }
        }

        /// <summary>在一个 Storage 里找一件带指定 tag、未被预定的物品。</summary>
        private static GameObject FindUnreservedInStore(Storage store, Tag tag)
        {
            if (store == null || store.items == null)
            {
                return null;
            }

            for (int i = 0; i < store.items.Count; i++)
            {
                GameObject go = store.items[i];
                if (go == null)
                {
                    continue;
                }

                KPrefabID id = go.GetComponent<KPrefabID>();
                Pickupable p = go.GetComponent<Pickupable>();
                if (id != null && p != null && id.HasTag(tag) && p.ReservedAmount <= 0f)
                {
                    return go;
                }
            }

            return null;
        }

        /// <summary>非「家」箱：按本星球未满足的搬运需求，把匹配的离散物品从「家」箱调过来。</summary>
        private void ServeDiscreteDemand()
        {
            GlobalItemCollector home = AggregateBoxRegistry.DiscreteHome;
            Storage homeStore = home != null ? home.BoxStorage : null;
            if (homeStore == null || homeStore.items == null || homeStore.items.Count == 0)
            {
                return;
            }

            GlobalChoreProvider gcp = GlobalChoreProvider.Instance;
            if (gcp == null || !gcp.fetchMap.TryGetValue(this.GetMyParentWorldId(), out List<FetchChore> fetches))
            {
                return;
            }

            foreach (FetchChore fc in fetches)
            {
                if (fc == null || fc.fetchTarget != null || fc.tags == null)
                {
                    continue;   // 无效 / 已经找到供应源了
                }

                foreach (Tag want in fc.tags)
                {
                    GameObject match = FindUnreservedInStore(homeStore, want);
                    if (match != null)
                    {
                        homeStore.Transfer(match, _storage, block_events: false, hide_popups: true);
                        break;
                    }
                }
            }
        }

        /// <summary>非「家」箱：本星球已没有对应需求、且未被预定的离散物品，送回「家」箱。</summary>
        private void ReturnIdleDiscrete()
        {
            GlobalItemCollector home = AggregateBoxRegistry.DiscreteHome;
            Storage homeStore = home != null ? home.BoxStorage : null;
            if (homeStore == null || _storage == null || _storage.items == null)
            {
                return;
            }

            List<FetchChore> fetches = null;
            GlobalChoreProvider.Instance?.fetchMap.TryGetValue(this.GetMyParentWorldId(), out fetches);

            for (int i = _storage.items.Count - 1; i >= 0; i--)
            {
                GameObject go = _storage.items[i];
                KPrefabID id = go != null ? go.GetComponent<KPrefabID>() : null;
                Pickupable p = go != null ? go.GetComponent<Pickupable>() : null;
                if (id == null || p == null || !IsDiscreteManaged(go) || p.ReservedAmount > 0f)
                {
                    continue;
                }

                bool wanted = false;
                if (fetches != null)
                {
                    foreach (FetchChore fc in fetches)
                    {
                        if (fc != null && fc.fetchTarget == null && fc.tags != null && fc.tags.Contains(id.PrefabTag))
                        {
                            wanted = true;
                            break;
                        }
                    }
                }

                if (!wanted)
                {
                    _storage.Transfer(go, homeStore, block_events: false, hide_popups: true);
                }
            }
        }

        /// <summary>预制体本身就是一块元素（预制体 tag == 元素 tag）的固体碎片。</summary>
        private static bool IsBulkSolidElementChunk(Pickupable p)
        {
            PrimaryElement pe = p.PrimaryElement;
            Element e = pe != null ? pe.Element : null;
            return e != null && e.IsSolid && p.KPrefabID != null && p.KPrefabID.PrefabTag == e.tag;
        }

        /// <summary>
        /// 这个已入库物品是不是"由我们当离散物品管理"的——即：不是散装元素缓冲块、
        /// 也不是活物。散装缓冲块归 RestockFromPool 管，不能在这里被当离散物品挪动。
        /// </summary>
        private static bool IsDiscreteManaged(GameObject go)
        {
            if (go == null || IsLivingThing(go))
            {
                return false;
            }
            Pickupable p = go.GetComponent<Pickupable>();
            return p != null && !IsBulkSolidElementChunk(p);
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
                if (id != null && IsDiscreteManaged(go) && !IsAcceptedByFilter(id))
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
                return false;   // 已在存储里，或正被搬运
            }

            KPrefabID id = p.KPrefabID;
            if (id == null || id.HasTag(GameTags.Stored))
            {
                return false;
            }

            if (p.ReservedAmount > 0f)
            {
                return false;   // 已被某个任务预定
            }

            // 蛋 / 衣物：安全的离散物品（可能带 Health），放行；仍受下面移动体/身份的硬排除保护
            bool safeDiscrete = id.HasTag(GameTags.Egg) || id.HasTag(GameTags.Clothes);

            // —— 生命/移动体一律排除（小人、动物、装袋动物）——
            if ((p.GetComponent<Health>() != null && !safeDiscrete) ||
                p.GetComponent<Navigator>() != null ||
                p.GetComponent<MinionIdentity>() != null ||
                id.HasTag(GameTags.DupeBrain) ||
                id.HasTag(GameTags.BaseMinion) ||
                id.HasTag(GameTags.Creature) ||
                id.HasTag(GameTags.BagableCreature))
            {
                return false;
            }

            // 游戏明确标记"别自动搬我"的（Clearable.isClearable == false）→ 跳过；
            // 没有 Clearable 组件的（很多离散物品如科技组件/医疗物资）仍然收——靠上面的硬排除保命。
            Clearable clearable = p.GetComponent<Clearable>();
            if (clearable != null && !clearable.isClearable)
            {
                return false;
            }

            PrimaryElement pe = p.PrimaryElement;
            return pe != null && pe.Mass > 0f;
        }
    }
}
