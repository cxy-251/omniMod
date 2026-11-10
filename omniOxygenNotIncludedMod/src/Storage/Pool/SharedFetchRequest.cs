using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Pool
{
    /// <summary>请求量与已接单量是不同阶段的数据。查找来源时 actualamount 尚未由 FetchAreaBegin 写入。</summary>
    internal static class SharedFetchRequest
    {
        private static readonly Tag Sulfur = new Tag("Sulfur");
        private static readonly Tag RockCrusher = new Tag("RockCrusher");

        internal static float GetAmount(FetchChore chore)
        {
            if (chore == null || chore.isNull) return 0f;
            float amount = chore.amount;
            if (!(amount > 0f)) amount = chore.originalAmount;
            // 过滤小于 1 克的无意义微量搬运（如农砖/机器消耗 0.000005 kg 产生的浮点残余），避免 831 台机械臂全图空转
            if (amount < 0.001f) return 0f;
            return !float.IsInfinity(amount) && !float.IsNaN(amount) ? amount : 0f;
        }

        private static string Category(FetchChore chore)
        {
            if (chore == null || chore.destination == null) return null;
            if (chore.tagsFirst == Sulfur || chore.requiredTag == Sulfur ||
                (chore.tags != null && chore.tags.Contains(Sulfur))) return "硫";
            return chore.destination.PrefabID() == RockCrusher ? "碎石机" : null;
        }

        internal static void Candidate(FetchChore chore, Pickupable pickup, int world)
        {
            string category = Category(chore);
            if (category == null || pickup == null ||
                !LogThrottle.ShouldLog("shared_candidate_" + world + "_" + category, 20f)) return;
            ModLog.Info($"[共享取货·候选] World {world} {category}：请求 {chore.originalAmount:0.###} kg，" +
                $"已接单 {chore.amount:0.###} kg；已在入口准备 {pickup.PrefabID()}，后续仍需原版接单与搬运。");
        }
        internal static void Accepted(FetchChore chore, Chore.Precondition.Context context, float amount)
        {
            string category = Category(chore);
            ChoreConsumerState worker = context.consumerState;
            if (category == null || worker == null || !worker.hasSolidTransferArm) return;
            int world = worker.gameObject.GetMyWorldId();
            if (!LogThrottle.ShouldLog("shared_accepted_" + world + "_" + category, 20f)) return;
            ModLog.Info($"[共享取货·接单] World {world} 机械臂已接受 {category} 搬运，实际预约目标量 {amount:0.###} kg。");
        }
        internal static void Completed(FetchChore chore, ChoreDriver driver, bool success)
        {
            string category = Category(chore);
            if (category == null || driver == null || driver.GetComponent<SolidTransferArm>() == null) return;
            int world = driver.GetMyWorldId();
            if (!LogThrottle.ShouldLog("shared_completed_" + world + "_" + category + "_" + success, 20f)) return;
            ModLog.Info($"[共享取货·结束] World {world} 机械臂 {category} 搬运结果：{(success ? "投递成功" : "取消或失败")}。");
        }
    }
}
