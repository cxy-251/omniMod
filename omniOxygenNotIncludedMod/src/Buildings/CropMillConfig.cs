using System.Collections.Generic;
using TUNING;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 农作物研磨机（Crop Mill）：
    /// 专门用于研磨、粉碎和精炼各类农作物的农业加工机械。
    ///
    /// 核心功能与机制：
    /// - 建筑类型：ComplexFabricator（制造机台），支持排队下单、循环生产与配方选择。
    /// - 自动化兼容：全面接入 OmniMod 免人力自动运行补丁，原料到达后全自动开工，无需小人值守。
    /// - 初始配方：汗甜玉米（Sweatcorn）-> 结晶蔗糖（Sucrose）。
    /// - 易扩展性：后续可随时直接追加各类植物、籽粒、根茎的深加工配方。
    /// </summary>
    public class CropMillConfig : IBuildingConfig
    {
        public const string ID = "OmniMod_CropMill";

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                id: ID,
                width: 2,
                height: 3,
                anim: "microbemusher_kanim",
                hitpoints: 30,
                construction_time: 30f,
                construction_mass: BUILDINGS.CONSTRUCTION_MASS_KG.TIER3,
                construction_materials: MATERIALS.ALL_METALS,
                melting_point: 800f,
                build_location_rule: BuildLocationRule.OnFloor,
                noise: NOISE_POLLUTION.NOISY.TIER1,
                decor: BUILDINGS.DECOR.PENALTY.TIER1);

            def.RequiresPowerInput = true;
            def.EnergyConsumptionWhenActive = 240f;
            def.ExhaustKilowattsWhenActive = 0.5f;
            def.SelfHeatKilowattsWhenActive = 1f;

            def.ViewMode = OverlayModes.Power.ID;
            def.AudioCategory = "Metal";
            def.PowerInputOffset = new CellOffset(0, 0);

            def.LogicInputPorts = LogicOperationalController.CreateSingleInputPortList(new CellOffset(0, 1));

            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.IndustrialMachinery);

            go.AddOrGet<DropAllWorkable>();
            go.AddOrGet<BuildingComplete>().isManuallyOperated = false;

            ComplexFabricator fabricator = go.AddOrGet<ComplexFabricator>();
            fabricator.sideScreenStyle = ComplexFabricatorSideScreen.StyleSetting.ListQueueHybrid;
            fabricator.duplicantOperated = false;
            fabricator.showProgressBar = true;

            BuildingTemplates.CreateDefaultStorage(go, false);
            fabricator.inStorage = go.GetComponent<Storage>();
            fabricator.buildStorage = go.GetComponent<Storage>();
            fabricator.outStorage = go.GetComponent<Storage>();
            fabricator.outputOffset = new Vector3(1f, 0.5f);

            ComplexFabricatorWorkable workable = go.AddOrGet<ComplexFabricatorWorkable>();
            workable.overrideAnims = new[] { Assets.GetAnim("anim_interacts_musher_kanim") };

            Prioritizable.AddRef(go);

            // 农业麦穗暖金色调微调
            BuildingTint tint = go.AddOrGet<BuildingTint>();
            tint.tint = new Color32(240, 210, 140, 255);

            ConfigureRecipes();
        }

        private static void ConfigureRecipes()
        {
            RegisterCornToSucroseRecipe();
        }

        private static void RegisterCornToSucroseRecipe()
        {
            ComplexRecipe.RecipeElement[] input = new[]
            {
                new ComplexRecipe.RecipeElement(TagManager.Create("GardenFoodPlantFood"), 100f)
            };
            ComplexRecipe.RecipeElement[] output = new[]
            {
                new ComplexRecipe.RecipeElement(SimHashes.Sucrose.CreateTag(), 100f, ComplexRecipe.RecipeElement.TemperatureOperation.AverageTemperature)
            };

            string recipeId = ComplexRecipeManager.MakeRecipeID(ID, input, output);
            if (ComplexRecipeManager.Get() != null && ComplexRecipeManager.Get().GetRecipe(recipeId) == null)
            {
                new ComplexRecipe(recipeId, input, output)
                {
                    time = 20f,
                    description = "研磨粉碎汗甜玉米，破坏植物纤维提取高纯度结晶蔗糖。",
                    nameDisplay = ComplexRecipe.RecipeNameDisplay.IngredientToResult,
                    fabricators = new List<Tag> { TagManager.Create(ID) },
                    sortOrder = 1
                };
            }
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<LogicOperationalController>();
        }
    }
}
