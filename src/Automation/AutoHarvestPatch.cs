using System;
using HarmonyLib;
using UnityEngine;

namespace OmniMod.Automation
{
    /// <summary>
    /// 农作物<b>零人力</b>收获：植物一成熟就自动收割，产物凭空产出（掉在植株位置，
    /// 随后由聚合箱收进全局池），完全不需要小人走过去。覆盖驯化作物、野生植物、乔木枝条。
    ///
    /// 原理（以 <c>StandardCropPlant</c> 状态机为例）：
    ///   成熟状态 <c>fruiting</c> 的 Enter 回调调用 <c>harvestable.SetCanBeHarvested(true)</c>；
    ///   该状态对 <c>GameHashes.Harvest</c> 事件（哈希 1272413801）有转换 → 进入 <c>harvest</c> 状态，
    ///   其 Enter 里 <c>crop.SpawnConfiguredFruit(null)</c> 产出果实、<c>SetCanBeHarvested(false)</c> 复位，
    ///   同时 <c>Growing</c> 监听同一事件重置生长周期。
    ///   而 <c>Harvestable.Harvest()</c> 本身就是 <c>Trigger(1272413801, this)</c>。
    /// 所以：植物成熟时，延迟一拍调用 <c>Harvest()</c> 即可完成零人力收割。
    /// 延迟一拍（0.12s，用 <see cref="GameScheduler"/>）是为了避免在状态机 Enter 回调里
    /// 同步触发状态转换。
    ///
    /// 同时用 Prefix 掐掉 <c>Harvestable.OnMarkedForHarvest</c>：不再生成任何小人收获任务，
    /// 收割完全由本补丁驱动。
    /// </summary>
    [HarmonyPatch(typeof(Harvestable), nameof(Harvestable.SetCanBeHarvested))]
    internal static class Harvestable_SetCanBeHarvested_ZeroLabor_Patch
    {
        private static int _logged;

        private static void Postfix(Harvestable __instance, bool state)
        {
            if (state && __instance != null)
            {
                Schedule(__instance);
            }
        }

        /// <summary>安排 0.12 秒后对这株植物执行零人力收割（若届时仍可收）。</summary>
        internal static void Schedule(Harvestable h)
        {
            if (h == null || GameScheduler.Instance == null)
            {
                return;
            }
            GameScheduler.Instance.Schedule("OmniZeroLaborHarvest", 0.12f, DoHarvest, h);
        }

        private static void DoHarvest(object obj)
        {
            Harvestable h = obj as Harvestable;
            if (h == null || h.gameObject == null || !h.CanBeHarvested)
            {
                return;
            }

            try
            {
                h.Harvest();   // Trigger(1272413801) → 状态机产出果实 + 复位生长
                if (_logged < 15)
                {
                    _logged++;
                    Debug.Log($"[OmniMod] 零人力收获: {h.name}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OmniMod] 零人力收获失败 {(h != null ? h.name : "?")}: {e.Message}");
            }
        }
    }

    /// <summary>读档 / 生成时就已经成熟的植物，补一刀零人力收获。</summary>
    [HarmonyPatch(typeof(Harvestable), "OnSpawn")]
    internal static class Harvestable_OnSpawn_ZeroLabor_Patch
    {
        private static void Postfix(Harvestable __instance)
        {
            if (__instance != null && __instance.CanBeHarvested)
            {
                Harvestable_SetCanBeHarvested_ZeroLabor_Patch.Schedule(__instance);
            }
        }
    }

    /// <summary>
    /// 零人力模式下不再需要小人收获任务：掐掉 <c>Harvestable.OnMarkedForHarvest</c>，
    /// 不生成 <c>WorkChore&lt;Harvestable&gt;</c>，也不显示"待收获"任务图标。
    /// </summary>
    [HarmonyPatch(typeof(Harvestable), nameof(Harvestable.OnMarkedForHarvest))]
    internal static class Harvestable_OnMarkedForHarvest_NoChore_Patch
    {
        private static bool Prefix()
        {
            return false;
        }
    }
}
