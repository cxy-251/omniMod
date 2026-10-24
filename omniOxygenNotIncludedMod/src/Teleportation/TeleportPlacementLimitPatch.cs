using System;
using HarmonyLib;
using UnityEngine;

namespace OmniMod.Teleportation
{
    /// <summary>
    /// 限制时空传送发射器与接收器在每个星球或火箭内部最多只能建造一台（1 per world/rocket）。
    /// 当玩家尝试在已存在该建筑的星球或火箭舱内建造第二台时，光标变红并提示限建说明。
    /// </summary>
    [HarmonyPatch(typeof(BuildingDef), nameof(BuildingDef.IsValidPlaceLocation),
        new Type[] { typeof(GameObject), typeof(int), typeof(Orientation), typeof(bool), typeof(string), typeof(bool) },
        new ArgumentType[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal })]
    internal static class BuildingDef_IsValidPlaceLocation_TeleportLimit_Patch
    {
        private static bool Prefix(BuildingDef __instance, GameObject source_go, int cell, Orientation orientation, bool replace_tile, ref string fail_reason, bool restrictToActiveWorld, ref bool __result)
        {
            if (__instance == null || !Grid.IsValidBuildingCell(cell)) return true;

            string prefabId = __instance.PrefabID;
            if (prefabId == OmniTeleportTransmitterConfig.ID || prefabId == OmniTeleportReceiverConfig.ID)
            {
                int worldId = Grid.WorldIdx[cell];
                if (OmniTeleportRegistry.WorldHasBuilding(worldId, prefabId, source_go))
                {
                    fail_reason = (prefabId == OmniTeleportTransmitterConfig.ID)
                        ? "当前星球/火箭内已建有【时空传送发射器】（每星球/火箭限建一台）"
                        : "当前星球/火箭内已建有【时空传送接收器】（每星球/火箭限建一台）";
                    __result = false;
                    return false;
                }
            }
            return true;
        }
    }
}
