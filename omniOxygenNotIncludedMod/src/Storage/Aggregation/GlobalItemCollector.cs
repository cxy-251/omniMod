using System.Collections.Generic;
using KSerialization;
using OmniMod.Core.Logging;
using OmniMod.Core.Safety;
using OmniMod.Pool;
using UnityEngine;

namespace OmniMod.Collection
{
    /// <summary>
    /// 全星球聚合箱物品收集调度器：
    /// 挂在杂物箱和食物箱上，协调散落物地面扫描、虚拟池缓冲、离散物品按需跨星调度及食物均衡。
    /// </summary>
    public class GlobalItemCollector : KMonoBehaviour, ISim1000ms
    {
        public enum CollectMode
        {
            Debris,
            Edibles,
        }

        public CollectMode mode = CollectMode.Debris;

        [Serialize]
        private bool _filterInitialized;

        [Serialize]
        private bool _backfillDiscoveredV3;

        private const float ScanIntervalSeconds = 4f;
        private const float MaxCollectTemperatureK = 673.15f; // 400°C

        private float _timer;
        private Storage _storage;
        private TreeFilterable _treeFilterable;
        private bool _isRedundant;

        internal bool IsRedundant => _isRedundant;
        public Storage BoxStorage => _storage;

        private readonly List<Pickupable> _candidates = new List<Pickupable>(64);

        protected override void OnSpawn()
        {
            base.OnSpawn();
            _storage = GetComponent<Storage>();
            _treeFilterable = GetComponent<TreeFilterable>();

            AggregateBoxRegistry.Register(this);
            _isRedundant = AggregateBoxRegistry.ExistsOnWorld(mode, this.GetMyWorldId(), except: this);
            if (_isRedundant)
            {
                ModLog.Warn("本星球已有同类型聚合箱，此箱不生效（仅保留安全放生）。");
            }

            BulkBufferManager.InsulateStoredItems(_storage);
            BulkBufferManager.MigrateOwnStorageToPool(this, _storage);

            FilterAutoManager.InitFilterIfNeeded(this, _treeFilterable, _storage, ref _filterInitialized);
            FilterAutoManager.BackfillDiscoveredOnce(this, _treeFilterable, _storage, ref _backfillDiscoveredV3);
            FilterAutoManager.SyncDiscoveredResources(this, _treeFilterable, _storage);

            if (DiscoveredResources.Instance != null)
            {
                DiscoveredResources.Instance.OnDiscover += OnResourceDiscovered;
            }

            LivingThingsSafety.EjectLivingThings(this);
            AggregateBoxRegistry.RefreshWorldBoxItems(this.GetMyWorldId());
        }

        private void OnResourceDiscovered(Tag category, Tag resource)
        {
            FilterAutoManager.OnResourceDiscovered(this, _treeFilterable, _storage, category, resource);
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
            LivingThingsSafety.EjectLivingThings(this);
            if (_isRedundant) return;

            // 每秒刷新本星球聚合箱内物品快照，供本星球机械手隔空取物 O(1) 匹配使用
            AggregateBoxRegistry.RefreshWorldBoxItems(this.GetMyWorldId());

            _timer += dt;

            ExceptionShield.Execute("GlobalItemCollector.Sim1000ms", () =>
            {
                if (mode == CollectMode.Debris)
                {
                    if (AggregateBoxRegistry.DiscreteHome == this)
                    {
                        DiscreteDemandManager.MigratePoolSeedsToHomeStorage(this, _storage);
                        FilterAutoManager.ReleaseUncheckedDiscrete(this, _treeFilterable, _storage);
                    }
                    else
                    {
                        DiscreteDemandManager.ServeDiscreteDemand(this, _storage);
                        DiscreteDemandManager.ReturnIdleDiscrete(this, _storage);
                    }
                    BulkBufferManager.RestockFromPool(this, _storage, _treeFilterable);
                }
                else if (mode == CollectMode.Edibles)
                {
                    FoodBalancer.RebalanceFoodAcrossBoxes();
                    FilterAutoManager.ReleaseUncheckedFood(this, _treeFilterable, _storage);
                }

                if (_timer >= ScanIntervalSeconds)
                {
                    _timer = 0f;
                    Collect();
                }
            });
        }

