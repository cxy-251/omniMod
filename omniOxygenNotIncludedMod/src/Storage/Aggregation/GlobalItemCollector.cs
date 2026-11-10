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
    /// 挂在杂物箱和食物箱上，协调散落物地面扫描、虚拟池缓冲、实体池入库和入口出库维护。
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
        private Storage _storage;
        private TreeFilterable _treeFilterable;
        private bool _isRedundant;

        internal bool IsRedundant => _isRedundant;
        public Storage BoxStorage => _storage;

        private readonly List<Pickupable> _scanItems = new List<Pickupable>(128);
        private int _scanCursor;
        private bool _scanInProgress;
        private bool _waitingForCandidates;
        private int _candidateVersion;
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

            _isRedundant = AggregateBoxRegistry.ExistsOnWorld(mode, this.GetMyWorldId(), except: this);
            AggregateBoxRegistry.Register(this);
            if (_isRedundant)
            {
                ModLog.Warn("本星球已有同类型聚合箱，此箱不生效（仅保留安全放生）。");
            }

            StorageBatchHelper.RepairStoredItems(_storage);
            if (!_isRedundant) OmniEntityPool.Instance?.AdoptPortal(_storage);
            // 分散各箱的全图扫描时刻，避免读档后每隔 6 秒集中到同一帧。
            _timer = (Mathf.Abs(GetInstanceID() % 600) / 600f) * ScanIntervalSeconds;
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
            WorldStorageIndex.QueueRefresh(this.GetMyWorldId());
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
            _waitingForCandidates = false;
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
                WorldStorageIndex.QueueRefresh(this.GetMyWorldId());

                _timer += dt;

                ExceptionShield.Execute("GlobalItemCollector.Sim1000ms", () =>
                {
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
            if (_scanInProgress || _waitingForCandidates || Components.Pickupables == null) return;
            _candidateVersion = CollectionCandidateSnapshot.RequestRefresh();
            _waitingForCandidates = true;
            TryStartCollection();
        }

        private void TryStartCollection()
        {
            int worldId = this.GetMyWorldId();
            if (worldId < 0 || worldId == 255) return;
            if (!CollectionCandidateSnapshot.TryGetWorldItems(worldId, _candidateVersion, out List<Pickupable> candidates)) return;
            _waitingForCandidates = false;
            _scanItems.Clear();
            if (candidates != null) _scanItems.AddRange(candidates);
            _scanCursor = 0;
            _scanInProgress = _scanItems.Count > 0;
        }

        public void Sim33ms(float dt)
        {
            if (!_scanInProgress && !_waitingForCandidates && !CollectionCandidateSnapshot.IsBuilding &&
                _maintenanceCursor >= _maintenanceItems.Count) return;
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
                // 扫描与维护合用原有 2ms 总预算，扫描每帧最多使用其中约 1ms。
                CollectionCandidateSnapshot.Advance(Math.Min(_collectionDeadline, Stopwatch.GetTimestamp() + Stopwatch.Frequency / 1000));
                if (_waitingForCandidates) TryStartCollection();
                MaintainStorageSlice();
                if (_scanInProgress && !_isRedundant && Stopwatch.GetTimestamp() < _collectionDeadline) CollectSlice();
            }
            catch (Exception ex)
            {
                _scanInProgress = false;
                _waitingForCandidates = false;
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
                if (pickup != null ? pickup.storage != _storage : !_storage.items.Contains(item)) continue;
                if (LivingThingsSafety.EjectIfLiving(this, item) || _isRedundant || pickup == null ||
                    pickup.ReservedAmount > 0f || _treeFilterable == null) continue;
                if (OmniEntityPool.CanManage(pickup))
                {
                    Assignable assign = pickup.GetComponent<Assignable>();
                    if (assign != null && assign.IsAssigned() &&
                        SharedItemDispatcher.GetAssigneeWorldId(assign.assignee) == this.GetMyWorldId())
                    {
                        continue;
                    }
                    OmniEntityPool pool = OmniEntityPool.Instance;
                    if (pool != null && !pool.IsCheckout(pickup)) pool.Deposit(pickup);
                }
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
                    // 快照发布后物品可能移动；最终操作前重新确认物品仍在本世界且可安全收集。
                    if (!IsLooseInertPickupable(pickup) || pickup.GetMyWorldId() != this.GetMyWorldId()) continue;
                    KPrefabID id = pickup.KPrefabID;
                    bool food = FilterAutoManager.IsFoodItem(id);
                    if (mode == CollectMode.Edibles ? !food : food) continue;
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
                        OmniEntityPool.Instance?.Deposit(pickup);
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

        internal static bool IsLooseInertPickupable(Pickupable p)
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
