using System;
using System.Reflection;
using HarmonyLib;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Farming
{
    /// <summary>
    /// 植物收获时自动视为具备复制人高级耕种技能（改良耕种 II / CanSalvagePlantFiber），
    /// 确保无论是小人收割、机器人收割还是成熟自然脱落，均必定产生植物外壳（Plant Husk / PlantFiber）。
    /// </summary>
    [HarmonyPatch(typeof(PlantFiberProducer), "OnHarvest")]
    public static class PlantFiberHarvestPatch
    {
        private static readonly MethodInfo SpawnMethod = AccessTools.Method(typeof(PlantFiberProducer), "SpawnPlantFiber");

        private static readonly Func<PlantFiberProducer, GameObject> SpawnFiberDelegate =
            SpawnMethod != null ? AccessTools.MethodDelegate<Func<PlantFiberProducer, GameObject>>(SpawnMethod) : null;

        private static bool Prefix(PlantFiberProducer __instance)
        {
            if (__instance == null) return true;
            try
            {
                if (SpawnFiberDelegate != null)
                {
                    SpawnFiberDelegate(__instance);
                }
                else if (SpawnMethod != null)
                {
                    SpawnMethod.Invoke(__instance, null);
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[PlantFiberHarvest] 生成植物外壳异常: {ex.Message}");
            }
            // 返回 false 跳过原版检查（原版要求必须由拥有 CanSalvagePlantFiber 技能的复制人手工收割）
            return false;
        }
    }
}
