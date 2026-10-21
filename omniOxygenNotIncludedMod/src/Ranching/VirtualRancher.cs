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

    [HarmonyPatch(typeof(RanchStation.Instance), "RanchCreature")]
    internal static class RanchStation_RanchCreature_Patch
    {
        private static readonly AccessTools.FieldRef<RanchedStates, RanchedStates.RanchStates> RanchStatesRef =
            AccessTools.FieldRefAccess<RanchedStates, RanchedStates.RanchStates>("ranch");

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
                    if (_lastActiveRanched.sm != null)
                    {
                        var ranch = RanchStatesRef(_lastActiveRanched.sm);
                        if (ranch?.Runaway != null)
                        {
                            _lastActiveRanched.GoTo(ranch.Runaway);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModLog.Warn($"照料离场跳转异常: {ex.Message}");
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
        private static readonly AccessTools.FieldRef<RanchedStates, RanchedStates.RanchStates> RanchStatesRef =
            AccessTools.FieldRefAccess<RanchedStates, RanchedStates.RanchStates>("ranch");

        private static void Postfix(RanchedStates __instance)
        {
            var ranch = RanchStatesRef(__instance);
            if (ranch?.Wavegoodbye != null && ranch?.Runaway != null)
            {
                ranch.Wavegoodbye.Enter(smi =>
                {
                    if (smi != null && !smi.IsNullOrStopped())
                    {
                        var rs = RanchStatesRef(smi.sm);
                        if (rs?.Runaway != null)
                        {
                            smi.GoTo(rs.Runaway);
                        }
                    }
                });
            }
        }
    }
}
