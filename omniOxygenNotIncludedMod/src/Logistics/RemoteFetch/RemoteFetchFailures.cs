using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace OmniMod.Logistics.RemoteFetch
{
    /// <summary>
    /// 失败查找短期节流器：
    /// 当某台机械臂在某世界对特定任务（Chore）无法找到目标材料时，在短期内直接返回失败。
    /// 修复重点：旧版使用全局 _generation++，导致任何世界哪怕存入一块铜矿，
    /// 都会瞬间把全星系所有机械臂的节流缓存全部击穿，导致 831 台机械臂在 10 秒内暴走轮询 21,209 次。
    /// 现改为按任务与目标世界稳定的 1.5 秒时间窗口节流，彻底阻断高频空转风暴。
    /// </summary>
    internal static class RemoteFetchFailures
    {
        private sealed class Failure
        {
            internal float Time;
            internal int TagsHash;
            internal Tag RequiredTag;
        }

        private static ConditionalWeakTable<FetchChore, Dictionary<int, Failure>> _chores =
            new ConditionalWeakTable<FetchChore, Dictionary<int, Failure>>();

        internal static bool Contains(FetchChore chore, int world)
        {
            if (chore == null) return false;
            return _chores.TryGetValue(chore, out var worlds) && worlds.TryGetValue(world, out Failure failure) &&
                failure.TagsHash == chore.tagsHash && failure.RequiredTag == chore.requiredTag &&
                Time.unscaledTime - failure.Time < 1.5f;
        }

        internal static void Record(FetchChore chore, int world)
        {
            if (chore == null) return;
            Dictionary<int, Failure> worlds = _chores.GetOrCreateValue(chore);
            if (!worlds.TryGetValue(world, out Failure failure)) worlds[world] = failure = new Failure();
            failure.Time = Time.unscaledTime;
            failure.TagsHash = chore.tagsHash;
            failure.RequiredTag = chore.requiredTag;
        }

        internal static void Invalidate()
        {
            // 不再执行全局暴力清空，允许每个任务的失败节流自然度过 1.5 秒冷却期
        }

        internal static void Clear()
        {
            _chores = new ConditionalWeakTable<FetchChore, Dictionary<int, Failure>>();
        }
    }
}
