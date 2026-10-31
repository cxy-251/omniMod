using System;
using HarmonyLib;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Machinery.Research
{
    /// <summary>沿用原版分析时长、鉴定入口和可序列化的 WorkTimeRemaining，自动分析已送入的突变种子。</summary>
    public sealed class GeneticAnalysisAutoRunner : KMonoBehaviour, ISim200ms
    {
        private GeneticAnalysisStationWorkable workable;
        private Operational operational;
        private KBatchedAnimController anim;
        private KSelectable selectable;
        private bool running;

        protected override void OnSpawn()
        {
            base.OnSpawn();
            workable = GetComponent<GeneticAnalysisStationWorkable>();
            operational = GetComponent<Operational>();
            anim = GetComponent<KBatchedAnimController>();
            selectable = GetComponent<KSelectable>();
            if (workable != null) workable.requiredSkillPerk = null;
            BuildingComplete building = GetComponent<BuildingComplete>();
            if (building != null) building.isManuallyOperated = false;
            selectable?.RemoveStatusItem(Db.Get().BuildingStatusItems.RequiresSkillPerk);
            selectable?.RemoveStatusItem(Db.Get().BuildingStatusItems.PendingWork);
        }

        public void Sim200ms(float dt)
        {
            if (workable == null || operational == null || workable.worker != null) return;
            try
            {
                GameObject seed = workable.storage != null ? workable.storage.FindFirst(GameTags.UnidentifiedSeed) : null;
                MutantPlant mutation = seed != null ? seed.GetComponent<MutantPlant>() : null;
                Pickupable pickup = seed != null ? seed.GetComponent<Pickupable>() : null;
                GeneticAnalysisStation.StatesInstance station = workable.statesInstance;
                if (!operational.IsOperational || mutation == null || pickup == null ||
                    pickup.PrimaryElement.Units < 1f || station == null ||
                    station.GetSeedForbidden(seed.PrefabID()))
                {
                    StopRunning();
                    return;
                }

                operational.SetActive(true);
                if (!running)
                {
                    selectable?.AddStatusItem(Db.Get().BuildingStatusItems.ComplexFabricatorResearching, seed);
                    running = true;
                }
                workable.ShowProgressBar(true);
                BuildingAnimHelper.PlayWorkingAnim(anim);
                float remaining = workable.WorkTimeRemaining;
                if (float.IsInfinity(remaining) || float.IsNaN(remaining) || remaining < 0f)
                    remaining = workable.GetWorkTime();
                workable.WorkTimeRemaining = Mathf.Max(0f, remaining - dt);
                if (workable.WorkTimeRemaining > 0f) return;

                // 原版负责拆出一颗种子、鉴定亚种、更新图鉴/成就并放回地面，不复制或销毁额外种子。
                workable.IdentifyMutant();
                workable.WorkTimeRemaining = workable.GetWorkTime();
                StopRunning();
                ModLog.Throttle("automatic_seed_analysis", "植物分析仪已自动完成一颗突变种子的分析", 10f);
            }
            catch (Exception ex)
            {
                StopRunning();
                ModLog.Throttle("automatic_seed_analysis_failed", $"植物分析仪自动分析失败：{ex.Message}", 10f);
            }
        }

        private void StopRunning()
        {
            if (!running) return;
            running = false;
            operational.SetActive(false);
            workable.ShowProgressBar(false);
            selectable?.RemoveStatusItem(Db.Get().BuildingStatusItems.ComplexFabricatorResearching);
            BuildingAnimHelper.StopWorkingAnim(anim, operational);
        }
    }

    [HarmonyPatch(typeof(GeneticAnalysisStationConfig), nameof(GeneticAnalysisStationConfig.DoPostConfigureComplete))]
    internal static class GeneticAnalysisStationConfig_AutoRun_Patch
    {
        private static void Postfix(GameObject go) => go.AddOrGet<GeneticAnalysisAutoRunner>();
    }

    [HarmonyPatch(typeof(GeneticAnalysisStationWorkable), "OnPrefabInit")]
    internal static class GeneticAnalysisStationWorkable_AutoRun_Patch
    {
        private static void Postfix(GeneticAnalysisStationWorkable __instance) => __instance.requiredSkillPerk = null;
    }

    [HarmonyPatch(typeof(GeneticAnalysisStation), "CreateChore")]
    internal static class GeneticAnalysisStation_CreateChore_AutoRun_Patch
    {
        private static void Postfix(Chore __result) => __result?.AddPrecondition(MachineryPreconditions.NeverAvailable);
    }
}
