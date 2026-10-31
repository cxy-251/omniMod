using System.Collections.Generic;
using OmniMod.Core.Safety;
using OmniMod.Pool;
using UnityEngine;

namespace OmniMod.Collection
{
    /// <summary>
    /// 虚拟固体资源池（OmniPool）与物理聚合箱缓冲管理器：
    /// 维持各星球杂物箱 4000kg (4吨) 散装固体物化缓冲，充分满足多台机械臂并发抓取与小人搬运需求，
    /// 杜绝单臂独占导致其余机械臂停转，溢出自动退回虚拟池，实现物理材料与虚拟池的双向无感平滑兑换。
    /// </summary>
    public static class BulkBufferManager
    {
        public const float LocalBufferTargetPerElementKg = 4000f;
        public const float ReturnToPoolThresholdKg = 3000f;
        public const byte NoDisease = byte.MaxValue;

        private static readonly HashSet<SimHashes> _elementScratch = new HashSet<SimHashes>();
        private static readonly Dictionary<SimHashes, float> _localBulkMass = new Dictionary<SimHashes, float>(64);
        private static readonly Dictionary<SimHashes, float> _localReservedMass = new Dictionary<SimHashes, float>(64);
        private static readonly Dictionary<SimHashes, float> _otherBoxesBulkMass = new Dictionary<SimHashes, float>(64);
        private static readonly Dictionary<SimHashes, PrimaryElement> _localBulkChunk = new Dictionary<SimHashes, PrimaryElement>(64);

        public static bool IsBulkSolidElementChunk(Pickupable p)
        {
            if (p == null) return false;
            PrimaryElement pe = p.PrimaryElement;
            Element e = pe != null ? pe.Element : null;
            return e != null && e.IsSolid && p.KPrefabID != null && p.KPrefabID.PrefabTag == e.tag;
        }

        public static void InsulateStoredItems(Storage storage)
        {
            if (storage == null || storage.items == null) return;
            foreach (GameObject go in storage.items)
            {
                if (go != null)
                {
                    Storage.MakeItemTemperatureInsulated(go, is_stored: true, is_initializing: false);
                }
            }
        }

        public static void MigrateOwnStorageToPool(GlobalItemCollector collector, Storage storage)
        {
            if (collector.mode != GlobalItemCollector.CollectMode.Debris || OmniPool.Instance == null || storage == null || storage.items == null)
            {
                return;
            }

            ExceptionShield.Execute("BulkBufferManager.MigrateOwnStorage", () =>
            {
                for (int i = storage.items.Count - 1; i >= 0; i--)
                {
                    GameObject go = storage.items[i];
                    if (go == null) continue;

                    Pickupable p = go.GetComponent<Pickupable>();
                    if (p != null && !LivingThingsSafety.IsLivingThing(go) && IsBulkSolidElementChunk(p))
                    {
                        PrimaryElement pe = p.PrimaryElement;
                        OmniPool.Instance.AddSolid(pe.ElementID, pe.Mass, pe.Temperature);
                        storage.Remove(go);
                        Util.KDestroyGameObject(go);
                    }
                }
            });
        }

