using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using HarmonyLib;
using OmniMod.Collection;

namespace OmniMod.Pool
{
    /// <summary>界面适配仅组合原版本地库存与共享库存，不负责调拨或物化。</summary>
    [HarmonyPatch(typeof(WorldInventory), nameof(WorldInventory.GetTotalAmount))]
    internal static class WorldInventory_GetTotalAmount_Patch
    {
        private static readonly AccessTools.FieldRef<WorldInventory, Dictionary<Tag, HashSet<Pickupable>>> NativeInventory =
            AccessTools.FieldRefAccess<WorldInventory, Dictionary<Tag, HashSet<Pickupable>>>("Inventory");

        private sealed class CachedAmount { internal int Frame; internal long Revision; internal float Value; }
        private static readonly ConditionalWeakTable<WorldInventory, Dictionary<Tag, CachedAmount>> LocalCache =
            new ConditionalWeakTable<WorldInventory, Dictionary<Tag, CachedAmount>>();

        private static void Postfix(WorldInventory __instance, Tag tag, ref float __result)
        {
            WorldContainer world = __instance != null ? __instance.WorldContainer : null;
            if (world == null || !SharedBoxInventory.HasAccess(world.id)) return;
            // 原版 accessibleAmounts 按标签分帧更新，不能拿它减去“当前入口量”：迁移期间会重复或漏计。
            // 从原版已登记的本地集合读取本地非池实体；出库等候物仍属于池，只加一次全局账本。
            if (!Game.IsOnMainThread()) return;
            OmniEntityPool pool = OmniEntityPool.Instance;
            long revision = pool?.Revision ?? -1;
            Dictionary<Tag, CachedAmount> cache = LocalCache.GetValue(__instance, key => new Dictionary<Tag, CachedAmount>());
            if (cache.TryGetValue(tag, out CachedAmount cached) && cached.Frame == Time.frameCount && cached.Revision == revision)
            {
                __result = cached.Value + SharedBoxInventory.GetMass(world.id, tag) + SharedBoxInventory.GetVirtualMass(world.id, tag);
                return;
            }
            float local = 0f;
            if (NativeInventory(__instance).TryGetValue(tag, out HashSet<Pickupable> pickups))
                foreach (Pickupable pickup in pickups)
                    if (pickup != null && pickup.GetMyWorldId() == world.id &&
                        !pickup.KPrefabID.HasTag(GameTags.StoredPrivate) && (pool == null || !pool.Owns(pickup)))
                        local += pickup.TotalAmount;
            if (cached == null) cache[tag] = cached = new CachedAmount();
            cached.Frame = Time.frameCount; cached.Revision = revision; cached.Value = local;
            __result = local + SharedBoxInventory.GetMass(world.id, tag) + SharedBoxInventory.GetVirtualMass(world.id, tag);
        }
    }

    [HarmonyPatch(typeof(ClusterUtil), nameof(ClusterUtil.GetAmountFromRelatedWorlds), new Type[] { typeof(WorldInventory), typeof(Tag) })]
    internal static class ClusterUtil_GetAmountFromRelatedWorlds_Patch
    {
        private static bool Prefix(WorldInventory __0, Tag __1, ref float __result)
        {
            if (__0 == null || __0.WorldContainer == null || !__1.IsValid ||
                !SharedBoxInventory.HasAccess(__0.WorldContainer.id)) return true;
            __result = __0.GetTotalAmount(__1, false);
            return false;
        }
    }

    internal static class SharedPickupView
    {
        // 原版关联世界查询会逐世界调用 GetPickupables；仅在最外层合并一次共享池。
        [ThreadStatic] internal static int RelatedDepth;

        internal static bool IsUsable(Pickupable pickup, Tag tag)
        {
            if (pickup == null || pickup.KPrefabID == null || pickup.KPrefabID.pendingDestruction) return false;
            if (pickup.KPrefabID.PrefabTag != tag && !pickup.KPrefabID.HasTag(tag)) return false;
            if (tag != GameTags.Edible) return true;
            Edible edible = pickup.GetComponent<Edible>();
            return pickup.PrimaryElement != null && edible != null && edible.FoodInfo != null && !string.IsNullOrEmpty(edible.FoodID);
        }

