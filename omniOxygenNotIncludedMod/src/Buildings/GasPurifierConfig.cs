using TUNING;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 「更多净化器」——一组吸收特定气体、产出固体（少数产出液体）副产物的净化建筑。
    /// 迁移自 workshop mod「More Purifiers」，按原理重做：不用它的自定义美术，
    /// 复用香草空气过滤器的动画（co2filter_kanim）。
    ///
    /// 原理：ElementConsumer 从四周抽取目标气体存进 Storage → ElementConverter 把气体
    /// 转成副产物 → 固体副产物由 ElementDropper 累积到 100kg 时掉落；液体副产物直接排出。
    /// </summary>
    public abstract class GasPurifierConfig : IBuildingConfig
    {
        protected abstract string Id { get; }
        protected abstract SimHashes InputGas { get; }
        protected abstract SimHashes Output { get; }
        protected abstract float InputRateKgPerSec { get; }
        protected abstract float OutputRateKgPerSec { get; }

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                id: Id,
                width: 1,
                height: 1,
                anim: "co2filter_kanim",
                hitpoints: 30,
                construction_time: 30f,
                construction_mass: TUNING.BUILDINGS.CONSTRUCTION_MASS_KG.TIER2,
                construction_materials: MATERIALS.REFINED_METALS,
                melting_point: 1600f,
                build_location_rule: BuildLocationRule.Anywhere,   // 可悬空建造
                decor: TUNING.BUILDINGS.DECOR.PENALTY.TIER1,
                noise: NOISE_POLLUTION.NOISY.TIER1);

            def.Overheatable = false;
            def.RequiresPowerInput = true;
            def.EnergyConsumptionWhenActive = 5f;
            def.ExhaustKilowattsWhenActive = 0.5f;
            def.SelfHeatKilowattsWhenActive = 1f;
            def.ViewMode = OverlayModes.Oxygen.ID;
            def.AudioCategory = "Metal";
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            go.AddOrGet<LoopingSounds>();
            Prioritizable.AddRef(go);

            Storage storage = BuildingTemplates.CreateDefaultStorage(go);
            storage.capacityKg = 1000f;
            storage.showInUI = true;

            Element outElement = ElementLoader.FindElementByHash(Output);
            Element inElement = ElementLoader.FindElementByHash(InputGas);
            bool outputIsSolid = outElement != null && outElement.IsSolid;

            ElementConsumer consumer = go.AddOrGet<ElementConsumer>();
            consumer.elementToConsume = InputGas;
            consumer.consumptionRate = InputRateKgPerSec;
            consumer.capacityKG = InputRateKgPerSec * 2f;
            consumer.consumptionRadius = 4;
            consumer.storeOnConsume = true;
            consumer.showInStatusPanel = true;
            consumer.sampleCellOffset = new Vector3(0f, 1f, 0f);
            consumer.isRequired = false;
            consumer.ignoreActiveChanged = true;

            ElementConverter converter = go.AddOrGet<ElementConverter>();
            converter.consumedElements = new[]
            {
                new ElementConverter.ConsumedElement(inElement != null ? inElement.tag : new Tag(InputGas.ToString()), InputRateKgPerSec),
            };
            converter.outputElements = new[]
            {
                new ElementConverter.OutputElement(OutputRateKgPerSec, Output, 0f, useEntityTemperature: false, storeOutput: outputIsSolid),
            };

            if (outputIsSolid)
            {
                ElementDropper dropper = go.AddComponent<ElementDropper>();
                dropper.emitTag = outElement.tag;
                dropper.emitMass = 100f;
                dropper.emitOffset = new Vector3(0f, 0.5f, 0f);
            }

            go.AddOrGet<KBatchedAnimController>().randomiseLoopedOffset = true;

            // 用所吸收气体的颜色给净化器上色（生成后由 BuildingTint 在 OnSpawn 时应用）
            if (inElement != null && inElement.substance != null)
            {
                Color32 c = inElement.substance.colour;
                c.a = 255;
                go.AddOrGet<BuildingTint>().tint = c;
            }
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGetDef<ActiveController.Def>();
        }
    }

    public class Co2PurifierConfig : GasPurifierConfig
    {
        public const string ID = "OmniMod_Co2Purifier";
        protected override string Id => ID;
        protected override SimHashes InputGas => SimHashes.CarbonDioxide;
        protected override SimHashes Output => SimHashes.Carbon;          // 煤炭
        protected override float InputRateKgPerSec => 0.5f;
        protected override float OutputRateKgPerSec => 0.3f;
    }

    public class ChlorinePurifierConfig : GasPurifierConfig
    {
        public const string ID = "OmniMod_ChlorinePurifier";
        protected override string Id => ID;
        protected override SimHashes InputGas => SimHashes.ChlorineGas;
        protected override SimHashes Output => SimHashes.BleachStone;      // 漂白石
        protected override float InputRateKgPerSec => 0.3f;
        protected override float OutputRateKgPerSec => 0.3f;
    }

    public class OxygenPurifierConfig : GasPurifierConfig
    {
        public const string ID = "OmniMod_OxygenPurifier";
        protected override string Id => ID;
        protected override SimHashes InputGas => SimHashes.Oxygen;
        protected override SimHashes Output => SimHashes.OxyRock;          // 氧石
        protected override float InputRateKgPerSec => 0.5f;
        protected override float OutputRateKgPerSec => 0.5f;
    }

    public class NaturalGasPurifierConfig : GasPurifierConfig
    {
        public const string ID = "OmniMod_NaturalGasPurifier";
        protected override string Id => ID;
        protected override SimHashes InputGas => SimHashes.Methane;
        protected override SimHashes Output => SimHashes.Fertilizer;       // 肥料
        protected override float InputRateKgPerSec => 0.3f;
        protected override float OutputRateKgPerSec => 0.3f;
    }

    public class HydrogenPurifierConfig : GasPurifierConfig
    {
        public const string ID = "OmniMod_HydrogenPurifier";
        protected override string Id => ID;
        protected override SimHashes InputGas => SimHashes.Hydrogen;
        protected override SimHashes Output => SimHashes.Water;           // 水（液体，直接排出）
        protected override float InputRateKgPerSec => 0.1f;
        protected override float OutputRateKgPerSec => 0.3f;
    }

    public class PollutedOxygenPurifierConfig : GasPurifierConfig
    {
        public const string ID = "OmniMod_PollutedOxygenPurifier";
        protected override string Id => ID;
        protected override SimHashes InputGas => SimHashes.ContaminatedOxygen;
        protected override SimHashes Output => SimHashes.SlimeMold;       // 菌泥
        protected override float InputRateKgPerSec => 0.5f;
        protected override float OutputRateKgPerSec => 0.3f;
    }
}
