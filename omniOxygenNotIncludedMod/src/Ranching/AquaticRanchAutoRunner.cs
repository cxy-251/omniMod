using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OmniMod.Core.Logging;
using OmniMod.Machinery;
using UnityEngine;

namespace OmniMod.Ranching
{
    /// <summary>
    /// 全自动照料、挤奶及修剪站自动运作器（支持陆生与水生照料站、剪毛站、挤奶站）：
    /// 动物就位后自动计时并执行原版照料回调，免人工照料，彻底消除复制人技能需求。
    /// </summary>
    [SkipSaveFileSerialization]
    public sealed class AquaticRanchAutoRunner : KMonoBehaviour, ISim200ms
    {
        private RanchStation.Instance station;
        private RancherChore.RancherWorkable workable;
        private Operational operational;
        private KBatchedAnimController anim;
        private RanchedStates.Instance workingCritter;
        private float remaining;
        private float recallTimer;

        internal static bool IsRanchStation(GameObject go)
        {
            if (go == null) return false;
            Tag id = go.PrefabID();
            return id == RanchStationConfig.ID ||
                   id == ShearingStationConfig.ID ||
                   id == MilkingStationConfig.ID ||
                   id == UnderwaterMilkingStationConfig.ID ||
                   id == UnderwaterShearingStationConfig.ID ||
                   id == UnderwaterRanchStationConfig.ID ||
                   go.GetDef<RanchStation.Def>() != null;
        }

        internal static bool IsAquaticStation(GameObject go)
        {
            if (go == null) return false;
            Tag id = go.PrefabID();
            return id == UnderwaterMilkingStationConfig.ID ||
                   id == UnderwaterShearingStationConfig.ID ||
                   id == UnderwaterRanchStationConfig.ID;
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            station = this.GetSMI<RanchStation.Instance>();
            workable = GetComponent<RancherChore.RancherWorkable>();
            operational = GetComponent<Operational>();
            anim = GetComponent<KBatchedAnimController>();

            if (workable != null)
            {
                workable.requiredSkillPerk = null;
                workable.requireMinionToWork = false;
                workable.SetShouldShowSkillPerkStatusItem(false);
            }

            BuildingComplete building = GetComponent<BuildingComplete>();
            if (building != null)
            {
                building.isManuallyOperated = false;
            }

            // 确保技能警告组件存在（RancherChore 构造函数硬断言必须存在该组件以获取技能前置条件，缺少会抛出 Assert 异常），
            // 状态图标已通过下方的 Harmony 补丁（AquaticRanch_SingleSkillWarning_Patch 等）彻底屏蔽。
            if (GetComponent<SkillPerkMissingComplainer>() == null && GetComponent<MultiSkillPerkMissingComplainer>() == null)
            {
                var complainer = gameObject.AddComponent<SkillPerkMissingComplainer>();
                complainer.requiredSkillPerk = Db.Get().SkillPerks.CanUseRanchStation.Id;
            }

            KSelectable selectable = GetComponent<KSelectable>();
            if (selectable != null)
            {
                selectable.RemoveStatusItem(Db.Get().BuildingStatusItems.PendingWork);
                selectable.RemoveStatusItem(Db.Get().BuildingStatusItems.RequiresSkillPerk);
                selectable.RemoveStatusItem(Db.Get().BuildingStatusItems.ColonyLacksRequiredSkillPerk);
                selectable.RemoveStatusItem(Db.Get().BuildingStatusItems.ClusterColonyLacksRequiredSkillPerk);
                selectable.RemoveStatusItem(Db.Get().BuildingStatusItems.ColonyLacksDupeWithMultiSkillPerk);
                selectable.RemoveStatusItem(Db.Get().BuildingStatusItems.WorkRequiresMinion);
            }
        }

