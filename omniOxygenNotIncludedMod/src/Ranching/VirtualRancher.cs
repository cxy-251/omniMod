using System;
using System.Collections.Generic;
using HarmonyLib;
using Klei.AI;
using OmniMod.Core.Logging;
using OmniMod.Core.Safety;
using UnityEngine;

namespace OmniMod.Ranching
{
    /// <summary>
    /// 全自动小动物照料站、挤奶站、剪毛站虚拟小人（免人工无人化养殖）
    /// </summary>
    internal class OmniVirtualRancher : WorkerBase
    {
        private Attributes attributes;
        private AttributeModifier ranchingBonus;
        private static int skillFrame = -1;
        private static float bestSkill = 15f;

        public override Attributes GetAttributes()
        {
            var ranchingAttr = Db.Get().Attributes.Ranching;
            if (skillFrame != Time.frameCount)
            {
                skillFrame = Time.frameCount;
                bestSkill = 15f;
                if (Components.LiveMinionIdentities != null)
                {
                    for (int i = 0; i < Components.LiveMinionIdentities.Count; i++)
                    {
                        var minion = Components.LiveMinionIdentities[i];
                        if (minion == null) continue;
                        var attr = minion.GetAttributes()?.Get(ranchingAttr.Id);
                        if (attr != null) bestSkill = Mathf.Max(bestSkill, attr.GetTotalValue());
                    }
                }
            }
            if (attributes == null)
            {
                attributes = new Attributes(gameObject);
                ranchingBonus = new AttributeModifier(ranchingAttr.Id, bestSkill, "OmniMod Automated Rancher", is_readonly: false);
                attributes.Add(ranchingAttr).Add(ranchingBonus);
            }
            else ranchingBonus.SetValue(bestSkill);
            return attributes;
        }

        public override AttributeConverterInstance GetAttributeConverter(string id) => null;
        public override KBatchedAnimController GetAnimController() => GetComponent<KBatchedAnimController>();
        public override State GetState() => State.Idle;
        public override StartWorkInfo GetStartWorkInfo() => null;
        public override Workable GetWorkable() => null;
        public override bool InstantlyFinish() => true;
        public override bool IsFetchDrone() => false;
        public override Guid OfferStatusItem(StatusItem item, object data = null) => Guid.Empty;
        public override void RevokeStatusItem(Guid id) { }
        public override void SetWorkCompleteData(object data) { }
        public override void StartWork(StartWorkInfo start_work_info) { }
        public override void StopWork() { }
        public override bool UsesMultiTool() => false;
        public override WorkResult Work(float dt) => WorkResult.Success;
    }

    internal static class AutoRanchTracker
    {
        private static readonly HashSet<int> _calledCritterIds = new HashSet<int>();

        internal static bool HasBeenCalled(int id) => _calledCritterIds.Contains(id);
        internal static void MarkCalled(int id) => _calledCritterIds.Add(id);
        internal static void Clear(int id) => _calledCritterIds.Remove(id);
    }

    internal static class AutoRanchHelper
    {
        private static readonly AccessTools.FieldRef<RanchStation.Instance, WorkerBase> RancherField =
            AccessTools.FieldRefAccess<RanchStation.Instance, WorkerBase>("rancher");

        internal static bool IsStationOperational(RanchStation.Instance smi)
        {
            if (smi == null || !smi.IsRunning()) return false;
            Operational op = smi.GetComponent<Operational>();
            if (op != null && !op.IsOperational) return false;
            if (smi.sm != null && smi.sm.Operational != null)
            {
                return smi.IsInsideState(smi.sm.Operational);
            }
            return true;
        }

        internal static void EnsureVirtualRancher(RanchStation.Instance smi)
        {
            if (smi == null || smi.gameObject == null) return;

            OmniVirtualRancher worker = smi.gameObject.AddOrGet<OmniVirtualRancher>();
            WorkerBase current = RancherField(smi);
            if (current == null || current != worker)
            {
                RancherField(smi) = worker;
            }
        }
    }

    [HarmonyPatch(typeof(RanchStation.Instance), "CreateChore")]
    internal static class RanchStation_CreateChore_Patch
    {
        private static void Postfix(RanchStation.Instance __instance, ref Chore __result)
        {
            Chore chore = __result;
            ExceptionShield.Execute("RanchStation.CreateChore", () =>
            {
                if (chore != null)
                {
                    chore.AddPrecondition(new Chore.Precondition
                    {
                        id = "OmniMod_PreventDuplicantRanching",
                        description = "OmniMod Automated Ranching",
                        fn = (ref Chore.Precondition.Context context, object data) => false
                    });
                    chore.showAvailabilityInHoverText = false;
                }
                AutoRanchHelper.EnsureVirtualRancher(__instance);
                ModLog.Throttle($"ranch_create_{__instance.gameObject.name}", $"照料站免人工生效：{__instance.gameObject.name} 任务已屏蔽复制人", 30f);
            });
        }
    }

