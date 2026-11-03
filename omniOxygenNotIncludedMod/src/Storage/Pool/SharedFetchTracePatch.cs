using HarmonyLib;
using OmniMod.Core.Logging;

namespace OmniMod.Pool
{
    // 仅记录本轮故障的硫与碎石机任务；每世界每阶段限频，日志本身不参与取货判断。
    [HarmonyPatch(typeof(FetchChore), nameof(FetchChore.FetchAreaBegin))]
    internal static class FetchChore_Shared_AcceptedTrace
    {
        private static void Postfix(FetchChore __instance, Chore.Precondition.Context context, float amount_to_be_fetched)
        {
            try { SharedFetchRequest.Accepted(__instance, context, amount_to_be_fetched); }
            catch (System.Exception ex) { ModLog.Throttle("shared_accepted_log", "[共享取货] 接单日志未完成：" + ex.Message, 30f); }
        }
    }
    [HarmonyPatch(typeof(FetchChore), nameof(FetchChore.FetchAreaEnd))]
    internal static class FetchChore_Shared_CompletedTrace
    {
        private static void Prefix(FetchChore __instance, ChoreDriver driver, bool is_success)
        {
            try { SharedFetchRequest.Completed(__instance, driver, is_success); }
            catch (System.Exception ex) { ModLog.Throttle("shared_completed_log", "[共享取货] 投递日志未完成：" + ex.Message, 30f); }
        }
    }
}
