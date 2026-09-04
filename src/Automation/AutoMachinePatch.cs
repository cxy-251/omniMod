using System.Reflection;
using HarmonyLib;

namespace OmniMod.Automation
{
    /// <summary>
    /// 工业机械自动运行——不需要人力操作。
    ///
    /// 复杂制造机 <see cref="ComplexFabricator"/> 覆盖了绝大多数工业机械：
    /// 碎石机、窑、金属精炼机、所有烹饪站（微生物研磨机/电烤炉/美食烹饪台/煤气灶/香料研磨机）、
    /// 药剂台、纺织机、玻璃锻造炉、分子锻造炉、陶瓷窑、氧石精炼机、超材料精炼机、蛋壳破碎机、榨汁机…
    ///
    /// 原理：ComplexFabricator 有个 <c>duplicantOperated</c> 开关。设为 false 后，游戏自带的
    /// <c>Sim200ms</c> 会在"有工作订单"时自行推进进度（<c>orderProgress += dt / recipe.time</c>）
    /// 并在完成时 <c>CompleteWorkingOrder()</c>——完全不需要小人。
    /// 仍然需要：通电、原料到位（原料由 OmniMod 的箱子 + 机械臂供应）。
    /// </summary>
    [HarmonyPatch(typeof(ComplexFabricator), "OnSpawn")]
    internal static class ComplexFabricator_OnSpawn_AutoRun_Patch
    {
        private static int _logged;

        private static void Postfix(ComplexFabricator __instance)
        {
            if (__instance != null)
            {
                __instance.duplicantOperated = false;
                if (_logged < 8)
                {
                    _logged++;
                    UnityEngine.Debug.Log($"[OmniMod] 机械自动运行：{__instance.name} 设为免人力操作");
                }
            }
        }
    }

    /// <summary>
    /// 自动研究：研究站 / 超级计算机 / 特殊研究设施——不需要小人操作。
    ///
    /// 这类建筑不是 ComplexFabricator，而是一个 Workable，内部挂 ElementConverter：
    /// 小人工作时 <c>OnWorkTick</c> 把转化速率设为 2+效率，转化器消耗原料 → 出研究点。
    ///
    /// 香草的 <c>Sim200ms</c> 会在"通电 + 选了研究 + 有原料"时生成一个小人操作任务。
    /// 我们用 Prefix 抢在前面：满足条件时，直接把转化速率保持在基础值 2、让转化器自行运转，
    /// 并<b>跳过原方法</b>（不生成小人任务）；已有的任务也取消掉。
    /// 有小人正在操作、或条件不满足时，放行原逻辑。
    /// </summary>
    [HarmonyPatch(typeof(ResearchCenter), "Sim200ms")]
    internal static class ResearchCenter_Sim200ms_AutoResearch_Patch
    {
        private static readonly MethodInfo HasMaterialMethod =
            AccessTools.Method(typeof(ResearchCenter), "HasMaterial");

        private static int _logged;

        private static bool Prefix(ResearchCenter __instance)
        {
            try
            {
                if (__instance == null || __instance.worker != null)
                {
                    return true;   // 有小人在操作 → 走香草逻辑
                }
                if (Research.Instance == null)
                {
                    return true;
                }
                if (Research.Instance.GetActiveResearch() == null)
                {
                    // 没选研究 → 自动挑下一项（未完成 + 前置已完成 + tier 最低）
                    AutoResearchAll.EnsureSomethingQueued();
                    if (Research.Instance.GetActiveResearch() == null)
                    {
                        return true;   // 整棵科技树都研究完了，放行香草逻辑
                    }
                }

                Operational op = __instance.GetComponent<Operational>();
                if (op == null || !op.IsOperational)
                {
                    return true;
                }
                if (HasMaterialMethod != null && !(bool)HasMaterialMethod.Invoke(__instance, null))
                {
                    return true;   // 没原料
                }

                ElementConverter conv = __instance.GetComponent<ElementConverter>();
                if (conv == null)
                {
                    return true;
                }

                // 取消香草可能已经建好的小人操作任务
                Traverse choreT = Traverse.Create(__instance).Field("chore");
                Chore chore = choreT.GetValue<Chore>();
                if (chore != null)
                {
                    chore.Cancel("OmniMod auto-research");
                    choreT.SetValue(null);
                }

                conv.SetWorkSpeedMultiplier(2f);
                op.SetActive(true);

                if (_logged < 5)
                {
                    _logged++;
                    UnityEngine.Debug.Log($"[OmniMod] 自动研究：{__instance.name} 自行运转，无需小人");
                }

                return false;   // 跳过原方法：不生成小人任务
            }
            catch
            {
                return true;
            }
        }
    }
}
