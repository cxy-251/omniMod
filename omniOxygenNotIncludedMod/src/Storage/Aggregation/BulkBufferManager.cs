using System;
using System.Collections.Generic;
using OmniMod.Core.Logging;
using OmniMod.Pool;
using UnityEngine;

namespace OmniMod.Collection
{
    /// <summary>材料统一保存在数值池；门户中的实际材料是按任务准备的出库实体，不再每世界预存四吨。</summary>
    public static class BulkBufferManager
    {
        public const byte NoDisease = byte.MaxValue;

        public static bool IsBulkSolidElementChunk(Pickupable pickup)
        {
            PrimaryElement primary = pickup != null ? pickup.PrimaryElement : null;
            Element element = primary != null ? primary.Element : null;
            return element != null && element.IsSolid && pickup.KPrefabID != null && pickup.KPrefabID.PrefabTag == element.tag;
        }

        public static void InsulateStoredItems(Storage storage)
        {
            if (storage == null) return;
            foreach (GameObject item in storage.items)
                if (item != null) Storage.MakeItemTemperatureInsulated(item, true, false);
        }

        public static void MigrateOwnStorageToPool(GlobalItemCollector collector, Storage storage)
        {
            // 新旧门户材料均由统一归属账本接管，随后分片回池；不在读档时扫描、销毁或批量生成碎块。
            if (collector != null && !collector.IsRedundant && collector.mode == GlobalItemCollector.CollectMode.Debris)
                OmniEntityPool.Instance?.AdoptPortal(storage);
        }

        /// <summary>只归还该实体未被任务预约的质量；已经被搬运的份额保持原对象与预约。</summary>
        internal static bool ReturnIdleChunkToPool(Pickupable pickup)
        {
            OmniPool pool = OmniPool.Instance;
            if (pool == null || !IsBulkSolidElementChunk(pickup) || pickup.storage == null ||
                !AggregateBoxRegistry.TryGetBox(pickup.storage, out _)) return false;
            Storage storage = pickup.storage;
            PrimaryElement primary = pickup.PrimaryElement;
            float amount = Mathf.Max(0f, primary.Mass - pickup.ReservedAmount);
            if (amount <= 0f) return false;
            SimHashes element = primary.ElementID;
            float temperature = primary.Temperature;
            // 在通知其它查询之前维护同一笔质量的归属；只从一边扣除一次，再写入数值池。
            if (pickup.ReservedAmount <= 0f)
            {
                storage.Remove(pickup.gameObject, do_disease_transfer: false);
                pickup.storage = null;
                pool.AddSolid(element, amount, temperature);
                Util.KDestroyGameObject(pickup.gameObject);
            }
            else
            {
                primary.Mass -= amount;
                OmniEntityPool.Instance?.Observe(pickup);
                pool.AddSolid(element, amount, temperature);
            }
            StorageBatchHelper.PublishChanges(storage);
            return true;
        }

        /// <summary>只为一次具体任务从一个温度桶出库；不预铺、不混入正在使用的其它材料堆。</summary>
        public static Pickupable MaterializeSolidFromPool(GlobalItemCollector collector, SimHashes element,
            float neededMass = 1000f, object request = null)
        {
            OmniPool pool = OmniPool.Instance;
            if (collector == null || collector.IsRedundant || collector.BoxStorage == null || pool == null ||
                !(neededMass > 0f) || float.IsNaN(neededMass) || float.IsInfinity(neededMass)) return null;
            Element substance = ElementLoader.FindElementByHash(element);
            if (substance == null || !substance.IsSolid || substance.substance == null) return null;
            float taken = pool.RemoveSolid(element, Mathf.Min(neededMass, pool.GetMass(element)), out float temperature);
            if (!(taken > 0f)) return null;
            GameObject item = null;
            bool stored = false;
            try
            {
                item = substance.substance.SpawnResource(collector.transform.GetPosition(), taken, temperature,
                    NoDisease, 0, prevent_merge: true, forceTemperature: false, manual_activation: true);
                if (item == null) throw new InvalidOperationException("材料预制体未生成");
                Pickupable pickup = item.GetComponent<Pickupable>();
                if (pickup == null) throw new InvalidOperationException("材料没有 Pickupable");
                pickup.prevent_absorb_until_stored = true;
                substance.substance.ActivateSubstanceGameObject(item, NoDisease, 0);
                GameObject result = StorageBatchHelper.Store(collector.BoxStorage, item);
                if (result != item) throw new InvalidOperationException("材料门户意外合堆");
                stored = true;
                OmniEntityPool.Instance?.HoldMaterial(pickup, request);
                StorageBatchHelper.PublishChanges(collector.BoxStorage, increased: true);
                return pickup;
            }
            catch (Exception ex)
            {
                // 实物仍在入口时保留它，不把同一份质量再次加回数值池。
                Pickupable pickup = item != null ? item.GetComponent<Pickupable>() : null;
                if (stored || (pickup != null && pickup.storage == collector.BoxStorage))
                {
                    OmniEntityPool.Instance?.HoldMaterial(pickup, request);
                    ModLog.Throttle("material_checkout_notify", "[材料共享] 物品已出库，通知稍后刷新：" + ex, 10f);
                    return pickup;
                }
                if (item != null)
                {
                    collector.BoxStorage.items.Remove(item);
                    Util.KDestroyGameObject(item);
                }
                pool.AddSolid(element, taken, temperature);
                ModLog.Throttle("material_checkout_failed", "[材料共享] 出库失败，已归还原质量：" + ex, 10f);
                return null;
            }
        }
    }
}
