using HarmonyLib;
using OmniMod.Core.Logging;
using OmniMod.Core.Safety;
using OmniMod.Ranching.Registries;
using UnityEngine;

namespace OmniMod.Ranching
{
    /// <summary>沿用原版房间、寻路、筛选和数量判定，只替代复制人在到站后的捕捉工作。</summary>
    internal static class AutoWrangleHelper
    {
        internal static bool IsEnabled(FixedCapturePoint.Instance point) =>
            !point.IsNullOrStopped() && point.GetAutomated() && FixedCapturePoint.ShouldBeOn(point);

        internal static bool IsEligible(FixedCapturePoint.Instance point, FixedCapturableMonitor.Instance target)
        {
            if (!IsEnabled(point) || target.IsNullOrStopped() || target.gameObject == null ||
                point.targetCapturable != target || target.targetCapturePoint != point ||
                target.GetComponent<Baggable>() == null || target.HasTag(GameTags.Creatures.Bagged)) return false;
            Pickupable pickup = target.GetComponent<Pickupable>();
            if (pickup != null && pickup.storage != null) return false;
            TreeFilterable filter = point.GetComponent<TreeFilterable>();
            if (filter != null && !filter.ContainsTag(target.PrefabTag)) return false;
            // 不覆盖原版 CanCapturable...：同房间、幼体开关、优先级、可达性和超量回调全部生效。
            return point.IsCreatureAvailableForFixedCapture();
        }

        internal static void AutoBagCreature(FixedCapturableMonitor.Instance target, FixedCapturePoint.Instance point)
        {
            if (!IsEligible(point, target)) return;
            GameObject go = target.gameObject;
            Baggable bag = go.GetComponent<Baggable>();

            // 调用者已离开动物状态 Enter 的调用栈；先退出捕捉任务，再结算与释放站点。
            var driver = go.GetComponent<ChoreDriver>();
            if (driver != null && driver.HasChore()) driver.StopChore();
            go.GetComponent<Capturable>()?.MarkForCapture(false);
            bag.SetWrangled();

            // 仅保留原版到站后的出料偏移（例如水生取出点）；禁止远程传送到取出点。
            if (point.def.postCaptureOffset.HasValue)
            {
                int cell = Grid.OffsetCell(Grid.PosToCell(point), point.def.postCaptureOffset.Value);
                if (Grid.IsValidCell(cell))
                {
                    go.transform.SetPosition(Grid.CellToPosCCC(cell, Grid.SceneLayer.Creatures));
                    go.GetComponent<Pickupable>()?.UpdateCachedCell(cell);
                }
            }
            go.AddTag(GameTags.Creatures.Deliverable);
            BaggedCreatureRegistry.Register(bag);
            point.isCurrentlyCapturingCreature = false;
            point.ClearRancherIsAvailableForCapturing();
            if (point.targetCapturable == target) point.ResetCapturePoint();
            ModLog.DebugLog($"小动物取出点：到站后自动打包 {go.name}");
        }
    }

    /// <summary>自动捕捉只在模拟回调结算，避免在动物状态入站时 StopChore 引起重入。</summary>
    [SkipSaveFileSerialization]
    public sealed class DeferredCritterCapture : KMonoBehaviour, ISim200ms
    {
        private static readonly AccessTools.FieldRef<FixedCaptureStates, FixedCaptureStates.CaptureStates> Capture =
            AccessTools.FieldRefAccess<FixedCaptureStates, FixedCaptureStates.CaptureStates>("capture");
        private FixedCapturePoint.Instance point;
        private FixedCapturableMonitor.Instance selected;

        internal void Refresh(FixedCapturePoint.Instance station)
        {
            point = station;
            var target = station.targetCapturable;
            if (target.IsNullOrStopped())
            {
                station.ClearRancherIsAvailableForCapturing();
                selected = null;
                return;
            }
            if (!AutoWrangleHelper.IsEnabled(station)) return;
            station.SetRancherIsAvailableForCapturing();
            if (selected == target) return;
            selected = target;
            CreatureBrain brain = target.GetComponent<CreatureBrain>();
            if (brain != null) Game.BrainScheduler?.PrioritizeBrain(brain);
            target.GetComponent<ChoreConsumer>()?.Trigger((int)GameHashes.ChoreInterrupt);
        }