        public static void RestockFromPool(GlobalItemCollector collector, Storage storage, TreeFilterable treeFilterable)
        {
            OmniPool pool = OmniPool.Instance;
            if (pool == null || treeFilterable == null || storage == null || storage.items == null)
            {
                return;
            }

            int worldId = collector.GetMyWorldId();
            if (worldId < 0) return;

            int boxCount = Mathf.Max(1, AggregateBoxRegistry.ActiveJunkBoxCount);
            bool isHome = AggregateBoxRegistry.DiscreteHome == collector;

            _elementScratch.Clear();
            _localBulkMass.Clear();
            _localReservedMass.Clear();
            _localBulkChunk.Clear();

            foreach (OmniPool.PoolLine line in pool.EnumerateJunk())
            {
                _elementScratch.Add(line.Element);
            }

            for (int i = 0; i < storage.items.Count; i++)
            {
                GameObject go = storage.items[i];
                if (go == null) continue;

                Pickupable p0 = go.GetComponent<Pickupable>();
                if (p0 != null && IsBulkSolidElementChunk(p0))
                {
                    PrimaryElement pe = p0.PrimaryElement;
                    SimHashes elemId = pe.ElementID;
                    _elementScratch.Add(elemId);
                    _localBulkMass[elemId] = _localBulkMass.TryGetValue(elemId, out float m) ? m + pe.Mass : pe.Mass;
                    _localReservedMass[elemId] = _localReservedMass.TryGetValue(elemId, out float rm) ? rm + p0.ReservedAmount : p0.ReservedAmount;
                    if (!_localBulkChunk.ContainsKey(elemId) && pe.Mass > 0.001f)
                    {
                        _localBulkChunk[elemId] = pe;
                    }
                }
            }

            bool storageChanged = false;
            bool otherMassCollected = false;
            foreach (SimHashes element in _elementScratch)
            {
                Element e = ElementLoader.FindElementByHash(element);
                if (e == null) continue;

                Tag tag = e.tag;
                float totalHave = _localBulkMass.TryGetValue(element, out float hm) ? hm : 0f;
                float reservedHave = _localReservedMass.TryGetValue(element, out float rhm) ? rhm : 0f;
                float unreservedHave = Mathf.Max(0f, totalHave - reservedHave);

                if (!treeFilterable.ContainsTag(tag))
                {
                    // 若属于合法散装固体且在储物柜白名单内，自动补勾进筛选器并正常缓冲，避免误当成"玩家手动取消勾选"
                    if (storage.storageFilters != null && storage.storageFilters.Contains(e.materialCategory))
                    {
                        treeFilterable.AddTagToFilter(tag);
                    }
                    else
                    {
                        if (unreservedHave > 0f)
                        {
                            storageChanged |= MoveBufferElementToPool(collector, storage, element, unreservedHave);
                            _localBulkMass[element] = reservedHave;
                        }
                        if (isHome)
                        {
                            FilterAutoManager.DrainUncheckedElementToFloor(collector, element, e);
                        }
                        continue;
                    }
                }

                float inPool = pool.GetMass(element);
                float target = Mathf.Min(inPool + unreservedHave, LocalBufferTargetPerElementKg);
                if (boxCount > 1 && inPool + unreservedHave < LocalBufferTargetPerElementKg)
                {
                    if (!otherMassCollected)
                    {
                        AggregateBoxRegistry.CollectOtherBoxesBulkMass(collector, _otherBoxesBulkMass);
                        otherMassCollected = true;
                    }
                    _otherBoxesBulkMass.TryGetValue(element, out float otherHave);
                    float fairShare = (inPool + unreservedHave + otherHave) / boxCount;
                    target = Mathf.Min(fairShare, LocalBufferTargetPerElementKg);
                }

                if (target > unreservedHave + 1f && inPool > 0.001f)
                {
                    float need = target - unreservedHave;
                    PrimaryElement chunk = _localBulkChunk.TryGetValue(element, out PrimaryElement peChunk) ? peChunk : null;
                    float taken;
                    float tempK;
                    if (chunk != null && chunk.Mass > 0.001f)
                    {
                        taken = pool.RemoveSolidNear(element, chunk.Temperature, need, out tempK);
                    }
                    else
                    {
                        taken = pool.RemoveSolid(element, need, out tempK);
                    }
                    if (taken > 0f)
                    {
                        if (chunk != null)
                        {
                            float finalTemp = GameUtil.GetFinalTemperature(chunk.Temperature, chunk.Mass, tempK, taken);
                            chunk.Mass += taken;
                            chunk.Temperature = finalTemp;
                            storageChanged = true;
                        }
                        else if (e.substance != null)
                        {
                            GameObject go = e.substance.SpawnResource(collector.transform.GetPosition(), taken, tempK, NoDisease, 0, prevent_merge: true, forceTemperature: false, manual_activation: true);
                            Pickupable p = go.GetComponent<Pickupable>();
                            if (p != null)
                            {
                                p.prevent_absorb_until_stored = true;
                            }
                            e.substance.ActivateSubstanceGameObject(go, NoDisease, 0);
                            StorageBatchHelper.Store(storage, go);
                            storageChanged = true;
                        }
                        _localBulkMass[element] = totalHave + taken;
                    }
                }
                else if (unreservedHave > target + ReturnToPoolThresholdKg ||
                         (boxCount > 1 && target < LocalBufferTargetPerElementKg - 1f && unreservedHave > target + 2f))
                {
                    float ret = unreservedHave - target;
                    storageChanged |= MoveBufferElementToPool(collector, storage, element, ret);
                    _localBulkMass[element] = Mathf.Max(0f, totalHave - ret);
                }
            }

            if (storageChanged) StorageBatchHelper.PublishChanges(storage, increased: true);
        }

