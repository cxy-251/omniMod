using System;
using System.Reflection;
using HarmonyLib;
using OmniMod.Core.Logging;
using OmniMod.Core.Safety;

namespace OmniMod.Machinery.Research
{
    /// <summary>
    /// 基础研究台与超级计算机免小人全自动研究
    /// </summary>
    [HarmonyPatch(typeof(ResearchCenter), "Sim200ms")]
    internal static class BasicResearchRunnerPatch
    {
        private static readonly Func<ResearchCenter, bool> HasMaterialFunc =
            (Func<ResearchCenter, bool>)Delegate.CreateDelegate(
                typeof(Func<ResearchCenter, bool>),
                AccessTools.Method(typeof(ResearchCenter), "HasMaterial"));

        private static readonly AccessTools.FieldRef<ResearchCenter, Chore> ChoreField =
            AccessTools.FieldRefAccess<ResearchCenter, Chore>("chore");

        private static bool Prefix(ResearchCenter __instance)
        {
            if (__instance == null) return true;

            return ExceptionShield.Execute("BasicResearchRunner.Sim200ms", () =>
            {
                if (__instance.worker != null)
                {
                    return true; // 有小人在操作 → 放行香草逻辑
                }
                if (global::Research.Instance == null)
                {
                    return true;
                }
                if (global::Research.Instance.GetActiveResearch() == null)
                {
                    return true; // 玩家还没选研究项目 → 放行
                }

                Operational op = __instance.GetComponent<Operational>();
                if (op == null || !op.IsOperational)
                {
                    return true;
                }
                if (HasMaterialFunc != null && !HasMaterialFunc(__instance))
                {
                    return true; // 缺少原料
                }

                ElementConverter conv = __instance.GetComponent<ElementConverter>();
                if (conv == null)
                {
                    return true;
                }

                // 取消香草可能已经建好的小人操作任务
                Chore chore = ChoreField(__instance);
                if (chore != null)
                {
                    chore.Cancel("OmniMod auto-research");
                    ChoreField(__instance) = null;
                }

                conv.SetWorkSpeedMultiplier(2f);
                op.SetActive(true);
                BuildingAnimHelper.PlayWorkingAnim(__instance.GetComponent<KBatchedAnimController>());

                ModLog.Throttle($"research_{__instance.PrefabID().Name}", $"自动研究：{__instance.name} 自行运转，无需小人", 30f);

                return false; // 跳过原方法，无需生成小人任务
            }, true);
        }
    }
}
