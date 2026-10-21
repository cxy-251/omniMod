using System.Collections.Generic;
using OmniMod.Core.Safety;
using OmniMod.Pool;
using UnityEngine;

namespace OmniMod.Collection
{
    /// <summary>
    /// 虚拟固体资源池（OmniPool）与物理聚合箱缓冲管理器：
    /// 维持各星球杂物箱 1000kg 散装固体物化缓冲，溢出自动退回虚拟池，实现物理材料与虚拟池的双向无感兑换。
    /// </summary>
    public static class BulkBufferManager
    {
        public const float LocalBufferTargetPerElementKg = 1000f;
        public const float ReturnToPoolThresholdKg = 200f;
        public const byte NoDisease = byte.MaxValue;

        private static readonly HashSet<SimHashes> _elementScratch = new HashSet<SimHashes>();
        private static readonly Dictionary<SimHashes, float> _localBulkMass = new Dictionary<SimHashes, float>(64);
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
                    if (!_localBulkChunk.ContainsKey(elemId) && pe.Mass > 0.001f)
                    {
                        _localBulkChunk[elemId] = pe;
                    }
                }
            }

            foreach (SimHashes element in _elementScratch)
            {
                Element e = ElementLoader.FindElementByHash(element);
                if (e == null) continue;

                Tag tag = e.tag;
                float have = _localBulkMass.TryGetValue(element, out float hm) ? hm : 0f;

                if (!treeFilterable.ContainsTag(tag))
                {
                    if (have > 0f)
                    {
                        MoveBufferElementToPool(collector, storage, element, have);
                        _localBulkMass[element] = 0f;
                    }
                    if (isHome)
                    {
                        FilterAutoManager.DrainUncheckedElementToFloor(collector, element, e);
                    }
                    continue;
                }

                float inPool = pool.GetMass(element);
                float target = Mathf.Min(inPool + have, LocalBufferTargetPerElementKg);
                if (boxCount > 1 && inPool + have < LocalBufferTargetPerElementKg)
                {
                    float otherHave = AggregateBoxRegistry.GetOtherBoxesBulkMass(collector, element);
                    float fairShare = (inPool + have + otherHave) / boxCount;
                    target = Mathf.Min(fairShare, LocalBufferTargetPerElementKg);
                }

                if (target > have + 1f && inPool > 0.001f)
                {
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
                            storage.Trigger(-1697596308, chunk.gameObject);
                            storage.OnStorageChange?.Invoke(chunk.gameObject);
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
                            storage.Store(go, hide_popups: true, block_events: false, do_disease_transfer: false);
                        }
                        _localBulkMass[element] = have + taken;
                    }
                }
                else if (have > target + ReturnToPoolThresholdKg ||
                         (boxCount > 1 && target < LocalBufferTargetPerElementKg - 1f && have > target + 2f))
                {
                    float ret = have - target;
                    MoveBufferElementToPool(collector, storage, element, ret);
                    _localBulkMass[element] = Mathf.Max(0f, have - ret);
                }
            }
        }

        public static void MoveBufferElementToPool(GlobalItemCollector collector, Storage storage, SimHashes element, float amount)
        {
            if (storage == null || storage.items == null || amount <= 0f || OmniPool.Instance == null)
            {
                return;
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

                float canTake = Mathf.Min(pe.Mass, remaining);
                OmniPool.Instance.AddSolid(element, canTake, pe.Temperature);
                pe.Mass -= canTake;
                remaining -= canTake;

                if (pe.Mass <= 0.001f)
                {
                    storage.Remove(go);
                    Util.KDestroyGameObject(go);
                }
                else
                {
                    storage.Trigger(-1697596308, go);
                    storage.OnStorageChange?.Invoke(go);
                }
            }
        }
    }
}
