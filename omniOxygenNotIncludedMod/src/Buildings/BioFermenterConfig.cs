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

            def.PowerInputOffset = new CellOffset(1, 0);

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

            // 固体进料调度（默认纯水模式下 BioFermenter 会自动 Pause(true) 该组件）
            ManualDeliveryKG delivery = go.AddComponent<ManualDeliveryKG>();
            delivery.SetStorage(storage);
            delivery.RequestedItemTag = BioFermenter.SucroseTag;
            delivery.capacity = 500f;
            delivery.refillMass = 100f;
            delivery.choreTypeIDHash = Db.Get().ChoreTypes.FetchCritical.IdHash;

            // 统一转换器：默认初始化为纯水直接发酵（水 2.0 kg/s -> 乙醇 2.0 kg/s + CO2 0.5 kg/s），无需任何固体原料
            // 更多配方（蔗糖催化/汗甜玉米/木材）可通过侧边栏随时热切换
            ElementConverter converter = go.AddComponent<ElementConverter>();
            converter.consumedElements = BioFermenter.ConsumedWaterOnly;
            converter.outputElements = BioFermenter.OutputWaterOnly;

            // 挂载主控逻辑组件（侧边栏配方切换、进料调度管理）与复制设置支持
            go.AddOrGet<CopyBuildingSettings>();
            go.AddOrGet<BioFermenter>();

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
