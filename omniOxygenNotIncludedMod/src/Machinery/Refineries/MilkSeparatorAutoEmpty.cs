using HarmonyLib;
using OmniMod.Core.Safety;

namespace OmniMod.Machinery.Refineries
{
    /// <summary>
    /// 咸乳分离机 / 提炼器（MilkSeparator）自动排空
    /// 咸乳蜡 / 鱼子酱积满时自动掉落到地面并切入完成状态，无需小人跑腿手动清空。
    /// </summary>
    [HarmonyPatch(typeof(MilkSeparator), nameof(MilkSeparator.InitializeStates))]
    internal static class MilkSeparator_AutoEmpty_Patch
    {
        private static void Postfix(MilkSeparator __instance)
        {
            if (__instance == null || __instance.operational == null || __instance.operational.full == null)
            {
                return;
            }

            __instance.operational.full
                .Enter(smi =>
                {
                    ExceptionShield.Execute("MilkSeparator.DropMilkFat", () =>
                    {
                        smi.DropMilkFat();
                        smi.GoTo(__instance.operational.emptyComplete);
                    });
                });
        }
    }

    [HarmonyPatch(typeof(MilkSeparator), "CreateEmptyChore")]
    internal static class MilkSeparator_CreateEmptyChore_Patch
    {
        private static void Postfix(MilkSeparator.Instance smi, ref Chore __result)
        {
            Chore chore = __result;
            ExceptionShield.Execute("MilkSeparator.CreateEmptyChore", () =>
            {
                if (smi != null)
                {
                    smi.DropMilkFat();
                    smi.GoTo(smi.sm.operational.emptyComplete);
                }
                chore?.AddPrecondition(MachineryPreconditions.NeverAvailable);
            });
        }
    }
}
