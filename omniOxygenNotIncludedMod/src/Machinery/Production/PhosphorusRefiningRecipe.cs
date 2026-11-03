using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace OmniMod.Machinery.Production
{
    /// <summary>窖炉新增磷矿精炼配方，与原精炼碳配方并列。</summary>
    [HarmonyPatch(typeof(KilnConfig), nameof(KilnConfig.ConfigureBuildingTemplate))]
    internal static class PhosphorusRefiningRecipe
    {
        internal static ComplexRecipe Recipe { get; private set; }
        internal const float OutputTemperature = 313.15f;

        private static void Postfix()
        {
            var ingredients = new[]
            {
                new ComplexRecipe.RecipeElement(SimHashes.Phosphorite.CreateTag(), 100f)
            };
            var products = new[]
            {
                new ComplexRecipe.RecipeElement(SimHashes.Phosphorus.CreateTag(), 100f,
                    ComplexRecipe.RecipeElement.TemperatureOperation.AverageTemperature)
            };
            string id = ComplexRecipeManager.MakeRecipeID(KilnConfig.ID, ingredients, products);
            ComplexRecipeManager manager = ComplexRecipeManager.Get();
            if (manager == null) return;
            Recipe = manager.GetRecipe(id);
            if (Recipe != null) return;
            Recipe = new ComplexRecipe(id, ingredients, products)
            {
                time = 40f,
                nameDisplay = ComplexRecipe.RecipeNameDisplay.IngredientToResult,
                description = ModStrings.T(
                    "在窖炉中将 100 kg 磷矿精炼为 100 kg 精炼磷，无需额外燃料、电力或冷却液。本配方以 40°C 出料，避免按窖炉通常的 80°C 出料后立即熔化。精炼磷在约 44°C 熔化，储区需保持低于熔点。",
                    "Refines 100 kg of phosphorite into 100 kg of phosphorus in the kiln, without extra fuel, power or coolant. This recipe outputs at 40°C to prevent immediate melting at the kiln's usual 80°C. Keep storage below phosphorus's melting point of about 44°C.").Resolve(),
                fabricators = new List<Tag> { KilnConfig.ID.ToTag() },
                sortOrder = 201
            };
        }
    }

    [HarmonyPatch(typeof(ComplexFabricator), "SpawnOrderProduct")]
    internal static class KilnPhosphorusOutputTemperature
    {
        private static void Postfix(ComplexFabricator __instance, ComplexRecipe recipe, List<GameObject> __result)
        {
            if (PhosphorusRefiningRecipe.Recipe == null || recipe != PhosphorusRefiningRecipe.Recipe ||
                __instance.PrefabID() != KilnConfig.ID.ToTag() || __result == null) return;
            foreach (GameObject output in __result)
            {
                PrimaryElement material = output != null ? output.GetComponent<PrimaryElement>() : null;
                if (material != null && material.ElementID == SimHashes.Phosphorus)
                    material.Temperature = PhosphorusRefiningRecipe.OutputTemperature;
            }
        }
    }
}