        private static bool MoveBufferElementToPool(GlobalItemCollector collector, Storage storage, SimHashes element, float amount)
        {
            if (storage == null || storage.items == null || amount <= 0f || OmniPool.Instance == null)
            {
                return false;
            }

            float remaining = amount;
            for (int i = storage.items.Count - 1; i >= 0 && remaining > 0.001f; i--)
            {
                GameObject go = storage.items[i];
                if (go == null) continue;

                Pickupable p = go.GetComponent<Pickupable>();
                if (p == null || !IsBulkSolidElementChunk(p)) continue;

                PrimaryElement pe = p.PrimaryElement;
                if (pe.ElementID != element) continue;

                // 核心安全防护：只退回未被任何任务预约（Unreserved）的自由质量！
                // 绝不侵蚀已被机械臂或小人预约的 ReservedAmount，杜绝破坏搬运任务导致死循环震荡！
                float unreservedInChunk = Mathf.Max(0f, pe.Mass - p.ReservedAmount);
                if (unreservedInChunk <= 0.001f) continue;

                float canTake = Mathf.Min(unreservedInChunk, remaining);
                OmniPool.Instance.AddSolid(element, canTake, pe.Temperature);
                remaining -= canTake;
                float massLeft = Mathf.Max(0f, pe.Mass - canTake);
                if (massLeft <= 0.001f && p.ReservedAmount <= 0.001f)
                {
                    // 先从所属容器解除引用，再销毁；先将质量置零会立即触发 Unity 销毁。
                    storage.items.RemoveAt(i);
                    p.storage = null;
                    Util.KDestroyGameObject(go);
                }
                else
                {
                    pe.Mass = massLeft;
                }
            }

            return remaining < amount;
        }

        /// <summary>
        /// 从 OmniPool 按需即时物化指定固体元素至聚合箱中，供机械臂或搬运任务即时取用。
        /// </summary>
        public static Pickupable MaterializeSolidFromPool(GlobalItemCollector collector, SimHashes element, float neededMass = 1000f)
        {
            if (collector == null || collector.BoxStorage == null) return null;
            OmniPool pool = OmniPool.Instance;
            if (pool == null) return null;

            Element e = ElementLoader.FindElementByHash(element);
            if (e == null || !e.IsSolid) return null;

            float inPool = pool.GetMass(element);
            if (inPool <= 0.001f) return null;

            // 确保元素已被发现与筛选器已勾选
            if (DiscoveredResources.Instance != null && !DiscoveredResources.Instance.IsDiscovered(e.tag))
            {
                DiscoveredResources.Instance.Discover(e.tag, e.materialCategory);
            }
            TreeFilterable tf = collector.GetComponent<TreeFilterable>();
            if (tf != null && !tf.ContainsTag(e.tag))
            {
                tf.AddTagToFilter(e.tag);
            }

            Storage storage = collector.BoxStorage;
            PrimaryElement chunk = null;
            float unreservedHave = 0f;
            if (storage.items != null)
            {
                for (int i = 0; i < storage.items.Count; i++)
                {
                    GameObject go = storage.items[i];
                    if (go == null) continue;
                    Pickupable pItem = go.GetComponent<Pickupable>();
                    if (pItem != null && IsBulkSolidElementChunk(pItem) && pItem.PrimaryElement != null && pItem.PrimaryElement.ElementID == element)
                    {
                        unreservedHave += Mathf.Max(0f, pItem.UnreservedFetchAmount);
                        if (chunk == null || pItem.PrimaryElement.Mass > chunk.Mass) chunk = pItem.PrimaryElement;
                    }
                }
            }

            // 只补足自由缓冲的差额；反复评估同一任务时不能每次额外物化 4 吨。
            float targetTake = Mathf.Min(inPool, Mathf.Max(0f, Mathf.Max(neededMass, LocalBufferTargetPerElementKg) - unreservedHave));
            if (targetTake <= 0.001f) return chunk != null ? chunk.GetComponent<Pickupable>() : null;

            float taken;
            float tempK;
            if (chunk != null && chunk.Mass > 0.001f)
            {
                taken = pool.RemoveSolidNear(element, chunk.Temperature, targetTake, out tempK);
                if (taken > 0f)
                {
                    float finalTemp = GameUtil.GetFinalTemperature(chunk.Temperature, chunk.Mass, tempK, taken);
                    chunk.Mass += taken;
                    chunk.Temperature = finalTemp;
                    StorageBatchHelper.PublishChanges(storage, increased: true);
                }
                return chunk.GetComponent<Pickupable>();
            }
            else if (e.substance != null)
            {
                taken = pool.RemoveSolid(element, targetTake, out tempK);
                if (taken > 0f)
                {
                    GameObject go = e.substance.SpawnResource(collector.transform.GetPosition(), taken, tempK, NoDisease, 0, prevent_merge: true, forceTemperature: false, manual_activation: true);
                    Pickupable p = go != null ? go.GetComponent<Pickupable>() : null;
                    if (p != null)
                    {
                        p.prevent_absorb_until_stored = true;
                    }
                    e.substance.ActivateSubstanceGameObject(go, NoDisease, 0);
                    GameObject stored = storage.Store(go, hide_popups: true, block_events: false, do_disease_transfer: false);
                    AggregateBoxRegistry.MarkWorldDirty(collector.GetMyWorldId());
                    return stored != null ? stored.GetComponent<Pickupable>() : null;
                }
            }

            return null;
        }
    }
}
