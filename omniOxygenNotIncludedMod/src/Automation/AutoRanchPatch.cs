using System;
using System.Collections.Generic;
using HarmonyLib;
using Klei.AI;
using UnityEngine;

namespace OmniMod.Automation
{
    /// <summary>
    /// 全自动小动物照料站、挤奶站、剪毛站（包含水下变种）：
    /// 小动物自动前往照料点，全自动梳毛、挤奶、剪毛，完全不需要复制人跑腿参与。
    /// 
    /// 涵盖建筑：
    /// - 照料站 (RanchStationConfig)
    /// - 挤奶站 (MilkingStationConfig)
    /// - 剪毛站 (ShearingStationConfig)
    /// - 水下照料站 (UnderwaterRanchStationConfig)
    /// - 水下挤奶站 (UnderwaterMilkingStationConfig)
    /// - 水下剪毛站 (UnderwaterShearingStationConfig)
    /// </summary>
    internal class OmniVirtualRancher : WorkerBase
    {
        public override Attributes GetAttributes()
        {
            var attrs = new Attributes(gameObject);
            var ranchingAttr = Db.Get().Attributes.Ranching;
            var instance = attrs.Add(ranchingAttr);

            // 基础提供 15 点照料技能加成；若殖民地有更高技能的养殖小人，则直接继承最高技能
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

    /// <summary>
    /// 1. 禁用复制人工作任务：让香草生成正常 Chore 避免 ToggleChore/SetupChore 抛 NRE，
    /// 但追加永远返回 false 的前置条件，并隐藏悬浮提示，使小人永远不会接取该任务。
    /// </summary>
    [HarmonyPatch(typeof(RanchStation.Instance), "CreateChore")]
    internal static class RanchStation_CreateChore_Patch
    {
        private static int _logged;

        private static void Postfix(RanchStation.Instance __instance, ref Chore __result)
        {
            if (__result != null)
            {
                __result.AddPrecondition(new Chore.Precondition
                {
                    id = "OmniMod_PreventDuplicantRanching",
                    description = "OmniMod Automated Ranching",
                    fn = (ref Chore.Precondition.Context context, object data) => false
                });
                __result.showAvailabilityInHoverText = false;
            }
            AutoRanchHelper.EnsureVirtualRancher(__instance);

            if (_logged < 5)
            {
                _logged++;
                Debug.Log($"[OmniMod] 照料站免人工操作生效：{__instance.gameObject.name} 任务已屏蔽复制人接取");
            }
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

    /// <summary>
    /// 2. 状态机就绪时保持 HasRancher 为 true，使小动物排队和校验逻辑正常运转。
    /// </summary>
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

    /// <summary>
    /// 3. 站台就绪状态判定：只要站台通电运转且当前台面上没有动物，即视作随时就绪，
    /// 确保小动物时刻判定 ShouldGoGetRanched 为 true，不会中途掉出排队。
    /// </summary>
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

    /// <summary>
    /// 4. 状态机启动时，初始化挂载虚拟照料 Worker。
    /// </summary>
    [HarmonyPatch(typeof(RanchStation.Instance), "StartSM")]
    internal static class RanchStation_StartSM_Patch
    {
        private static void Postfix(RanchStation.Instance __instance)
        {
            AutoRanchHelper.EnsureVirtualRancher(__instance);
        }
    }

    /// <summary>
    /// 5. 空间判定兜底：若房间探针暂时未识别到 Cavity，直接取建筑所在 Cell 的 Cavity，避免寻路中断。
    /// </summary>
    [HarmonyPatch(typeof(RanchStation.Instance), "GetStationCavity")]
    internal static class RanchStation_GetStationCavity_Patch
    {
        private static void Postfix(RanchStation.Instance __instance, ref CavityInfo __result)
        {
            if (__result == null && __instance != null)
            {
                __result = Game.Instance.roomProber.GetCavityForCell(__instance.GetRanchNavTarget());
            }
        }
    }

    /// <summary>
    /// 6. 只要站台处于正常通电工作状态，绝不断开并解散排队小动物。
    /// </summary>
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

    /// <summary>
    /// 7. 寻路并召唤小动物：
    /// - 放行原版 FindRanchable，使其每 200ms 持续刷新并筛选厩舍内需要照料/产奶/剪毛的动物；
    /// - 精确单次通知：对加入 targetRanchables 的每只动物仅广播一次呼唤信号，避免重复打断移动。
    /// </summary>
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
                    Game.BrainScheduler.PrioritizeBrain(critter.GetComponent<CreatureBrain>());
                    critter.Trigger((int)GameHashes.RancherReadyAtRanchStation);
                }
            }
        }
    }

    /// <summary>
    /// 8. 动物完成照料后，清理呼唤状态记录。
    /// </summary>
    [HarmonyPatch(typeof(RanchStation.Instance), "RanchCreature")]
    internal static class RanchStation_RanchCreature_Patch
    {
        private static void Prefix(RanchStation.Instance __instance)
        {
            if (__instance != null && __instance.ActiveRanchable != null && __instance.ActiveRanchable.gameObject != null)
            {
                AutoRanchTracker.Clear(__instance.ActiveRanchable.gameObject.GetInstanceID());
            }
        }
    }

    /// <summary>
    /// 9. 动物离开队列时，清理呼唤状态记录。
    /// </summary>
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

    /// <summary>
    /// 10. 小动物到达照料站：自动完成照料/挤奶/剪毛，触发动画与产物掉落，小动物愉快离开。
    /// </summary>
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

            // 延迟 0.25 秒执行完成，确保小动物进入站台、动画图层切换正常
            if (GameScheduler.Instance != null)
            {
                GameScheduler.Instance.Schedule("OmniAutoRanch", 0.25f, _ =>
                {
                    if (AutoRanchHelper.IsStationOperational(__instance) && __instance.ActiveRanchable == targetCritter)
                    {
                        AutoRanchHelper.EnsureVirtualRancher(__instance);
                        __instance.RanchCreature();
                    }
                });
            }
            else
            {
                __instance.RanchCreature();
            }
        }
    }
}
