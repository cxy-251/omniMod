using System.Collections.Generic;
using TUNING;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 水热合成油反应釜（Petroleum Synthesizer）：
    /// 复用乙醇蒸馏器模型（附带深碳黑/重工业钢灰微调），通过水热重整与费托合成高压将水转化为石油。
    ///
    /// - 液体输入：纯水（Water）2.0 kg/s
    /// - 固体进料：煤炭（Coal）0.5 kg/s，或精炼碳（Refined Carbon）0.25 kg/s
    /// - 液体输出：精炼石油（Petroleum）2.0 kg/s（接排液管直出）
    /// - 额定功率：480W
    /// </summary>
    public class PetroleumSynthesizerConfig : IBuildingConfig
    {
        public const string ID = "OmniMod_PetroleumSynthesizer";

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
                noise: NOISE_POLLUTION.NOISY.TIER2,
                decor: BUILDINGS.DECOR.PENALTY.TIER1);

            def.RequiresPowerInput = true;
            def.EnergyConsumptionWhenActive = 480f;
            def.ExhaustKilowattsWhenActive = 1f;
            def.SelfHeatKilowattsWhenActive = 2f;

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

            // 出液管道接口：排出石油 2 kg/s
            ConduitDispenser dispenser = go.AddOrGet<ConduitDispenser>();
            dispenser.conduitType = ConduitType.Liquid;
            dispenser.alwaysDispense = true;
            dispenser.elementFilter = new[] { SimHashes.Petroleum };

            // 固体进料支持 1：煤炭（Coal）
            ManualDeliveryKG coalDelivery = go.AddComponent<ManualDeliveryKG>();
            coalDelivery.SetStorage(storage);
            coalDelivery.RequestedItemTag = SimHashes.Carbon.CreateTag();
            coalDelivery.capacity = 500f;
            coalDelivery.refillMass = 100f;
            coalDelivery.choreTypeIDHash = Db.Get().ChoreTypes.FetchCritical.IdHash;

            // 固体进料支持 2：精炼碳（Refined Carbon）
            ManualDeliveryKG refinedCarbonDelivery = go.AddComponent<ManualDeliveryKG>();
            refinedCarbonDelivery.SetStorage(storage);
            refinedCarbonDelivery.RequestedItemTag = SimHashes.RefinedCarbon.CreateTag();
            refinedCarbonDelivery.capacity = 250f;
            refinedCarbonDelivery.refillMass = 50f;
            refinedCarbonDelivery.choreTypeIDHash = Db.Get().ChoreTypes.FetchCritical.IdHash;

            // 反应配方 1：水 (2.0kg) + 煤炭 (0.5kg) -> 精炼石油 (2.0kg)
            ElementConverter coalConverter = go.AddComponent<ElementConverter>();
            coalConverter.consumedElements = new[]
            {
                new ElementConverter.ConsumedElement(SimHashes.Water.CreateTag(), 2.0f),
                new ElementConverter.ConsumedElement(SimHashes.Carbon.CreateTag(), 0.5f),
            };
            coalConverter.outputElements = new[]
            {
                new ElementConverter.OutputElement(2.0f, SimHashes.Petroleum, 333.15f, useEntityTemperature: false, storeOutput: true),
            };

            // 反应配方 2：水 (2.0kg) + 精炼碳 (0.25kg) -> 精炼石油 (2.0kg)
            ElementConverter refinedCarbonConverter = go.AddComponent<ElementConverter>();
            refinedCarbonConverter.consumedElements = new[]
            {
                new ElementConverter.ConsumedElement(SimHashes.Water.CreateTag(), 2.0f),
                new ElementConverter.ConsumedElement(SimHashes.RefinedCarbon.CreateTag(), 0.25f),
            };
            refinedCarbonConverter.outputElements = new[]
            {
                new ElementConverter.OutputElement(2.0f, SimHashes.Petroleum, 333.15f, useEntityTemperature: false, storeOutput: true),
            };

            Prioritizable.AddRef(go);

            // 重工业深碳黑/钢灰微调
            BuildingTint tint = go.AddOrGet<BuildingTint>();
            tint.tint = new Color32(110, 115, 130, 255);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<LogicOperationalController>();
            go.AddOrGetDef<PoweredActiveController.Def>();
        }
    }
}
