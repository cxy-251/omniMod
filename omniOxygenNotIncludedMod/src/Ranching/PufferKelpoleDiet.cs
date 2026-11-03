using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace OmniMod.Ranching
{
    // 成年与幼年鼓气鱼都经过这个非泛型工厂，读档时也使用更新后的食谱。
    [HarmonyPatch(typeof(BasePufferFish), nameof(BasePufferFish.CreatePrefab))]
    internal static class PufferKelpoleDiet
    {
        private static void Postfix(GameObject __result)
        {
            if (__result == null) return;
            var calories = __result.GetDef<CreatureCalorieMonitor.Def>();
            var consumer = __result.GetDef<SolidConsumerMonitor.Def>();
            if (calories?.diet?.infos == null || consumer == null) return;
            Tag kelpole = SeaFairyConfig.ID.ToTag();
            foreach (Diet.Info info in calories.diet.infos)
                if (info.consumedTags.Contains(kelpole)) return;

            var infos = new List<Diet.Info>(calories.diet.infos);
            // 同矿甲龟／颚鱼：5 kg 藻蝌蚪提供 400 kcal；鼓气鱼仍按自身单位热量产氧。
            // 使用原版 EatPrey 消费、死亡及事件流程，不另行删除猎物或额外生成掉落。
            infos.Add(new Diet.Info(new HashSet<Tag> { kelpole }, PufferFishTuning.POOP_ELEMENT,
                80000f, PufferFishTuning.PICKUPABLE_TO_PRODUCT_EFFICIENCY *
                (80000f / PufferFishTuning.CALORIES_PER_PICKUPABLE_KG),
                food_type: Diet.Info.FoodType.EatPrey,
                eat_anims: new[] { "eat_pre", "eat_loop", "idle_loop" }));
            var diet = new Diet(infos.ToArray());
            calories.diet = diet;
            consumer.diet = diet;
        }
    }
}
