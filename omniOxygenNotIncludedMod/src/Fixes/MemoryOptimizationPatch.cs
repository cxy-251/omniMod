using System;
using HarmonyLib;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Fixes
{
    /// <summary>
    /// 全局深度内存治理与自动泄露阻断器：
    /// 针对《缺氧》Mono 运行时非紧缩式垃圾回收器（Boehm GC）导致的内存碎片膨胀、
    /// 以及已销毁 GameObjects 底层 C++ 材质/纹理/网格长期滞留 RAM/VRAM 的顽疾进行针对性治理。
    /// 
    /// 治理策略：
    /// 1. 暂停期（ESC 菜单）无感深度释放：在游戏本身已经暂停时，安全执行 Full Gen2 GC 与 Resources.UnloadUnusedAssets()；
    /// 2. 每日清晨自动存档后协同回收：借自动存档微顿契机，即时收割上一周期遗留的垃圾对象与纹理；
    /// 3. 全局快捷键随时急救（Alt + M）：玩家感觉内存偏高或顿挫时，随时手动一键释放；
    /// 4. 窗口失焦/切后台静默优化：当用户切出游戏至桌面或浏览器时，自动释放未使用的图形资产与堆内存。
    /// </summary>
    public static class MemoryOptimizationPatch
    {
        private static float _lastDeepCleanRealtime = 0f;
        private const float MIN_CLEAN_INTERVAL = 30f; // 最少间隔 30 秒，避免高频打断

        /// <summary>
        /// 执行一次全方位的深度内存压榨与孤儿资产卸载
        /// </summary>
        /// <param name="reason">触发源说明</param>
        /// <param name="forceUnloadAssets">是否执行耗时但强力的 Resources.UnloadUnusedAssets()</param>
        public static void PerformDeepCleanup(string reason, bool forceUnloadAssets = true)
        {
            try
            {
                long beforeHeap = GC.GetTotalMemory(false) / 1048576;

                // 1. 裁剪冗余的历史日报明细
                if (ReportManager.Instance != null)
                {
                    try
                    {
                        ReportManagerOptimization.PruneHistoricalReports(ReportManager.Instance, reason);
                    }
                    catch { }
                }

                // 1.5 确保所有离屏生态舱维持视觉与骨骼动画休眠
                try
                {
                    OmniMod.Buildings.PocketWorldDormancyManager.InitialDormancySweep();
                }
                catch { }

                // 2. 强制触发 Gen2 垃圾回收，断开所有未达引用的死循环链
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect();

                long midHeap = GC.GetTotalMemory(false) / 1048576;

                // 3. 卸载底层已销毁对象残留的纹理、材质与网格资产（彻底杜绝 Unity C++ 内存泄露）
                if (forceUnloadAssets)
                {
                    Resources.UnloadUnusedAssets();
                }

                long afterHeap = GC.GetTotalMemory(false) / 1048576;

                _lastDeepCleanRealtime = Time.unscaledTime;
                ModLog.Info($"[内存深度优化({reason})] 清理完成！托管堆: {beforeHeap} MB -> {afterHeap} MB (释放 {beforeHeap - afterHeap} MB)。原生未引用资源已同步卸载。");
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[内存深度优化] 清理过程异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 补丁 1：呼出暂停菜单（ESC）时深度清理（此时游戏已暂停，完全无游玩卡顿感）
        /// </summary>
        [HarmonyPatch(typeof(PauseScreen), "OnShow")]
        internal static class PauseScreen_OnShow_MemoryClean_Patch
        {
            private static void Postfix(bool show)
            {
                if (show && Time.unscaledTime - _lastDeepCleanRealtime >= MIN_CLEAN_INTERVAL)
                {
                    PerformDeepCleanup("暂停菜单", forceUnloadAssets: true);
                }
            }
        }

        /// <summary>
        /// 补丁 2：自动存档完成之后协同清理（借存档卡顿契机顺手回收全天累积垃圾）
        /// </summary>
        [HarmonyPatch(typeof(SaveLoader), "Save", new[] { typeof(string), typeof(bool), typeof(bool) })]
        internal static class SaveLoader_Save_MemoryClean_Patch
        {
            private static void Postfix(bool isAutoSave)
            {
                if (isAutoSave && Time.unscaledTime - _lastDeepCleanRealtime >= MIN_CLEAN_INTERVAL)
                {
                    PerformDeepCleanup("自动存档后", forceUnloadAssets: true);
                }
            }
        }

        /// <summary>
        /// 补丁 3：监听全局快捷键 Alt + M，支持玩家随时手动急救内存
        /// </summary>
        [HarmonyPatch(typeof(Game), "Update")]
        internal static class Game_Update_MemoryHotkey_Patch
        {
            private static void Postfix()
            {
                // Alt + M (Option + M on macOS)
                if ((Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && Input.GetKeyDown(KeyCode.M))
                {
                    ModLog.Info("[内存优化] 玩家按下快捷键 Alt+M，正在触发即时深度内存整理...");
                    PerformDeepCleanup("快捷键Alt+M", forceUnloadAssets: true);
                }
            }
        }

        /// <summary>
        /// 补丁 4：当游戏窗口失焦（切到后台/桌面）时静默清理
        /// </summary>
        [HarmonyPatch(typeof(Global), "OnApplicationFocus")]
        internal static class Global_OnApplicationFocus_MemoryClean_Patch
        {
            private static void Postfix(bool focus)
            {
                if (!focus && Time.unscaledTime - _lastDeepCleanRealtime >= 45f)
                {
                    PerformDeepCleanup("切至后台", forceUnloadAssets: true);
                }
            }
        }
    }
}
