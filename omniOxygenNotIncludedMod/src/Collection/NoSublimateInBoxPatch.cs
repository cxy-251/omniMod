using HarmonyLib;

namespace OmniMod.Collection
{
    /// <summary>
    /// 放在我们聚合箱（杂物箱/食物箱）里的物品不挥发气体（污染土、漂白石、氧石、黏液等）。
    ///
    /// 原版做法是给存储加 Seal 修饰（物品打 Sealed 标签，Sublimates 看到就不挥发），但 Sealed
    /// 标签会让状态栏里便携电池的电量、食物卡路里显示成 0（见 JunkBoxConfig 的注释），所以不用。
    /// 这里直接在挥发组件每 200ms 的更新里判断：物品当前所在的 Storage 属于我们的聚合箱，就跳过。
    /// 杂物池里的散装材料本来就是纯数据，不存在挥发。
    /// </summary>
    [HarmonyPatch(typeof(Sublimates), nameof(Sublimates.Sim200ms))]
    internal static class Sublimates_NoSublimateInBox_Patch
    {
        private static bool Prefix(Sublimates __instance)
        {
            Pickupable p = __instance.GetComponent<Pickupable>();
            Storage storage = p != null ? p.storage : null;
            if (storage != null && storage.GetComponent<GlobalItemCollector>() != null)
            {
                return false;   // 在聚合箱里：不挥发
            }
            return true;
        }
    }
}
