using System.Collections.Generic;
using OmniMod.Core.Logging;
using OmniMod.Core.Safety;
using UnityEngine;

namespace OmniMod.Collection
{
    /// <summary>
    /// 聚合箱活物安全排斥与兜底释放：
    /// 严禁小动物、小动物蛋、复制人被吸入聚合箱。若有活物误入，立即安全弹出至地面。
    /// </summary>
    public static class LivingThingsSafety
    {
        public static bool IsLivingThing(GameObject go)
        {
            if (go == null) return false;
            KPrefabID kpid = go.GetComponent<KPrefabID>();
            if (kpid == null) return false;

            return kpid.HasTag(GameTags.BaseMinion) ||
                   kpid.HasTag(GameTags.Creature) ||
                   kpid.HasTag(GameTags.Egg) ||
                   kpid.HasTag(GameTags.IncubatableEgg) ||
                   kpid.HasTag(GameTags.Trapped) ||
                   kpid.HasTag(GameTags.BagableCreature) ||
                   go.GetComponent<MinionIdentity>() != null ||
                   go.GetComponent<CreatureBrain>() != null ||
                   go.GetComponent<IncubationMonitor.Instance>() != null;
        }

        public static void EjectLivingThings(GlobalItemCollector collector)
        {
            if (collector == null || collector.BoxStorage == null) return;
            Storage storage = collector.BoxStorage;
            if (storage.items == null || storage.items.Count == 0) return;

            ExceptionShield.Execute("LivingThingsSafety.Eject", () =>
            {
                var toEject = new List<GameObject>(4);
                for (int i = 0; i < storage.items.Count; i++)
                {
                    GameObject go = storage.items[i];
                    if (IsLivingThing(go))
                    {
                        toEject.Add(go);
                    }
                }

                if (toEject.Count > 0)
                {
                    Vector3 dropPos = collector.transform.position + Vector3.up;
                    for (int i = 0; i < toEject.Count; i++)
                    {
                        GameObject go = toEject[i];
                        storage.Drop(go, true);
                        go.transform.position = dropPos;
                        ModLog.Warn($"[安全排斥] 从聚合箱中弹出误入的活物/蛋: {go.name}");
                    }
                }
            });
        }
    }
}
