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
        public override Attributes GetAttributes()
        {
            var attrs = new Attributes(gameObject);
            var ranchingAttr = Db.Get().Attributes.Ranching;
            var instance = attrs.Add(ranchingAttr);

            float bestSkill = 15f;
            if (Components.LiveMinionIdentities != null)
            {
                for (int i = 0; i < Components.LiveMinionIdentities.Count; i++)
                {
                    var minion = Components.LiveMinionIdentities[i];
                    if (minion == null) continue;
                    var attr = minion.GetAttributes()?.Get(ranchingAttr.Id);
                    if (attr != null && attr.GetTotalValue() > bestSkill)
                    {
                        bestSkill = attr.GetTotalValue();
                    }
                }
            }

            instance.Add(new AttributeModifier(ranchingAttr.Id, bestSkill, "OmniMod Automated Rancher"));
            return attrs;
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

    [HarmonyPatch(typeof(RanchStation.Instance), "TriggerRanchStationNoLongerAvailable")]
    internal static class RanchStation_TriggerRanchStationNoLongerAvailable_Patch
    {
        private static bool Prefix(RanchStation.Instance __instance)
        {
            if (AutoRanchHelper.IsStationOperational(__instance))
            {
                return false;
            }
            return true;
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

            if (__instance.ActiveRanchable != null)
            {
                __instance.RanchCreature();
                return;
            }

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
                        ranchable.TargetRanchStation = null;
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
            if (!__result || __instance == null || __instance.TargetRanchStation == null)
            {
                return;
            }

            var station = __instance.TargetRanchStation;
            if (!station.IsRunning())
            {
                __result = false;
                __instance.TargetRanchStation = null;
                return;
            }

            // 小动物只有确实在照料站呼叫等待队列（IsCritterInQueue）中，
            // 或者当前正在该照料站操作台上（ActiveRanchable == __instance.States），才产生“想被照料”行为。
            // 彻底杜绝照料完成后小动物因 IsRancherReady 常驻 true 而持续触发 WantsToGetRanched 兴奋抽搐死循环！
            bool isInQueue = station.IsCritterInQueue(__instance);
            bool isActive = (station.ActiveRanchable != null && station.ActiveRanchable == __instance.States);

            if (!isInQueue && !isActive)
            {
                __result = false;
                __instance.TargetRanchStation = null;
            }
        }
    }

    [HarmonyPatch(typeof(RanchStation.Instance), "RanchCreature")]
    internal static class RanchStation_RanchCreature_Patch
    {
        private static RanchedStates.Instance _lastActiveRanched;

        private static void Prefix(RanchStation.Instance __instance)
        {
            _lastActiveRanched = __instance?.ActiveRanchable;
            if (_lastActiveRanched != null && _lastActiveRanched.gameObject != null)
            {
                AutoRanchTracker.Clear(_lastActiveRanched.gameObject.GetInstanceID());
            }
        }

        private static void Postfix(RanchStation.Instance __instance)
        {
            if (_lastActiveRanched != null && !_lastActiveRanched.IsNullOrStopped())
            {
                try
                {
                    int critterId = _lastActiveRanched.gameObject != null ? _lastActiveRanched.gameObject.GetInstanceID() : 0;
                    if (critterId != 0)
                    {
                        AutoRanchTracker.Clear(critterId);
                    }

                    if (_lastActiveRanched.Monitor != null)
                    {
                        _lastActiveRanched.Monitor.TargetRanchStation = null;
                    }

                    _lastActiveRanched.StopSM("OmniMod virtual ranch completed");
                }
                catch (Exception ex)
                {
                    ModLog.Warn($"照料完成退出异常: {ex.Message}");
                }
            }
            _lastActiveRanched = null;
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

            RanchedStates.Instance targetCritter = __instance.ActiveRanchable ?? critter;
            if (targetCritter == null)
            {
                return;
            }

            __instance.RanchCreature();
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
