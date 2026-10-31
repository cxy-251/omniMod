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

            bool hasAccess = AggregateBoxRegistry.WorldHasAccessToSharedBox(world.id, effectiveParentWorld, GlobalItemCollector.CollectMode.Debris, isPocket) ||
                             AggregateBoxRegistry.WorldHasAccessToSharedBox(world.id, effectiveParentWorld, GlobalItemCollector.CollectMode.Edibles, isPocket);

            if (!hasAccess)
            {
                return;
            }

            // 扣除本世界已包含在原生 __result 中的本地聚合箱存量，绝不重复计入！
            float localBoxMass = 0f;
            AggregateBoxRegistry.TryGetWorldBoxMass(world.id, tag, out localBoxMass);
            float localNonBoxMass = UnityEngine.Mathf.Max(0f, __result - localBoxMass);

            float sharedMass = 0f;
            if (OmniPool.Instance != null)
            {
                sharedMass += OmniPool.Instance.GetMassMatchingTag(tag);
                sharedMass += OmniPool.Instance.GetSeedCount(tag);
            }

            // 获取全星系所有生效聚合箱该 Tag 真实总质量（去重星球，每个星球只算一次）
            sharedMass += AggregateBoxRegistry.GetSharedBoxMass(world.id, effectiveParentWorld, tag, isPocket);

            __result = localNonBoxMass + sharedMass;
        }
    }

    /// <summary>
    /// 当接入全星系共享箱时，inventory.GetAmount(tag, false) 已经完美包含了全共享池及当前世界库存。
    /// 拦截原版在母星与关联子世界（生态舱、停靠飞船）之间的重复累加循环，防止总数成倍虚高或舱内外不一致。
    /// </summary>
    [HarmonyPatch(typeof(ClusterUtil), nameof(ClusterUtil.GetAmountFromRelatedWorlds), new System.Type[] { typeof(WorldInventory), typeof(Tag) })]
    internal static class ClusterUtil_GetAmountFromRelatedWorlds_Patch
    {
        private static bool Prefix(WorldInventory inventory, Tag tag, ref float __result)
        {
            if (inventory == null || inventory.WorldContainer == null || !tag.IsValid)
            {
                return true;
            }

            WorldContainer world = inventory.WorldContainer;
            bool isPocket = OmniPocketHabitat.IsPocketHabitatWorld(world);
            int effectiveParent = isPocket ? OmniPocketHabitat.GetExteriorWorldId(world.id) : world.ParentWorldId;

            if (AggregateBoxRegistry.WorldHasAccessToSharedBox(world.id, effectiveParent, GlobalItemCollector.CollectMode.Debris, isPocket) ||
                AggregateBoxRegistry.WorldHasAccessToSharedBox(world.id, effectiveParent, GlobalItemCollector.CollectMode.Edibles, isPocket))
            {
                __result = inventory.GetTotalAmount(tag, false);
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(ClusterUtil), nameof(ClusterUtil.GetPickupablesFromRelatedWorlds), new System.Type[] { typeof(WorldInventory), typeof(Tag) })]
    internal static class ClusterUtil_GetPickupablesFromRelatedWorlds_Patch
    {
        private static void Postfix(WorldInventory inventory, Tag tag, ref List<Pickupable> __result)
        {
            if (inventory == null || inventory.WorldContainer == null || !tag.IsValid) return;

            WorldContainer world = inventory.WorldContainer;
            bool isPocket = OmniPocketHabitat.IsPocketHabitatWorld(world);
            int effectiveParent = isPocket ? OmniPocketHabitat.GetExteriorWorldId(world.id) : world.ParentWorldId;

            if (AggregateBoxRegistry.WorldHasAccessToSharedBox(world.id, effectiveParent, GlobalItemCollector.CollectMode.Debris, isPocket) ||
                AggregateBoxRegistry.WorldHasAccessToSharedBox(world.id, effectiveParent, GlobalItemCollector.CollectMode.Edibles, isPocket))
            {
                ICollection<Pickupable> col = __result;
                AggregateBoxRegistry.AppendPlanetBoxPickupables(world.id, effectiveParent, tag, ref col, isPocket);
                if (col != null && !ReferenceEquals(col, __result))
                {
                    __result = col as List<Pickupable> ?? new List<Pickupable>(col);
                }
            }

            if (__result == null || __result.Count <= 1) return;
            HashSet<Pickupable> seen = new HashSet<Pickupable>();
            for (int i = __result.Count - 1; i >= 0; i--)
            {
                Pickupable p = __result[i];
                if (p == null || p.gameObject == null || !seen.Add(p))
                {
                    __result.RemoveAt(i);
                }
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
            int effectiveParentWorld = isPocket ? OmniPocketHabitat.GetExteriorWorldId(world.id) : world.ParentWorldId;
            AggregateBoxRegistry.AppendPlanetBoxPickupables(world.id, effectiveParentWorld, tag, ref __result, isPocket);
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
