using System.Collections.Generic;
using OmniMod.Pool;
using UnityEngine;

namespace OmniMod.Collection
{
    /// <summary>门户授予访问权；实体总量只向唯一归属账本读取，不再累加星球箱库存。</summary>
    internal static class SharedBoxInventory
    {
        internal static bool HasAccess(int world, GlobalItemCollector.CollectMode mode) =>
            AggregateBoxRegistry.GetActiveBoxForWorld(world, mode) != null;
        internal static bool HasAccess(int world) => AccessMask(world) != 0;
        internal static int AccessMask(int world) =>
            (HasAccess(world, GlobalItemCollector.CollectMode.Debris) ? 1 : 0) |
            (HasAccess(world, GlobalItemCollector.CollectMode.Edibles) ? 2 : 0);
        internal static void Invalidate() { }
        internal static float GetMass(int world, Tag tag) => OmniEntityPool.Instance?.GetAmount(AccessMask(world), tag) ?? 0f;
        internal static float GetMassForMode(GlobalItemCollector.CollectMode mode, Tag tag) =>
            OmniEntityPool.Instance?.GetMass(1 << (int)mode, tag) ?? 0f;
        internal static float GetVirtualMass(int world, Tag tag)
        {
            OmniPool pool = OmniPool.Instance;
            return pool != null && HasAccess(world, GlobalItemCollector.CollectMode.Debris)
                ? pool.GetMassMatchingTag(tag) + pool.GetSeedCount(tag) : 0f;
        }
        internal static bool HasItem(int world, Tag tag) =>
            HasAccess(world) && (GetVirtualMass(world, tag) > 0f || GetMass(world, tag) > 0f);
        internal static float GetPortalOwnedMass(Storage storage, Tag tag)
        {
            OmniEntityPool pool = OmniEntityPool.Instance;
            if (pool == null || storage == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < storage.items.Count; i++)
            {
                GameObject item = storage.items[i];
                Pickupable pickup = item != null ? item.GetComponent<Pickupable>() : null;
                if (pickup != null && pool.Owns(pickup) && pickup.KPrefabID.HasTag(tag)) sum += pickup.PrimaryElement.Mass;
            }
            return sum;
        }
        internal static void AppendPickupables(int world, Tag tag, ref ICollection<Pickupable> result)
        {
            if (!Game.IsOnMainThread() || !tag.IsValid) return;
            int access = AccessMask(world);
            OmniEntityPool pool = OmniEntityPool.Instance;
            if (access == 0 || pool == null)
            { SharedPickupView.RemoveInvalid(tag, ref result); return; }
            var combined = new HashSet<Pickupable>();
            if (result != null)
                foreach (Pickupable pickup in result)
                    if (SharedPickupView.IsUsable(pickup, tag)) combined.Add(pickup);
            pool.Append(access, tag, combined);
            // 全部失效时也要发布空视图，不能继续返回未过滤的原列表。
            result = combined;
        }
    }
}
