using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace OmniMod.Ranching
{
    /// <summary>成年与幼年颚鱼共用工厂；追加活体藻蝌蚪食谱，保留原有鱼类和鱼肉食谱。</summary>
    [HarmonyPatch(typeof(BasePrehistoricPacuConfig), nameof(BasePrehistoricPacuConfig.CreatePrefab))]
    internal static class JawboKelpoleDiet
    {
        private static void Postfix(GameObject __result)
        {
            if (__result == null) return;
            var calories = __result.GetDef<CreatureCalorieMonitor.Def>();
            var consumer = __result.GetDef<SolidConsumerMonitor.Def>();
            if (calories?.diet?.infos == null || consumer == null) return;

            Tag kelpole = SeaFairyConfig.ID.ToTag();
            bool hasKelpole = false;
            var infos = new List<Diet.Info>();
            foreach (Diet.Info info in calories.diet.infos)
            {
                if (info.consumedTags.Contains(kelpole)) hasKelpole = true;
                var copy = new Diet.Info(info);
                if (copy.foodType == Diet.Info.FoodType.EatPrey)
                {
                    // 幼鱼沿用对应成鱼的质量换算；成年优先由猎物选择策略保证。
                    AddBaby(copy, "Pacu", BabyPacuConfig.ID);
                    AddBaby(copy, "PacuCleaner", BabyPacuCleanerConfig.ID);
                    AddBaby(copy, "PacuTropical", BabyPacuTropicalConfig.ID);
                    AddBaby(copy, "ParrotFish", BabyParrotFishConfig.ID);
                    AddBaby(copy, "PufferFish", BabyPufferFishConfig.ID);
                }
                infos.Add(copy);
            }
            // 原版矿甲龟：每千克藻蝌蚪 80,000 卡，产物质量换算系数为 50 / 2 / 5。
            // 颚鱼沿用自身的铁锈产物、代谢速度、胃容量与排泄阈值。
            if (!hasKelpole) infos.Add(new Diet.Info(new HashSet<Tag> { kelpole }, PrehistoricPacuTuning.POOP_ELEMENT,
                80000f, SeaTurtleTuning.POOP_KG_PER_CYCLE / 2f / 5f,
                food_type: Diet.Info.FoodType.EatPrey));
            var diet = new Diet(infos.ToArray());
            calories.diet = diet;
            consumer.diet = diet;
        }
        private static void AddBaby(Diet.Info info, string adult, string baby)
        {
            if (info.consumedTags.Contains(adult.ToTag())) info.consumedTags.Add(baby.ToTag());
        }
    }
}
