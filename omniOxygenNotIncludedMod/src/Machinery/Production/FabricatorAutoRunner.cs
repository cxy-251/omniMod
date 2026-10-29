using System;
using System.Reflection;
using HarmonyLib;
using OmniMod.Core.Logging;
using OmniMod.Core.Safety;
using UnityEngine;

namespace OmniMod.Machinery.Production
{
    /// <summary>
    /// 复杂制造机 <see cref="ComplexFabricator"/>（烹饪台/碎石机/精炼炉/纺织机等）免人工自动运行
    /// </summary>
    [HarmonyPatch(typeof(ComplexFabricator), "OnSpawn")]
    internal static class ComplexFabricator_OnSpawn_AutoRun_Patch
    {
        private static void Postfix(ComplexFabricator __instance)
        {
            if (__instance == null) return;

            ExceptionShield.Execute("ComplexFabricator.OnSpawn", () =>
            {
                __instance.duplicantOperated = false;
                __instance.showProgressBar = true;

                BuildingComplete bc = __instance.GetComponent<BuildingComplete>();
                if (bc != null)
                {
                    bc.isManuallyOperated = false;
                }

                if (__instance.Workable != null)
                {
                    __instance.Workable.requiredSkillPerk = null;
                }

                KSelectable sel = __instance.GetComponent<KSelectable>();
                if (sel != null)
                {
                    sel.RemoveStatusItem(Db.Get().BuildingStatusItems.RequiresSkillPerk);
                    sel.RemoveStatusItem(Db.Get().BuildingStatusItems.PendingWork);
                }

                ModLog.Throttle($"spawn_{__instance.PrefabID().Name}", $"机械自动运行：{__instance.name} 设为免人力操作", 60f);
            });
        }
    }

    [HarmonyPatch(typeof(SushiBar), "OnSpawn")]
    internal static class SushiBar_OnSpawn_AutoRun_Patch
    {
        private static void Postfix(SushiBar __instance)
        {
            if (__instance == null) return;

            ExceptionShield.Execute("SushiBar.OnSpawn", () =>
            {
                __instance.duplicantOperated = false;
                __instance.showProgressBar = true;

                BuildingComplete bc = __instance.GetComponent<BuildingComplete>();
                if (bc != null)
                {
                    bc.isManuallyOperated = false;
                }

                if (__instance.Workable != null)
                {
                    __instance.Workable.requiredSkillPerk = null;
                }

                KSelectable sel = __instance.GetComponent<KSelectable>();
                if (sel != null)
                {
                    sel.RemoveStatusItem(Db.Get().BuildingStatusItems.RequiresSkillPerk);
                    sel.RemoveStatusItem(Db.Get().BuildingStatusItems.PendingWork);
                }
            });
        }
    }

    /// <summary>
    /// 工业制造机与烹饪站免人力全自动运行补丁
    /// </summary>
    [HarmonyPatch(typeof(ComplexFabricator), "Sim200ms")]
    internal static class ComplexFabricator_Sim200ms_AutoRun_Patch
    {
        private static readonly AccessTools.FieldRef<ComplexFabricator, ComplexRecipe[]> RecipeListField =
            AccessTools.FieldRefAccess<ComplexFabricator, ComplexRecipe[]>("recipe_list");

        private static readonly AccessTools.FieldRef<ComplexFabricator, int> WorkingOrderIdxField =
            AccessTools.FieldRefAccess<ComplexFabricator, int>("workingOrderIdx");

        private static readonly AccessTools.FieldRef<ComplexFabricator, float> OrderProgressField =
            AccessTools.FieldRefAccess<ComplexFabricator, float>("orderProgress");

        private static readonly AccessTools.FieldRef<ComplexFabricator, Chore> ChoreField =
            AccessTools.FieldRefAccess<ComplexFabricator, Chore>("chore");

        private static readonly Action<ComplexFabricator, bool> ShowProgressBarAction =
            (Action<ComplexFabricator, bool>)Delegate.CreateDelegate(
                typeof(Action<ComplexFabricator, bool>),
                AccessTools.Method(typeof(ComplexFabricator), "ShowProgressBar", new[] { typeof(bool) }));

        private static readonly Action<ComplexFabricator> CancelChoreAction =
            (Action<ComplexFabricator>)Delegate.CreateDelegate(
                typeof(Action<ComplexFabricator>),
                AccessTools.Method(typeof(ComplexFabricator), "CancelChore"));

