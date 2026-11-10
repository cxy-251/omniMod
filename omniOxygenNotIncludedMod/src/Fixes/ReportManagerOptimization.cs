using System;
using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Fixes
{
    /// <summary>
    /// 日报历史瘦身与内存深度整理：
    /// 《缺氧》原版自第 1 周期起永久保留每日详尽的 ReportEntry.Context（包括每名复制人、每台建筑的明细条目与文本）。
    /// 在数百周期、大量口袋世界、数万建筑的基地中，这会导致累积上百万个存活对象，
    /// 不仅吞噬数 GB 堆内存，而且导致 Mono 每次 Gen2 GC 必须遍历全部引用图谱，造成严重卡顿。
    ///
    /// 本优化保留最近 2 个周期的全部明细，对更早周期仅保留总量数值统计（确保殖民地概况历史折线图完好无损），
    /// 清除无用的深层上下文与文本索引，并在读档、日落结算、开启暂停菜单时联合触发 Gen2 GC 与
    /// Resources.UnloadUnusedAssets()，彻底释放已销毁实体/小人/建筑的托管对象与底层 Metal/GPU 纹理缓存。
    /// </summary>
    [HarmonyPatch(typeof(ReportManager), "OnDeserialized")]
    internal static class ReportManager_OnDeserialized_Patch
    {
        private static void Postfix(ReportManager __instance)
        {
            ReportManagerOptimization.PruneHistoricalReports(__instance, "读档后");
        }
    }

    [HarmonyPatch(typeof(ReportManager), "OnNightTime")]
    internal static class ReportManager_OnNightTime_Patch
    {
        private static void Postfix(ReportManager __instance)
        {
            ReportManagerOptimization.PruneHistoricalReports(__instance, "周期交替");
        }
    }

    [HarmonyPatch(typeof(Game), "OnSpawn")]
    internal static class Game_OnSpawn_ReportPrune_Patch
    {
        private static void Postfix()
        {
            try
            {
                ReportManagerOptimization.PruneHistoricalReports(ReportManager.Instance, "游戏初始化");
            }
            catch { }
        }
    }


    public static class ReportManagerOptimization
    {
        public const int KeepDetailedCycles = 2; // 保留最近 2 周期明细，历史周期仅保留折线图统计总量

        public static void PruneHistoricalReports(ReportManager rm, string triggerReason)
        {
            if (rm == null)
            {
                CleanupMemory(triggerReason);
                return;
            }

            List<ReportManager.DailyReport> reports = rm.reports;
            if (reports == null || reports.Count == 0)
            {
                CleanupMemory(triggerReason);
                return;
            }

            try
            {
                int currentDay = GameClock.Instance != null ? GameClock.Instance.GetCycle() : (reports[reports.Count - 1].day);
                int cutoffDay = currentDay - KeepDetailedCycles;
                if (cutoffDay <= 0)
                {
                    CleanupMemory(triggerReason);
                    return;
                }

                int prunedCycles = 0;
                int prunedEntries = 0;

                for (int i = 0; i < reports.Count; i++)
                {
                    ReportManager.DailyReport report = reports[i];
                    if (report == null || report.day >= cutoffDay)
                    {
                        continue;
                    }

                    if (report.reportEntries != null)
                    {
                        for (int j = 0; j < report.reportEntries.Count; j++)
                        {
                            ReportManager.ReportEntry entry = report.reportEntries[j];
                            if (entry == null)
                            {
                                continue;
                            }

                            int count = entry.contextEntries.Count;
                            if (count > 0)
                            {
                                prunedEntries += count;
                                entry.contextEntries.Clear();
                                entry.contextEntries.ShrinkToFit();
                            }
                            entry.context = null;
                            entry.noteStorageId = -1;
                        }
                    }
                    prunedCycles++;
                }

                CleanupMemory(triggerReason, prunedCycles, cutoffDay, prunedEntries);
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[日报瘦身] 执行异常: {ex.Message}");
            }
        }

        public static void CleanupMemory(string triggerReason, int prunedCycles = 0, int cutoffDay = 0, int prunedEntries = 0)
        {
            if (prunedEntries <= 0)
            {
                // 无新增清理项时无需执行垃圾回收，杜绝引发游玩过程中的数秒无意义主线程卡顿
                return;
            }

            long beforeHeap = GC.GetTotalMemory(false) / 1048576;
            // 适度回收被裁剪的日报对象，不调用耗时极巨且会冻结画面的 Resources.UnloadUnusedAssets()
            GC.Collect();
            long afterHeap = GC.GetTotalMemory(false) / 1048576;

            ModLog.Info($"[日报瘦身({triggerReason})] 已清理第 1 至 {cutoffDay} 周期明细（共 {prunedCycles} 周期），释放 {prunedEntries} 个上下文细项。托管堆: {beforeHeap} MB -> {afterHeap} MB。");
        }
    }
}
