using System;
using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Fixes
{
    /// <summary>
    /// 修复在种植箱/土培砖/水培砖选种界面（PlanterSideScreen / ReceptacleSideScreen）点击种子时的 NullReferenceException 闪退：
    /// 
    /// 触发链路：
    /// MultiToggle.OnPointerClick -> ReceptacleSideScreen.ToggleClicked -> UpdateState 
    /// -> CanDepositEntity -> PlanterSideScreen.AdditionalCanDepositTest 
    /// -> WorldInventory.GetCountWithAdditionalTag -> KPrefabIDExtensions.HasTag
    /// 
    /// 崩溃机理：
    /// 当种子被消耗、拾取或销毁后，WorldInventory 的库存集合可能尚未完全同步，
    /// 遍历 collection 时包含已失效或已在 Unity 底层销毁的 Pickupable 实体。
    /// 原版 KPrefabIDExtensions.HasTag(Component cmp, Tag tag) 未做非空校验，
    /// 直接调用 cmp.GetComponent&lt;KPrefabID&gt;().HasTag(tag)，触发底层 TryGetComponentFastPath 抛出 NRE 导致游戏闪退。
    /// 
    /// 解决方案（四重彻底加固）：
    /// 1. KPrefabIDExtensions.HasTag (Component &amp; GameObject)：拦截空引用，对象无效或无 KPrefabID 时安全返回 false。
    /// 2. WorldInventory.GetCountWithAdditionalTag：遍历前置非空筛选，并加 try-catch 兜底。
    /// 3. PlanterSideScreen.AdditionalCanDepositTest：挂载 Finalizer 拦截任何未预期的异常。
    /// 4. ReceptacleSideScreen.CanDepositEntity &amp; UpdateState：挂载 Finalizer 阻断 UI 更新过程中的崩溃。
    /// </summary>
    public static class PlanterSideScreenCrashFixPatch
    {
        private static int _loggedInventory;

        [HarmonyPatch(typeof(KPrefabIDExtensions), nameof(KPrefabIDExtensions.HasTag), new Type[] { typeof(Component), typeof(Tag) })]
        public static class KPrefabIDExtensions_HasTag_Component_Patch
        {
            public static bool Prefix(Component cmp, Tag tag, ref bool __result)
            {
                if (cmp == null)
                {
                    __result = false;
                    return false;
                }
                KPrefabID kpid = cmp.GetComponent<KPrefabID>();
                if (kpid == null)
                {
                    __result = false;
                    return false;
                }
                __result = kpid.HasTag(tag);
                return false;
            }
        }

        [HarmonyPatch(typeof(KPrefabIDExtensions), nameof(KPrefabIDExtensions.HasTag), new Type[] { typeof(GameObject), typeof(Tag) })]
        public static class KPrefabIDExtensions_HasTag_GameObject_Patch
        {
            public static bool Prefix(GameObject go, Tag tag, ref bool __result)
            {
                if (go == null)
                {
                    __result = false;
                    return false;
                }
                KPrefabID kpid = go.GetComponent<KPrefabID>();
                if (kpid == null)
                {
                    __result = false;
                    return false;
                }
                __result = kpid.HasTag(tag);
                return false;
            }
        }

        [HarmonyPatch(typeof(WorldInventory), nameof(WorldInventory.GetCountWithAdditionalTag))]
        public static class WorldInventory_GetCountWithAdditionalTag_Patch
        {
            public static bool Prefix(WorldInventory __instance, Tag tag, Tag additionalTag, bool includeRelatedWorlds, ref int __result)
            {
                if (__instance == null || !tag.IsValid)
                {
                    __result = 0;
                    return false;
                }

                try
                {
                    ICollection<Pickupable> collection = includeRelatedWorlds
                        ? ClusterUtil.GetPickupablesFromRelatedWorlds(__instance, tag)
                        : __instance.GetPickupables(tag);

                    if (collection == null)
                    {
                        __result = 0;
                        return false;
                    }

                    int count = 0;
                    if (additionalTag.IsValid)
                    {
                        foreach (Pickupable p in collection)
                        {
                            if (p != null && p.gameObject != null && p.HasTag(additionalTag))
                            {
                                count++;
                            }
                        }
                    }
                    else
                    {
                        foreach (Pickupable p in collection)
                        {
                            if (p != null && p.gameObject != null)
                            {
                                count++;
                            }
                        }
                    }

                    __result = count;
                    return false;
                }
                catch (Exception ex)
                {
                    if (_loggedInventory < 10)
                    {
                        _loggedInventory++;
                        ModLog.Warn($"[CrashFix] WorldInventory.GetCountWithAdditionalTag 异常已安全抑制: {ex.Message}");
                    }
                    __result = 0;
                    return false;
                }
            }
        }

        [HarmonyPatch(typeof(PlanterSideScreen), "AdditionalCanDepositTest")]
        public static class PlanterSideScreen_AdditionalCanDepositTest_Patch
        {
            public static Exception Finalizer(Exception __exception, ref bool __result)
            {
                if (__exception != null)
                {
                    ModLog.Warn($"[CrashFix] PlanterSideScreen.AdditionalCanDepositTest 异常已安全抑制: {__exception.Message}");
                    __result = false;
                    return null;
                }
                return null;
            }
        }

        [HarmonyPatch(typeof(ReceptacleSideScreen), "CanDepositEntity")]
        public static class ReceptacleSideScreen_CanDepositEntity_Patch
        {
            public static Exception Finalizer(Exception __exception, ref bool __result)
            {
                if (__exception != null)
                {
                    ModLog.Warn($"[CrashFix] ReceptacleSideScreen.CanDepositEntity 异常已安全抑制: {__exception.Message}");
                    __result = false;
                    return null;
                }
                return null;
            }
        }

        [HarmonyPatch(typeof(ReceptacleSideScreen), "UpdateState")]
        public static class ReceptacleSideScreen_UpdateState_Patch
        {
            public static Exception Finalizer(Exception __exception)
            {
                if (__exception != null)
                {
                    ModLog.Warn($"[CrashFix] ReceptacleSideScreen.UpdateState 异常已安全抑制: {__exception.Message}");
                    return null;
                }
                return null;
            }
        }
    }
}
