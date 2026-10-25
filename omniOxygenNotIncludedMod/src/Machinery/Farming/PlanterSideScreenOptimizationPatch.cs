using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace OmniMod.Machinery.Farming
{
    /// <summary>
    /// 解决种植砖/土培砖/水培砖选种界面（PlanterSideScreen / ReceptacleSideScreen）卡顿掉帧问题：
    /// 1. 同帧缓存 ReceptacleSideScreen.GetAvailableAmount，消除每次点击种子或刷新时，
    ///    对数十种种子重复 6 轮以上（共两百余次）库存统计与集合遍历造成的严重掉帧；
    /// 2. 拦截 PlanterSideScreen.ToggleClicked 末尾冗余的第二次 UpdateState(null)，
    ///    杜绝单次点击种子时重复销毁并重建变异种子 UI 与预览模型的双重卡顿。
    /// </summary>
    [HarmonyPatch(typeof(ReceptacleSideScreen), "GetAvailableAmount")]
    internal static class ReceptacleSideScreen_GetAvailableAmount_Patch
    {
        private static int _lastFrame = -1;
        private static SingleEntityReceptacle _lastTarget = null;
        private static readonly Dictionary<Tag, float> _frameCache = new Dictionary<Tag, float>();

        internal static void ClearCache()
        {
            _frameCache.Clear();
            _lastFrame = -1;
            _lastTarget = null;
        }

        private static bool Prefix(ReceptacleSideScreen __instance, Tag tag, SingleEntityReceptacle ___targetReceptacle, ref float __result)
        {
            if (__instance == null)
            {
                return true;
            }

            int currentFrame = Time.frameCount;
            SingleEntityReceptacle currentTarget = ___targetReceptacle;
            if (currentFrame != _lastFrame || currentTarget != _lastTarget)
            {
                _lastFrame = currentFrame;
                _lastTarget = currentTarget;
                _frameCache.Clear();
            }
            else if (_frameCache.TryGetValue(tag, out float cached))
            {
                __result = cached;
                return false;
            }

            return true;
        }

        private static void Postfix(ReceptacleSideScreen __instance, Tag tag, float __result)
        {
            if (__instance != null)
            {
                _frameCache[tag] = __result;
            }
        }
    }

    [HarmonyPatch(typeof(PlanterSideScreen), "ToggleClicked")]
    internal static class PlanterSideScreen_ToggleClicked_Optimization_Patch
    {
        private static bool _inToggleClicked = false;
        private static int _updateStateCount = 0;

        private static void Prefix()
        {
            _inToggleClicked = true;
            _updateStateCount = 0;
            // 每次用户点击切换种子时清空上一轮缓存，确保最新数据
            ReceptacleSideScreen_GetAvailableAmount_Patch.ClearCache();
        }

        private static void Postfix()
        {
            _inToggleClicked = false;
            _updateStateCount = 0;
        }

        [HarmonyPatch(typeof(PlanterSideScreen), "UpdateState")]
        private static class PlanterSideScreen_UpdateState_Patch
        {
            private static bool Prefix()
            {
                if (_inToggleClicked)
                {
                    _updateStateCount++;
                    // base.ToggleClicked 内部已经执行过一次完整的 UpdateState(null)，
                    // 原版 PlanterSideScreen.ToggleClicked 末尾额外又调了一次 UpdateState(null)，
                    // 导致同帧内对所有种子 UI 销毁重建两次。此处跳过第二次冗余调用。
                    if (_updateStateCount > 1)
                    {
                        return false;
                    }
                }
                return true;
            }
        }
    }
}
