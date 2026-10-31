using System;
using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Fixes
{
    /// <summary>
    /// 在植物规划消耗前清理已销毁的旧库存引用；不再每次 Storage.Store 都扫描整个容器。
    /// 投递完成异常时取消该次搬运并释放预约，避免只吞掉异常后机械臂停在中间状态。
    /// </summary>
    public static class PlantElementAbsorberCrashFixPatch
    {
        private static int _loggedPurge;
        private static int _loggedMonitor;

        private static void SafePurgeNullItems(Storage storage)
        {
            if (storage == null || storage.items == null) return;
            int removed = storage.items.RemoveAll(it => it == null);
            if (removed > 0 && _loggedPurge < 10)
            {
                _loggedPurge++;
                ModLog.Info($"[CrashFix] 已清理 Storage (ID: {storage.name}) 中 {removed} 个残留已销毁物品，已在植物消耗规划前移除。");
            }
        }

        [HarmonyPatch(typeof(PlantElementAbsorber), nameof(PlantElementAbsorber.PlanConsume), new Type[] { typeof(Storage), typeof(PlantElementAbsorber.ConsumeInfo[]), typeof(float), typeof(List<PlantElementAbsorber.Planner.ConsumeCommand>) })]
        public static class PlantElementAbsorber_PlanConsume_Patch
        {
            public static void Prefix(Storage storage)
            {
                SafePurgeNullItems(storage);
            }
        }

        [HarmonyPatch(typeof(PlantElementAbsorber), nameof(PlantElementAbsorber.PlanConsume), new Type[] { typeof(Storage), typeof(PlantElementAbsorber.LocalInfo), typeof(float), typeof(List<PlantElementAbsorber.Planner.ConsumeCommand>) })]
        public static class PlantElementAbsorber_PlanConsume_LocalInfo_Patch
        {
            public static void Prefix(Storage storage)
            {
                SafePurgeNullItems(storage);
            }
        }

        [HarmonyPatch(typeof(PlantElementAbsorber), nameof(PlantElementAbsorber.FindLargest), new Type[] { typeof(Storage), typeof(PlantElementAbsorber.ConsumeInfo[]) })]
        public static class PlantElementAbsorber_FindLargest_Patch
        {
            public static void Prefix(Storage storage)
            {
                SafePurgeNullItems(storage);
            }
        }

        [HarmonyPatch(typeof(FertilizationMonitor.Instance), nameof(FertilizationMonitor.Instance.UpdateFertilization))]
        public static class FertilizationMonitor_UpdateFertilization_Patch
        {
            public static Exception Finalizer(Exception __exception)
            {
                if (__exception != null)
                {
                    if (_loggedMonitor < 10)
                    {
                        _loggedMonitor++;
                        ModLog.Warn($"[CrashFix] FertilizationMonitor.UpdateFertilization 本轮更新失败，跳过本轮计算: {__exception.Message}");
                    }
                    return null;
                }
                return null;
            }
        }

        [HarmonyPatch(typeof(IrrigationMonitor.Instance), nameof(IrrigationMonitor.Instance.UpdateIrrigation))]
        public static class IrrigationMonitor_UpdateIrrigation_Patch
        {
            public static Exception Finalizer(Exception __exception)
            {
                if (__exception != null)
                {
                    if (_loggedMonitor < 10)
                    {
                        _loggedMonitor++;
                        ModLog.Warn($"[CrashFix] IrrigationMonitor.UpdateIrrigation 本轮更新失败，跳过本轮计算: {__exception.Message}");
                    }
                    return null;
                }
                return null;
            }
        }

        [HarmonyPatch(typeof(FetchAreaChore.StatesInstance), nameof(FetchAreaChore.StatesInstance.DeliverComplete))]
        public static class FetchAreaChore_DeliverComplete_Patch
        {
            public static Exception Finalizer(FetchAreaChore.StatesInstance __instance, Exception __exception)
            {
                if (__exception == null) return null;
                try
                {
                    ChoreDriver driver = __instance?.sm.fetcher.Get<ChoreDriver>(__instance);
                    if (driver == null) return __exception;
                    driver.StopChore();
                    ModLog.Throttle("delivery_failed", $"[CrashFix] 投递回调失败，已取消本次搬运并释放预约：{__exception}", 10f);
                    return null;
                }
                catch (Exception cleanupError)
                {
                    ModLog.Warn($"[CrashFix] 搬运清理失败：{cleanupError.Message}");
                    return __exception;
                }
            }
        }
    }
}
