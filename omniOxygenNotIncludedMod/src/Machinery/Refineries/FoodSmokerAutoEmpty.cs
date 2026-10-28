using HarmonyLib;
using OmniMod.Core.Safety;

namespace OmniMod.Machinery.Refineries
{
    internal static class FoodSmokerHelper
    {
        internal static readonly AccessTools.FieldRef<FoodSmoker, GameStateMachine<FoodSmoker, FoodSmoker.StatesInstance, IStateMachineTarget, FoodSmoker.Def>.State> RequestEmptyField =
            AccessTools.FieldRefAccess<FoodSmoker, GameStateMachine<FoodSmoker, FoodSmoker.StatesInstance, IStateMachineTarget, FoodSmoker.Def>.State>("requestEmpty");

        internal static readonly AccessTools.FieldRef<FoodSmoker, GameStateMachine<FoodSmoker, FoodSmoker.StatesInstance, IStateMachineTarget, FoodSmoker.Def>.State> WorkingField =
            AccessTools.FieldRefAccess<FoodSmoker, GameStateMachine<FoodSmoker, FoodSmoker.StatesInstance, IStateMachineTarget, FoodSmoker.Def>.State>("working");
    }

    /// <summary>
    /// 食物烟熏机（FoodSmoker）自动排空
    /// 熟肉熏烤完成存入输出仓后，全自动掉落到地面，无需小人手动清空。
    /// </summary>
    [HarmonyPatch(typeof(FoodSmoker), nameof(FoodSmoker.InitializeStates))]
    internal static class FoodSmoker_InitializeStates_AutoEmpty_Patch
    {
        private static void Postfix(FoodSmoker __instance)
        {
            if (__instance == null) return;

            var requestEmpty = FoodSmokerHelper.RequestEmptyField(__instance);
            var working = FoodSmokerHelper.WorkingField(__instance);

            if (requestEmpty != null && working != null)
            {
                requestEmpty.Enter((FoodSmoker.StatesInstance smi) =>
                {
                    ExceptionShield.Execute("FoodSmoker.OnEmptyComplete", () =>
                    {
                        if (smi != null)
                        {
                            smi.OnEmptyComplete(null);
                            smi.GoTo(working);
                        }
                    });
                });
            }
        }
    }

    [HarmonyPatch(typeof(FoodSmoker), "CreateChore")]
    internal static class FoodSmoker_CreateChore_Patch
    {
        private static void Postfix(FoodSmoker.StatesInstance smi, ref Chore __result)
        {
            Chore chore = __result;
            ExceptionShield.Execute("FoodSmoker.CreateChore", () =>
            {
                if (smi != null)
                {
                    smi.OnEmptyComplete(null);
                    var working = FoodSmokerHelper.WorkingField(smi.sm);
                    if (working != null)
                    {
                        smi.GoTo(working);
                    }
                }
                chore?.AddPrecondition(MachineryPreconditions.NeverAvailable);
            });
        }
    }
}
