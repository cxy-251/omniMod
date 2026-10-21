using HarmonyLib;
using OmniMod.Core.Safety;

namespace OmniMod.Machinery.Production
{
    /// <summary>
    /// 自定义工业制造配方：碎石机冰块粉碎、窖炉多原料炼炭等
    /// </summary>
    [HarmonyPatch(typeof(RockCrusherConfig), "ConfigureBuildingTemplate")]
    internal static class RockCrusherConfig_ConfigureBuildingTemplate_SnowRecipe_Patch
    {
        private static void Postfix()
        {
            ExceptionShield.Execute("CustomRecipes.RockCrusher_Snow", () =>
            {
                ComplexRecipe.RecipeElement[] input = new[]
                {
                    new ComplexRecipe.RecipeElement(SimHashes.Ice.CreateTag(), 100f)
                };
                ComplexRecipe.RecipeElement[] output = new[]
                {
                    new ComplexRecipe.RecipeElement(SimHashes.Snow.CreateTag(), 100f, ComplexRecipe.RecipeElement.TemperatureOperation.AverageTemperature)
                };
                string recipeId = ComplexRecipeManager.MakeRecipeID("RockCrusher", input, output);
                if (ComplexRecipeManager.Get() != null && ComplexRecipeManager.Get().GetRecipe(recipeId) != null)
                {
                    return;
                }

                new ComplexRecipe(recipeId, input, output)
                {
                    time = 20f,
                    description = "将致密冰块粉碎成疏松雪花，供寒带植物（如糖心树）施肥使用。",
                    nameDisplay = ComplexRecipe.RecipeNameDisplay.IngredientToResult,
                    fabricators = new System.Collections.Generic.List<Tag> { TagManager.Create("RockCrusher") },
                    sortOrder = 9
                };
            });
        }
    }

    [HarmonyPatch(typeof(KilnConfig), "ConfigureBuildingTemplate")]
    internal static class KilnConfig_ConfigureBuildingTemplate_CoalRecipes_Patch
    {
        private static void Postfix()
        {
            ExceptionShield.Execute("CustomRecipes.Kiln_Coal", () =>
            {
                var inputTags = new System.Collections.Generic.List<Tag>();
                if (GameTags.BasicWoods != null && GameTags.BasicWoods.Length > 0)
                {
                    inputTags.AddRange(GameTags.BasicWoods);
                }
                else
                {
                    inputTags.Add(TagManager.Create("WoodLog"));
                }
                inputTags.Add(SimHashes.RefinedCarbon.CreateTag());
                inputTags.Add(SimHashes.Dirt.CreateTag());
                inputTags.Add(SimHashes.Peat.CreateTag());

                var input = new ComplexRecipe.RecipeElement[]
                {
                    new ComplexRecipe.RecipeElement(inputTags.ToArray(), 100f)
                };
                var output = new ComplexRecipe.RecipeElement[]
                {
                    new ComplexRecipe.RecipeElement(SimHashes.Carbon.CreateTag(), 100f, ComplexRecipe.RecipeElement.TemperatureOperation.AverageTemperature)
                };

                string recipeId = ComplexRecipeManager.MakeRecipeID("Kiln", input, output);
                if (ComplexRecipeManager.Get() != null && ComplexRecipeManager.Get().GetRecipe(recipeId) != null)
                {
                    return;
                }

                new ComplexRecipe(recipeId, input, output)
                {
                    time = 20f,
                    description = "将木材、精炼碳、泥土或泥炭在低氧密闭窖炉中碳化为煤炭，高效供给电网与工业需求。",
                    nameDisplay = ComplexRecipe.RecipeNameDisplay.Result,
                    fabricators = new System.Collections.Generic.List<Tag> { TagManager.Create("Kiln") },
                    sortOrder = 150
                };
            });
        }
    }
}
