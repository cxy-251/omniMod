using HarmonyLib;

namespace OmniMod.Rooms
{
    /// <summary>
    /// 宴会厅（Banquet Hall）藏品要求放宽补丁：
    /// 原版宴会厅强制要求底座/展架上陈列具有 GameTags.Ornament 的“官方真藏品”（如太空文物）。
    /// 此补丁将宴会厅判定放宽至与自然馆/展览馆一致：
    /// 在展台、展架上放置任何物品（种子、矿石、食物、文物等），或摆放花盆/种植箱，均视为合格饰品生效。
    /// </summary>
    public static class BanquetHallPatch
    {
        [HarmonyPatch(typeof(RoomConstraints), "CheckOrnament")]
        public static class RoomConstraints_CheckOrnament_Patch
        {
            public static bool Prefix(KPrefabID entityOrBuilding, ref bool __result)
            {
                if (entityOrBuilding == null) return true;

                // 展台、展架陈列任意物品（种子、矿石、食物、珍藏等）
                if (entityOrBuilding.HasTag(GameTags.OrnamentDisplayer) || entityOrBuilding.HasTag(GameTags.PedestalDisplayable))
                {
                    var receptacle = entityOrBuilding.GetComponent<SingleEntityReceptacle>();
                    if (receptacle != null && receptacle.Occupant != null)
                    {
                        if (receptacle.operational == null || receptacle.operational.IsOperational)
                        {
                            __result = true;
                            return false;
                        }
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(OrnamentReceptacle), "get_IsHoldingOrnament")]
        public static class OrnamentReceptacle_IsHoldingOrnament_Patch
        {
            public static bool Prefix(OrnamentReceptacle __instance, ref bool __result)
            {
                if (__instance != null && __instance.Occupant != null)
                {
                    __result = true;
                    return false;
                }
                return true;
            }
        }
    }
}