        public void Sim200ms(float dt)
        {
            if (App.isLoading || Game.Instance == null || point.IsNullOrStopped()) return;
            var target = point.targetCapturable;
            if (target.IsNullOrStopped()) { selected = null; return; }
            ExceptionShield.Execute("FixedCapturePoint.DeferredCapture", () =>
            {
                if (!AutoWrangleHelper.IsEnabled(point))
                {
                    // 只撤销本组件吸引的目标；手动捕捉仍由原版复制人任务管理。
                    if (selected != target) return;
                    point.ClearRancherIsAvailableForCapturing();
                    point.isCurrentlyCapturingCreature = false;
                    point.ResetCapturePoint();
                    selected = null;
                    return;
                }
                var state = target.GetSMI<FixedCaptureStates.Instance>();
                if (state.IsNullOrStopped() || state.GetCapturePoint() != point) return;
                var capture = Capture(state.sm);
                // 必须先通过原版 MoveTo 到站；近距离、等待超时都不能替代实际抵达。
                if (!state.IsInsideState(capture.move.waitforranchertobeready) &&
                    !state.IsInsideState(capture.ranching)) return;
                if (!AutoWrangleHelper.IsEligible(point, target))
                {
                    point.ClearRancherIsAvailableForCapturing();
                    point.isCurrentlyCapturingCreature = false;
                    point.ResetCapturePoint();
                    selected = null;
                    return;
                }
                AutoWrangleHelper.AutoBagCreature(target, point);
                selected = null;
            });
        }
    }

    [HarmonyPatch(typeof(FixedCapturePoint.Instance), nameof(FixedCapturePoint.Instance.FindFixedCapturable))]
    internal static class FixedCapturePoint_FindFixedCapturable_Patch
    {
        private static void Postfix(FixedCapturePoint.Instance __instance)
        {
            if (__instance == null || __instance.gameObject == null) return;
            __instance.gameObject.AddOrGet<DeferredCritterCapture>().Refresh(__instance);
        }
    }

    [HarmonyPatch(typeof(FixedCapturePoint.Instance), nameof(FixedCapturePoint.Instance.StartSM))]
    internal static class FixedCapturePoint_StartSM_Patch
    {
        private static void Postfix(FixedCapturePoint.Instance __instance)
        {
            if (__instance?.gameObject != null)
                __instance.gameObject.AddOrGet<DeferredCritterCapture>().Refresh(__instance);
        }
    }

    /// <summary>自动模式由模拟回调完成捕捉，不额外派复制人；保留手动捕捉任务。</summary>
    [HarmonyPatch(typeof(FixedCapturePoint.Instance), nameof(FixedCapturePoint.Instance.CreateChore))]
    internal static class FixedCapturePoint_CreateChore_Patch
    {
        private static void Postfix(FixedCapturePoint.Instance __instance, ref Chore __result)
        {
            if (__result == null || !__instance.GetAutomated()) return;
            __result.AddPrecondition(new Chore.Precondition
            {
                id = "OmniMod_PreventDuplicantCapture",
                description = "OmniMod Automated Capture",
                fn = (ref Chore.Precondition.Context context, object data) => false
            });
            __result.showAvailabilityInHoverText = false;
        }
    }

    [HarmonyPatch(typeof(Game), "OnDestroy")]
    internal static class AutoRanch_Game_OnDestroy_Patch
    {
        private static void Postfix()
        {
            CritterRoomAccounting.Reset();
            ComfortRanchPolicy.Reset();
            BaggedCreatureRegistry.Clear();
            CreatureDeliveryRegistry.Clear();
        }
    }
}
