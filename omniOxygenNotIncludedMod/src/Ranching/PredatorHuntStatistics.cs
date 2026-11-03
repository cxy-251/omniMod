using System;
using System.Collections.Generic;
using HarmonyLib;
using KSerialization;
using UnityEngine;

namespace OmniMod.Ranching
{
    public sealed partial class PredatorPopulation
    {
        [Serialize] private int successfulCulls;
        [Serialize] private int successfulMeals;
        [Serialize] private string lastHuntPrefab;
        [Serialize] private int lastHuntCycle = -1;
        [Serialize] private bool lastHuntWasCull;
        [Serialize] private List<int> pendingCullIds = new List<int>();
        private Guid huntStatusHandle;

        private void RestoreHuntStatistics()
        {
            if (pendingCullIds == null) pendingCullIds = new List<int>();
            foreach (int id in pendingCullIds) PredatorHuntStatistics.Track(id, this);
            huntStatusHandle = GetComponent<KSelectable>()?.AddStatusItem(PredatorHuntStatistics.Status, this) ?? Guid.Empty;
        }
        private void CleanupHuntStatistics()
        {
            foreach (int id in pendingCullIds) PredatorHuntStatistics.Forget(id, this);
            if (huntStatusHandle != Guid.Empty) GetComponent<KSelectable>()?.RemoveStatusItem(huntStatusHandle, true);
        }
        internal void TrackCull(KPrefabID prey)
        {
            // 沙盒删除等路径没有完成死亡掉落时不补计；下次猎杀时清理失效的整型预约。
            for (int i = pendingCullIds.Count - 1; i >= 0; i--)
                if (KPrefabIDTracker.Get().GetInstance(pendingCullIds[i]) == null)
                {
                    PredatorHuntStatistics.Forget(pendingCullIds[i], this);
                    pendingCullIds.RemoveAt(i);
                }
            if (!pendingCullIds.Contains(prey.InstanceID)) pendingCullIds.Add(prey.InstanceID);
            PredatorHuntStatistics.Track(prey.InstanceID, this);
        }
        private void InheritHuntStatistics(object data)
        {
            GameObject baby = data as GameObject;
            var source = baby != null ? baby.GetComponent<PredatorPopulation>() : null;
            if (source == null || source == this || source.GetDef<BabyMonitor.Def>() == null) return;
            successfulCulls = source.successfulCulls;
            successfulMeals = source.successfulMeals;
            lastHuntPrefab = source.lastHuntPrefab;
            lastHuntCycle = source.lastHuntCycle;
            lastHuntWasCull = source.lastHuntWasCull;
        }
        internal void ConfirmCull(KPrefabID prey)
        {
            if (pendingCullIds.Remove(prey.InstanceID)) RecordHunt(prey.PrefabTag.ToString(), true);
        }
        internal void RecordHunt(string prefab, bool cull)
        {
            if (cull) { if (successfulCulls < int.MaxValue) successfulCulls++; }
            else if (successfulMeals < int.MaxValue) successfulMeals++;
            lastHuntPrefab = prefab;
            lastHuntCycle = GameClock.Instance != null ? GameClock.Instance.GetCycle() + 1 : -1;
            lastHuntWasCull = cull;
        }
        internal string HuntSummary => ModStrings.T(
            $"已猎杀：{(long)successfulCulls + successfulMeals}只（主动{successfulCulls}／捕食{successfulMeals}）",
            $"Hunted: {(long)successfulCulls + successfulMeals} (culled {successfulCulls} / eaten {successfulMeals})").Resolve();
        internal string HuntTooltip()
        {
            string text = ModStrings.T(
                $"本个体成功主动猎杀 {successfulCulls} 只，饥饿捕食活体 {successfulMeals} 只。\n主动猎杀在原版死亡、屠宰掉落完成后计数；捕食在原版成功消耗活体后计数。扑空、仅追逐、吃散落肉和受保护而放弃的目标不计。\n从此版本起累计，随存档保存；不改变猎杀数量或频率。",
                $"This critter has successfully culled {successfulCulls} and eaten {successfulMeals} live prey.\nCulls count after native death and butchering complete; meals count after live prey are consumed. Misses, pursuits, loose meat and protected targets do not count.\nTracked from this version onward and saved with the critter. No change to culling limits or rate.").Resolve();
            if (!string.IsNullOrEmpty(lastHuntPrefab))
            {
                GameObject prefab = Assets.TryGetPrefab(lastHuntPrefab.ToTag());
                string prey = prefab != null ? prefab.GetProperName() : lastHuntPrefab;
                text += ModStrings.T($"\n最近：第 {lastHuntCycle} 周期，{prey}（{(lastHuntWasCull ? "主动猎杀" : "饥饿捕食")}）",
                    $"\nLatest: cycle {lastHuntCycle}, {prey} ({(lastHuntWasCull ? "culled" : "eaten")})").Resolve();
            }
            else text += ModStrings.T("\n尚无成功猎杀记录。", "\nNo confirmed hunts yet.").Resolve();
            return text + PredatorPopulationRules.DensitySummary(this);
        }
    }

    internal static class PredatorHuntStatistics
    {
        private static readonly Dictionary<int, int> credits = new Dictionary<int, int>();
        private static StatusItem status;
        internal static StatusItem Status
        {
            get
            {
                if (status == null)
                {
                    status = new StatusItem("OmniPredatorHunts", "", "", "", StatusItem.IconType.Info,
                        NotificationType.Neutral, false, OverlayModes.None.ID, showWorldIcon: false);
                    status.SetResolveStringCallback((_, data) => ((PredatorPopulation)data).HuntSummary);
                    status.resolveTooltipCallback = (_, data) => ((PredatorPopulation)data).HuntTooltip();
                }
                return status;
            }
        }
        internal static void Track(int preyId, PredatorPopulation owner) => credits[preyId] = owner.GetComponent<KPrefabID>().InstanceID;
        internal static void Forget(int preyId, PredatorPopulation owner)
        {
            if (credits.TryGetValue(preyId, out int id) && id == owner.GetComponent<KPrefabID>().InstanceID) credits.Remove(preyId);
        }
        internal static void Confirm(KPrefabID prey)
        {
            if (prey == null || !credits.TryGetValue(prey.InstanceID, out int actorId)) return;
            credits.Remove(prey.InstanceID);
            var actor = KPrefabIDTracker.Get().GetInstance(actorId);
            if (actor != null) actor.GetComponent<PredatorPopulation>()?.ConfirmCull(prey);
        }
        internal static void Reset() { credits.Clear(); status = null; }
    }

    [HarmonyPatch(typeof(Butcherable), nameof(Butcherable.OnButcherComplete))]
    internal static class PredatorHuntStatistics_ConfirmedDeath
    {
        private static void Prefix(bool ___butchered, out bool __state) => __state = ___butchered;
        private static void Postfix(Butcherable __instance, bool ___butchered, bool __state)
        {
            if (!__state && ___butchered) PredatorHuntStatistics.Confirm(__instance.GetComponent<KPrefabID>());
        }
    }
}
