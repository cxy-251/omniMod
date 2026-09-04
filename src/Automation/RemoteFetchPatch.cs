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
    ///
    /// 关键：<c>Navigator.GetNavigationCost</c> 有多个重载。小人搬运的"接近拾取物"子状态
    /// 用的是"到某个格子（带接近偏移）要多远"这一版——所以要拦的是**接收格子的重载**，
    /// 对"箱子所在格"一律返回 0（在旁边）。同时拦 IApproachable 版和机械臂的可达判断。
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

    // 到"箱子所在格"的移动代价 = 0（带偏移版）
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
            if (!__result && AggregateBoxRegistry.IsActiveJunkBoxCell(cell))
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// 机械臂只扫描自己半径内的物品——箱子在半径外就完全看不到。
    /// 这里在机械臂每次刷新候选物品之后，把"本星球箱内的可传送物品"直接补进它的候选列表，
    /// 无视距离。之后配合 IsWithinReach 补丁，机械臂就能原地取、再送去工地（含泥土送砖块、种子种砖块）。
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

                int world = __instance.gameObject.GetMyWorldId();
                KPrefabID armId = __instance.GetComponent<KPrefabID>();
                int armInstance = armId != null ? armId.InstanceID : -1;

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
                    if (armInstance != -1 && !p.CouldBePickedUpByTransferArm(armInstance))
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
