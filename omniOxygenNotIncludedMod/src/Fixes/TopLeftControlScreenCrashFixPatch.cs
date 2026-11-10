using HarmonyLib;
using OmniMod.Core.Logging;
using STRINGS;
using UnityEngine;

namespace OmniMod.Fixes
{
    /// <summary>
    /// 防御性修复左上角控制面板（TopLeftControlScreen）在场景加载初期的空指针崩溃：
    /// 原版 TopLeftControlScreen 在 OnActivate 阶段较早调用 UpdateSandboxToggleState 与 CheckSandboxModeLocked，
    /// 此时 SaveGame.Instance 尚未生成，直接解引用 SaveGame.Instance.sandboxEnabled 引发 NullReferenceException。
    /// </summary>
    public static class TopLeftControlScreenCrashFixPatch
    {
        [HarmonyPatch(typeof(TopLeftControlScreen), "CheckSandboxModeLocked")]
        public static class TopLeftControlScreen_CheckSandboxModeLocked_Patch
        {
            public static bool Prefix(ref bool __result)
            {
                if (SaveGame.Instance == null)
                {
                    __result = true; // 存档未就绪时默认锁定，防止解引用崩溃
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(TopLeftControlScreen), nameof(TopLeftControlScreen.UpdateSandboxToggleState))]
        public static class TopLeftControlScreen_UpdateSandboxToggleState_Patch
        {
            public static bool Prefix(TopLeftControlScreen __instance)
            {
                if (__instance == null) return false;

                if (SaveGame.Instance == null)
                {
                    try
                    {
                        MultiToggle toggle = Traverse.Create(__instance).Field<MultiToggle>("sandboxToggle").Value;
                        if (toggle != null)
                        {
                            toggle.ChangeState(0);
                            toggle.gameObject.SetActive(false);
                        }
                    }
                    catch { }
                    return false; // 在 SaveGame 就绪前安全退出
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(SaveGame), "OnSpawn")]
        public static class SaveGame_OnSpawn_TopLeftControl_Patch
        {
            public static void Postfix()
            {
                try
                {
                    if (TopLeftControlScreen.Instance != null)
                    {
                        TopLeftControlScreen.Instance.RefreshName();
                        TopLeftControlScreen.Instance.UpdateSandboxToggleState();
                    }
                }
                catch (System.Exception ex)
                {
                    ModLog.Warn($"[TopLeftControlScreen] SaveGame.OnSpawn 后置刷新异常: {ex.Message}");
                }
            }
        }
    }
}
