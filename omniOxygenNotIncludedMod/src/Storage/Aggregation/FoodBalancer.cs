using System.Collections.Generic;
using OmniMod.Core.Safety;
using UnityEngine;

namespace OmniMod.Collection
{
    /// <summary>
    /// 全星系食物跨星均衡调度器：
    /// 协调所有生效的食物箱，将可拆分食物适度均衡调配到各个星球的食物箱中，确保跨星球食物箱不空竭。
    /// </summary>
    public static class FoodBalancer
    {
        private static readonly List<GlobalItemCollector> _boxesScratch = new List<GlobalItemCollector>(8);
        private static readonly HashSet<Tag> _foodTagsScratch = new HashSet<Tag>();

        public static void RebalanceFoodAcrossBoxes()
        {
            ExceptionShield.Execute("FoodBalancer.Rebalance", () =>
            {
                _boxesScratch.Clear();
                AggregateBoxRegistry.GetActiveBoxes(GlobalItemCollector.CollectMode.Edibles, _boxesScratch);
                int boxCount = _boxesScratch.Count;
                if (boxCount <= 1) return;

                _foodTagsScratch.Clear();
                for (int b = 0; b < boxCount; b++)
                {
                    Storage st = _boxesScratch[b].BoxStorage;
                    if (st == null || st.items == null) continue;
                    for (int i = 0; i < st.items.Count; i++)
                    {
                        GameObject go = st.items[i];
                        KPrefabID id = go != null ? go.GetComponent<KPrefabID>() : null;
                        if (id != null && !LivingThingsSafety.IsLivingThing(go))
                        {
                            _foodTagsScratch.Add(id.PrefabTag);
                        }
                    }
                }

                foreach (Tag foodTag in _foodTagsScratch)
                {
                    float totalMass = 0f;
                    for (int b = 0; b < boxCount; b++)
                    {
                        totalMass += DiscreteDemandManager.GetUnreservedTagMass(_boxesScratch[b].BoxStorage, foodTag);
                    }

                    float targetPerBox = totalMass / boxCount;
                    if (targetPerBox < 0.2f) continue;

                    for (int r = 0; r < boxCount; r++)
                    {
                        Storage recipient = _boxesScratch[r].BoxStorage;
                        float rHave = DiscreteDemandManager.GetUnreservedTagMass(recipient, foodTag);
                        if (rHave >= targetPerBox * 0.65f) continue;

                        float need = targetPerBox - rHave;
                        for (int d = 0; d < boxCount && need > 0.05f; d++)
                        {
                            if (d == r) continue;
                            Storage donor = _boxesScratch[d].BoxStorage;
                            float dHave = DiscreteDemandManager.GetUnreservedTagMass(donor, foodTag);
                            float dSurplus = dHave - targetPerBox;
                            if (dSurplus <= 0.05f) continue;

                            float toMove = Mathf.Min(need, dSurplus);
                            for (int i = donor.items.Count - 1; i >= 0 && toMove > 0.05f; i--)
                            {
                                GameObject go = donor.items[i];
                                if (go == null) continue;

                                KPrefabID id = go.GetComponent<KPrefabID>();
                                Pickupable p = go.GetComponent<Pickupable>();
                                if (id == null || p == null || p.ReservedAmount > 0f || id.PrefabTag != foodTag)
                                {
                                    continue;
                                }

                                float avail = p.TotalAmount;
                                if (avail > toMove + 0.05f)
                                {
                                    Pickupable split = p.Take(toMove);
                                    if (split != null)
                                    {
                                        recipient.Store(split.gameObject, hide_popups: true);
                                        need -= toMove;
                                        toMove = 0f;
                                    }
                                }
                                else
                                {
                                    donor.Transfer(go, recipient, block_events: false, hide_popups: true);
                                    need -= avail;
                                    toMove -= avail;
                                }
                            }
                        }
                    }
                }
            });
        }
    }
}
