using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Buildings;
using OmniMod.Collection;

namespace OmniMod.Pool
{
    /// <summary>
    /// 让所有建有聚合箱星球及折叠生态舱内部的建造栏、资源统计面板、农田选种界面（PlanterSideScreen）、
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

            bool isPocket = OmniPocketHabitat.IsPocketHabitatWorld(world);
            int effectiveParentWorld = isPocket ? OmniPocketHabitat.GetExteriorWorldId(world.id) : world.ParentWorldId;

            // 当 ClusterUtil.GetAmountFromRelatedWorlds 遍历母星及停靠火箭（以 includeRelatedWorlds = false 逐个调用）时，
            // 只在母星（world.id == world.ParentWorldId）追加一次全局共享存量，防止停靠多艘火箭时重复累加。
            // 但如果当前属于生态舱内部，始终允许追加！
            if (!includeRelatedWorlds && !isPocket && world.id != world.ParentWorldId)
            {
                return;
            }

            bool hasAccess = isPocket || AggregateBoxRegistry.WorldHasActiveJunkBox(world.id) ||
                             (effectiveParentWorld >= 0 && AggregateBoxRegistry.WorldHasActiveJunkBox(effectiveParentWorld));

            if (OmniPool.Instance != null && hasAccess)
            {
                float poolMass = OmniPool.Instance.GetMassMatchingTag(tag);
                if (poolMass > 0f)
                {
                    __result += poolMass;
                }

                int seedCount = OmniPool.Instance.GetSeedCount(tag);
                if (seedCount > 0)
                {
                    __result += seedCount;
                }
            }

            float sharedBoxMass = AggregateBoxRegistry.GetSharedRemoteBoxMass(world.id, effectiveParentWorld, tag, isPocket);
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
            WorldContainer world = __instance != null ? __instance.WorldContainer : null;
            if (world == null)
            {
                return;
            }

            bool isPocket = OmniPocketHabitat.IsPocketHabitatWorld(world);
            if (includeRelatedWorlds && !isPocket)
            {
                return;
            }

            if (!isPocket && world.id != world.ParentWorldId)
            {
                return;
            }

            int effectiveParentWorld = isPocket ? OmniPocketHabitat.GetExteriorWorldId(world.id) : world.ParentWorldId;
            AggregateBoxRegistry.AppendSharedRemoteBoxPickupables(world.id, effectiveParentWorld, tag, ref __result, isPocket);
        }
    }

    [HarmonyPatch(typeof(WorldInventory), nameof(WorldInventory.CreatePickupablesList))]
    internal static class WorldInventory_CreatePickupablesList_Patch
    {
        private static void Postfix(WorldInventory __instance, Tag tag, ref List<Pickupable> __result)
        {
            WorldContainer world = __instance != null ? __instance.WorldContainer : null;
            if (world == null)
            {
                return;
            }

            bool isPocket = OmniPocketHabitat.IsPocketHabitatWorld(world);
            if (!isPocket && world.id != world.ParentWorldId)
            {
                return;
            }

            int effectiveParentWorld = isPocket ? OmniPocketHabitat.GetExteriorWorldId(world.id) : world.ParentWorldId;
            ICollection<Pickupable> col = __result;
            AggregateBoxRegistry.AppendSharedRemoteBoxPickupables(world.id, effectiveParentWorld, tag, ref col, isPocket);
            if (col != null && !ReferenceEquals(col, __result))
            {
                __result = col as List<Pickupable> ?? new List<Pickupable>(col);
            }
        }
    }
}
