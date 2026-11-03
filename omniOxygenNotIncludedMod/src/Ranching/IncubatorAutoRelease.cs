using HarmonyLib;
using OmniMod.Core.Safety;
using UnityEngine;

namespace OmniMod.Ranching
{
    internal static class IncubatorAutoRelease
    {
        private static bool IsFish(GameObject baby)
        {
            CreatureBrain brain = baby.GetComponent<CreatureBrain>();
            if (brain == null) return false;
            Tag species = brain.species;
            // 用物种覆盖帕库鱼变种与幼体，不将同为 Swimmer 的矿甲龟也当成鱼。
            return species == GameTags.Creatures.Species.PacuSpecies ||
                species == GameTags.Creatures.Species.PrehistoricPacuSpecies ||
                species == GameTags.Creatures.Species.ParrotFishSpecies ||
                species == GameTags.Creatures.Species.PufferFishSpecies;
        }

        /// <summary>自动出雏：鱼苗保留袋装以供搬运，其它幼体自由活动。</summary>
        internal static void Release(EggIncubator incubator, GameObject baby)
        {
            if (incubator == null || baby == null || incubator.Occupant != baby ||
                baby.HasTag(GameTags.Egg)) return;

            ExceptionShield.Execute("EggIncubator.ReleaseHatchling", () =>
            {
                Tag nextEgg = incubator.requestedEntityTag;
                Tag nextFilter = incubator.requestedEntityAdditionalFilterTag;
                Baggable bag = baby.GetComponent<Baggable>();
                bool packFish = bag != null && IsFish(baby);
                if (bag != null) bag.keepWrangledNextTimeRemovedFromStorage = packFish;
                incubator.OrderRemoveOccupant();

                // 只处理刚从这台孵化器弹出的实体，不修改地上的动物或其它存储中的动物。
                var pickup = baby.GetComponent<Pickupable>();
                if (pickup == null || pickup.storage == null)
                {
                    if (packFish)
                    {
                        bag.SetWrangled();
                        baby.AddTag(GameTags.Creatures.Deliverable);
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
