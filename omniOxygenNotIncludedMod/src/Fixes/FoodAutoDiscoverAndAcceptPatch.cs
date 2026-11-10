using HarmonyLib;
using OmniMod.Collection;
using UnityEngine;

namespace OmniMod.Fixes
{
    /// <summary>
    /// 食物与烹饪材料自动发现与勾选补丁：
    /// 解决新存档或游戏中新出现食物（如打印胶囊空投、烹饪新菜品、收获作物、脱水食品等）时，
    /// 食物箱（FoodBox）筛选列表中缺失该食物选项，导致无法勾选与无法收纳的问题。
    /// </summary>
    public static class FoodAutoDiscoverAndAcceptPatch
    {
        /// <summary>
        /// 当地面散落物或新生成的物品 Spawn 时，若是食物相关物品，立即在 DiscoveredResources 中发现该资源，
        /// 确保筛选界面（TreeFilterableSideScreen）能够展示该食物行。
        /// </summary>
        [HarmonyPatch(typeof(Pickupable), "OnSpawn")]
        internal static class PickupableFoodAutoDiscoverPatch
        {
            private static void Postfix(Pickupable __instance)
            {
                if (__instance == null) return;
                KPrefabID id = __instance.KPrefabID;
                if (id != null && FilterAutoManager.IsFoodItem(id))
                {
                    FilterAutoManager.EnsureDiscovered(id);
                }
            }
        }

        /// <summary>
        /// 当打印胶囊的补给包（Care Package）投放内容物时，提前完成资源发现，
        /// 确保食物掉出的一瞬间食物箱就已识别并将其纳入可收集筛选范围。
        /// </summary>
        [HarmonyPatch(typeof(CarePackage), "SpawnContents")]
        internal static class CarePackageFoodAutoDiscoverPatch
        {
            private static void Prefix(CarePackage __instance)
            {
                if (__instance == null || __instance.info == null || string.IsNullOrEmpty(__instance.info.id)) return;
                Tag tag = TagManager.Create(__instance.info.id);
                GameObject prefab = Assets.TryGetPrefab(tag);
                if (prefab != null)
                {
                    KPrefabID pid = prefab.GetComponent<KPrefabID>();
                    if (pid != null && FilterAutoManager.IsFoodItem(pid))
                    {
                        FilterAutoManager.EnsureDiscovered(pid);
                    }
                }
            }
        }

        /// <summary>
        /// 当玩家点击食物箱打开侧边栏筛选列表（TreeFilterableSideScreen）时，
        /// 预先同步并发现场景中所有食物，并补全 FoodBox 的 TreeFilterable 勾选，
        /// 保证玩家打开面板时能完整看到新来的食物选项且已默认勾选。
        /// </summary>
        [HarmonyPatch(typeof(TreeFilterableSideScreen), "SetTarget")]
        internal static class TreeFilterableSideScreenFoodSyncPatch
        {
            private static void Prefix(GameObject target)
            {
                if (target == null) return;
                GlobalItemCollector collector = target.GetComponent<GlobalItemCollector>();
                if (collector != null && collector.mode == GlobalItemCollector.CollectMode.Edibles)
                {
                    TreeFilterable tree = target.GetComponent<TreeFilterable>();
                    Storage storage = target.GetComponent<Storage>();
                    FilterAutoManager.SyncDiscoveredResources(collector, tree, storage);
                }
            }
        }
    }
}