        public void Sim200ms(float dt)
        {
            try
            {
                if (station == null) station = this.GetSMI<RanchStation.Instance>();
                if (station == null || workable == null || operational == null || workable.worker != null) return;
                if (!AutoRanchHelper.IsStationOperational(station))
                {
                    EndWork();
                    return;
                }
                AutoRanchHelper.EnsureVirtualRancher(station);
                RanchedStates.Instance critter = station.ActiveRanchable;
                if (critter.IsNullOrStopped())
                {
                    if (critter != null) station.Abandon(critter.Monitor);
                    EndWork();
                    recallTimer -= dt;
                    if (recallTimer <= 0f && station.IsCritterAvailableForRanching)
                    {
                        // 队列内的动物允许低频重新呼叫，避免导航或状态中断导致漏检
                        recallTimer = 4f;
                        station.MessageRancherReady();
                    }
                    return;
                }
                if (workingCritter != critter)
                {
                    EndWork();
                    workingCritter = critter;
                    // 快速照料/剪毛/挤奶：缩短为原版时间的1/4（介于1.5秒至3秒之间），兼顾动画表现与高通量
                    remaining = Mathf.Clamp(station.def.WorkTime * 0.25f, 1.5f, 3.0f);
                    workable.WorkTimeRemaining = remaining;
                    station.def.OnRanchWorkBegins?.Invoke(critter, workable);
                    critter.AnimController.Play(station.def.RanchedPreAnim);
                    critter.AnimController.Queue(station.def.RanchedLoopAnim, KAnim.PlayMode.Loop);
                    workable.ShowProgressBar(true);
                    operational.SetActive(true);
                    BuildingAnimHelper.PlayWorkingAnim(anim);
                    GetComponent<KSelectable>()?.RemoveStatusItem(Db.Get().BuildingStatusItems.PendingWork);
                }
                station.def.OnRanchWorkTick?.Invoke(critter.gameObject, dt, workable);
                remaining = Mathf.Max(0f, remaining - dt);
                workable.WorkTimeRemaining = remaining;
                if (remaining > 0f) return;

                // 照料完成：播放结束动画并触发原版结算回调（给予"Ranched"效果、恢复血量、掉落毛发或产奶等）
                critter.AnimController.Play(station.def.RanchedPstAnim);
                station.RanchCreature();
                EndWork();
                ModLog.Throttle("ranch_auto_done_" + this.PrefabID().Name,
                    $"照料站已自动完成工作：{name}", 10f);
            }
            catch (Exception ex)
            {
                // 回调若已部分执行，先释放这一轮，避免每个 tick 重复结算产物
                if (station != null && workingCritter != null && station.ActiveRanchable == workingCritter)
                    station.Abandon(workingCritter.Monitor);
                EndWork();
                ModLog.Throttle("ranch_auto_failed", $"照料站自动工作失败：{ex.Message}", 10f);
            }
        }

        private void EndWork()
        {
            if (workingCritter == null) return;
            workingCritter = null;
            operational?.SetActive(false);
            if (workable != null)
            {
                workable.ShowProgressBar(false);
                workable.WorkTimeRemaining = workable.GetWorkTime();
            }
            BuildingAnimHelper.StopWorkingAnim(anim, operational);
        }
    }

