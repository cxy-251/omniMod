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
