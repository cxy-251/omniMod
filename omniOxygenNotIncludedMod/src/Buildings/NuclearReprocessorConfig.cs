using System.Collections.Generic;
using TUNING;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 核废料再处理机（Nuclear Waste Reprocessor）：
    /// 利用乏燃料化学分离与放射性同位素重富集工艺，将反应堆排放的核废料（液态核废料 / 固态核废料块）
    /// 高效提取还原为纯净的浓缩铀，实现核能燃料的完全闭式循环。
    ///
    /// - 液体输入：液态核废料（NuclearWaste）5.0 kg/s（接排废管道直接连续通入）
    /// - 固体进料：固态核废料（SolidNuclearWaste）5.0 kg/s（支持复制人/机械臂按需搬运装填）
    /// - 固体产出：浓缩铀（EnrichedUranium）0.5 kg/s（10:1 高效提纯）
    /// - 产物处理：默认满 20kg 自动弹出至地面（由聚合箱自动吸入虚拟池并供机械臂隔空取物），亦支持保留在机内供贴身机械臂直取
    /// - 额定功率：480W
    /// </summary>
    public class NuclearReprocessorConfig : IBuildingConfig
    {
        public const string ID = "OmniMod_NuclearReprocessor";

        public override string[] GetRequiredDlcIds()
        {
            return DlcManager.EXPANSION1;
        }

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                id: ID,
                width: 3,
                height: 4,
                anim: "enrichmentCentrifuge_kanim",
                hitpoints: 100,
                construction_time: 40f,
                construction_mass: BUILDINGS.CONSTRUCTION_MASS_KG.TIER4,
                construction_materials: MATERIALS.REFINED_METALS,
                melting_point: 2400f,
                build_location_rule: BuildLocationRule.OnFloor,
                noise: NOISE_POLLUTION.NOISY.TIER3,
                decor: BUILDINGS.DECOR.PENALTY.TIER1);

            def.RequiresPowerInput = true;
            def.EnergyConsumptionWhenActive = 480f;
            def.ExhaustKilowattsWhenActive = 0.5f;
            def.SelfHeatKilowattsWhenActive = 2f;
            def.PowerInputOffset = new CellOffset(2, 0);

            def.InputConduitType = ConduitType.Liquid;
            def.UtilityInputOffset = new CellOffset(0, 0);

            def.ViewMode = OverlayModes.LiquidConduits.ID;
            def.AudioCategory = "HollowMetal";
            def.AudioSize = "large";

            def.Floodable = false;
            def.Overheatable = false;
            def.Invincible = true;

            def.LogicInputPorts = LogicOperationalController.CreateSingleInputPortList(new CellOffset(1, 0));

            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.IndustrialMachinery);
            go.GetComponent<KPrefabID>().AddTag(GameTags.CorrosionProof);

            Storage storage = BuildingTemplates.CreateDefaultStorage(go);
            storage.capacityKg = 2000f;
            storage.showInUI = true;
            storage.allowItemRemoval = true;
            storage.SetDefaultStoredItemModifiers(Storage.StandardSealedStorage);

            // 液态核废料管道接入 (最大吞吐 10 kg/s)
            ConduitConsumer consumer = go.AddOrGet<ConduitConsumer>();
            consumer.conduitType = ConduitType.Liquid;
            consumer.consumptionRate = 10f;
            consumer.capacityKG = 50f;
            consumer.capacityTag = SimHashes.NuclearWaste.CreateTag();
            consumer.wrongElementResult = ConduitConsumer.WrongElementResult.Store;
            consumer.forceAlwaysSatisfied = true;
            consumer.storage = storage;

            // 固态核废料装载接入 (支持小人/机械手搬运装载)
            ManualDeliveryKG delivery = go.AddComponent<ManualDeliveryKG>();
            delivery.SetStorage(storage);
            delivery.RequestedItemTag = SimHashes.SolidNuclearWaste.CreateTag();
            delivery.capacity = 500f;
            delivery.refillMass = 100f;
            delivery.choreTypeIDHash = Db.Get().ChoreTypes.MachineFetch.IdHash;
            delivery.allowPause = true;

            // 基础转换器：5 kg/s 核废料 -> 0.5 kg/s 浓缩铀（支持液态与固态双通道，默认液态激活）
            ElementConverter converter = go.AddComponent<ElementConverter>();
            converter.consumedElements = new[]
            {
                new ElementConverter.ConsumedElement(NuclearReprocessor.LiquidWasteTag, NuclearReprocessor.WasteConsumptionPerSecond, true),
                new ElementConverter.ConsumedElement(NuclearReprocessor.SolidWasteTag, NuclearReprocessor.WasteConsumptionPerSecond, false),
            };
            converter.outputElements = NuclearReprocessor.OutputEnrichedUranium;

            // 运转微量辐射（铅套屏蔽下维持 120 rads）
            RadiationEmitter emitter = go.AddComponent<RadiationEmitter>();
            emitter.emitType = RadiationEmitter.RadiationEmitterType.Constant;
            emitter.emitRadiusX = 3;
            emitter.emitRadiusY = 3;
            emitter.emitRads = 120f;
            emitter.emissionOffset = new Vector3(1f, 1.5f, 0f);

            // 主控逻辑与出料管理组件
            go.AddOrGet<CopyBuildingSettings>();
            go.AddOrGet<NuclearReprocessor>();
            go.AddOrGet<NuclearReprocessorEjector>();

            Prioritizable.AddRef(go);

            // 专属高科技辐射翡翠荧光涂装
            BuildingTint tint = go.AddOrGet<BuildingTint>();
            tint.tint = new Color32(110, 255, 175, 255);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<LogicOperationalController>();
            go.AddOrGetDef<PoweredActiveController.Def>();
        }
    }
}