        private void Collect()
        {
            int worldId = this.GetMyWorldId();
            if (worldId < 0) return;

            _candidates.Clear();

            if (mode == CollectMode.Edibles)
            {
                foreach (Edible edible in Components.Edibles.WorldItemsEnumerate(worldId, false))
                {
                    if (edible == null) continue;
                    Pickupable p = edible.GetComponent<Pickupable>();
                    if (IsLooseInertPickupable(p) && FilterAutoManager.IsAcceptedByFilter(this, _treeFilterable, p.KPrefabID))
                    {
                        _candidates.Add(p);
                    }
                }

                foreach (Pickupable p in Components.Pickupables.WorldItemsEnumerate(worldId, false))
                {
                    KPrefabID id = p != null ? p.KPrefabID : null;
                    if (id != null && id.HasTag(GameTags.CookingIngredient) && !id.HasTag(GameTags.Edible)
                        && IsLooseInertPickupable(p) && FilterAutoManager.IsAcceptedByFilter(this, _treeFilterable, id))
                    {
                        _candidates.Add(p);
                    }
                }
            }
            else
            {
                if (OmniPool.Instance == null) return;

                foreach (Pickupable p in Components.Pickupables.WorldItemsEnumerate(worldId, false))
                {
                    if (!IsLooseInertPickupable(p)) continue;
                    KPrefabID id = p.KPrefabID;

                    if (id.HasTag(GameTags.Edible)) continue;
                    if (!FilterAutoManager.IsAcceptedByFilter(this, _treeFilterable, id)) continue;

                    if (BulkBufferManager.IsBulkSolidElementChunk(p))
                    {
                        if (!FilterAutoManager.PassesFilter(_treeFilterable, p.KPrefabID.PrefabTag)) continue;
                        _candidates.Add(p);
                    }
                    else
                    {
                        _candidates.Add(p);
                    }
                }
            }

            for (int i = 0; i < _candidates.Count; i++)
            {
                Pickupable p = _candidates[i];
                if (p == null || p.storage != null || p.ReservedAmount > 0f) continue;

                if (mode == CollectMode.Debris && BulkBufferManager.IsBulkSolidElementChunk(p))
                {
                    PrimaryElement pe = p.PrimaryElement;
                    OmniPool.Instance.AddSolid(pe.ElementID, pe.Mass, pe.Temperature);
                    Util.KDestroyGameObject(p.gameObject);
                }
                else if (mode == CollectMode.Debris)
                {
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
                    _storage.Store(p.gameObject, hide_popups: true);
                }
            }

            _candidates.Clear();
        }

        private static bool IsLooseInertPickupable(Pickupable p)
        {
            if (p == null || p.storage != null) return false;

            KPrefabID id = p.KPrefabID;
            if (id == null || id.HasTag(GameTags.Stored) || p.ReservedAmount > 0f) return false;

            if (id.HasTag(GameTags.Creature) ||
                id.HasTag(GameTags.BaseMinion) ||
                id.HasTag(GameTags.DupeBrain) ||
                id.HasTag(GameTags.BagableCreature))
            {
                return false;
            }

            PrimaryElement pe = p.PrimaryElement;
            if (pe == null || pe.Mass <= 0f || pe.Temperature > MaxCollectTemperatureK) return false;

            Element e = pe.Element;
            if (e != null && (e.IsLiquid || e.IsGas)) return false;

            if (e != null && e.IsSolid && id.PrefabTag == e.tag)
            {
                Clearable clearableOre = p.GetComponent<Clearable>();
                return clearableOre == null || clearableOre.isClearable;
            }

            bool safeDiscrete = id.HasTag(GameTags.Egg) || id.HasTag(GameTags.Clothes);
            if ((p.GetComponent<Health>() != null && !safeDiscrete) ||
                p.GetComponent<Navigator>() != null ||
                p.GetComponent<MinionIdentity>() != null)
            {
                return false;
            }

            Clearable clearable = p.GetComponent<Clearable>();
            return clearable == null || clearable.isClearable;
        }

        internal static bool IsBulkSolidElementChunk(Pickupable p) => BulkBufferManager.IsBulkSolidElementChunk(p);
        internal static bool IsDiscreteManaged(GameObject go) => DiscreteDemandManager.IsDiscreteManaged(go);
    }
}