        private static readonly Action<ComplexFabricator> RefreshAndStartNextOrderAction =
            (Action<ComplexFabricator>)Delegate.CreateDelegate(
                typeof(Action<ComplexFabricator>),
                AccessTools.Method(typeof(ComplexFabricator), "RefreshAndStartNextOrder"));

        private static readonly Func<ComplexFabricator, ComplexRecipe, Storage, bool> HasIngredientsFunc =
            (Func<ComplexFabricator, ComplexRecipe, Storage, bool>)Delegate.CreateDelegate(
                typeof(Func<ComplexFabricator, ComplexRecipe, Storage, bool>),
                AccessTools.Method(typeof(ComplexFabricator), "HasIngredients", new[] { typeof(ComplexRecipe), typeof(Storage) }));

        private static bool Prefix(ComplexFabricator __instance, float dt)
        {
            if (__instance == null) return true;

            try
            {
                // 保持免人力开关与进度条始终开启
                if (__instance.duplicantOperated)
                {
                    __instance.duplicantOperated = false;
                }
                __instance.showProgressBar = true;

                // 如果有小人正在手动操作，放行原版
                if (__instance.Workable != null && __instance.Workable.worker != null)
                {
                    return true;
                }

                // 清除技能需求限制提示
                if (__instance.Workable != null && !string.IsNullOrEmpty(__instance.Workable.requiredSkillPerk))
                {
                    __instance.Workable.requiredSkillPerk = null;
                }

                // 取消可能残留的小人操作任务
                if (ChoreField(__instance) != null)
                {
                    CancelChoreAction?.Invoke(__instance);
                }

                Operational op = __instance.GetComponent<Operational>();
                if (op == null || !op.IsOperational)
                {
                    return false;
                }

                int workingOrderIdx = WorkingOrderIdxField(__instance);
                bool hasWorkingOrder = workingOrderIdx > -1;

                if (!hasWorkingOrder)
                {
                    ComplexRecipe next = __instance.NextOrder;
                    if (next != null && HasIngredientsFunc != null && HasIngredientsFunc(__instance, next, __instance.inStorage))
                    {
                        RefreshAndStartNextOrderAction?.Invoke(__instance);
                        workingOrderIdx = WorkingOrderIdxField(__instance);
                        hasWorkingOrder = workingOrderIdx > -1;
                    }
                }

                op.SetActive(hasWorkingOrder);

                ComplexRecipe[] recipeList = RecipeListField(__instance) ?? __instance.GetRecipes();

                if (hasWorkingOrder && recipeList != null && workingOrderIdx >= 0 && workingOrderIdx < recipeList.Length)
                {
                    ShowProgressBarAction?.Invoke(__instance, true);

                    ComplexRecipe recipe = recipeList[workingOrderIdx];
                    float baseTime = recipe != null && recipe.time > 0f ? recipe.time : 40f;
                    float speedMultiplier = 2.5f;
                    float progress = (dt / baseTime) * speedMultiplier;

                    float currentProgress = OrderProgressField(__instance) + progress;
                    OrderProgressField(__instance) = currentProgress;

                    BuildingAnimHelper.PlayWorkingAnim(__instance.GetComponent<KBatchedAnimController>());

                    KSelectable selectable = __instance.GetComponent<KSelectable>();
                    if (selectable != null)
                    {
                        selectable.RemoveStatusItem(Db.Get().BuildingStatusItems.RequiresSkillPerk);
                        selectable.RemoveStatusItem(Db.Get().BuildingStatusItems.PendingWork);
                    }

                    if (currentProgress >= 1f)
                    {
                        ShowProgressBarAction?.Invoke(__instance, false);
                        __instance.CompleteWorkingOrder();
                    }

                    ModLog.DebugLog($"机械自动运行中：{__instance.name} 正在全自动加工");
                }
                else
                {
                    ShowProgressBarAction?.Invoke(__instance, false);
                    BuildingAnimHelper.StopWorkingAnim(__instance.GetComponent<KBatchedAnimController>(), op);
                }

                return false;
            }
            catch (Exception ex)
            {
                ModLog.Error("ComplexFabricator.Sim200ms failed", ex);
                return true;
            }
        }
    }
}
