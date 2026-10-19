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
        private static void Postfix(TreeFilterableSideScreen __instance, Tag tag, ref float __result)
        {
            Storage storage = Traverse.Create(__instance).Field("storage").GetValue<Storage>();
            GlobalItemCollector box = storage != null ? storage.GetComponent<GlobalItemCollector>() : null;
            if (box == null)
            {
                return;   // 不是我们的聚合箱
            }

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

            // 加上其它星球同类型聚合箱（杂物箱或食物箱）里的缓冲/共享存量，使任意星球的侧边栏都显示全星系总存量
            __result += AggregateBoxRegistry.GetOtherBoxesMassAvailable(box, tag);
        }
    }
}
