using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Collection;

namespace OmniMod.Pool
{
    /// <summary>
    /// 让所有建有聚合箱星球的建造栏、资源统计面板、农田选种界面（PlanterSideScreen）、
    /// 孵化器选蛋界面、展台界面、仿生人找电池传感器、卡路里统计等，
    /// 100% 看到并使用全局杂物池（OmniPool）以及「离散物品之家」（DiscreteHome）与「食物之家」（FoodHome）中的全部物品，
    /// 彻底消除按星球均分。
    /// </summary>
    [HarmonyPatch(typeof(WorldInventory), nameof(WorldInventory.GetTotalAmount))]
    internal static class WorldInventory_GetTotalAmount_Patch
    {
        private static void Postfix(WorldInventory __instance, Tag tag, bool includeRelatedWorlds, ref float __result)
        {
            WorldContainer world = __instance != null ? __instance.WorldContainer : null;
            if (world == null)
            {
                return;
            }

            // 当 ClusterUtil.GetAmountFromRelatedWorlds 遍历母星及停靠火箭（以 includeRelatedWorlds = false 逐个调用）时，
            // 只在母星（world.id == world.ParentWorldId）追加一次全局共享存量，防止停靠多艘火箭时重复累加。
            if (!includeRelatedWorlds && world.id != world.ParentWorldId)
            {
                return;
            }

            if (OmniPool.Instance != null && AggregateBoxRegistry.WorldHasActiveJunkBox(world.id))
            {
                float poolMass = OmniPool.Instance.GetMassMatchingTag(tag);
                if (poolMass > 0f)
                {
                    __result += poolMass;
                }
            }

            float sharedBoxMass = AggregateBoxRegistry.GetSharedRemoteBoxMass(world.id, world.ParentWorldId, tag);
            if (sharedBoxMass > 0f)
            {
                __result += sharedBoxMass;
            }
        }
    }

    [HarmonyPatch(typeof(WorldInventory), nameof(WorldInventory.GetPickupables))]
    internal static class WorldInventory_GetPickupables_Patch
    {
        private static void Postfix(WorldInventory __instance, Tag tag, bool includeRelatedWorlds, ref ICollection<Pickupable> __result)
        {
            // includeRelatedWorlds == true 时内部会调用 ClusterUtil.GetPickupablesFromRelatedWorlds，
            // 后者对每个关联世界以 includeRelatedWorlds = false 调用 GetPickupables。
            // 因此只在 !includeRelatedWorlds 且当前世界是母星（world.id == world.ParentWorldId）时追加一次跨星主箱物品。
            if (includeRelatedWorlds)
            {
                return;
            }

            WorldContainer world = __instance != null ? __instance.WorldContainer : null;
            if (world == null || world.id != world.ParentWorldId)
            {
                return;
            }

            AggregateBoxRegistry.AppendSharedRemoteBoxPickupables(world.id, world.ParentWorldId, tag, ref __result);
        }
    }

    [HarmonyPatch(typeof(WorldInventory), nameof(WorldInventory.CreatePickupablesList))]
    internal static class WorldInventory_CreatePickupablesList_Patch
    {
        private static void Postfix(WorldInventory __instance, Tag tag, ref List<Pickupable> __result)
        {
            WorldContainer world = __instance != null ? __instance.WorldContainer : null;
            if (world == null || world.id != world.ParentWorldId)
            {
                return;
            }

            ICollection<Pickupable> col = __result;
            AggregateBoxRegistry.AppendSharedRemoteBoxPickupables(world.id, world.ParentWorldId, tag, ref col);
            if (col != null && !ReferenceEquals(col, __result))
            {
                __result = col as List<Pickupable> ?? new List<Pickupable>(col);
            }
        }
    }
}
