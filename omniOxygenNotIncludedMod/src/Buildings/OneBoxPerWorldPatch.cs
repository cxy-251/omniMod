using System.Linq;
using System.Reflection;
using HarmonyLib;
using OmniMod.Collection;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// "每颗星球每种聚合箱只能有一个"的建造期校验。
    ///
    /// BuildingDef.IsValidPlaceLocation(...) 是所有"这里能不能放"判断的最底层入口
    /// （放置预览每帧都会调它）。我们在它返回 true 之后追加一条判断：
    /// 如果正在放的是杂物箱/食物箱，且目标格所在星球已经有一个同类箱子，就把结果改成
    /// false 并给出提示文字——放置预览会变红、点不下去。
    /// </summary>
    [HarmonyPatch]
    internal static class BuildingDef_IsValidPlaceLocation_Patch
    {
        // IsValidPlaceLocation 有多个重载，取参数最多（6 个）的那个最底层实现
        private static MethodBase TargetMethod()
        {
            return typeof(BuildingDef)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .First(m => m.Name == nameof(BuildingDef.IsValidPlaceLocation)
                            && m.GetParameters().Length == 6);
        }

        private static void Postfix(BuildingDef __instance, int cell, ref bool __result, ref string fail_reason)
        {
            if (!__result)
            {
                return;   // 本来就不能放，不用管
            }

            string id = __instance.PrefabID;
            if (id != JunkBoxConfig.Id && id != FoodBoxConfig.Id)
            {
                return;
            }

            if (!Grid.IsValidCell(cell))
            {
                return;
            }

            int worldId = Grid.WorldIdx[cell];
            if (AggregateBoxRegistry.ExistsOnWorld(id, worldId))
            {
                __result = false;
                fail_reason = ModStrings.T(
                    "本星球已经有一个" + (id == JunkBoxConfig.Id ? "杂物箱" : "食物箱") + "了（每星球限一个）。",
                    "This planet already has a " + (id == JunkBoxConfig.Id ? "Junk Box" : "Food Box") + " (one per planet).")
                    .Resolve();
            }
        }
    }
}
