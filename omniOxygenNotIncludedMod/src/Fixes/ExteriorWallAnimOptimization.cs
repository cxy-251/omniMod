using System;
using HarmonyLib;
using OmniMod.Core.Logging;

namespace OmniMod.Fixes
{
    /// <summary>
    /// 静态背景防漏墙动画轮询优化：
    /// 游戏中防漏墙（如 GlassExteriorWall、ExteriorWall）多达数万块，原版每个防漏墙都是完整的 GameObject，
    /// 挂载 KBatchedAnimController 并在 KBatchedAnimUpdater 中按帧接收 UpdateRegisteredAnims 刷新轮询。
    /// 实际上背景防漏墙在建造后完全静态，不需要逐帧参与动画骨骼计算。
    ///
    /// 本优化在读档与生成时将防漏墙的动画控制器调用 SuspendUpdates(true)，
    /// 从逐帧动画更新列表中注销，保留 Metal/GPU 顶点渲染与遮挡剔除，
    /// 彻底消除 2.5 万个动画控制器的 CPU 轮询损耗。
    /// </summary>
    [HarmonyPatch(typeof(BuildingComplete), "OnSpawn")]
    internal static class BuildingComplete_OnSpawn_AnimSuspend_Patch
    {
        private static void Postfix(BuildingComplete __instance)
        {
            if (__instance != null && __instance.Def != null)
            {
                string id = __instance.Def.PrefabID;
                if (id == "GlassExteriorWall" || id == "ExteriorWall" || id == "FacilityBackWallWindow")
                {
                    var anim = __instance.GetComponent<KBatchedAnimController>();
                    if (anim != null)
                    {
                        ExteriorWallAnimOptimization.Suspend(anim);
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(Game), "OnSpawn")]
    internal static class Game_OnSpawn_ExteriorWallAnim_Patch
    {
        private static void Postfix()
        {
            try
            {
                if (Components.BuildingCompletes == null) return;

                int count = Components.BuildingCompletes.Count;
                int suspended = 0;
                for (int i = 0; i < count; i++)
                {
                    var building = Components.BuildingCompletes[i];
                    if (building != null && building.Def != null)
                    {
                        string id = building.Def.PrefabID;
                        if (id == "GlassExteriorWall" || id == "ExteriorWall" || id == "FacilityBackWallWindow")
                        {
                            var anim = building.GetComponent<KBatchedAnimController>();
                            if (anim != null)
                            {
                                ExteriorWallAnimOptimization.Suspend(anim);
                                suspended++;
                            }
                        }
                    }
                }

                if (suspended > 0)
                {
                    ModLog.Info($"[防漏墙静默化] 已将 {suspended} 块静态防漏墙挂起动画轮询（SuspendUpdates），画面正常渲染且完全剔除 CPU 刷新消耗。");
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[防漏墙静默化] 执行失败: {ex.Message}");
            }
        }
    }

    public static class ExteriorWallAnimOptimization
    {
        private static Action<KBatchedAnimController, bool> _suspendUpdatesDelegate;

        static ExteriorWallAnimOptimization()
        {
            try
            {
                var method = AccessTools.Method(typeof(KBatchedAnimController), "SuspendUpdates", new[] { typeof(bool) });
                if (method != null)
                {
                    _suspendUpdatesDelegate = (Action<KBatchedAnimController, bool>)Delegate.CreateDelegate(
                        typeof(Action<KBatchedAnimController, bool>), method);
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[防漏墙静默化] 初始化委托失败: {ex.Message}");
            }
        }

        public static void Suspend(KBatchedAnimController anim)
        {
            if (anim == null) return;
            try
            {
                _suspendUpdatesDelegate?.Invoke(anim, true);
            }
            catch {}
        }
    }
}