        internal static void RemoveInvalid(Tag tag, ref ICollection<Pickupable> result)
        {
            if (result == null || !Game.IsOnMainThread()) return;
            bool invalid = false;
            foreach (Pickupable pickup in result)
                if (!IsUsable(pickup, tag)) { invalid = true; break; }
            if (!invalid) return;
            // 只替换查询视图，不在原版 HashSet 的枚举过程中删除实体或修改库存。
            var valid = new List<Pickupable>(result.Count);
            foreach (Pickupable pickup in result) if (IsUsable(pickup, tag)) valid.Add(pickup);
            result = valid;
        }

        internal static void Append(WorldInventory inventory, Tag tag, ref List<Pickupable> result)
        {
            WorldContainer world = inventory != null ? inventory.WorldContainer : null;
            if (world == null) return;
            ICollection<Pickupable> collection = result;
            SharedBoxInventory.AppendPickupables(world.id, tag, ref collection);
            if (!ReferenceEquals(collection, result))
                result = collection as List<Pickupable> ?? new List<Pickupable>(collection);
            // 原版关联世界列表可能还带着旧存档的重复或已销毁引用，压缩副本而不修改 Inventory 集合。
            if (result == null) return;
            var seen = new HashSet<Pickupable>();
            int write = 0;
            for (int i = 0; i < result.Count; i++)
            {
                Pickupable pickup = result[i];
                if (IsUsable(pickup, tag) && seen.Add(pickup)) result[write++] = pickup;
            }
            if (write < result.Count) result.RemoveRange(write, result.Count - write);
        }
    }

    [HarmonyPatch(typeof(ClusterUtil), nameof(ClusterUtil.GetPickupablesFromRelatedWorlds), new Type[] { typeof(WorldInventory), typeof(Tag), typeof(List<Pickupable>), }, new ArgumentType[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Ref })]
    internal static class ClusterUtil_GetPickupablesFromRelatedWorlds_IntoList_Patch
    {
        private static void Prefix(out bool __state)
        {
            __state = true;
            SharedPickupView.RelatedDepth++;
        }

        private static void Postfix(WorldInventory __0, Tag __1, ref List<Pickupable> __2)
        {
            if (SharedPickupView.RelatedDepth == 1) SharedPickupView.Append(__0, __1, ref __2);
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state) SharedPickupView.RelatedDepth--;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ClusterUtil), nameof(ClusterUtil.GetPickupablesFromRelatedWorlds), new Type[] { typeof(WorldInventory), typeof(Tag) })]
    internal static class ClusterUtil_GetPickupablesFromRelatedWorlds_Patch
    {
        private static void Prefix(out bool __state)
        {
            __state = true;
            SharedPickupView.RelatedDepth++;
        }

        private static void Postfix(WorldInventory __0, Tag __1, ref List<Pickupable> __result)
        {
            if (SharedPickupView.RelatedDepth == 1) SharedPickupView.Append(__0, __1, ref __result);
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state) SharedPickupView.RelatedDepth--;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(WorldInventory), nameof(WorldInventory.GetPickupables))]
    internal static class WorldInventory_GetPickupables_Patch
    {
        private static void Postfix(WorldInventory __instance, Tag tag, bool includeRelatedWorlds, ref ICollection<Pickupable> __result)
        {
            WorldContainer world = __instance != null ? __instance.WorldContainer : null;
            if (world != null && !includeRelatedWorlds && SharedPickupView.RelatedDepth == 0)
                SharedBoxInventory.AppendPickupables(world.id, tag, ref __result);
        }
    }

    [HarmonyPatch(typeof(WorldInventory), nameof(WorldInventory.CreatePickupablesList))]
    internal static class WorldInventory_CreatePickupablesList_Patch
    {
        private static void Postfix(WorldInventory __instance, Tag tag, ref List<Pickupable> __result)
        {
            if (SharedPickupView.RelatedDepth == 0) SharedPickupView.Append(__instance, tag, ref __result);
        }
    }
}
