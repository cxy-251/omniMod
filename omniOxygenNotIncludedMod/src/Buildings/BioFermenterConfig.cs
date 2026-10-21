using System.Collections.Generic;
using TUNING;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 生物发酵反应罐（Bio-Fermenter）：
    /// 复用乙醇蒸馏器模型（附带生化浅绿/琥珀发酵色微调），专门用于高通量消耗多余的纯水。
    ///
    /// - 液体输入：纯水（Water）2.0 kg/s
    /// - 固体进料：蔗糖（Sucrose）0.5 kg/s，或汗甜玉米（Sweatcorn）0.5 kg/s
    /// - 液体输出：液体乙醇（Ethanol）2.0 kg/s（接排液管直出）
    /// - 气体副产：二氧化碳（CO2）0.5 kg/s（散逸到环境中）
    /// - 额定功率：240W
    /// </summary>
    public class BioFermenterConfig : IBuildingConfig
    {
        public const string ID = "OmniMod_BioFermenter";

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                id: ID,
                width: 4,
                height: 3,
                anim: "ethanoldistillery_kanim",
                hitpoints: 100,
                construction_time: 30f,
                construction_mass: BUILDINGS.CONSTRUCTION_MASS_KG.TIER3,
                construction_materials: MATERIALS.REFINED_METALS,
                melting_point: 800f,
                build_location_rule: BuildLocationRule.OnFloor,
                noise: NOISE_POLLUTION.NOISY.TIER1,
                decor: BUILDINGS.DECOR.PENALTY.TIER1);

            def.RequiresPowerInput = true;
            def.EnergyConsumptionWhenActive = 240f;
            def.ExhaustKilowattsWhenActive = 0.5f;
            def.SelfHeatKilowattsWhenActive = 1f;

            def.InputConduitType = ConduitType.Liquid;
            def.UtilityInputOffset = new CellOffset(0, 0);

            def.OutputConduitType = ConduitType.Liquid;
            def.UtilityOutputOffset = new CellOffset(2, 0);

            def.ViewMode = OverlayModes.LiquidConduits.ID;
            def.AudioCategory = "HollowMetal";
            def.AudioSize = "large";

            def.LogicInputPorts = LogicOperationalController.CreateSingleInputPortList(new CellOffset(0, 1));

            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.IndustrialMachinery);

            Storage storage = BuildingTemplates.CreateDefaultStorage(go);
            storage.capacityKg = 2000f;
            storage.showInUI = true;
            storage.SetDefaultStoredItemModifiers(Storage.StandardSealedStorage);

            // 进水管道接口：吞吐纯水 2 kg/s
            ConduitConsumer consumer = go.AddOrGet<ConduitConsumer>();
            consumer.conduitType = ConduitType.Liquid;
            consumer.consumptionRate = 10f;
            consumer.capacityKG = 20f;
            consumer.capacityTag = SimHashes.Water.CreateTag();
            consumer.wrongElementResult = ConduitConsumer.WrongElementResult.Dump;
            consumer.forceAlwaysSatisfied = true;

            // 出液管道接口：排出乙醇 2 kg/s
            ConduitDispenser dispenser = go.AddOrGet<ConduitDispenser>();
            dispenser.conduitType = ConduitType.Liquid;
            dispenser.alwaysDispense = true;
            dispenser.elementFilter = new[] { SimHashes.Ethanol };

            // 固体进料支持 1：蔗糖（Sucrose）
            ManualDeliveryKG sucroseDelivery = go.AddComponent<ManualDeliveryKG>();
            sucroseDelivery.SetStorage(storage);
            sucroseDelivery.RequestedItemTag = SimHashes.Sucrose.CreateTag();
            sucroseDelivery.capacity = 500f;
            sucroseDelivery.refillMass = 100f;
            sucroseDelivery.choreTypeIDHash = Db.Get().ChoreTypes.FetchCritical.IdHash;

            // 固体进料支持 2：汗甜玉米（GardenFoodPlantFood）
            ManualDeliveryKG cornDelivery = go.AddComponent<ManualDeliveryKG>();
            cornDelivery.SetStorage(storage);
            cornDelivery.RequestedItemTag = TagManager.Create("GardenFoodPlantFood");
            cornDelivery.capacity = 500f;
            cornDelivery.refillMass = 100f;
            cornDelivery.choreTypeIDHash = Db.Get().ChoreTypes.FetchCritical.IdHash;

            // 反应配方 1：水 (2.0kg) + 蔗糖 (0.5kg) -> 乙醇 (2.0kg) + CO2 (0.5kg)
            ElementConverter sucroseConverter = go.AddComponent<ElementConverter>();
            sucroseConverter.consumedElements = new[]
            {
                new ElementConverter.ConsumedElement(SimHashes.Water.CreateTag(), 2.0f),
                new ElementConverter.ConsumedElement(SimHashes.Sucrose.CreateTag(), 0.5f),
            };
            sucroseConverter.outputElements = new[]
            {
                new ElementConverter.OutputElement(2.0f, SimHashes.Ethanol, 313.15f, useEntityTemperature: false, storeOutput: true),
                new ElementConverter.OutputElement(0.5f, SimHashes.CarbonDioxide, 313.15f, useEntityTemperature: false, storeOutput: false, outputElementOffsety: 1.5f),
            };

            // 反应配方 2：水 (2.0kg) + 汗甜玉米 (0.5kg) -> 乙醇 (2.0kg) + CO2 (0.5kg)
            ElementConverter cornConverter = go.AddComponent<ElementConverter>();
            cornConverter.consumedElements = new[]
            {
                new ElementConverter.ConsumedElement(SimHashes.Water.CreateTag(), 2.0f),
                new ElementConverter.ConsumedElement(TagManager.Create("GardenFoodPlantFood"), 0.5f),
            };
            cornConverter.outputElements = new[]
            {
                new ElementConverter.OutputElement(2.0f, SimHashes.Ethanol, 313.15f, useEntityTemperature: false, storeOutput: true),
                new ElementConverter.OutputElement(0.5f, SimHashes.CarbonDioxide, 313.15f, useEntityTemperature: false, storeOutput: false, outputElementOffsety: 1.5f),
            };

            Prioritizable.AddRef(go);

            // 生物发酵浅绿/浅琥珀金视觉微调
            BuildingTint tint = go.AddOrGet<BuildingTint>();
            tint.tint = new Color32(200, 255, 175, 255);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<LogicOperationalController>();
            go.AddOrGetDef<PoweredActiveController.Def>();
        }
    }
}
