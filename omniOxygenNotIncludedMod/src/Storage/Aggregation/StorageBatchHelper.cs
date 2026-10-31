using HarmonyLib;
using UnityEngine;

namespace OmniMod.Collection
{
    internal static class StorageBatchHelper
    {
        private static readonly AccessTools.FieldRef<Storage, System.Action> StorageIncreased =
            AccessTools.FieldRefAccess<Storage, System.Action>("OnStorageIncreased");

        /// <summary>合并容器通知时仍逐物品发送 OnStore，维护归属、Stored 标签与空间索引。</summary>
        internal static GameObject Store(Storage storage, GameObject item)
        {
            GameObject stored = storage.Store(item, hide_popups: true, block_events: true, do_disease_transfer: false);
            // 合并入现有堆叠时返回存活的旧物体；只有新入库物体需要初始化入库状态。
            if (stored != null && stored == item)
            {
                stored.Trigger((int)GameHashes.OnStore, storage);
            }
            return stored;
        }

        internal static void PublishChanges(Storage storage, bool increased = false)
        {
            if (storage == null) return;
            storage.items.RemoveAll(item => item == null);
            AggregateBoxRegistry.MarkWorldDirty(storage.GetMyWorldId());
            OmniMod.Logistics.RemoteFetch.SolidTransferArm_FindFetchTarget_Patch.InvalidateFailedCache();
            storage.Trigger((int)GameHashes.OnStorageChange, storage.gameObject);
            storage.OnStorageChange?.Invoke(storage.gameObject);
            storage.Trigger((int)GameHashes.OnStorageInteracted, storage);
            if (increased) StorageIncreased(storage)?.Invoke();
        }

        /// <summary>接管旧版静默入库留下的物品，仅恢复仍实际挂在该容器下的实体。</summary>
        internal static void RepairStoredItems(Storage storage)
        {
            if (storage == null || storage.items == null) return;
            storage.items.RemoveAll(item => item == null);
            for (int i = 0; i < storage.items.Count; i++)
            {
                GameObject item = storage.items[i];
                Pickupable pickup = item.GetComponent<Pickupable>();
                if (pickup != null && pickup.storage == null && item.transform.parent == storage.transform)
                {
                    item.Trigger((int)GameHashes.OnStore, storage);
                }
            }
        }
    }
}
