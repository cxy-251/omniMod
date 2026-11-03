using HarmonyLib;
using OmniMod.Collection;
using UnityEngine;

namespace OmniMod.Pool
{
    /// <summary>
    /// 让"点箱子出现的资源筛选侧栏"里每一项显示的数量，反映全局杂物池的存量，
    /// 而不是箱子那个近乎空的按需缓冲区。
    ///
    /// 侧栏每行的数量来自 TreeFilterableSideScreen.GetAmountInStorage(tag)
    /// （内部就是 storage.GetMassAvailable(tag)）。我们在其后追加池中对应质量。
    /// </summary>
    [HarmonyPatch(typeof(TreeFilterableSideScreen), nameof(TreeFilterableSideScreen.GetAmountInStorage))]
    internal static class TreeFilterableSideScreen_GetAmountInStorage_Patch
    {
        private static readonly AccessTools.FieldRef<TreeFilterableSideScreen, Storage> StorageField =
            AccessTools.FieldRefAccess<TreeFilterableSideScreen, Storage>("storage");

        internal static Storage GetStorage(TreeFilterableSideScreen screen) => screen != null ? StorageField(screen) : null;

        private static void Postfix(TreeFilterableSideScreen __instance, Tag tag, ref float __result)
        {
            Storage storage = GetStorage(__instance);
            GlobalItemCollector box = storage != null ? storage.GetComponent<GlobalItemCollector>() : null;
            if (box == null)
            {
                return;   // 不是我们的聚合箱
            }

            if (box.IsRedundant) return;
            if (box.mode == GlobalItemCollector.CollectMode.Debris && OmniPool.Instance != null)
            {
                // 散装元素：加上纯数据池里的量
                __result += OmniPool.Instance.GetMassMatchingTag(tag);

                // 种子：加上全局种子池里的数量
                int seedCount = OmniPool.Instance.GetSeedCount(tag);
                if (seedCount > 0)
                {
                    __result += seedCount;
                }
            }

            if (!box.IsRedundant)
                __result = Mathf.Max(0f, __result - SharedBoxInventory.GetPortalOwnedMass(storage, tag)) +
                    SharedBoxInventory.GetMassForMode(box.mode, tag);
        }
    }
}
