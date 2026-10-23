using System;
using HarmonyLib;
using OmniMod.Core.Logging;

namespace OmniMod.Fixes
{
    /// <summary>
    /// 修复原版 Bug：Pickupable 在所在格子发生变化时，由 CellChangeMonitor 调用 OnCellChange()。
    /// 当物品正在被吸收（absorb）、被拾取入箱（stored）、融化或被销毁时，
    /// 其内部的 objectLayerListItem 会被注销置空，但 CellChangeMonitor 的帧更新回调中
    /// 仍可能在当帧持有该引用并触发 OnCellChange()。
    /// 
    /// 此时 OnCellChange 内部执行 this.objectLayerListItem.Update(...)，由于 objectLayerListItem 为 null，
    /// 直接在 Game.Update -> CellChangeMonitor.RenderEveryTick 中抛出 NullReferenceException，
    /// 导致游戏彻底崩溃跳出。
    /// 
    /// 这里通过 Prefix 提前拦截无效调用（对象为空或 objectLayerListItem 为空），
    /// 并通过 Finalizer 终极抑制该方法内部可能抛出的任何未知异常，彻底防止闪退。
    /// </summary>
    [HarmonyPatch(typeof(Pickupable), "OnCellChange")]
    internal static class PickupableCrashFixPatch
    {
        private static int _loggedPrefix;
        private static int _loggedFinalizer;

        [HarmonyPrefix]
        private static bool Prefix(Pickupable __instance)
        {
            if (__instance == null || __instance.gameObject == null || __instance.objectLayerListItem == null)
            {
                if (_loggedPrefix < 10)
                {
                    _loggedPrefix++;
                    ModLog.Warn("[CrashFix] Pickupable.OnCellChange: 拦截到无效对象或已失效的 objectLayerListItem，已安全跳过（防止原版 NRE 崩溃）");
                }
                return false;
            }
            return true;
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception)
        {
            if (__exception != null)
            {
                if (_loggedFinalizer < 10)
                {
                    _loggedFinalizer++;
                    ModLog.Warn($"[CrashFix] Pickupable.OnCellChange 异常已安全抑制，避免游戏闪退: {__exception.Message}");
                }
                return null;
            }
            return null;
        }
    }
}
