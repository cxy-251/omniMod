using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
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
    public class GlobalItemCollector : KMonoBehaviour, ISim1000ms, ISim33ms
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

        private const float ScanIntervalSeconds = 6f;
        private const float MaxCollectTemperatureK = 673.15f; // 400°C

        private float _timer;
        private float _restockTimer;
        private Storage _storage;
        private TreeFilterable _treeFilterable;
        private bool _isRedundant;

        internal bool IsRedundant => _isRedundant;
        public Storage BoxStorage => _storage;

        private readonly List<Pickupable> _scanItems = new List<Pickupable>(128);
        private int _scanCursor;
        private bool _scanInProgress;
        private static int _collectionBudgetFrame = -1;
        private static long _collectionDeadline;
        private const int MaxItemsPerSlice = 64;
        private readonly HashSet<Storage> _changedStorages = new HashSet<Storage>();
        private readonly List<GameObject> _maintenanceItems = new List<GameObject>();
        private int _maintenanceCursor;

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

            StorageBatchHelper.RepairStoredItems(_storage);
            // 分散各箱的全图扫描时刻，避免读档后每隔 6 秒集中到同一帧。
            _timer = (Mathf.Abs(GetInstanceID() % 600) / 600f) * ScanIntervalSeconds;
            _restockTimer = (Mathf.Abs(GetInstanceID() % 300) / 100f);
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
            if (_storage != null)
            {
                _storage.Subscribe((int)GameHashes.OnStorageChange, (data) => AggregateBoxRegistry.MarkWorldDirty(this.GetMyWorldId()));
            }
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
            _scanInProgress = false;
            _scanItems.Clear();
            _maintenanceItems.Clear();
            AggregateBoxRegistry.Unregister(this);
            base.OnCleanUp();
        }

        public void Sim1000ms(float dt)
        {
            ModLog.CallTimer started = ModLog.StartCall();
            try
            {
                if (_maintenanceCursor >= _maintenanceItems.Count && _storage != null)
                {
                    _maintenanceItems.Clear();
                    _maintenanceItems.AddRange(_storage.items);
                    _maintenanceCursor = 0;
                }
                if (_isRedundant) return;

                // 仅在脏标记触发时刷新快照，绝不每秒无谓重建
                AggregateBoxRegistry.RefreshWorldBoxItems(this.GetMyWorldId());

                _timer += dt;
                _restockTimer += dt;

                ExceptionShield.Execute("GlobalItemCollector.Sim1000ms", () =>
                {
                    if (mode == CollectMode.Debris)
                    {
                        if (AggregateBoxRegistry.DiscreteHome == this)
                        {
                            DiscreteDemandManager.MigratePoolSeedsToHomeStorage(this, _storage);
                        }
                        else
                        {
                            DiscreteDemandManager.ServeDiscreteDemand(this, _storage);
                        }

                        if (_restockTimer >= 3f)
                        {
                            _restockTimer = 0f;
                            BulkBufferManager.RestockFromPool(this, _storage, _treeFilterable);
                        }
                    }
                    else if (mode == CollectMode.Edibles)
                    {
                        FoodBalancer.RebalanceFoodAcrossBoxes();
                    }

                    if (_timer >= ScanIntervalSeconds)
                    {
                        _timer = 0f;
                        BeginCollection();
                    }
                });
            }
            finally
            {
                ModLog.SlowCall("聚合箱每秒维护", started, _storage != null ? _storage.items.Count : 0);
            }
        }

        private void BeginCollection()
        {
            if (_scanInProgress || Components.Pickupables == null) return;
            int worldId = this.GetMyWorldId();
            if (worldId < 0 || worldId == 255) return;
            _scanItems.Clear();
            _scanItems.AddRange(Components.Pickupables.GetWorldItems(worldId, false));
            _scanCursor = 0;
            _scanInProgress = _scanItems.Count > 0;
        }

        public void Sim33ms(float dt)
        {
            if (!_scanInProgress && _maintenanceCursor >= _maintenanceItems.Count) return;
            int frame = Time.frameCount;
            if (_collectionBudgetFrame != frame)
            {
                _collectionBudgetFrame = frame;
                // 所有箱子共用每个渲染帧约 2ms 的收集预算，避免随箱子数量叠加。
                _collectionDeadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 500;
            }
            if (Stopwatch.GetTimestamp() >= _collectionDeadline) return;
            ModLog.CallTimer started = ModLog.StartCall();
            try
            {
                MaintainStorageSlice();
                if (_scanInProgress && !_isRedundant && Stopwatch.GetTimestamp() < _collectionDeadline) CollectSlice();
            }
            catch (Exception ex)
            {
                _scanInProgress = false;
                _scanItems.Clear();
                _maintenanceItems.Clear();
                _maintenanceCursor = 0;
                ModLog.Throttle("collection_slice_failed", $"分批收集失败，稍后重新扫描：{ex.Message}", 10f);
            }
            finally
            {
                ModLog.SlowCall("分批收集", started, _scanItems.Count);
            }
        }

        private void MaintainStorageSlice()
        {
            int examined = 0;
            while (_maintenanceCursor < _maintenanceItems.Count && examined++ < MaxItemsPerSlice &&
                   Stopwatch.GetTimestamp() < _collectionDeadline)
            {
                GameObject item = _maintenanceItems[_maintenanceCursor++];
                if (item == null) continue;
                Pickupable pickup = item.GetComponent<Pickupable>();
                // 快照之后物品可能已经被取走；只处理仍属于本箱的物品。
                if (pickup != null ? pickup.storage != _storage : !_storage.items.Contains(item)) continue;
                if (LivingThingsSafety.EjectIfLiving(this, item) || _isRedundant || pickup == null ||
                    pickup.ReservedAmount > 0f || _treeFilterable == null) continue;
                bool checkFilter = mode == CollectMode.Edibles ||
                                   (AggregateBoxRegistry.DiscreteHome == this && !BulkBufferManager.IsBulkSolidElementChunk(pickup));
                if (checkFilter && !FilterAutoManager.IsAcceptedByFilter(this, _treeFilterable, pickup.KPrefabID))
                    _storage.Drop(item);
            }
            if (_maintenanceCursor >= _maintenanceItems.Count)
            {
                _maintenanceItems.Clear();
                _maintenanceCursor = 0;
            }
        }

        private void CollectSlice()
        {
            _changedStorages.Clear();
            int examined = 0;
            try
            {
                while (_scanCursor < _scanItems.Count && examined < MaxItemsPerSlice &&
                       Stopwatch.GetTimestamp() < _collectionDeadline)
                {
                    Pickupable pickup = _scanItems[_scanCursor++];
                    examined++;
                    if (!IsLooseInertPickupable(pickup)) continue;
                    KPrefabID id = pickup.KPrefabID;
                    bool food = id.HasTag(GameTags.Edible) || id.HasTag(GameTags.CookingIngredient);
                    if (mode == CollectMode.Edibles ? !food : id.HasTag(GameTags.Edible)) continue;
                    if (!FilterAutoManager.IsAcceptedByFilter(this, _treeFilterable, id)) continue;

                    if (mode == CollectMode.Debris && BulkBufferManager.IsBulkSolidElementChunk(pickup))
                    {
                        if (OmniPool.Instance == null || !FilterAutoManager.PassesFilter(_treeFilterable, id.PrefabTag)) continue;
                        PrimaryElement element = pickup.PrimaryElement;
                        OmniPool.Instance.AddSolid(element.ElementID, element.Mass, element.Temperature);
                        Util.KDestroyGameObject(pickup.gameObject);
                    }
                    else
                    {
                        Storage destination = _storage;
                        if (mode == CollectMode.Debris)
                        {
                            Assignable assignable = pickup.GetComponent<Assignable>();
                            if (assignable == null || !assignable.IsAssigned() ||
                                AggregateBoxRegistry.GetAssigneeWorldId(assignable.assignee) != this.GetMyWorldId())
                            {
                                GlobalItemCollector home = AggregateBoxRegistry.DiscreteHome;
                                if (home != null && home.BoxStorage != null) destination = home.BoxStorage;
                            }
                        }
                        _changedStorages.Add(destination);
                        StorageBatchHelper.Store(destination, pickup.gameObject);
                    }
                }
                if (_scanCursor >= _scanItems.Count)
                {
                    _scanInProgress = false;
                    _scanItems.Clear();
                }
            }
            finally
            {
                foreach (Storage storage in _changedStorages)
                {
                    StorageBatchHelper.PublishChanges(storage, increased: true);
                }
                _changedStorages.Clear();
            }
        }

        private static bool IsLooseInertPickupable(Pickupable p)
        {
            if (p == null || p.storage != null) return false;

            KPrefabID id = p.KPrefabID;
            if (id == null || id.HasTag(GameTags.Stored) || p.ReservedAmount > 0f) return false;

            if (id.HasTag(GameTags.Creature) ||
                id.HasTag(GameTags.BaseMinion) ||
                id.HasTag(GameTags.DupeBrain) ||
                id.HasTag(GameTags.BagableCreature) ||
                id.HasTag(GameTags.Robot))
            {
                return false;
            }

            PrimaryElement pe = p.PrimaryElement;
            if (pe == null || pe.Mass <= 0f || pe.Temperature > MaxCollectTemperatureK) return false;

            Element e = pe.Element;
            if (e != null && (e.IsLiquid || e.IsGas)) return false;

            return true;
        }

        internal static bool IsBulkSolidElementChunk(Pickupable p) => BulkBufferManager.IsBulkSolidElementChunk(p);
        internal static bool IsDiscreteManaged(GameObject go) => DiscreteDemandManager.IsDiscreteManaged(go);
    }
}
