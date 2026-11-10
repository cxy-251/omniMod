using System;
using System.Collections.Generic;
using HarmonyLib;
using KSerialization;
using Klei.AI;
using OmniMod.Collection;
using OmniMod.Core.Logging;
using OmniMod.Logistics.RemoteFetch;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace OmniMod.Pool
{
    /// <summary>
    /// 唯一实体库。Storage 的原生 ISaveLoadableDetails 保存整个物品及其嵌套组件，
    /// 不把蛋、变异种子、调味食物或分配关系压成 prefab/count。
    /// 出库仍归池管理，实际被工作者取走后才解除归属；门户存量不是第二份库存。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class OmniEntityPool : Storage, ISim33ms
    {
        internal sealed class Entry
        {
            internal Pickupable Pickup;
            internal Storage Outlet;
            internal object Request;
            internal float ReturnAt;
            internal Tag[] Tags;
            internal int[] TagSlots;
            internal int Access;
            internal float Amount;
            internal float Mass;
            internal float Calories;
            internal int Slot;
        }

        public static OmniEntityPool Instance { get; private set; }
        internal static bool Stopping { get; private set; }
        internal static bool Moving => _moving > 0;
        private static int _moving;
        private static readonly Action<Storage, GameObject, bool, bool> ApplyModifiers =
            AccessTools.MethodDelegate<Action<Storage, GameObject, bool, bool>>(
                AccessTools.Method(typeof(Storage), "ApplyStoredItemModifiers"));
        private readonly Dictionary<Pickupable, Entry> _entries = new Dictionary<Pickupable, Entry>();
        private readonly List<Entry> _ordered = new List<Entry>();
        private readonly Dictionary<object, HashSet<Pickupable>> _requests = new Dictionary<object, HashSet<Pickupable>>();
        private readonly Dictionary<Tag, List<Pickupable>> _byTag = new Dictionary<Tag, List<Pickupable>>();
        private readonly Dictionary<Tag, double>[] _amounts =
        {
            new Dictionary<Tag, double>(), new Dictionary<Tag, double>(),
            new Dictionary<Tag, double>(), new Dictionary<Tag, double>()
        };
        private readonly Dictionary<Tag, double>[] _masses =
        {
            new Dictionary<Tag, double>(), new Dictionary<Tag, double>(),
            new Dictionary<Tag, double>(), new Dictionary<Tag, double>()
        };
        private double _calories;
        private static Effect _eggPreservation;
        private readonly object _viewLock = new object();
        private int _cursor;
        private float _clock;
        private float _nextLegacySeed;
        private float _nextConsolidation;
        private const float CheckoutLifetime = 30f;
        internal int EntityCount => _entries.Count;
        internal long Revision { get; private set; }

        protected override void OnPrefabInit()
        {
            Stopping = false;
            Instance = this;
            allowItemRemoval = false;
            ignoreSourcePriority = true;
            capacityKg = float.MaxValue;
            showCapacityStatusItem = false;
            showCapacityAsMainStatus = false;
            showUnreachableStatus = false;
            showInUI = false;
            sendOnStoreOnSpawn = true;
            doDiseaseTransfer = false;
            SetDefaultStoredItemModifiers(new List<StoredItemModifier>
            { StoredItemModifier.Hide, StoredItemModifier.Preserve, StoredItemModifier.Insulate });
            base.OnPrefabInit();
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            Instance = this;
            StorageBatchHelper.RepairStoredItems(this);
            for (int i = 0; i < items.Count; i++)
            {
                Pickupable pickup = items[i] != null ? items[i].GetComponent<Pickupable>() : null;
                if (pickup != null) Track(pickup, null, null, 0f);
            }
            int initialCount = EntityCount;
            int merged = ConsolidateAll();
            ModLog.Info($"[实体共享池] 已载入 {initialCount} 个实体堆叠，合堆瘦身清理 {merged} 个冗余碎片，当前存量 {EntityCount} 个实体堆叠；箱子仅作为存取入口。");
        }

        protected override void OnCleanUp()
        {
            if (ReferenceEquals(Instance, this)) { Stopping = true; Instance = null; }
            _entries.Clear(); _ordered.Clear(); _byTag.Clear(); _requests.Clear();
            lock (_viewLock) { foreach (var view in _amounts) view.Clear(); foreach (var view in _masses) view.Clear(); _calories = 0f; }
            base.OnCleanUp();
        }

        internal static void BeginShutdown() { Stopping = true; }
        internal static bool IsPool(Storage storage) => storage is OmniEntityPool;
        internal static bool CanManage(Pickupable pickup)
        {
            if (pickup == null || pickup.PrimaryElement == null ||
                !GlobalItemCollector.IsDiscreteManaged(pickup.gameObject) ||
                pickup.PrimaryElement.Element == null || !pickup.PrimaryElement.Element.IsSolid)
            {
                return false;
            }
            if (pickup.storage != null)
            {
                if (pickup.storage.GetComponent<MinionIdentity>() != null ||
                    pickup.storage.GetComponent<MinionAssignablesProxy>() != null ||
                    pickup.storage.storageID == GameTags.StoragesIds.BionicUpgradeStorage)
                {
                    return false;
                }
            }
            return true;
        }
        internal static bool CanTrackAtPortal(Pickupable pickup) => CanManage(pickup) || BulkBufferManager.IsBulkSolidElementChunk(pickup);
        internal void HoldMaterial(Pickupable pickup, object request)
        {
            if (!BulkBufferManager.IsBulkSolidElementChunk(pickup) ||
                !AggregateBoxRegistry.TryGetBox(pickup.storage, out _)) return;
            Track(pickup, pickup.storage, request, _clock + CheckoutLifetime);
        }
        internal static int AccessFor(KPrefabID id)
        {
            if (id == null) return 0;
            Element material = ElementLoader.GetElement(id.PrefabTag);
            if (material != null && material.IsSolid) return 1;
            bool isFood = FilterAutoManager.IsFoodItem(id);
            return (isFood && !id.HasTag(GameTags.Dehydrated) ? 0 : 1) | (isFood ? 2 : 0);
        }
        internal bool Owns(Pickupable pickup) => pickup != null && _entries.TryGetValue(pickup, out Entry entry) &&
            (pickup.storage == this || (entry.Outlet != null && pickup.storage == entry.Outlet));
        internal bool IsCheckout(Pickupable pickup) => pickup != null && _entries.TryGetValue(pickup, out Entry entry) &&
            entry.Outlet != null && pickup.storage == entry.Outlet && entry.ReturnAt > _clock;

        internal void AdoptPortal(Storage storage)
        {
            if (storage == null || Stopping) return;
            for (int i = 0; i < storage.items.Count; i++)
            {
                Pickupable pickup = storage.items[i] != null ? storage.items[i].GetComponent<Pickupable>() : null;
                if (CanTrackAtPortal(pickup) && pickup.storage == storage && !_entries.ContainsKey(pickup))
                    Track(pickup, storage, null, 0f);
            }
        }

        internal void Observe(Pickupable pickup)
        {
            if (pickup == null || Moving || Stopping || App.isLoading) return;
            if (_entries.TryGetValue(pickup, out Entry entry))
            {
                if (!Owns(pickup)) Untrack(entry);
                else Refresh(entry);
            }
            else if (CanTrackAtPortal(pickup) && AggregateBoxRegistry.TryGetBox(pickup.storage, out _))
                Track(pickup, pickup.storage, null, 0f);
        }

        internal void Removed(Storage storage, GameObject item)
        {
            if (Moving || Stopping || item == null) return;
            Pickupable pickup = item.GetComponent<Pickupable>();
            if (pickup != null && _entries.TryGetValue(pickup, out Entry entry) &&
                (storage == this || storage == entry.Outlet)) Untrack(entry);
        }

        internal bool Deposit(Pickupable pickup)
        {
            if (Stopping || App.isLoading || !CanManage(pickup) || pickup.ReservedAmount > 0f) return false;
            if (pickup.storage == this) { Observe(pickup); return true; }
            if (IsCheckout(pickup)) return true;
            if (TryMergeIncoming(pickup)) return true;
            if (!MoveIntact(pickup, this)) return false;
            Track(pickup, null, null, 0f);
            return true;
        }

        internal static Tag GetMergeIdentity(Pickupable pickup)
        {
            if (pickup == null || pickup.KPrefabID == null) return Tag.Invalid;
            Tag tag = pickup.KPrefabID.PrefabTag;

            MutantPlant m = pickup.GetComponent<MutantPlant>();
            if (m != null && !m.IsOriginal)
            {
                return new Tag(tag.Name + "__" + m.SubSpeciesID.Name + (m.IsIdentified ? "_ID" : "_UNID"));
            }

            Edible e = pickup.GetComponent<Edible>();
            if (e != null)
            {
                string spiceKey = "";
                if (e.Spices != null && e.Spices.Count > 0)
                {
                    for (int i = 0; i < e.Spices.Count; i++)
                        spiceKey += "_" + e.Spices[i].Id;
                }
                bool rehydrated = pickup.HasTag(GameTags.Rehydrated);
                bool dehydrated = pickup.HasTag(GameTags.Dehydrated);
                if (!string.IsNullOrEmpty(spiceKey) || rehydrated || dehydrated)
                {
                    return new Tag(tag.Name + spiceKey + (rehydrated ? "_R" : "") + (dehydrated ? "_D" : ""));
                }
            }

            return tag;
        }

        internal static bool CanBeMaster(Pickupable pickup)
        {
            if (pickup == null || pickup.wasAbsorbed || pickup.ReservedAmount > 0f) return false;
            if (pickup.KPrefabID == null || pickup.PrimaryElement == null) return false;

            Assignable assign = pickup.GetComponent<Assignable>();
            if (assign != null && assign.IsAssigned()) return false;

            return true;
        }

        internal static bool CanMerge(Pickupable master, Pickupable source)
        {
            if (master == null || source == null || ReferenceEquals(master, source)) return false;
            if (master.wasAbsorbed || source.wasAbsorbed) return false;
            if (master.KPrefabID == null || source.KPrefabID == null) return false;
            if (master.KPrefabID.PrefabTag != source.KPrefabID.PrefabTag) return false;
            if (master.PrimaryElement == null || source.PrimaryElement == null) return false;
            if (master.PrimaryElement.ElementID != source.PrimaryElement.ElementID) return false;

            Assignable assignA = master.GetComponent<Assignable>();
            Assignable assignB = source.GetComponent<Assignable>();
            if ((assignA != null && assignA.IsAssigned()) || (assignB != null && assignB.IsAssigned())) return false;

            MutantPlant mA = master.GetComponent<MutantPlant>();
            MutantPlant mB = source.GetComponent<MutantPlant>();
            if (mA != null || mB != null)
            {
                if (mA == null || mB == null) return false;
                if (mA.IsOriginal != mB.IsOriginal) return false;
                if (!mA.IsOriginal)
                {
                    if (mA.SubSpeciesID != mB.SubSpeciesID) return false;
                    if (mA.IsIdentified != mB.IsIdentified) return false;
                }
            }

            Edible eA = master.GetComponent<Edible>();
            Edible eB = source.GetComponent<Edible>();
            if (eA != null || eB != null)
            {
                if (eA == null || eB == null || !eA.CanAbsorb(eB)) return false;
            }

            return true;
        }

        private bool MergeInto(Pickupable master, Pickupable source)
        {
            if (!CanMerge(master, source)) return false;
            if (!_entries.TryGetValue(source, out Entry sourceEntry)) return false;
            if (!_entries.TryGetValue(master, out Entry masterEntry)) return false;
            if (masterEntry.Outlet != null || sourceEntry.Outlet != null) return false;
            if (master.ReservedAmount > 0f || source.ReservedAmount > 0f) return false;

            PrimaryElement masterPe = master.PrimaryElement;
            PrimaryElement sourcePe = source.PrimaryElement;

            float newMass = masterPe.Mass + sourcePe.Mass;
            if (newMass > 0f)
            {
                float finalTemp = (masterPe.Mass * masterPe.Temperature + sourcePe.Mass * sourcePe.Temperature) / newMass;
                masterPe.SetMassTemperature(newMass, finalTemp);
            }

            if (sourcePe.DiseaseCount > 0)
            {
                masterPe.AddDisease(sourcePe.DiseaseIdx, sourcePe.DiseaseCount, "合堆吸收");
            }

            master.Trigger((int)GameHashes.Absorb, source);

            if (master.OnTake == null)
            {
                master.OnTake = (p, units) => EntitySplitter.Split(p, units);
            }

            Untrack(sourceEntry);
            items.Remove(source.gameObject);
            Util.KDestroyGameObject(source.gameObject);
            Refresh(masterEntry);

            return true;
        }

        private bool TryMergeIncoming(Pickupable pickup)
        {
            if (pickup == null || pickup.wasAbsorbed || pickup.ReservedAmount > 0f) return false;
            if (pickup.KPrefabID == null || pickup.PrimaryElement == null) return false;
            Tag tag = pickup.KPrefabID.PrefabTag;

            if (!_byTag.TryGetValue(tag, out List<Pickupable> candidates)) return false;

            for (int i = 0; i < candidates.Count; i++)
            {
                Pickupable master = candidates[i];
                if (master != null && master.storage == this && master.ReservedAmount <= 0f &&
                    _entries.TryGetValue(master, out Entry masterEntry) && masterEntry.Outlet == null)
                {
                    if (CanMerge(master, pickup))
                    {
                        Storage previous = pickup.storage;
                        previous?.Remove(pickup.gameObject, do_disease_transfer: false);

                        PrimaryElement masterPe = master.PrimaryElement;
                        PrimaryElement sourcePe = pickup.PrimaryElement;

                        float newMass = masterPe.Mass + sourcePe.Mass;
                        if (newMass > 0f)
                        {
                            float finalTemp = (masterPe.Mass * masterPe.Temperature + sourcePe.Mass * sourcePe.Temperature) / newMass;
                            masterPe.SetMassTemperature(newMass, finalTemp);
                        }

                        if (sourcePe.DiseaseCount > 0)
                        {
                            masterPe.AddDisease(sourcePe.DiseaseIdx, sourcePe.DiseaseCount, "入库合并");
                        }

                        master.Trigger((int)GameHashes.Absorb, pickup);

                        if (master.OnTake == null)
                        {
                            master.OnTake = (p, units) => EntitySplitter.Split(p, units);
                        }

                        if (_entries.TryGetValue(pickup, out Entry currentEntry)) Untrack(currentEntry);

                        Util.KDestroyGameObject(pickup.gameObject);
                        Refresh(masterEntry);
                        Notify(previous);
                        return true;
                    }
                }
            }
            return false;
        }

        internal int ConsolidateAll()
        {
            if (Stopping) return 0;
            int mergedCount = 0;
            var masterByIdentity = new Dictionary<Tag, Pickupable>();

            var candidates = new List<Pickupable>(_ordered.Count);
            for (int i = 0; i < _ordered.Count; i++)
            {
                Entry entry = _ordered[i];
                if (entry != null && entry.Outlet == null && entry.Pickup != null && entry.Pickup.storage == this)
                {
                    candidates.Add(entry.Pickup);
                }
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                Pickupable pickup = candidates[i];
                if (pickup == null || pickup.wasAbsorbed || pickup.storage != this || pickup.ReservedAmount > 0f) continue;
                if (pickup.KPrefabID == null) continue;

                Tag identity = GetMergeIdentity(pickup);
                if (!identity.IsValid) continue;

                if (!masterByIdentity.TryGetValue(identity, out Pickupable master))
                {
                    if (CanBeMaster(pickup))
                    {
                        masterByIdentity[identity] = pickup;
                    }
                    continue;
                }

                if (master == pickup || master == null || master.wasAbsorbed)
                {
                    if (CanBeMaster(pickup)) masterByIdentity[identity] = pickup;
                    continue;
                }

                if (MergeInto(master, pickup))
                {
                    mergedCount++;
                }
            }

            items.RemoveAll(x => x == null);
            return mergedCount;
        }

        internal Pickupable Checkout(Pickupable pickup, GlobalItemCollector portal, float amount, object request,
            bool assignmentIntent = false)
        {
            if (Stopping || App.isLoading || pickup == null || portal == null || portal.IsRedundant ||
                portal.BoxStorage == null || !(amount > 0f) || float.IsInfinity(amount) || float.IsNaN(amount) ||
                pickup.ReservedAmount > 0f || !Owns(pickup) || !EntityPoolAccess.PortalAccepts(portal, pickup)) return null;
            Assignable assignable = pickup.GetComponent<Assignable>();
            if (!assignmentIntent && assignable != null && assignable.IsAssigned() &&
                SharedItemDispatcher.GetAssigneeWorldId(assignable.assignee) != portal.GetMyWorldId()) return null;
            Entry entry = _entries[pickup];
            if (pickup.storage == portal.BoxStorage)
            {
                // 入口已经准备了该实体；多个探测者可以看到它，实际排他预约仍由原版 Pickupable 执行。
                entry.ReturnAt = _clock + CheckoutLifetime;
                SetRequest(entry, request);
                return pickup;
            }
            if (!assignmentIntent && entry.Outlet != null && entry.ReturnAt > _clock) return null;
            float take = Mathf.Min(amount, pickup.UnreservedFetchAmount);
            Pickupable portion = pickup;
            _moving++;
            try
            {
                if (take < pickup.FetchTotalAmount)
                {
                    if (pickup.OnTake != null)
                    {
                        portion = pickup.Take(take);
                    }
                    else
                    {
                        float massPerUnit = pickup.PrimaryElement != null && pickup.PrimaryElement.MassPerUnit > 0f
                            ? pickup.PrimaryElement.MassPerUnit : 1f;
                        float units = Mathf.Min(pickup.TotalAmount, take / massPerUnit);
                        portion = EntitySplitter.Split(pickup, units);
                    }
                    Refresh(entry);
                }
                if (portion == null) return null;
                if (!MoveIntact(portion, portal.BoxStorage))
                {
                    if (portion != pickup) DepositDetached(portion);
                    return null;
                }
                if (portion == pickup && _entries.TryGetValue(pickup, out Entry current)) Untrack(current);
                Track(portion, portal.BoxStorage, request, _clock + CheckoutLifetime);
                return portion;
            }
            finally { _moving--; }
        }

        internal void ReturnPortal(Storage storage)
        {
            if (Stopping || App.isLoading || storage == null) return;
            // 只在拆除入口时遍历该入口；中央库与其它入口不会被 DropAll 触及。
            var contents = storage.items.ToArray();
            foreach (GameObject item in contents)
            {
                Pickupable pickup = item != null ? item.GetComponent<Pickupable>() : null;
                if (pickup == null || pickup.ReservedAmount > 0f) continue;
                if (BulkBufferManager.IsBulkSolidElementChunk(pickup))
                { BulkBufferManager.ReturnIdleChunkToPool(pickup); continue; }
                if (!CanManage(pickup)) continue;
                if (MoveIntact(pickup, this)) Track(pickup, null, null, 0f);
            }
        }

        private void DepositDetached(Pickupable pickup)
        {
            if (MoveIntact(pickup, this)) Track(pickup, null, null, 0f);
        }

        // 不使用 Storage.Transfer/自动吸收：它们可能把另一个带不同状态的实体合并销毁。
        private bool MoveIntact(Pickupable pickup, Storage destination)
        {
            if (pickup == null || destination == null) return false;
            if (pickup.storage == destination) return true;
            GameObject item = pickup.gameObject;
            Storage previous = pickup.storage;
            Vector3 position = item.transform.GetPosition();
            _moving++;
            try
            {
                previous?.Remove(item, do_disease_transfer: false);
                StoreIntact(destination, item);
                Notify(previous); Notify(destination);
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Throttle("entity_pool_move", "[实体共享池] 移动失败，正在恢复原归属：" + ex, 10f);
                try
                {
                    if (destination.items.Contains(item)) destination.Remove(item, do_disease_transfer: false);
                    if (previous != null) StoreIntact(previous, item);
                    else
                    {
                        item.transform.parent = null;
                        item.transform.SetPosition(position);
                        item.Trigger((int)GameHashes.OnStore);
                    }
                }
                catch (Exception restore) { ModLog.Error("[实体共享池] 恢复失败，保留实体并记录位置：" + position, restore); }
                return false;
            }
            finally { _moving--; }
        }

        internal static void StoreIntact(Storage storage, GameObject item)
        {
            GameObject result = storage.Store(item, hide_popups: true, block_events: true,
                do_disease_transfer: false, is_deserializing: true);
            if (result != item) throw new InvalidOperationException("原实体未能完整入库");
            ApplyModifiers(storage, item, true, false);
            item.Trigger((int)GameHashes.OnStore, storage);
        }
        internal static void ApplyPortalModifiers(Storage storage, GameObject item) => ApplyModifiers(storage, item, true, false);
        private static void Notify(Storage storage)
        {
            if (storage == null) return;
            if (!(storage is OmniEntityPool)) StorageBatchHelper.PublishChanges(storage, increased: true);
            RemoteFetchFailures.Invalidate();
        }

        internal float GetAmount(int access, Tag tag)
        {
            if (access <= 0 || access > 3) return 0f;
            lock (_viewLock) return _amounts[access].TryGetValue(tag, out double amount) ? (float)Math.Max(0d, amount) : 0f;
        }
        internal float GetMass(int access, Tag tag)
        {
            if (access <= 0 || access > 3) return 0f;
            lock (_viewLock) return _masses[access].TryGetValue(tag, out double mass) ? (float)Math.Max(0d, mass) : 0f;
        }
        internal float FoodCalories { get { lock (_viewLock) return (float)Math.Max(0d, _calories); } }
        internal List<Pickupable> Find(Tag tag) => tag.IsValid && _byTag.TryGetValue(tag, out var set) ? set : null;
        internal void Append(int access, Tag tag, HashSet<Pickupable> result)
        {
            List<Pickupable> set = Find(tag);
            if (set == null) return;
            foreach (Pickupable pickup in set)
                if (SharedPickupView.IsUsable(pickup, tag) && Owns(pickup) && (_entries[pickup].Access & access) != 0) result.Add(pickup);
        }
        internal void ForgetDestroyed(Pickupable pickup)
        {
            if (!Stopping && !ReferenceEquals(pickup, null) && _entries.TryGetValue(pickup, out Entry entry)) Untrack(entry);
        }
        private void Track(Pickupable pickup, Storage outlet, object request, float returnAt)
        {
            if (_entries.TryGetValue(pickup, out Entry old)) Untrack(old);
            var tags = new HashSet<Tag>(pickup.KPrefabID.Tags) { pickup.KPrefabID.PrefabTag };
            var entry = new Entry { Pickup = pickup, Outlet = outlet, Request = request, ReturnAt = returnAt,
                Tags = new Tag[tags.Count], TagSlots = new int[tags.Count], Access = AccessFor(pickup.KPrefabID),
                Amount = pickup.TotalAmount, Mass = pickup.PrimaryElement.Mass, Calories = CaloriesFor(pickup), Slot = _ordered.Count };
            if (FilterAutoManager.IsFoodItem(pickup.KPrefabID)) FilterAutoManager.EnsureDiscovered(pickup.KPrefabID);
            tags.CopyTo(entry.Tags);
            _entries.Add(pickup, entry); _ordered.Add(entry);
            entry.Request = null;
            SetRequest(entry, request);
            for (int i = 0; i < entry.Tags.Length; i++)
            {
                Tag tag = entry.Tags[i];
                if (!_byTag.TryGetValue(tag, out var set)) _byTag[tag] = set = new List<Pickupable>();
                entry.TagSlots[i] = set.Count;
                set.Add(pickup);
            }
            ChangeAmount(entry, entry.Amount, entry.Mass, entry.Calories);
            PreserveEgg(pickup, true);
        }
        private void Untrack(Entry entry)
        {
            // 储存移除和实体销毁可能连续到达；同一条账本记录只允许释放一次。
            if (!_entries.TryGetValue(entry.Pickup, out Entry current) || !ReferenceEquals(current, entry)) return;
            ChangeAmount(entry, -entry.Amount, -entry.Mass, -entry.Calories);
            PreserveEgg(entry.Pickup, false);
            SetRequest(entry, null);
            for (int i = 0; i < entry.Tags.Length; i++)
            {
                Tag tag = entry.Tags[i];
                List<Pickupable> set = _byTag[tag];
                int slot = entry.TagSlots[i], tail = set.Count - 1;
                if (slot != tail)
                {
                    Pickupable moved = set[tail];
                    set[slot] = moved;
                    Entry movedEntry = _entries[moved];
                    movedEntry.TagSlots[Array.IndexOf(movedEntry.Tags, tag)] = slot;
                }
                set.RemoveAt(tail);
                if (set.Count == 0) _byTag.Remove(tag);
            }
            _entries.Remove(entry.Pickup);
            int last = _ordered.Count - 1;
            if (entry.Slot != last) { _ordered[entry.Slot] = _ordered[last]; _ordered[entry.Slot].Slot = entry.Slot; }
            _ordered.RemoveAt(last);
        }
        private void SetRequest(Entry entry, object request)
        {
            if (ReferenceEquals(entry.Request, request)) return;
            if (entry.Request != null && _requests.TryGetValue(entry.Request, out var previous))
            {
                previous.Remove(entry.Pickup);
                if (previous.Count == 0) _requests.Remove(entry.Request);
            }
            entry.Request = request;
            if (request == null) return;
            if (!_requests.TryGetValue(request, out var pending)) _requests[request] = pending = new HashSet<Pickupable>();
            pending.Add(entry.Pickup);
        }
        internal void ReleaseRequest(object request)
        {
            if (Stopping || request == null || !_requests.TryGetValue(request, out var pending)) return;
            foreach (Pickupable pickup in pending)
                if (pickup != null && _entries.TryGetValue(pickup, out Entry entry))
                { entry.ReturnAt = 0f; entry.Request = null; }
            _requests.Remove(request);
        }

        private void Refresh(Entry entry)
        {
            Pickupable pickup = entry.Pickup;
            if (pickup == null || !Owns(pickup)) { Untrack(entry); return; }
            PreserveEgg(pickup, true);
            // 动态标签（充电状态、种子分析等）也属于索引的一部分。
            bool tagsChanged = entry.Access != AccessFor(pickup.KPrefabID);
            int count = 0;
            foreach (Tag tag in pickup.KPrefabID.Tags)
            {
                if (tag == pickup.KPrefabID.PrefabTag) continue;
                count++;
                if (Array.IndexOf(entry.Tags, tag) < 0) tagsChanged = true;
            }
            if (entry.Tags.Length != count + 1) tagsChanged = true;
            if (tagsChanged)
            {
                Storage outlet = entry.Outlet; object request = entry.Request; float deadline = entry.ReturnAt;
                Untrack(entry); Track(pickup, outlet, request, deadline);
            }
            else
            {
                float calories = CaloriesFor(pickup), mass = pickup.PrimaryElement.Mass;
                if (entry.Amount == pickup.TotalAmount && entry.Mass == mass && entry.Calories == calories) return;
                ChangeAmount(entry, (double)pickup.TotalAmount - entry.Amount, (double)mass - entry.Mass, (double)calories - entry.Calories);
                entry.Amount = pickup.TotalAmount; entry.Mass = mass; entry.Calories = calories;
            }
        }
        private void ChangeAmount(Entry entry, double delta, double massDelta, double caloriesDelta)
        {
            lock (_viewLock)
            {
                for (int access = 1; access <= 3; access++)
                {
                    if ((entry.Access & access) == 0) continue;
                    foreach (Tag tag in entry.Tags)
                    {
                        _amounts[access].TryGetValue(tag, out double old);
                        double value = old + delta;
                        if (value > 0.000001f) _amounts[access][tag] = value;
                        else _amounts[access].Remove(tag);
                        _masses[access].TryGetValue(tag, out double oldMass);
                        double mass = oldMass + massDelta;
                        if (mass > 0.000001f) _masses[access][tag] = mass;
                        else _masses[access].Remove(tag);
                    }
                }
                _calories += caloriesDelta;
                Revision++;
            }
        }
        private static float CaloriesFor(Pickupable pickup)
        {
            Edible edible = pickup.GetComponent<Edible>();
            return edible != null && edible.FoodInfo != null && edible.FoodInfo.CaloriesPerUnit > 0f
                ? edible.FoodInfo.CaloriesPerUnit * pickup.PrimaryElement.Units : 0f;
        }
        private static void PreserveEgg(Pickupable pickup, bool preserve)
        {
            if (pickup == null || !pickup.HasTag(GameTags.IncubatableEgg)) return;
            Effects effects = pickup.GetComponent<Effects>();
            if (effects == null) return;
            const string id = "OmniEntityPoolEggPreservation";
            if (!preserve) { effects.Remove(id); return; }
            if (App.isLoading || !pickup.isSpawned || effects.Get(id) != null) return;
            if (_eggPreservation == null)
            {
                _eggPreservation = new Effect(id, "共享池保存", "保存蛋的活力", 0f, false, false, false);
                // ONI 744825 的 IncubationSuppressed 每秒扣 1/60 活力；只在池及等待领取的出口中抵消。
                _eggPreservation.Add(new AttributeModifier(Db.Get().Amounts.Viability.deltaAttribute.Id,
                    1f / 60f, "共享池保存"));
            }
            effects.Add(_eggPreservation, should_save: false);
        }

        public void Sim33ms(float dt)
        {
            if (Stopping || App.isLoading || Game.Instance == null) return;
            _clock += dt;
            long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 1000;
            int count = Math.Min(32, _ordered.Count);
            try
            {
                while (count-- > 0 && _ordered.Count > 0 && Stopwatch.GetTimestamp() < deadline)
                {
                    if (_cursor >= _ordered.Count) _cursor = 0;
                    Entry entry = _ordered[_cursor++];
                    Pickupable pickup = entry.Pickup;
                    if (pickup == null || !Owns(pickup)) { Untrack(entry); continue; }
                    EnsurePoolCell(pickup);
                    Refresh(entry);
                    if (!_entries.TryGetValue(pickup, out entry)) continue;
                    if (entry.Outlet == null)
                    {
                        Assignable assigned = pickup.GetComponent<Assignable>();
                        if (assigned != null && assigned.IsAssigned())
                        {
                            GameObject owner = (assigned.assignee as MinionAssignablesProxy)?.GetTargetGameObject();
                            GlobalItemCollector portal = owner != null ? EntityPoolAccess.FindPortal(owner.GetMyWorldId(),
                                pickup, owner.GetComponent<Navigator>(), false) : null;
                            if (portal != null) Checkout(pickup, portal, pickup.UnreservedFetchAmount, assigned);
                        }
                        continue;
                    }
                    if (pickup.ReservedAmount > 0f || entry.ReturnAt > _clock) continue;
                    if (BulkBufferManager.IsBulkSolidElementChunk(pickup))
                    {
                        BulkBufferManager.ReturnIdleChunkToPool(pickup);
                        continue;
                    }
                    // 已分配设备也是按需出库，不让它在入口与池之间每秒往返。
                    Assignable assignment = pickup.GetComponent<Assignable>();
                    if (assignment != null && assignment.IsAssigned() &&
                        SharedItemDispatcher.GetAssigneeWorldId(assignment.assignee) == entry.Outlet.GetMyWorldId())
                    { entry.ReturnAt = _clock + CheckoutLifetime; continue; }
                    if (TryMergeIncoming(pickup)) continue;
                    if (MoveIntact(pickup, this)) Track(pickup, null, null, 0f);
                }
                if (_clock >= _nextLegacySeed)
                {
                    _nextLegacySeed = _clock + 1f;
                    MigrateLegacySeeds();
                }
                if (_clock >= _nextConsolidation && _ordered.Count > 100)
                {
                    _nextConsolidation = _clock + 10f;
                    ConsolidateAll();
                }
            }
            catch (Exception ex) { ModLog.Throttle("entity_pool_maintenance", "[实体共享池] 分片维护稍后重试：" + ex, 10f); }
        }

        private void EnsurePoolCell(Pickupable pickup)
        {
            if (pickup.storage != this) return;
            int cell = Grid.PosToCell(pickup);
            if (Grid.IsValidCell(cell) && Grid.WorldIdx[cell] != 255) return;
            for (int i = 0; i < AggregateBoxRegistry.BoxesCount; i++)
            {
                var portal = AggregateBoxRegistry.GetBox(i);
                if (portal == null || portal.IsRedundant) continue;
                int anchor = Grid.PosToCell(portal);
                if (!Grid.IsValidCell(anchor) || Grid.WorldIdx[anchor] == 255) continue;
                pickup.transform.SetPosition(portal.transform.GetPosition());
                pickup.UpdateCachedCell(anchor);
                return;
            }
        }

        private void MigrateLegacySeeds()
        {
            OmniPool legacy = OmniPool.Instance;
            if (legacy == null) return;
            Tag tag = Tag.Invalid; int count = 0;
            GameObject prefab = null;
            foreach (var pair in legacy.EnumerateSeeds())
            {
                if (pair.Value <= 0) continue;
                GameObject candidate = Assets.TryGetPrefab(pair.Key);
                if (candidate == null || candidate.GetComponent<Pickupable>() == null || candidate.GetComponent<PrimaryElement>() == null) continue;
                tag = pair.Key; count = pair.Value; prefab = candidate; break;
            }
            if (count == 0) return;
            // 仅旧的纯数量种子没有实体可迁移；先成功入库，最后再扣旧账，失败不改旧账。
            GlobalItemCollector portal = null;
            for (int i = 0; i < AggregateBoxRegistry.BoxesCount; i++)
            {
                var box = AggregateBoxRegistry.GetBox(i);
                if (box != null && !box.IsRedundant) { portal = box; break; }
            }
            if (portal == null) return;
            GameObject item = GameUtil.KInstantiate(prefab, portal.transform.GetPosition(), Grid.SceneLayer.Ore);
            item.SetActive(true);
            item.GetComponent<PrimaryElement>().Units = count;
            if (Deposit(item.GetComponent<Pickupable>()))
            {
                if (legacy.RemoveSeed(tag, count)) return;
                // 即使第三方在入库事件里改动了旧账，也不保留双份库存。
                Remove(item, do_disease_transfer: false);
            }
            Util.KDestroyGameObject(item);
        }
    }
}
