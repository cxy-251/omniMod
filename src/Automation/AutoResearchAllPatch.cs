using UnityEngine;

namespace OmniMod.Automation
{
    /// <summary>
    /// 自动滚完整棵科技树——玩家永远不用手动去点"研究什么"。
    ///
    /// 背景：<see cref="ResearchCenter_Sim200ms_AutoResearch_Patch"/> 让研究站免人力自转研究点，
    /// 但它在"没有活动研究"时会放行香草逻辑——也就是说你不选，就什么都不研究。
    ///
    /// 这里补上"自动选"：没有活动研究时，从全科技表里挑一项
    /// 【未完成 + 前置全部完成 + 在科技树上有节点 + 层级(tier)最低】的科技，
    /// 调 <see cref="Research.SetActiveResearch"/> 塞进队列。
    /// 香草的 <c>CheckBuyResearch</c> 会在点数够时自动购买、并 <c>GetNextTech</c> 推进；
    /// 我们下一拍再补下一项。如此把整棵树滚完。
    ///
    /// 由 <see cref="ResearchCenter_Sim200ms_AutoResearch_Patch"/> 的 Prefix 每 200ms 调用一次
    /// （没有研究站时本来也没法研究，不调用无妨）。
    /// </summary>
    internal static class AutoResearchAll
    {
        private static int _logged;

        /// <summary>确保"有一项研究在进行中"。已经有活动研究、或全部科技已完成时什么都不做。</summary>
        internal static void EnsureSomethingQueued()
        {
            Research research = Research.Instance;
            if (research == null || research.GetActiveResearch() != null)
            {
                return;
            }

            if (Db.Get() == null || Db.Get().Techs == null || Db.Get().Techs.resources == null)
            {
                return;
            }

            Tech pick = null;
            int bestTier = int.MaxValue;

            foreach (Tech tech in Db.Get().Techs.resources)
            {
                if (tech == null || !tech.FoundNode)
                {
                    continue;
                }
                if (tech.IsComplete() || !tech.ArePrerequisitesComplete())
                {
                    continue;
                }
                if (tech.tier < bestTier)
                {
                    bestTier = tech.tier;
                    pick = tech;
                }
            }

            if (pick == null)
            {
                return;   // 全研究完了
            }

            research.SetActiveResearch(pick, clearQueue: false);

            if (_logged < 30)
            {
                _logged++;
                Debug.Log($"[OmniMod] 自动研究：选定下一项科技 {pick.Name}（tier {pick.tier}）");
            }
        }
    }
}
