using System;
using System.Collections;
using HarmonyLib;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Fixes
{
    /// <summary>
    /// 全面防御修复原版缩略图相机（Timelapser）在初始化和换日时的空指针异常：
    /// 1. Timelapser.OnPrefabInit 触发极早，此时 GameClock.Instance、SaveGame.Instance、CameraController.Instance、Camera.main 可能尚未初始化；
    /// 2. 原版无判空调用 CycleTimeToScreenshot、RefreshRenderTextureSize、GameClock.Subscribe 等必然引发连环 NullReferenceException；
    /// 3. 本补丁完全安全接管 OnPrefabInit，在组件未就绪时优雅跳过，并在 Game.OnSpawn 和 SaveGame.OnSpawn 时自动完成延迟初始化与事件订阅。
    /// </summary>
    public static class TimelapserCrashFixPatch
    {
        [HarmonyPatch(typeof(Timelapser), "OnPrefabInit")]
        public static class Timelapser_OnPrefabInit_Patch
        {
            public static bool Prefix(Timelapser __instance)
            {
                if (__instance == null) return false;

                try
                {
                    // 1. 订阅游戏设置变更 (GameOptionsUpdated = 75424175)
                    if (Game.Instance != null)
                    {
                        Game.Instance.Subscribe((int)GameHashes.GameOptionsUpdated, (data) =>
                        {
                            try { Traverse.Create(__instance).Method("RefreshRenderTextureSize", data).GetValue(); } catch { }
                        });
                    }

                    // 2. 若 SaveGame 已经存在则尝试刷新尺寸，否则跳过
                    if (SaveGame.Instance != null)
                    {
                        try { Traverse.Create(__instance).Method("RefreshRenderTextureSize", (object)null).GetValue(); } catch { }
                    }

                    // 3. 关联冻结相机
                    if (CameraController.Instance != null)
                    {
                        Traverse.Create(__instance).Field<Camera>("freezeCamera").Value = CameraController.Instance.timelapseFreezeCamera;
                    }

                    // 4. 仅在 GameClock 存在时安全检查与订阅换日
                    if (GameClock.Instance != null)
                    {
                        float timeToScreenshot = -1f;
                        try { timeToScreenshot = Traverse.Create(__instance).Method("CycleTimeToScreenshot").GetValue<float>(); } catch { }
                        if (timeToScreenshot > 0f)
                        {
                            try { Traverse.Create(__instance).Method("OnNewDay", (object)null).GetValue(); } catch { }
                        }
                        GameClock.Instance.Subscribe((int)GameHashes.NewDay, (data) =>
                        {
                            try { Traverse.Create(__instance).Method("OnNewDay", data).GetValue(); } catch { }
                        });
                    }

                    // 5. 屏幕大小变动安全处理
                    if (Camera.main != null)
                    {
                        try { Traverse.Create(__instance).Method("OnResize").GetValue(); } catch { }
                    }
                    if (ScreenResize.Instance != null)
                    {
                        System.Action onResizeAction = () =>
                        {
                            try
                            {
                                if (Camera.main != null && __instance != null)
                                {
                                    Traverse.Create(__instance).Method("OnResize").GetValue();
                                }
                            }
                            catch { }
                        };
                        ScreenResize.Instance.OnResize = (System.Action)Delegate.Combine(ScreenResize.Instance.OnResize, onResizeAction);
                    }

                    // 6. 启动截图渲染协程
                    IEnumerator renderCoro = Traverse.Create(__instance).Method("Render").GetValue<IEnumerator>();
                    if (renderCoro != null)
                    {
                        __instance.StartCoroutine(renderCoro);
                    }
                }
                catch (Exception ex)
                {
                    ModLog.Warn($"[Timelapser.OnPrefabInit] 安全初始化保底拦截: {ex.Message}");
                }

                return false; // 完全跳过原版脆弱的 OnPrefabInit
            }
        }

        [HarmonyPatch(typeof(Timelapser), "CycleTimeToScreenshot")]
        public static class Timelapser_CycleTimeToScreenshot_Patch
        {
            public static bool Prefix(ref float __result)
            {
                if (GameClock.Instance == null)
                {
                    __result = -1f;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Timelapser), "timelapseUserEnabled", MethodType.Getter)]
        public static class Timelapser_TimelapseUserEnabled_Patch
        {
            public static bool Prefix(ref bool __result)
            {
                if (SaveGame.Instance == null)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Timelapser), "RefreshRenderTextureSize")]
        public static class Timelapser_RefreshRenderTextureSize_Patch
        {
            public static bool Prefix(Timelapser __instance)
            {
                if (__instance == null || SaveGame.Instance == null)
                {
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Timelapser), "OnNewDay")]
        public static class Timelapser_OnNewDay_Patch
        {
            public static bool Prefix(Timelapser __instance)
            {
                if (__instance == null || GameClock.Instance == null || ClusterManager.Instance == null || ClusterManager.Instance.WorldContainers == null)
                {
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Timelapser), "OnResize")]
        public static class Timelapser_OnResize_Patch
        {
            public static bool Prefix(Timelapser __instance)
            {
                if (__instance == null || Camera.main == null)
                {
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// 当游戏或存档生命周期就绪时，安全补齐延迟初始化的引用与订阅
        /// </summary>
        public static void EnsureTimelapserInitialized(Timelapser timelapser)
        {
            if (timelapser == null) return;
            try
            {
                var trav = Traverse.Create(timelapser);

                // 补齐 freezeCamera
                if (trav.Field<Camera>("freezeCamera").Value == null && CameraController.Instance != null)
                {
                    trav.Field<Camera>("freezeCamera").Value = CameraController.Instance.timelapseFreezeCamera;
                }

                // 补齐 GameClock 换日订阅
                if (GameClock.Instance != null)
                {
                    Action<object> onNewDay = (data) =>
                    {
                        try { trav.Method("OnNewDay", data).GetValue(); } catch { }
                    };
                    GameClock.Instance.Unsubscribe((int)GameHashes.NewDay, onNewDay);
                    GameClock.Instance.Subscribe((int)GameHashes.NewDay, onNewDay);
                }

                // 补齐 freezeTexture
                if (Camera.main != null && timelapser.freezeTexture == null)
                {
                    try { trav.Method("OnResize").GetValue(); } catch { }
                }

                // 补齐 bufferRenderTexture
                if (SaveGame.Instance != null)
                {
                    try { trav.Method("RefreshRenderTextureSize", (object)null).GetValue(); } catch { }
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[Timelapser] EnsureTimelapserInitialized exception: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(Game), "OnSpawn")]
        public static class Game_OnSpawn_Timelapse_Patch
        {
            public static void Postfix()
            {
                if (Game.Instance != null && Game.Instance.timelapser != null)
                {
                    EnsureTimelapserInitialized(Game.Instance.timelapser);
                }
            }
        }

        [HarmonyPatch(typeof(SaveGame), "OnSpawn")]
        public static class SaveGame_OnSpawn_Timelapse_Patch
        {
            public static void Postfix()
            {
                if (Game.Instance != null && Game.Instance.timelapser != null)
                {
                    EnsureTimelapserInitialized(Game.Instance.timelapser);
                }
            }
        }
    }
}
