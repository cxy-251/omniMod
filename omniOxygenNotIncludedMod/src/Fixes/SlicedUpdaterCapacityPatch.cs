using System;
using HarmonyLib;
using OmniMod.Core.Logging;

namespace OmniMod.Fixes
{
    /// <summary>
    /// 切片更新器容量扩容（安全版，绝不 Patch 泛型方法）：
    /// 原版 PressureVulnerable、DrowningMonitor、TemperatureVulnerable 的切片上限固定为 200~300。
    /// 当玩家种植了数千株作物、蓄养上千只动物时，单片排队实例超过上限，
    /// 导致游戏以每秒 10 次的极高频率向 Player.log 疯狂写入警告（每周期产生上万条报错与字符串垃圾），
    /// 且溢出的作物/动物在当前 tick 被直接截断跳过更新。
    ///
    /// 必须注意：Mono 运行时对引用类型的泛型类（SlicedUpdaterSim1000ms<T>）采用共享代码（Generic Sharing）。
    /// 若用 Harmony Patch 其内部泛型方法（如 InitializeSlices），会导致 Mono 的 RGCTX（运行时泛型上下文）错乱，
    /// 进而引发 List<T> 底层数组类型冲突（ArrayTypeMismatchException）。
    ///
    /// 因此本补丁绝不挂载任何泛型方法，而是在游戏初始化阶段直接修改单例公开属性 maxUpdatesPer200ms。
    /// </summary>
    [HarmonyPatch(typeof(Game), "OnPrefabInit")]
    internal static class Game_OnPrefabInit_SlicedUpdater_Patch
    {
        private static void Postfix()
        {
            SlicedUpdaterCapacityOptimizer.Apply();
        }
    }

    [HarmonyPatch(typeof(Game), "OnSpawn")]
    internal static class Game_OnSpawn_SlicedUpdater_Patch
    {
        private static void Postfix()
        {
            SlicedUpdaterCapacityOptimizer.Apply();
        }
    }

    public static class SlicedUpdaterCapacityOptimizer
    {
        public static void Apply()
        {
            try
            {
                if (SlicedUpdaterSim1000ms<PressureVulnerable>.instance != null)
                {
                    SlicedUpdaterSim1000ms<PressureVulnerable>.instance.maxUpdatesPer200ms = 10000;
                }
                if (SlicedUpdaterSim1000ms<DrowningMonitor>.instance != null)
                {
                    SlicedUpdaterSim1000ms<DrowningMonitor>.instance.maxUpdatesPer200ms = 10000;
                }
                if (SlicedUpdaterSim1000ms<TemperatureVulnerable>.instance != null)
                {
                    SlicedUpdaterSim1000ms<TemperatureVulnerable>.instance.maxUpdatesPer200ms = 10000;
                }
                ModLog.Info("[切片更新器] 植物气压/淹没/温度监测器切片容量已安全扩容至 10000，日志洪峰警报已平息。");
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[切片更新器] 扩容失败: {ex.Message}");
            }
        }
    }
}
