using TUNING;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 全能冷冻造雪制冰机（Auto Cryo Snow & Ice Maker）：
    /// 复用冷柜模型（带冰晶浅蓝纯净微调），直接管道通入纯水（2.0 kg/s），
    /// 无需人工值守，全自动急速恒温冷冻为低温疏松雪花（Snow）或坚硬冰块（Ice），
    /// 并在地面直接生成，支持自选结晶温度（-100°C ~ -1°C）与复制设置。
    /// </summary>
    public class AutoIceMakerConfig : IBuildingConfig
    {
        public const string ID = "OmniMod_AutoIceMaker";

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                id: ID,
                width: 2,
                height: 3,
                anim: "freezerator_kanim",
                hitpoints: 30,
                construction_time: 30f,
                construction_mass: BUILDINGS.CONSTRUCTION_MASS_KG.TIER3,
                construction_materials: MATERIALS.REFINED_METALS,
                melting_point: 800f,
                build_location_rule: BuildLocationRule.OnFloor,
                noise: NOISE_POLLUTION.NOISY.TIER1,
                decor: BUILDINGS.DECOR.PENALTY.TIER1);

            def.RequiresPowerInput = true;
            def.EnergyConsumptionWhenActive = 240f;
            def.ExhaustKilowattsWhenActive = 0f;
            def.SelfHeatKilowattsWhenActive = 0.5f;

            def.PowerInputOffset = new CellOffset(1, 0);

            def.InputConduitType = ConduitType.Liquid;
            def.UtilityInputOffset = new CellOffset(0, 0);

            def.ViewMode = OverlayModes.LiquidConduits.ID;
            def.AudioCategory = "Metal";
            def.AudioSize = "medium";

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
            consumer.wrongElementResult = ConduitConsumer.WrongElementResult.Store;
            consumer.forceAlwaysSatisfied = true;

            go.AddOrGet<CopyBuildingSettings>();
            go.AddOrGet<AutoIceMaker>();

            Prioritizable.AddRef(go);

            // 冰晶浅蓝冷色调微调
            BuildingTint tint = go.AddOrGet<BuildingTint>();
            tint.tint = new Color32(175, 230, 255, 255);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<LogicOperationalController>();
            go.AddOrGetDef<PoweredActiveController.Def>();
        }
    }
}
