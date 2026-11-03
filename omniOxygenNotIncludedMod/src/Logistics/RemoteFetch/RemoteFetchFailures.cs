using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace OmniMod.Logistics.RemoteFetch
{
    /// <summary>
    /// 空查找只对同一任务、同一来源世界短暂节流。不能按物品标签共享失败：
    /// 其它目的地的筛选、优先级和可用来源可能不同。弱引用避免已结束任务留在缓存中。
    /// </summary>
    internal static class RemoteFetchFailures
    {
        private sealed class Failure
        {
            internal float Time;
            internal int Generation;
            internal int TagsHash;
            internal Tag RequiredTag;
        }

        private static ConditionalWeakTable<FetchChore, Dictionary<int, Failure>> _chores =
            new ConditionalWeakTable<FetchChore, Dictionary<int, Failure>>();
        private static int _generation;

        internal static bool Contains(FetchChore chore, int world)
        {
            return _chores.TryGetValue(chore, out var worlds) && worlds.TryGetValue(world, out Failure failure) &&
                failure.Generation == _generation && failure.TagsHash == chore.tagsHash &&
                failure.RequiredTag == chore.requiredTag && Time.unscaledTime - failure.Time < 3f;
        }

        internal static void Record(FetchChore chore, int world)
        {
            Dictionary<int, Failure> worlds = _chores.GetOrCreateValue(chore);
            if (!worlds.TryGetValue(world, out Failure failure)) worlds[world] = failure = new Failure();
            failure.Time = Time.unscaledTime;
            failure.Generation = _generation;
            failure.TagsHash = chore.tagsHash;
            failure.RequiredTag = chore.requiredTag;
        }

        internal static void Invalidate() => _generation++;

        internal static void Clear()
        {
            _chores = new ConditionalWeakTable<FetchChore, Dictionary<int, Failure>>();
            _generation = 0;
        }
    }
}
