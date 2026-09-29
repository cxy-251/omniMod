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

        /// <summary>
        /// 是否已做过一次"补勾后来发现的资源"。老存档里箱子建好之后才发现的资源（冰、烹饪原料等）
        /// 可能从来没被勾上：游戏自带的 TreeFilterable.OnDiscover 只在"其它类别全勾"时才自动补勾，
        /// 而杂物箱默认故意不勾蛋/衣物，这个条件永远不成立。（V1 那次把烹饪原料类的种子漏了，
        /// 换个新字段让老存档再补一次。）
        /// </summary>
        [Serialize]
        private bool _backfillDiscoveredV2;

        private const float ScanIntervalSeconds = 2f;
        /// <summary>高于 100°C 的不收（烫手的留在原地），低温不限——冰之类照收。</summary>
        private const float MaxCollectTemperatureK = 373.15f;   // 100°C


        /// <summary>缓冲区里某元素超出目标这么多（kg）才退回池，避免抖动。</summary>
        private const float ReturnToPoolThresholdKg = 500f;

        /// <summary>缓冲区里每种元素的上限（kg）。箱子是池的窗口，这里只是防止单元素无限堆积。</summary>
        private const float BufferCapPerElementKg = 100_000f;

        /// <summary>"无疾病"的 disease 索引哨兵值。</summary>
        private const byte NoDisease = byte.MaxValue;

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

            EjectLivingThings();   // 兜底：任何时候都不允许活物留在箱子里

            // 隔空取物 = 把池里的材料"物化"成箱子里的真实碎块（按本星球实际搬运需求），
            // 之后由小人/机械臂/机器人从箱子正常取用、运进工地。中间有真实搬运者。
            if (!_isRedundant && mode == CollectMode.Debris)
            {
                RestockFromPool();
                ReleaseUncheckedDiscrete();
                StockBionicBatteries();

                // 离散物品（种子等）现在靠「隔空取物」补丁——「家」箱对全星球任意位置都算"就在旁边"，
                // 小人/机械臂直接从「家」箱取，不再需要 box→box 的调货搬运。
            }

            // 主线程刷新"本星球箱内物品"快照，供机械臂（工作线程）无视距离取用（在物化后刷新，保证同秒即见）
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

            if (!_isRedundant)
            {
                Collect();
            }
        }

        /// <summary>
        /// 箱子是全局杂物池的窗口：把池里的材料"物化"成缓冲区里的真实碎块，
        /// 镜像出池里该元素的全部存量（单元素封顶 <see cref="BufferCapPerElementKg"/>，
        /// 只是防止无限堆积，不是按需筛选）。多出来的退回池。
        /// 池与缓冲区质量互斥（RemoveSolid 是搬移不是复制），所以总量守恒，不会凭空多出材料。
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

            // 遍历池内容 + 缓冲区已有内容的并集（复用 scratch，避免每秒分配）
            _elementScratch.Clear();
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

                float inPool = pool.GetMass(element);
                float target = Mathf.Min(inPool + have, BufferCapPerElementKg);

                if (target > have + 1f)
                {
                    // 池里同种材料按温度分档存放。Storage.AddOre 会把新料和缓冲区已有的那块按质量混温，
                    // 所以缓冲区有料时只从"同一温度档"补；那一档没了就等小人把缓冲区这块用完，
                    // 缓冲区空了再从别的温度档补。（不要把缓冲区退回池再换档：池里只剩零碎小档时
                    // 会"物化→退回→再物化"来回倒腾。）
                    PrimaryElement chunk = have > 0f ? _storage.FindPrimaryElement(element) : null;
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
                        _storage.AddOre(element, taken, tempK, NoDisease, 0);
                        Debug.Log($"[OmniMod] 物化 {taken:0.#} kg {e.name} 进箱子");
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
            if (mode == CollectMode.Debris)
            {
                if (category == GameTags.Egg || category == GameTags.Clothes)
                {
                    return false;
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
            if (_backfillDiscoveredV2 || _treeFilterable == null || DiscoveredResources.Instance == null
                || _storage == null || _storage.storageFilters == null)
            {
                return;
            }
            _backfillDiscoveredV2 = true;

            var accepted = new HashSet<Tag>(_treeFilterable.GetTags());
            var categories = new List<Tag>(_storage.storageFilters);
            int added = 0;
            foreach (Tag category in categories)
            {
                foreach (Tag resource in DiscoveredResources.Instance.GetDiscoveredResourcesFromTag(category))
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

        /// <summary>每个仿生复制人在本星球箱子里常备几块充满电的电池。</summary>
        private const int BionicBatteriesPerDupe = 4;

        /// <summary>
        /// 仿生复制人"换电池"不走普通搬运任务（FetchChore），而是自己的传感器
        /// （ClosestElectrobankSensor）只在「本星球及对接火箭」的物品里找电池。离散物品统一存在
        /// 「家」箱，家箱在别的星球时，仿生人一块电池都看不到。所以：本星球有仿生人、而本箱不是
        /// 家箱时，从家箱调几块充满电、仿生人能用的电池过来常备在本箱。
        /// </summary>
        private void StockBionicBatteries()
        {
            GlobalItemCollector home = AggregateBoxRegistry.DiscreteHome;
            Storage homeStore = home != null ? home.BoxStorage : null;
            if (home == this || homeStore == null || homeStore.items == null || _storage == null)
            {
                return;
            }

            int myWorld = this.GetMyParentWorldId();
            int bionics = 0;
            foreach (MinionIdentity mi in Components.LiveMinionIdentities.Items)
            {
                if (mi != null && mi.GetMyParentWorldId() == myWorld && mi.HasTag(GameTags.Minions.Models.Bionic))
                {
                    bionics++;
                }
            }
            if (bionics == 0)
            {
                return;
            }

            int want = bionics * BionicBatteriesPerDupe;
            int have = 0;
            foreach (GameObject go in _storage.items)
            {
                if (IsUsableBionicBattery(go))
                {
                    have++;
                }
            }

            for (int i = homeStore.items.Count - 1; i >= 0 && have < want; i--)
            {
                GameObject go = homeStore.items[i];
                Pickupable p = go != null ? go.GetComponent<Pickupable>() : null;
                if (IsUsableBionicBattery(go) && p != null && p.ReservedAmount <= 0f)
                {
                    homeStore.Transfer(go, _storage, block_events: false, hide_popups: true);
                    have++;
                }
            }
        }

        private static bool IsUsableBionicBattery(GameObject go)
        {
            KPrefabID id = go != null ? go.GetComponent<KPrefabID>() : null;
            if (id == null || !id.HasTag(GameTags.ChargedPortableBattery))
            {
                return false;
            }
            foreach (Tag t in GameTags.BionicIncompatibleBatteries)
            {
                if (id.HasTag(t))
                {
                    return false;
                }
            }
            return true;
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
            // 高于 100°C 的不收，其余（含冰等低温物品）都收
            return pe != null && pe.Mass > 0f && pe.Temperature <= MaxCollectTemperatureK;
        }
    }
}
