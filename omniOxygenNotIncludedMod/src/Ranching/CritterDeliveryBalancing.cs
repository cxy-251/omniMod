using System;
using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Core.Logging;
using OmniMod.Ranching.Registries;
using UnityEngine;

namespace OmniMod.Ranching
{
    /// <summary>
    /// 放生点全星系均匀负载调度系统（均匀放生）：
    /// 解决原版游戏总是单吊一个放生点（离孵化器最近或最早建造的放生点）塞满、
    /// 其他放生点长期饥饿/空置的问题。
    /// 
    /// 核心算法：
    /// 1. 动态感知每个放生点的“有效容纳数”（已在房间内的动物数 + 正在搬运途中的动物数）。
    /// 2. 计算饱和度比率（Fullness Ratio = 有效动物数 / 目标上限）。
    /// 3. 为空置率更高的放生点动态赋予更高的 priorityMod（1 ~ 100）。
    ///    - 0% 饱和度：priorityMod = 100（最高优）
    ///    - 50% 饱和度：priorityMod = 50
    ///    - 97.5% 饱和度：priorityMod = 2
    /// 4. 在原版 Chore+Precondition+Context.CompareTo 中，priorityMod 优于寻路距离 cost，
    ///    促使复制人和机械臂自动轮流向各个生态舱/房间放生，实现多世界、多生态舱 1-by-1 严格均匀放生！
    /// </summary>
    public static class CritterDeliveryBalancing
    {
        private static readonly AccessTools.FieldRef<CreatureDeliveryPoint, List<FetchOrder2>> FetchesRef =
            AccessTools.FieldRefAccess<CreatureDeliveryPoint, List<FetchOrder2>>("fetches");

        private static int _lastBalanceFrame = -1;

        public static void BalanceAllDropOffs()
        {
            if (Time.frameCount == _lastBalanceFrame) return;
            _lastBalanceFrame = Time.frameCount;

            var points = CreatureDeliveryRegistry.Snapshot();
            if (points == null || points.Length == 0) return;

            for (int i = 0; i < points.Length; i++)
            {
                var point = points[i];
                if (point != null && point.isSpawned)
                {
                    UpdateDropOffPriority(point);
                }
            }
        }

        public static void UpdateDropOffPriority(CreatureDeliveryPoint point)
        {
            if (point == null || !point.isSpawned || point.critterCapacity == null) return;

            var fetches = FetchesRef(point);
            if (fetches == null || fetches.Count == 0) return;

            int limit = point.critterCapacity.creatureLimit;
            if (limit <= 0) return;

            int current = point.critterCapacity.storedCreatureCount;
            int inProgress = 0;
            for (int i = 0; i < fetches.Count; i++)
            {
                var fetch = fetches[i];
                if (fetch != null && fetch.InProgress)
                {
                    inProgress++;
                }
            }

            int effectiveCount = current + inProgress;
            if (effectiveCount >= limit)
            {
                // 已满或即将搬满，置低优先级
                for (int i = 0; i < fetches.Count; i++)
                {
                    fetches[i]?.SetPriorityMod(0);
                }
                return;
            }

            // 计算未饱和度：越空 priorityMod 越高（1 ~ 100）
            float fullness = Mathf.Clamp01((float)effectiveCount / limit);
            int priorityMod = Mathf.Clamp(Mathf.RoundToInt((1f - fullness) * 100f), 1, 100);

            for (int i = 0; i < fetches.Count; i++)
            {
                var fetch = fetches[i];
                if (fetch != null && fetch.PriorityMod != priorityMod)
                {
                    fetch.SetPriorityMod(priorityMod);
                }
            }
        }
    }

    [HarmonyPatch(typeof(CreatureDeliveryPoint), "RebalanceFetches")]
    internal static class CreatureDeliveryPoint_RebalanceFetches_Balancing_Patch
    {
        private static void Postfix(CreatureDeliveryPoint __instance)
        {
            if (__instance != null)
            {
                CritterDeliveryBalancing.UpdateDropOffPriority(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(CreatureDeliveryPoint), "OnFetchBegun")]
    internal static class CreatureDeliveryPoint_OnFetchBegun_Balancing_Patch
    {
        private static void Postfix()
        {
            CritterDeliveryBalancing.BalanceAllDropOffs();
        }
    }

    [HarmonyPatch(typeof(CreatureDeliveryPoint), "OnFetchComplete")]
    internal static class CreatureDeliveryPoint_OnFetchComplete_Balancing_Patch
    {
        private static void Postfix()
        {
            CritterDeliveryBalancing.BalanceAllDropOffs();
        }
    }
}
