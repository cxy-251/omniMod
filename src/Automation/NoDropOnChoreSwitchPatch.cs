using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace OmniMod.Automation
{
    /// <summary>
    /// 不让小人在切换任务时把手里"还有用的货"丢在地上。
    ///
    /// 香草机制：<c>MinionModifiers.OnBeginChore</c> 在小人<b>每次开始一个新任务</b>时触发，
    /// 无条件 <c>GetComponent&lt;Storage&gt;().DropAll()</c>——把随身携带的一切倒在脚下。
    /// 于是"取了货 → 半路被更高优先级任务打断 → 货掉地上 → OmniMod 聚合箱秒回收 →
    /// 机器搬运需求重新生成 → 小人再跑一趟取货"，来回空跑，货永远送不到。
    ///
    /// 这个补丁用 Prefix 取代那次 DropAll：只丢"本星球当前没有任何搬运需求要的"东西，
    /// 仍被某个搬运任务需要的货<b>继续带着</b>——小人喘完气/吃完饭回来直接送到目的地。
    /// 真正无主的杂物照样丢下（保留香草"不许小人囤积"的本意）。
    /// </summary>
    [HarmonyPatch(typeof(MinionModifiers), "OnBeginChore")]
    internal static class MinionModifiers_OnBeginChore_NoDrop_Patch
    {
        private static readonly HashSet<Tag> _wantedScratch = new HashSet<Tag>();
        private static readonly List<GameObject> _dropScratch = new List<GameObject>(8);
        private static int _logged;

        private static bool Prefix(MinionModifiers __instance)
        {
            try
            {
                Storage storage = __instance.GetComponent<Storage>();
                if (storage == null || storage.items == null || storage.items.Count == 0)
                {
                    return false;   // 手里没东西，香草那次 DropAll 也是空操作，直接跳过
                }

                // 收集"本星球所有待办搬运任务想要的 tag"
                _wantedScratch.Clear();
                GlobalChoreProvider gcp = GlobalChoreProvider.Instance;
                int worldId = __instance.gameObject.GetMyParentWorldId();
                if (gcp != null
                    && gcp.fetchMap.TryGetValue(worldId, out List<FetchChore> fetches)
                    && fetches != null)
                {
                    for (int i = 0; i < fetches.Count; i++)
                    {
                        FetchChore fc = fetches[i];
                        if (fc != null && fc.tags != null)
                        {
                            foreach (Tag t in fc.tags)
                            {
                                _wantedScratch.Add(t);
                            }
                        }
                    }
                }

                // 逐件判断：还有搬运需求要的 → 留着继续带；无主的 → 丢下
                _dropScratch.Clear();
                for (int i = 0; i < storage.items.Count; i++)
                {
                    GameObject go = storage.items[i];
                    if (go == null)
                    {
                        continue;
                    }

                    KPrefabID id = go.GetComponent<KPrefabID>();
                    bool stillWanted = id != null &&
                        (_wantedScratch.Contains(id.PrefabTag) ||
                         (id.HasTag(GameTags.Edible) && _wantedScratch.Contains(GameTags.Edible)));

                    if (!stillWanted)
                    {
                        _dropScratch.Add(go);
                    }
                }

                for (int i = 0; i < _dropScratch.Count; i++)
                {
                    storage.Drop(_dropScratch[i]);
                }

                int kept = storage.items.Count;
                if (kept > 0 && _logged < 20)
                {
                    _logged++;
                    Debug.Log($"[OmniMod] 免丢货：{__instance.name} 切换任务，保留携带 {kept} 件仍被需要的货");
                }

                _dropScratch.Clear();
                return false;   // 已接管，跳过香草的无条件 DropAll
            }
            catch
            {
                return true;    // 出错就放行香草逻辑，别把小人卡死
            }
        }
    }
}
