using HarmonyLib;
using OmniMod.Core.Safety;
using UnityEngine;

namespace OmniMod.Ranching
{
    /// <summary>
    /// 孵化仓自动出雏与看门狗：
    /// 幼体孵化破壳后，自动弹出并打包（Baggable.SetWrangled），由机械臂就近转运，无需复制人抱取。
    /// </summary>
    [HarmonyPatch(typeof(EggIncubator), nameof(EggIncubator.StoreBaby))]
    internal static class EggIncubator_StoreBaby_Patch
    {
        private static void Postfix(EggIncubator __instance, GameObject baby)
        {
            if (__instance == null || baby == null) return;

            ExceptionShield.Execute("EggIncubator.StoreBaby", () =>
            {
                __instance.OrderRemoveOccupant();

                Baggable baggable = baby.GetComponent<Baggable>();
                if (baggable != null)
                {
                    baby.AddTag(GameTags.Creatures.Deliverable);
                    baggable.SetWrangled();
                }

                __instance.Trigger((int)GameHashes.OccupantChanged, null);
            });
        }
    }

    [HarmonyPatch(typeof(EggIncubator), "OnSpawn")]
    internal static class EggIncubator_OnSpawn_Watchdog_Patch
    {
        private static void Postfix(EggIncubator __instance)
        {
            if (__instance == null) return;

            ExceptionShield.Execute("EggIncubator.OnSpawn", () =>
            {
                GameObject occupant = __instance.Occupant;
                if (occupant != null && !occupant.HasTag(GameTags.Egg))
                {
                    __instance.OrderRemoveOccupant();

                    Baggable baggable = occupant.GetComponent<Baggable>();
                    if (baggable != null)
                    {
                        occupant.AddTag(GameTags.Creatures.Deliverable);
                        baggable.SetWrangled();
                    }

                    __instance.Trigger((int)GameHashes.OccupantChanged, null);
                }
            });
        }
    }
}
