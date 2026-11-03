using HarmonyLib;
using OmniMod.Core.Logging;

namespace OmniMod.Collection
{
    [HarmonyPatch(typeof(Game), "SimEveryTick")]
    internal static class Game_SimEveryTick_WorldIndex_Patch
    {
        private static void Postfix()
        {
            try
            {
                WorldStorageIndex.Advance();
            }
            catch (System.Exception ex)
            {
                WorldStorageIndex.Abort();
                ModLog.Throttle("world_index_failed", $"仓储索引更新失败，将重新登记：{ex.Message}", 10f);
            }
        }
    }

    [HarmonyPatch(typeof(Game), "OnCleanUp")]
    internal static class Game_OnCleanUp_WorldIndex_Patch
    {
        private static void Prefix()
        {
            OmniMod.Pool.OmniEntityPool.BeginShutdown();
            WorldStorageIndex.Clear();
            AggregateBoxRegistry.Clear();
            OmniMod.Logistics.RemoteFetch.RemoteFetchFailures.Clear();
            CollectionCandidateSnapshot.Clear();
        }
    }
}
