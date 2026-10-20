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

    internal static class AutoRanchHelper
    {
        private static readonly AccessTools.FieldRef<RanchStation.Instance, WorkerBase> RancherField =
            AccessTools.FieldRefAccess<RanchStation.Instance, WorkerBase>("rancher");

        internal static bool IsStationOperational(RanchStation.Instance smi)
        {
            if (smi == null || !smi.IsRunning()) return false;
            Operational op = smi.GetComponent<Operational>();
            if (op != null && !op.IsOperational) return false;
            return smi.IsInsideState(smi.sm.Operational);
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
    /// 1. 禁用复制人工作任务：返回 null，小人永远不会被派来操作照料站。
    /// </summary>
    [HarmonyPatch(typeof(RanchStation.Instance), "CreateChore")]
    internal static class RanchStation_CreateChore_Patch
    {
        private static int _logged;

        private static bool Prefix(RanchStation.Instance __instance, ref Chore __result)
        {
            __result = null;
            if (_logged < 5)
            {
                _logged++;
                Debug.Log($"[OmniMod] 照料站免人工操作生效：{__instance.gameObject.name} 不再生成复制人工作任务");
            }
            return false;
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
    /// 3. 状态机启动时，初始化挂载虚拟照料 Worker。
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
    /// 4. 寻路并召唤小动物：当照料站通电运转且空闲时，主动广播就绪信号，唤引符合条件的小动物主动过来。
    /// </summary>
    [HarmonyPatch(typeof(RanchStation.Instance), "FindRanchable")]
    internal static class RanchStation_FindRanchable_Patch
    {
        private static void Postfix(RanchStation.Instance __instance)
        {
            if (!AutoRanchHelper.IsStationOperational(__instance))
            {
                return;
            }

            AutoRanchHelper.EnsureVirtualRancher(__instance);

            if (__instance.ActiveRanchable == null)
            {
                if (!__instance.IsRancherReady)
                {
                    __instance.MessageRancherReady();
                }
                else
                {
                    __instance.MessageRanchables(GameHashes.RancherReadyAtRanchStation);
                }
            }
        }
    }

    /// <summary>
    /// 5. 小动物到达照料站：自动完成照料/挤奶/剪毛，触发动画与产物掉落，小动物愉快离开。
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
