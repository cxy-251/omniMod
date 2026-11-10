using System;
using System.Reflection;
using HarmonyLib;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Fixes
{
    /// <summary>
    /// 防御性修复 GameScheduler 与 GameSchedulerClock 在初始化初期的空指针崩溃：
    /// 1. 原版 Game.OnSpawn 较早调用 RefreshRadiationLoop()，其中调用 GameScheduler.Schedule()；
    /// 2. GameSchedulerClock.GetTime() 直接解引用 GameClock.Instance.GetTime()，在生命周期交错时抛出 NRE；
    /// 3. 本补丁在 Game.OnSpawn 前期自动校准 GameClock.Instance，并在 GameSchedulerClock.GetTime() 做全局空值保底。
    /// </summary>
    public static class GameSchedulerCrashFixPatch
    {
        [HarmonyPatch(typeof(Game), "OnSpawn")]
        public static class Game_OnSpawn_Safety_Patch
        {
            public static void Prefix(Game __instance)
            {
                try
                {
                    // 1. 保障 GameClock.Instance
                    if (GameClock.Instance == null && __instance != null)
                    {
                        GameClock clock = __instance.GetComponent<GameClock>() ??
                                          __instance.GetComponentInChildren<GameClock>(true);
                        if (clock != null)
                        {
                            GameClock.Instance = clock;
                        }
                    }

                    // 2. 保障 CameraController.Instance
                    if (CameraController.Instance == null)
                    {
#pragma warning disable CS0618
                        CameraController cam = UnityEngine.Object.FindObjectOfType<CameraController>();
#pragma warning restore CS0618
                        if (cam != null)
                        {
                            Traverse.Create(typeof(CameraController)).Property("Instance").SetValue(cam);
                        }
                    }

                    // 3. 保障 WaterCubes.Instance
                    if (WaterCubes.Instance == null)
                    {
#pragma warning disable CS0618
                        WaterCubes wc = UnityEngine.Object.FindObjectOfType<WaterCubes>();
#pragma warning restore CS0618
                        if (wc != null)
                        {
                            Traverse.Create(typeof(WaterCubes)).Property("Instance").SetValue(wc);
                        }
                    }

                    // 4. 保障 SaveGame.Instance 存在安全引用
                    if (SaveGame.Instance == null)
                    {
#pragma warning disable CS0618
                        SaveGame sg = UnityEngine.Object.FindObjectOfType<SaveGame>();
#pragma warning restore CS0618
                        if (sg != null)
                        {
                            SaveGame.Instance = sg;
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModLog.Warn($"[Game.OnSpawn] Safety Prefix guard: {ex.Message}");
                }
            }

            public static Exception Finalizer(Exception __exception)
            {
                if (__exception != null)
                {
                    ModLog.Warn($"[Game.OnSpawn] 捕获并成功拦截异常，阻止启动崩溃: {__exception}");
                    return null; // 吞掉异常，确保 KMonoBehaviour.Spawn 成功完成，杜绝弹窗崩溃
                }
                return null;
            }
        }

        [HarmonyPatch]
        public static class GameSchedulerClock_GetTime_Patch
        {
            public static MethodBase TargetMethod()
            {
                Type clockType = typeof(GameScheduler).GetNestedType("GameSchedulerClock", BindingFlags.Public | BindingFlags.NonPublic);
                return clockType?.GetMethod("GetTime", BindingFlags.Public | BindingFlags.Instance);
            }

            public static bool Prefix(ref float __result)
            {
                if (GameClock.Instance != null)
                {
                    return true; // 正常运行原版逻辑
                }

                try
                {
                    // 兜底一：尝试从当前 Game 实例恢复
                    if (Game.Instance != null)
                    {
                        GameClock clock = Game.Instance.GetComponent<GameClock>() ??
                                          Game.Instance.GetComponentInChildren<GameClock>(true);
                        if (clock != null)
                        {
                            GameClock.Instance = clock;
                            __result = clock.GetTime();
                            return false;
                        }
                    }

                    // 兜底二：场景全局查找
#pragma warning disable CS0618
                    GameClock globalClock = UnityEngine.Object.FindObjectOfType<GameClock>();
#pragma warning restore CS0618
                    if (globalClock != null)
                    {
                        GameClock.Instance = globalClock;
                        __result = globalClock.GetTime();
                        return false;
                    }
                }
                catch
                {
                    // 忽略查找异常
                }

                // 兜底三：极早期返回 0f，确保调度器可正常压栈而不崩溃
                __result = 0f;
                return false;
            }
        }

        [HarmonyPatch(typeof(Game), "RefreshRadiationLoop")]
        public static class Game_RefreshRadiationLoop_Patch
        {
            public static bool Prefix()
            {
                if (GameScheduler.Instance == null)
                {
                    return false;
                }
                return true;
            }
        }
    }
}
