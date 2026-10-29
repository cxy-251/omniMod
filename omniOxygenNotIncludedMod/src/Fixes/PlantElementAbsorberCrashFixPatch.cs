using System;
using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Fixes
{
    /// <summary>
    /// 修复原版植物施肥/灌溉计算中的空引用崩溃（NullReferenceException）：
    /// 当农砖（Farm Tile）或种植箱内的肥料被植物消耗完毕时，Klei 底层直接销毁了该 GameObject，
    /// 但该对象在 Storage.items 列表中残留为 null（或已销毁的 Unity 对象）。
    /// 
    /// 当机械手或复制人向农砖补送新肥料（Storage.Store）时，会触发 GameHashes.OnStorageChange 事件，
    /// 进而执行 FertilizationMonitor.UpdateFertilization -> PlantElementAbsorber.PlanConsume -> new Planner(storage)。
    /// 
    /// 原版 Planner..ctor(Storage storage) 遍历 storage.items 时未经非空校验直接调用：
    /// item.TryGetComponent&lt;KPrefabID&gt;(out var component)，
    /// 导致底层在 TryGetComponentFastPath 中抛出 NullReferenceException，引发游戏瞬间闪退中断！
    /// 
    /// 本补丁提供三重防护：
    /// 1. Storage.Store 前置清理：每次存入物资时，自动移除 storage.items 中所有已失效或被销毁的 null 残留。
    /// 2. PlantElementAbsorber 计算前置清理：在构造 Planner 计算消耗前，自动确保 storage.items 纯净无空项。
    /// 3. UpdateFertilization / UpdateIrrigation / DeliverComplete 异常安全网：提供 Finalizer 拦截任何突发异常，保障游戏平稳运行。
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
                ModLog.Info($"[CrashFix] 已清理 Storage (ID: {storage.name}) 中 {removed} 个残留已销毁物品，成功阻断 Planner 空引用崩溃。");
            }
        }

        [HarmonyPatch(typeof(Storage), nameof(Storage.Store), new Type[] { typeof(GameObject), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
        public static class Storage_Store_CleanNulls_Patch
        {
            public static void Prefix(Storage __instance)
            {
                SafePurgeNullItems(__instance);
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
                        ModLog.Warn($"[CrashFix] FertilizationMonitor.UpdateFertilization 捕获到异常并已安全化解: {__exception.Message}");
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
                        ModLog.Warn($"[CrashFix] IrrigationMonitor.UpdateIrrigation 捕获到异常并已安全化解: {__exception.Message}");
                    }
                    return null;
                }
                return null;
            }
        }

        [HarmonyPatch(typeof(FetchAreaChore.StatesInstance), nameof(FetchAreaChore.StatesInstance.DeliverComplete))]
        public static class FetchAreaChore_DeliverComplete_Patch
        {
            public static Exception Finalizer(Exception __exception)
            {
                if (__exception != null)
                {
                    ModLog.Warn($"[CrashFix] FetchAreaChore.DeliverComplete: 捕获并抑制投递完成回调异常，避免游戏崩溃: {__exception.Message}");
                    return null;
                }
                return null;
            }
        }
    }
}
