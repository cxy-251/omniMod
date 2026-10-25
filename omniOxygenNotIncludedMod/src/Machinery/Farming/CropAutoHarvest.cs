using System;
using HarmonyLib;
using OmniMod.Core.Logging;
using OmniMod.Core.Safety;

namespace OmniMod.Machinery.Farming
{
    /// <summary>
    /// 农作物零人力收获：植物成熟瞬间自动产出果实，小人免跑腿收割
    /// </summary>
    [HarmonyPatch(typeof(Harvestable), nameof(Harvestable.SetCanBeHarvested))]
    internal static class Harvestable_SetCanBeHarvested_ZeroLabor_Patch
    {
        private static void Postfix(Harvestable __instance, bool state)
        {
            if (state && __instance != null)
            {
                Schedule(__instance);
            }
        }

        internal static void Schedule(Harvestable h)
        {
            if (h == null || GameScheduler.Instance == null)
            {
                return;
            }
            GameScheduler.Instance.Schedule("OmniZeroLaborHarvest", 0.12f, DoHarvest, h);
        }

        private static void DoHarvest(object obj)
        {
            Harvestable h = obj as Harvestable;
            if (h == null || h.gameObject == null || !h.CanBeHarvested)
            {
                return;
            }

            // 糖心树枝（SpaceTreeBranch）：树枝是光合作用器官，必须保留在树上吸收光照产出花蜜/糖水。
            // 若被自动收获当成木材秒砍，会导致树枝刚长成就消失，且因无成熟树枝而彻底无法产蜜。
            // 因此糖心树枝默认不自动收割（除非玩家手动点击单次标记收割木材）。
            if (h.PrefabID() == "SpaceTreeBranch")
            {
                HarvestDesignatable hd = h.GetComponent<HarvestDesignatable>();
                if (hd == null || !hd.MarkedForHarvest)
                {
                    return;
                }
            }

            ExceptionShield.Execute("CropAutoHarvest.DoHarvest", () =>
            {
                h.Harvest();
                ModLog.Throttle($"harvest_{h.PrefabID().Name}", $"零人力收获: {h.name}", 15f);
            });
        }
    }

    [HarmonyPatch(typeof(Harvestable), "OnSpawn")]
    internal static class Harvestable_OnSpawn_ZeroLabor_Patch
    {
        private static void Postfix(Harvestable __instance)
        {
            if (__instance != null && __instance.CanBeHarvested)
            {
                Harvestable_SetCanBeHarvested_ZeroLabor_Patch.Schedule(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Harvestable), nameof(Harvestable.OnMarkedForHarvest))]
    internal static class Harvestable_OnMarkedForHarvest_NoChore_Patch
    {
        private static bool Prefix()
        {
            return false;
        }
    }
}