    [HarmonyPatch(typeof(RanchStation.Instance), "OnRancherChanged")]
    internal static class RanchStation_OnRancherChanged_Patch
    {
        private static void Postfix(RanchStation.Instance __instance)
        {
            AutoRanchHelper.EnsureVirtualRancher(__instance);
        }
    }

    [HarmonyPatch(typeof(RanchStation.Instance), "get_HasRancher")]
    internal static class RanchStation_HasRancher_Patch
    {
        private static void Postfix(RanchStation.Instance __instance, ref bool __result)
        {
            if (AutoRanchHelper.IsStationOperational(__instance))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(RanchStation.Instance), "get_IsRancherReady")]
    internal static class RanchStation_IsRancherReady_Patch
    {
        private static void Postfix(RanchStation.Instance __instance, ref bool __result)
        {
            if (AutoRanchHelper.IsStationOperational(__instance))
            {
                __result = (__instance.ActiveRanchable == null);
            }
        }
    }

    [HarmonyPatch(typeof(RanchStation.Instance), "StartSM")]
    internal static class RanchStation_StartSM_Patch
    {
        private static void Postfix(RanchStation.Instance __instance)
        {
            AutoRanchHelper.EnsureVirtualRancher(__instance);
            if (AquaticRanchAutoRunner.IsAquaticStation(__instance.gameObject))
                __instance.gameObject.AddOrGet<AquaticRanchAutoRunner>();
        }
    }

    [HarmonyPatch(typeof(RanchStation.Instance), "GetStationCavity")]
    internal static class RanchStation_GetStationCavity_Patch
    {
        private static void Postfix(RanchStation.Instance __instance, ref CavityInfo __result)
        {
            if (__result == null && __instance != null)
            {
                __result = Game.Instance?.roomProber?.GetCavityForCell(__instance.GetRanchNavTarget());
            }
        }
    }

    [HarmonyPatch(typeof(RanchStation.Instance), "FindRanchable")]
    internal static class RanchStation_FindRanchable_Patch
    {
        private static readonly AccessTools.FieldRef<RanchStation.Instance, List<RanchableMonitor.Instance>> TargetRanchablesField =
            AccessTools.FieldRefAccess<RanchStation.Instance, List<RanchableMonitor.Instance>>("targetRanchables");

        private static void Postfix(RanchStation.Instance __instance)
        {
            if (!AutoRanchHelper.IsStationOperational(__instance))
            {
                return;
            }

            AutoRanchHelper.EnsureVirtualRancher(__instance);

            // 预约或入站过程中不结算；MessageCreatureArrived 只安排后续模拟回调。
            if (__instance.ActiveRanchable != null) return;

            List<RanchableMonitor.Instance> list = TargetRanchablesField(__instance);
            if (list == null || list.Count == 0)
            {
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                var critter = list[i];
                if (critter == null || critter.IsNullOrStopped() || critter.gameObject == null)
                {
                    continue;
                }

                int id = critter.gameObject.GetInstanceID();
                if (!AutoRanchTracker.HasBeenCalled(id))
                {
                    AutoRanchTracker.MarkCalled(id);
                    __instance.sm.RancherIsReady.Set(true, __instance);
                    Game.BrainScheduler?.PrioritizeBrain(critter.GetComponent<CreatureBrain>());
                    critter.Trigger((int)GameHashes.RancherReadyAtRanchStation);
                }
            }
        }
    }

