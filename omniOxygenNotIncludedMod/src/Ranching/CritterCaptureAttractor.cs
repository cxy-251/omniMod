using HarmonyLib;
using OmniMod.Core.Safety;
using OmniMod.Ranching.Registries;
using UnityEngine;

namespace OmniMod.Ranching
{
    /// <summary>
    /// 动物移出点自动吸引与秒打包：
    /// 移出点设定条件后，目标小动物自动被吸引走近，并在 2 格范围内瞬间自动打包成袋装动物，供机械臂隔空抓取。
    /// </summary>
    [HarmonyPatch(typeof(FixedCapturePoint.Instance), nameof(FixedCapturePoint.Instance.FindFixedCapturable))]
    internal static class FixedCapturePoint_FindFixedCapturable_Patch
    {
        private static readonly AccessTools.FieldRef<FixedCapturePoint.Instance, int> CaptureCellRef =
            AccessTools.FieldRefAccess<FixedCapturePoint.Instance, int>("captureCell");

        private static void Postfix(FixedCapturePoint.Instance __instance)
        {
            if (__instance == null || !__instance.IsOperational) return;

            ExceptionShield.Execute("FixedCapturePoint.FindFixedCapturable", () =>
            {
                var target = __instance.targetCapturable;
                if (target != null && !target.IsNullOrStopped() && target.gameObject != null)
                {
                    __instance.SetRancherIsAvailableForCapturing();

                    int creatureCell = Grid.PosToCell(target.transform.GetPosition());
                    int captureCell = CaptureCellRef(__instance);

                    if (Grid.IsValidCell(creatureCell) && Grid.IsValidCell(captureCell))
                    {
                        int dist = Grid.GetCellDistance(creatureCell, captureCell);
                        if (dist <= 2)
                        {
                            AutoWrangleTargetCreature(__instance);
                        }
                    }
                }
            });
        }

        internal static void AutoWrangleTargetCreature(FixedCapturePoint.Instance smi)
        {
            if (smi == null || smi.targetCapturable == null || smi.targetCapturable.IsNullOrStopped()) return;
            GameObject go = smi.targetCapturable.gameObject;
            if (go == null) return;

            Capturable capturable = go.GetComponent<Capturable>();
            if (capturable != null)
            {
                capturable.MarkForCapture(false);
            }
            Baggable baggable = go.GetComponent<Baggable>();
            if (baggable != null)
            {
                go.AddTag(GameTags.Creatures.Deliverable);
                baggable.SetWrangled();
            }
            smi.ResetCapturePoint();
        }
    }

    [HarmonyPatch(typeof(FixedCapturePoint.Instance), nameof(FixedCapturePoint.Instance.StartSM))]
    internal static class FixedCapturePoint_StartSM_Patch
    {
        private static void Postfix(FixedCapturePoint.Instance __instance)
        {
            if (__instance == null || __instance.gameObject == null) return;
            __instance.Subscribe((int)GameHashes.CreatureArrivedAtCapturePoint, (data) =>
            {
                FixedCapturePoint_FindFixedCapturable_Patch.AutoWrangleTargetCreature(__instance);
            });
        }
    }

    [HarmonyPatch(typeof(FixedCapturePoint.Instance), nameof(FixedCapturePoint.Instance.CreateChore))]
    internal static class FixedCapturePoint_CreateChore_Patch
    {
        private static void Postfix(ref Chore __result)
        {
            if (__result != null)
            {
                __result.AddPrecondition(new Chore.Precondition
                {
                    id = "OmniMod_PreventDuplicantCapture",
                    description = "OmniMod Automated Capture",
                    fn = (ref Chore.Precondition.Context context, object data) => false
                });
                __result.showAvailabilityInHoverText = false;
            }
        }
    }


    [HarmonyPatch(typeof(Game), "OnDestroy")]
    internal static class AutoRanch_Game_OnDestroy_Patch
    {
        private static void Postfix()
        {
            BaggedCreatureRegistry.Clear();
            CreatureDeliveryRegistry.Clear();
        }
    }
}
