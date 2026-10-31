using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Collection;
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

        private static void Finalizer(System.Exception __exception)
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

    /// <summary>
    /// 针对 PlanterSideScreen.RefreshSubspeciesToggles 的安全容灾补丁：
    /// 原版游戏在遍历 worldInventory.GetPickupables 时若遇到已被消耗或销毁但残留的种子引用，
    /// 其 KPrefabIDExtensions.HasTag 会因底层 null 抛出 NullReferenceException 导致游戏崩溃闪退。
    /// 此处添加 Finalizer 彻底拦截并抑制潜在空指针异常，绝不弹窗闪退。
    /// </summary>
    [HarmonyPatch(typeof(PlanterSideScreen), "RefreshSubspeciesToggles")]
    internal static class PlanterSideScreen_RefreshSubspeciesToggles_Safe_Patch
    {
        private static System.Exception Finalizer(System.Exception __exception)
        {
            if (__exception != null)
            {
                Debug.LogWarning("[OmniMod] 成功拦截 PlanterSideScreen.RefreshSubspeciesToggles 潜在空指针异常，已阻断闪退: " + __exception.Message);
                return null;
            }
            return null;
        }
    }

    /// <summary>
    /// 确保种植箱、种植砖、水培砖在请求种子时，如果全星系（离散物品之家 DiscreteHome、OmniPool 或任意星球聚合箱）
    /// 存有该种子，状态项绝对不会被误标为【没有可用种子】（NoAvailableSeed），而是正确显示【等待种子运送】（AwaitingSeedDelivery）。
    /// </summary>
    [HarmonyPatch(typeof(SingleEntityReceptacle), "UpdateStatusItem", typeof(KSelectable))]
    internal static class SingleEntityReceptacle_UpdateStatusItem_Patch
    {
        private static void Postfix(SingleEntityReceptacle __instance, KSelectable selectable)
        {
            if (__instance == null || selectable == null || __instance.Occupant != null)
            {
                return;
            }

            FetchChore fetchChore = __instance.GetActiveRequest;
            if (fetchChore == null || fetchChore.tags == null)
            {
                return;
            }

            StatusItem noneAvailable = Db.Get()?.BuildingStatusItems?.NoAvailableSeed;
            StatusItem awaiting = Db.Get()?.BuildingStatusItems?.AwaitingSeedDelivery;

            if (noneAvailable != null && awaiting != null && selectable.HasStatusItem(noneAvailable))
            {
                int worldId = __instance.GetMyWorldId();
                bool isPocket = OmniMod.Buildings.OmniPocketHabitat.IsPocketHabitatWorld(worldId);
                int parentWorldId = isPocket ? OmniMod.Buildings.OmniPocketHabitat.GetExteriorWorldId(worldId) : -1;

                bool hasSeed = false;
                foreach (Tag tag in fetchChore.tags)
                {
                    if (AggregateBoxRegistry.HasPlanetItem(worldId, parentWorldId, tag, isPocket))
                    {
                        hasSeed = true;
                        break;
                    }
                }

                if (hasSeed)
                {
                    selectable.SetStatusItem(Db.Get().StatusItemCategories.EntityReceptacle, awaiting);
                }
            }
        }
    }
}