    [HarmonyPatch(typeof(RanchStation.Instance), "CanRanchableBeRanchedAtRanchStation")]
    internal static class RanchStation_CanRanchableBeRanchedAtRanchStation_Patch
    {
        private static void Prefix(RanchStation.Instance __instance, RanchableMonitor.Instance ranchable)
        {
            if (ranchable == null || ranchable.IsNullOrStopped() || __instance == null)
            {
                return;
            }

            try
            {
                var otherStation = ranchable.TargetRanchStation;
                if (otherStation != null && otherStation != __instance)
                {
                    // 若另一台照料站并未正在照料该小动物，且小动物也不在另一台照料站的呼叫等待队列中，
                    // 说明残留的目标站引用已失效，予以清理，允许当前照料站（如剪毛站）正常呼叫该小动物！
                    if (otherStation.ActiveRanchable != ranchable.States && !otherStation.IsCritterInQueue(ranchable))
                    {
                        RanchStateLifecycle.RequestRelease(ranchable, otherStation);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn($"CanRanchableBeRanchedAtRanchStation Prefix 异常: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(RanchableMonitor.Instance), nameof(RanchableMonitor.Instance.ShouldGoGetRanched))]
    internal static class RanchableMonitor_ShouldGoGetRanched_Patch
    {
        private static void Postfix(RanchableMonitor.Instance __instance, ref bool __result)
        {
            if (__instance == null || __instance.TargetRanchStation == null) return;
            RanchStation.Instance station = __instance.TargetRanchStation;
            if (!RanchStateLifecycle.IsOwnedByStation(__instance, station))
            {
                __result = false;
                RanchStateLifecycle.RequestRelease(__instance, station);
            }
        }
    }

    [HarmonyPatch(typeof(RanchStation.Instance), "RanchCreature")]
    internal static class RanchStation_RanchCreature_Patch
    {
        private static void Prefix(RanchStation.Instance __instance, out RanchedStates.Instance __state)
        {
            __state = __instance?.ActiveRanchable;
            if (__state != null && __state.gameObject != null)
            {
                AutoRanchTracker.Clear(__state.gameObject.GetInstanceID());
            }
        }

        private static void Postfix(RanchStation.Instance __instance, RanchedStates.Instance __state)
        {
            if (__state != null && !__state.IsNullOrStopped() && __instance.ActiveRanchable != __state)
            {
                try
                {
                    int critterId = __state.gameObject != null ? __state.gameObject.GetInstanceID() : 0;
                    if (critterId != 0)
                    {
                        AutoRanchTracker.Clear(critterId);
                    }

                    // 结算已经离开入站状态切换栈；先退出旧任务，由原版 root.Exit 一并清理目标。
                    if (__state.Monitor != null && ReferenceEquals(__state.Monitor.TargetRanchStation, __instance))
                        __state.StopSM("OmniMod virtual ranch completed");
                }
                catch (Exception ex)
                {
                    ModLog.Warn($"照料完成退出异常: {ex.Message}");
                }
            }
        }
    }

    [HarmonyPatch(typeof(RanchStation.Instance), "Abandon")]
    internal static class RanchStation_Abandon_Patch
    {
        private static void Postfix(RanchableMonitor.Instance critter)
        {
            if (critter != null && critter.gameObject != null)
            {
                AutoRanchTracker.Clear(critter.gameObject.GetInstanceID());
            }
        }
    }

    [HarmonyPatch(typeof(RanchStation.Instance), "MessageCreatureArrived")]
    internal static class RanchStation_MessageCreatureArrived_Patch
    {
        private static void Postfix(RanchStation.Instance __instance, RanchedStates.Instance critter)
        {
            if (!AutoRanchHelper.IsStationOperational(__instance))
            {
                return;
            }

            AutoRanchHelper.EnsureVirtualRancher(__instance);
            // 水生站在动物的入站状态切换结束后，由模拟回调计时并执行原版工作回调。
            if (AquaticRanchAutoRunner.IsAquaticStation(__instance.gameObject)) return;

            RanchedStates.Instance targetCritter = __instance.ActiveRanchable ?? critter;
            if (targetCritter == null)
            {
                return;
            }

            __instance.gameObject.AddOrGet<DeferredRanchCompletion>().Request(targetCritter);
        }
    }

    [HarmonyPatch(typeof(RanchedStates), "InitializeStates")]
    internal static class RanchedStates_InitializeStates_Patch
    {
        private static void Postfix(RanchedStates __instance)
        {
            __instance.root.Exit("OmniMod_ClearTrackerOnExit", smi =>
            {
                if (smi?.gameObject != null)
                {
                    AutoRanchTracker.Clear(smi.gameObject.GetInstanceID());
                }
                if (smi?.Monitor != null && smi.Monitor.TargetRanchStation != null)
                {
                    if (!smi.Monitor.TargetRanchStation.IsCritterInQueue(smi.Monitor) &&
                        smi.Monitor.TargetRanchStation.ActiveRanchable != smi)
                    {
                        smi.Monitor.TargetRanchStation = null;
                    }
                }
            });
        }
    }
}
