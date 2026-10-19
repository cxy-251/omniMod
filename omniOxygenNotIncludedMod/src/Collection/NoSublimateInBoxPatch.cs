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
            if (p != null && AggregateBoxRegistry.IsBoxStorage(p.storage))
            {
                return false;   // 在聚合箱里：不挥发
            }
            return true;
        }
    }

    /// <summary>
    /// 阻止聚合箱（杂物箱 / 食物箱）上的 StorageLocker / RationBox 通过 FilteredStorage
    /// 生成 10 亿 kg 的手动搬运任务（FetchList2），以及阻止每次存取物品时遍历箱内所有物品重算容量仪表盘。
    /// 聚合箱完全靠 GlobalItemCollector 自动吸取与物化，绝不需要小人往箱子里搬东西。
    /// </summary>
    [HarmonyPatch(typeof(FilteredStorage), "OnFilterChanged")]
    internal static class FilteredStorage_OnFilterChanged_NoFetchForOmniBox_Patch
    {
        private static readonly AccessTools.FieldRef<FilteredStorage, KMonoBehaviour> RootField =
            AccessTools.FieldRefAccess<FilteredStorage, KMonoBehaviour>("root");

        private static bool Prefix(FilteredStorage __instance)
        {
            KMonoBehaviour root = __instance != null ? RootField(__instance) : null;
            if (root != null && root.GetComponent<GlobalItemCollector>() != null)
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(FilteredStorage), "OnStorageChanged")]
    internal static class FilteredStorage_OnStorageChanged_SkipForOmniBox_Patch
    {
        private static readonly AccessTools.FieldRef<FilteredStorage, Storage> StorageField =
            AccessTools.FieldRefAccess<FilteredStorage, Storage>("storage");

        private static bool Prefix(FilteredStorage __instance)
        {
            Storage storage = __instance != null ? StorageField(__instance) : null;
            if (AggregateBoxRegistry.IsBoxStorage(storage))
            {
                return false;
            }
            return true;
        }
    }
}
