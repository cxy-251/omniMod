using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Collection;
using UnityEngine;

namespace OmniMod.Automation
{
    /// <summary>
    /// 「隔空取物」。
    ///
    /// 让 OmniMod 聚合箱里的物品，对任何小人 / 机械臂 / 机器人来说都像"就在身边"——
    /// 取物那一步不用走过去；取完照常搬去工地（中间有真实搬运者）。
    /// </summary>
    internal static class RemoteFetch
    {
        internal static bool IsInBox(IApproachable approachable)
        {
            Pickupable p = approachable as Pickupable;
            return p != null && AggregateBoxRegistry.IsBoxStorage(p.storage);
        }

        private static int _logCount;

        internal static void LogOnce(string msg)
        {
            if (_logCount < 3)
            {
                _logCount++;
                Debug.Log("[OmniMod] " + msg);
            }
        }
    }

    // =========================================================================
    // 小人寻路判断：到"箱子所在格" / 箱内物品的移动代价 = 0
    // =========================================================================

    [HarmonyPatch(typeof(Navigator), nameof(Navigator.GetNavigationCost), new[] { typeof(int), typeof(CellOffset[]) })]
    internal static class Navigator_GetNavigationCost_CellOffsets_Patch
    {
        private static void Postfix(int cell, ref int __result)
        {
            if (__result != 0 && AggregateBoxRegistry.IsActiveJunkBoxCell(cell))
            {
                __result = 0;
                RemoteFetch.LogOnce("隔空取物：到箱子格的移动代价改为 0（CellOffset[]）");
            }
        }
    }

    [HarmonyPatch(typeof(Navigator), nameof(Navigator.GetNavigationCost), new[] { typeof(int), typeof(IReadOnlyList<CellOffset>) })]
    internal static class Navigator_GetNavigationCost_CellList_Patch
    {
        private static void Postfix(int cell, ref int __result)
        {
            if (__result != 0 && AggregateBoxRegistry.IsActiveJunkBoxCell(cell))
            {
                __result = 0;
                RemoteFetch.LogOnce("隔空取物：到箱子格的移动代价改为 0（IReadOnlyList）");
            }
        }
    }

    [HarmonyPatch(typeof(Navigator), nameof(Navigator.GetNavigationCost), new[] { typeof(IApproachable) })]
    internal static class Navigator_GetNavigationCost_IApproachable_Patch
    {
        private static void Postfix(IApproachable approachable, ref int __result)
        {
            if (__result != 0 && RemoteFetch.IsInBox(approachable))
            {
                __result = 0;
                RemoteFetch.LogOnce("隔空取物：到箱内物品的移动代价改为 0（IApproachable）");
            }
        }
    }

    // 机械臂：目标在够到范围内 → 直接拾取不移动
    [HarmonyPatch(typeof(ChoreConsumer), nameof(ChoreConsumer.IsWithinReach))]
    internal static class ChoreConsumer_IsWithinReach_Patch
    {
        private static void Postfix(IApproachable approachable, ref bool __result)
        {
            if (!__result && RemoteFetch.IsInBox(approachable))
            {
                __result = true;
                RemoteFetch.LogOnce("隔空取物：机械臂可够到箱内物品");
            }
        }
    }

    // 机械臂领取搬运任务前的可达校验
    [HarmonyPatch(typeof(SolidTransferArm), nameof(SolidTransferArm.IsCellReachable))]
    internal static class SolidTransferArm_IsCellReachable_Patch
    {
        private static void Postfix(int cell, ref bool __result)
        {
            if (!__result && AggregateBoxRegistry.IsActiveBoxCell(cell))
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// 主线程上寻找搬运目标：如果机械臂身边的物品列表没找到匹配物，立即在箱内物品查找。
    /// </summary>
    [HarmonyPatch(typeof(SolidTransferArm), nameof(SolidTransferArm.FindFetchTarget))]
    internal static class SolidTransferArm_FindFetchTarget_Patch
    {
        private static void Postfix(SolidTransferArm __instance, Storage destination, FetchChore chore, ref Pickupable __result)
        {
            if (__result != null || __instance == null || destination == null || chore == null)
            {
                return;
            }

            int world = __instance.GetMyWorldId();
            Pickupable[] boxItems = AggregateBoxRegistry.GetWorldBoxItems(world);
            for (int i = 0; i < boxItems.Length; i++)
            {
                Pickupable p = boxItems[i];
                if (p == null || p.KPrefabID == null || p.UnreservedFetchAmount <= 0f)
                {
                    continue;
                }
                if (!Assets.IsTagSolidTransferArmConveyable(p.KPrefabID.PrefabTag))
                {
                    continue;
                }
                if (FetchManager.IsFetchablePickup(p, chore, destination))
                {
                    __result = p;
                    RemoteFetch.LogOnce("隔空取物：机械臂锁定箱内目标: " + p.name);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// 机械臂工作线程异步刷新候选物品：把本星球箱内可传送物品补进候选列表。
    /// 注意：此方法在工作线程运行，切忌调用 __instance.gameObject 等 Unity 主线程属性！
    /// </summary>
    [HarmonyPatch(typeof(SolidTransferArm), "AsyncUpdate")]
    internal static class SolidTransferArm_AsyncUpdate_Patch
    {
        private static void Postfix(SolidTransferArm __instance)
        {
            try
            {
                var pickupables = Traverse.Create(__instance).Field("pickupables").GetValue<List<Pickupable>>();
                if (pickupables == null)
                {
                    return;
                }

                int gameCell = Traverse.Create(__instance).Field("gameCell").GetValue<int>();
                if (!Grid.IsValidCell(gameCell))
                {
                    return;
                }
                int world = (int)Grid.WorldIdx[gameCell];
                Pickupable[] boxItems = AggregateBoxRegistry.GetWorldBoxItems(world);
                for (int i = 0; i < boxItems.Length; i++)
                {
                    Pickupable p = boxItems[i];
                    if (p == null || p.KPrefabID == null)
                    {
                        continue;
                    }
                    if (!Assets.IsTagSolidTransferArmConveyable(p.KPrefabID.PrefabTag))
                    {
                        continue;
                    }
                    if (p.UnreservedFetchAmount <= 0f)
                    {
                        continue;
                    }
                    if (!pickupables.Contains(p))
                    {
                        pickupables.Add(p);
                    }
                }
            }
            catch
            {
                // 工作线程里出任何岔子都别影响机械臂本身
            }
        }
    }
}
