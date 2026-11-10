using HarmonyLib;
using OmniMod.Core.Safety;
using UnityEngine;

namespace OmniMod.Ranching
{
    internal static class IncubatorAutoRelease
    {
        internal static bool IsAquatic(GameObject baby)
        {
            if (baby == null) return false;

            // 1. 明确的水生/游泳生物标签
            if (baby.HasTag(GameTags.SwimmingCreature) || baby.HasTag(GameTags.Creatures.Swimmer))
            {
                return true;
            }

            // 2. 检查物种标签（涵盖帕库鱼、远古帕库鱼、矿甲龟、海仙女、海马、乌贼、鹦鹉鱼、刺豚等全系水生物种）
            CreatureBrain brain = baby.GetComponent<CreatureBrain>();
            if (brain != null)
            {
                Tag species = brain.species;
                if (species == GameTags.Creatures.Species.PacuSpecies ||
                    species == GameTags.Creatures.Species.PrehistoricPacuSpecies ||
                    species == GameTags.Creatures.Species.SeaTurtleSpecies ||
                    species == GameTags.Creatures.Species.SeaFairySpecies ||
                    species == GameTags.Creatures.Species.SeaHorseSpecies ||
                    species == GameTags.Creatures.Species.SquidSpecies ||
                    species == GameTags.Creatures.Species.ParrotFishSpecies ||
                    species == GameTags.Creatures.Species.PufferFishSpecies)
                {
                    return true;
                }
            }

            // 3. 检查导航网格
            Navigator nav = baby.GetComponent<Navigator>();
            if (nav != null && !string.IsNullOrEmpty(nav.NavGridName) &&
                nav.NavGridName.IndexOf("Swimmer", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        /// <summary>自动出雏：水生动物幼苗保留袋装以供搬运，陆生幼体自由活动。</summary>
        internal static void Release(EggIncubator incubator, GameObject baby)
        {
            if (incubator == null || baby == null || incubator.Occupant != baby ||
                baby.HasTag(GameTags.Egg)) return;

            ExceptionShield.Execute("EggIncubator.ReleaseHatchling", () =>
            {
                Tag nextEgg = incubator.requestedEntityTag;
                Tag nextFilter = incubator.requestedEntityAdditionalFilterTag;
                Baggable bag = baby.GetComponent<Baggable>();
                bool packAquatic = bag != null && IsAquatic(baby);
                if (bag != null) bag.keepWrangledNextTimeRemovedFromStorage = packAquatic;
                incubator.OrderRemoveOccupant();

                // 只处理刚从这台孵化器弹出的实体，不修改地上的动物或其它存储中的动物。
                var pickup = baby.GetComponent<Pickupable>();
                if (pickup == null || pickup.storage == null)
                {
                    if (packAquatic)
                    {
                        bag.SetWrangled();
                        baby.AddTag(GameTags.Creatures.Deliverable);
                        OmniMod.Ranching.Registries.BaggedCreatureRegistry.Register(bag);
                    }
                    else
                    {
                        if (bag != null) bag.Free();
                        baby.RemoveTag(GameTags.Creatures.Deliverable);
                    }
                    baby.GetComponent<KBatchedAnimController>()?.SetSceneLayer(Grid.SceneLayer.Creatures);
                    CreatureBrain brain = baby.GetComponent<CreatureBrain>();
                    if (brain != null) Game.BrainScheduler?.PrioritizeBrain(brain);
                }

                // OrderRemoveOccupant 在调用 ClearOccupant 前清空 Occupant，需补回自动换蛋订单。
                if (incubator.autoReplaceEntity && nextEgg.IsValid && nextEgg != GameTags.Empty &&
                    incubator.GetActiveRequest == null)
                    incubator.CreateOrder(nextEgg, nextFilter);
            });
        }
    }

    [HarmonyPatch(typeof(EggIncubator), nameof(EggIncubator.StoreBaby))]
    internal static class EggIncubator_StoreBaby_Patch
    {
        private static void Postfix(EggIncubator __instance, GameObject baby) =>
            IncubatorAutoRelease.Release(__instance, baby);
    }

    [HarmonyPatch(typeof(EggIncubator), "OnSpawn")]
    internal static class EggIncubator_OnSpawn_Watchdog_Patch
    {
        private static void Postfix(EggIncubator __instance)
        {
            if (__instance != null) IncubatorAutoRelease.Release(__instance, __instance.Occupant);
        }
    }
}