    // 在建筑定义生成时移除技能元数据，建造预览和侧栏也不再宣称需要有技能的复制人
    [HarmonyPatch]
    internal static class AquaticRanch_BuildingSkill_Patch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(RanchStationConfig), nameof(RanchStationConfig.CreateBuildingDef));
            yield return AccessTools.Method(typeof(ShearingStationConfig), nameof(ShearingStationConfig.CreateBuildingDef));
            yield return AccessTools.Method(typeof(MilkingStationConfig), nameof(MilkingStationConfig.CreateBuildingDef));
            yield return AccessTools.Method(typeof(UnderwaterMilkingStationConfig), nameof(UnderwaterMilkingStationConfig.CreateBuildingDef));
            yield return AccessTools.Method(typeof(UnderwaterShearingStationConfig), nameof(UnderwaterShearingStationConfig.CreateBuildingDef));
            yield return AccessTools.Method(typeof(UnderwaterRanchStationConfig), nameof(UnderwaterRanchStationConfig.CreateBuildingDef));
        }

        private static void Postfix(BuildingDef __result)
        {
            if (__result != null) __result.RequiredSkillPerkID = null;
        }
    }

    [HarmonyPatch(typeof(RancherChore.RancherWorkable), "OnPrefabInit")]
    internal static class AquaticRanch_WorkableSkill_Patch
    {
        private static void Postfix(RancherChore.RancherWorkable __instance)
        {
            if (!AquaticRanchAutoRunner.IsRanchStation(__instance.gameObject)) return;
            __instance.requiredSkillPerk = null;
            __instance.requireMinionToWork = false;
        }
    }

    [HarmonyPatch(typeof(SkillPerkMissingComplainer), "UpdateStatusItem")]
    internal static class AquaticRanch_SingleSkillWarning_Patch
    {
        private static readonly AccessTools.FieldRef<SkillPerkMissingComplainer, Guid> StatusHandle =
            AccessTools.FieldRefAccess<SkillPerkMissingComplainer, Guid>("workStatusItemHandle");

        private static bool Prefix(SkillPerkMissingComplainer __instance)
        {
            if (!AquaticRanchAutoRunner.IsRanchStation(__instance.gameObject)) return true;
            Guid handle = StatusHandle(__instance);
            if (handle != Guid.Empty) __instance.GetComponent<KSelectable>()?.RemoveStatusItem(handle);
            StatusHandle(__instance) = Guid.Empty;
            return false;
        }
    }

    [HarmonyPatch(typeof(MultiSkillPerkMissingComplainer), "UpdateStatusItem")]
    internal static class AquaticRanch_SkillWarning_Patch
    {
        private static readonly AccessTools.FieldRef<MultiSkillPerkMissingComplainer, Guid> StatusHandle =
            AccessTools.FieldRefAccess<MultiSkillPerkMissingComplainer, Guid>("workStatusItemHandle");

        private static bool Prefix(MultiSkillPerkMissingComplainer __instance)
        {
            if (!AquaticRanchAutoRunner.IsRanchStation(__instance.gameObject)) return true;
            Guid handle = StatusHandle(__instance);
            if (handle != Guid.Empty) __instance.GetComponent<KSelectable>()?.RemoveStatusItem(handle);
            StatusHandle(__instance) = Guid.Empty;
            return false;
        }
    }

    // 水生动物的实际停靠格与建筑原点可能相差一格，校验时与 RanchedStates.GetRanchNavTarget 保持一致。
    [HarmonyPatch(typeof(RanchStation.Instance), "CanRanchableBeRanchedAtRanchStation")]
    internal static class AquaticRanch_Navigation_Patch
    {
        private static void Postfix(RanchStation.Instance __instance, RanchableMonitor.Instance ranchable, ref bool __result)
        {
            if (__result || !AquaticRanchAutoRunner.IsAquaticStation(__instance.gameObject) ||
                !AutoRanchHelper.IsStationOperational(__instance) || ranchable.IsNullOrStopped() ||
                ranchable.NavComponent == null || !ranchable.HasTag(GameTags.Creatures.Swimmer)) return;
            RanchStation.Instance other = ranchable.TargetRanchStation;
            if (other != null && other != __instance && other.IsRunning() && other.HasRancher) return;
            if (!__instance.def.IsCritterEligibleToBeRanchedCb(ranchable.gameObject, __instance) ||
                !ranchable.ChoreConsumer.IsChoreEqualOrAboveCurrentChorePriority<RanchedStates>()) return;
            var cavity = __instance.GetCavityInfo();
            if (cavity.IsNone() || Game.Instance.roomProber.GetCavityForCell(Grid.PosToCell(ranchable)) != cavity.Unwrap()) return;

            int target = __instance.GetRanchNavTarget();
            if (ranchable.HasTag(GameTags.Creatures.Flyer)) target = Grid.CellAbove(target);
            if (ranchable.HasTag(GameTags.LargeCreature))
            {
                int left = Grid.CellLeft(target);
                if (Grid.PosToXY(ranchable.gameObject.transform.position).x > Grid.CellToXY(target).x &&
                    ranchable.NavComponent.GetNavigationCost(left) != -1) target = left;
            }
            else target = Grid.CellRight(target);
            __result = Grid.IsValidCell(target) && ranchable.NavComponent.GetNavigationCost(target) != -1;
        }
    }
}
