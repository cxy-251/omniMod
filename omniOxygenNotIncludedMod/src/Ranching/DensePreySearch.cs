using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace OmniMod.Ranching
{
    /// <summary>密集养殖区按食谱共享候选快照，每轮有界寻路；不改变数量、代谢、寿命或猎物消费。</summary>
    internal static class DensePreySearch
    {
        private const int DenseThreshold = 128;
        private const int SearchBudget = 64;
        private sealed class Candidates
        {
            // 只保存原版实例 ID，死亡／销毁的鱼不会被缓存继续强引用。
            internal readonly List<int> items = new List<int>();
            internal float refreshAt;
        }
        private sealed class RoomIndex
        {
            internal readonly Dictionary<Diet, Candidates> diets = new Dictionary<Diet, Candidates>();
        }
        private sealed class Cursor { internal int next; }
        private static ConditionalWeakTable<CavityInfo, RoomIndex> rooms = new ConditionalWeakTable<CavityInfo, RoomIndex>();
        private static ConditionalWeakTable<SolidConsumerMonitor.Instance, Cursor> cursors =
            new ConditionalWeakTable<SolidConsumerMonitor.Instance, Cursor>();

        internal static bool TryFind(SolidConsumerMonitor.Instance smi, out bool found)
        {
            found = false;
            if (smi == null || App.isLoading || Game.Instance?.roomProber == null) return false;
            int cell = Grid.PosToCell(smi);
            if (!Grid.IsValidCell(cell)) return false;
            CavityInfo room = Game.Instance.roomProber.GetCavityForCell(cell);
            if (room == null || room.creatures.Count < DenseThreshold || smi.diet == null) return false;
            RoomIndex index = rooms.GetValue(room, _ => new RoomIndex());
            if (!index.diets.TryGetValue(smi.diet, out Candidates candidates))
            {
                candidates = new Candidates();
                index.diets.Add(smi.diet, candidates);
            }
            float now = GameClock.Instance.GetTime();
            if (now >= candidates.refreshAt)
            {
                candidates.refreshAt = now + 1f;
                candidates.items.Clear();
                foreach (KPrefabID prey in room.creatures)
                    if (prey != null && prey.InstanceID != KPrefabID.InvalidInstanceID &&
                        smi.diet.GetDietInfo(prey.PrefabTag) != null) candidates.items.Add(prey.InstanceID);
            }
            if (candidates.items.Count == 0) return true;
            Cursor cursor = cursors.GetValue(smi, _ => new Cursor());
            KPrefabID best = null;
            int bestCost = int.MaxValue;
            int world = Grid.WorldIdx[cell];
            for (int i = 0, limit = Mathf.Min(SearchBudget, candidates.items.Count); i < limit; i++)
            {
                if (cursor.next >= candidates.items.Count) cursor.next = 0;
                KPrefabID prey = KPrefabIDTracker.Get().GetInstance(candidates.items[cursor.next++]);
                if (prey == null || prey.HasTag(GameTags.Creatures.ReservedByCreature)) continue;
                int preyCell = Grid.PosToCell(prey);
                if (!Grid.IsValidCell(preyCell) || Grid.WorldIdx[preyCell] != world ||
                    Game.Instance.roomProber.GetCavityForCell(preyCell) != room ||
                    smi.diet.GetDietInfo(prey.PrefabTag) == null) continue;
                int cost = smi.GetCost(prey.gameObject);
                if (cost < 0 || cost >= bestCost) continue;
                best = prey; bestCost = cost;
                if (cost < 3) break;
            }
            if (best == null) return true;
            smi.SetTargetEdible(best.gameObject, bestCost);
            smi.targetEdibleOffset = smi.GetBestEdibleOffset(best.gameObject);
            found = true;
            return true;
        }

        internal static void Reset()
        {
            rooms = new ConditionalWeakTable<CavityInfo, RoomIndex>();
            cursors = new ConditionalWeakTable<SolidConsumerMonitor.Instance, Cursor>();
        }
    }

    [HarmonyPatch(typeof(SolidConsumerMonitor), "FindPreyInCavity")]
    internal static class DensePreySearch_Find_Patch
    {
        private static bool Prefix(SolidConsumerMonitor.Instance smi, ref bool __result)
        {
            var hunter = smi.GetComponent<PredatorPopulation>();
            if (hunter != null) { __result = hunter.Choose(smi); return false; }
            return !DensePreySearch.TryFind(smi, out __result);
        }
    }

    [HarmonyPatch(typeof(Game), "OnDestroy")]
    internal static class DensePreySearch_Reset_Patch
    {
        private static void Postfix() => DensePreySearch.Reset();
    }
}
