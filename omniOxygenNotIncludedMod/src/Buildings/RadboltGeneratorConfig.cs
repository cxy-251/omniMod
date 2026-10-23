using System.Collections.Generic;
using KSerialization;
using OmniMod.Core.Logging;
using STRINGS;
using TUNING;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 辐射粒子发电机（Radbolt Generator）：
    /// 复用材料研究终端（辐射研究站 / material_research_centre_kanim）的外观与粒子接口，
    /// 转换为高功率电网发电机。
    ///
    /// - 额定功率：20kW（20,000W，支持供电调谐提升至 30,000W）。
    /// - 存储控制：侧边栏支持自由设定粒子存储上限（0 ~ 1000 粒子，默认 100 粒子）。
    ///             存满后后续粒子自动无损穿透传导至下游发电机。
    /// - 粒子消耗：0.25 辐射粒子 / 秒（1 粒子 = 80,000 焦耳清洁能源），平稳运行。
    /// - 自动化端口：带自动化输入端（可连智能电池）与粒子储量满信号输出端。
    /// - 设置复制：支持 Copy Settings（快捷键 C），一键同步全厂存储上限设置。
    /// </summary>
    public class RadboltGeneratorConfig : IBuildingConfig
    {
        public const string ID = "OmniMod_RadboltGenerator";
        public const string PORT_ID = "HEP_STORAGE";

        public override string[] GetRequiredDlcIds()
        {
            return DlcManager.EXPANSION1;
        }

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                id: ID,
                width: 5,
                height: 3,
                anim: "material_research_centre_kanim",
                hitpoints: 100,
                construction_time: 30f,
                construction_mass: TUNING.BUILDINGS.CONSTRUCTION_MASS_KG.TIER4,
                construction_materials: MATERIALS.REFINED_METALS,
                melting_point: 1600f,
                build_location_rule: BuildLocationRule.OnFloor,
                noise: NOISE_POLLUTION.NOISY.TIER1,
                decor: TUNING.BUILDINGS.DECOR.NONE);

            def.GeneratorWattageRating = 20000f;
            def.GeneratorBaseCapacity = 20000f;
            def.RequiresPowerOutput = true;
            def.PowerOutputOffset = new CellOffset(1, 0);
            def.ExhaustKilowattsWhenActive = 1f;
            def.SelfHeatKilowattsWhenActive = 4f;
            def.UseHighEnergyParticleInputPort = true;
            def.HighEnergyParticleInputOffset = new CellOffset(-2, 1);
            def.ViewMode = OverlayModes.Power.ID;
            def.AudioCategory = "Metal";
            def.AudioSize = "large";

            def.LogicInputPorts = LogicOperationalController.CreateSingleInputPortList(new CellOffset(-1, 0));
            def.LogicOutputPorts = new List<LogicPorts.Port>
            {
                LogicPorts.Port.OutputPort(
                    PORT_ID,
                    new CellOffset(2, 2),
                    STRINGS.BUILDINGS.PREFABS.HEPENGINE.LOGIC_PORT_STORAGE,
                    STRINGS.BUILDINGS.PREFABS.HEPENGINE.LOGIC_PORT_STORAGE_ACTIVE,
                    STRINGS.BUILDINGS.PREFABS.HEPENGINE.LOGIC_PORT_STORAGE_INACTIVE)
            };

            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.RadiationIDs, ID);
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.IndustrialMachinery);
            go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.PowerBuilding);
            go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.GeneratorType);
            go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.HeavyDutyGeneratorType);

            Prioritizable.AddRef(go);

            HighEnergyParticleStorage storage = go.AddOrGet<HighEnergyParticleStorage>();
            storage.autoStore = true;
            storage.capacity = RadboltGenerator.DEFAULT_STORAGE_CAPACITY;
            storage.PORT_ID = PORT_ID;
            storage.showCapacityStatusItem = true;

            go.AddOrGet<CopyBuildingSettings>();

            RadboltGenerator generator = go.AddOrGet<RadboltGenerator>();
            generator.powerDistributionOrder = 8;

            Tinkerable.MakePowerTinkerable(go);
            go.AddOrGetDef<PoweredActiveController.Def>();

            // 给建筑增加科技感淡青色微调，以在视觉上区别于原版材料研究终端
            BuildingTint tint = go.AddOrGet<BuildingTint>();
            tint.tint = new Color32(140, 230, 255, 255);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<LogicOperationalController>();
        }
    }

    [SerializationConfig(MemberSerialization.OptIn)]
    public class RadboltGenerator : Generator, ISingleSliderControl
    {
        public const float DEFAULT_STORAGE_CAPACITY = 100f; // 默认 100 粒子容量

        [MyCmpReq]
        private HighEnergyParticleStorage _particleStorage;

        [MyCmpReq]
        private KBatchedAnimController _kbac;

        [Serialize]
        [SerializeField]
        public float targetStorageCapacity = DEFAULT_STORAGE_CAPACITY; // 侧边栏可调粒子存储上限（默认 100 粒子，范围 0 ~ 1000 粒子）

        [Serialize]
        [SerializeField]
        public float particlesPerSecond = 0.25f; // 0.25 粒子/秒 = 20kW（20,000W）满载发电

        private bool _wasActive;

        private static readonly EventSystem.IntraObjectHandler<RadboltGenerator> OnCopySettingsDelegate =
            new EventSystem.IntraObjectHandler<RadboltGenerator>((component, data) => component.OnCopySettings(data));

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Subscribe((int)GameHashes.CopySettings, OnCopySettingsDelegate);
        }

        private void OnCopySettings(object data)
        {
            GameObject source = data as GameObject;
            if (source == null) return;

            RadboltGenerator sourceGen = source.GetComponent<RadboltGenerator>();
            if (sourceGen == null) return;

            targetStorageCapacity = sourceGen.targetStorageCapacity;
            if (_particleStorage != null)
            {
                _particleStorage.capacity = targetStorageCapacity;
            }
            ModLog.Info($"[辐射粒子发电机] 已从 {source.name} 复制粒子存储上限设置：{targetStorageCapacity:0} 粒子");
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            _particleStorage = GetComponent<HighEnergyParticleStorage>();
            _kbac = GetComponent<KBatchedAnimController>();

            // 升级旧存档数据：原旧版本若存量上限异常（如旧版的 4000），平滑升级为默认 100 粒子
            if (targetStorageCapacity > 1000f)
            {
                targetStorageCapacity = DEFAULT_STORAGE_CAPACITY;
            }

            if (particlesPerSecond >= 9.9f || particlesPerSecond <= 0f)
            {
                particlesPerSecond = 0.25f;
            }

            if (_particleStorage != null)
            {
                _particleStorage.capacity = targetStorageCapacity;
            }
        }

        public override void EnergySim200ms(float dt)
        {
            base.EnergySim200ms(dt);

            if (_particleStorage != null && _particleStorage.capacity != targetStorageCapacity)
            {
                _particleStorage.capacity = targetStorageCapacity;
            }

            operational.SetFlag(Generator.wireConnectedFlag, CircuitID != ushort.MaxValue);

            if (!operational.IsOperational)
            {
                SetActiveState(false);
                return;
            }

            if (_particleStorage == null || _particleStorage.IsEmpty() || _particleStorage.Particles <= 0f)
            {
                SetActiveState(false);
                selectable.SetStatusItem(Db.Get().StatusItemCategories.Power, Db.Get().BuildingStatusItems.GeneratorOffline, this);
                selectable.ToggleStatusItem(Db.Get().BuildingStatusItems.WaitingForHighEnergyParticles, true);
                return;
            }

            selectable.ToggleStatusItem(Db.Get().BuildingStatusItems.WaitingForHighEnergyParticles, false);

            float toConsume = Mathf.Min(_particleStorage.Particles, particlesPerSecond * dt);
            if (toConsume > 0f)
            {
                _particleStorage.ConsumeAndGet(toConsume);
                float fraction = toConsume / (particlesPerSecond * dt);
                GenerateJoules(WattageRating * dt * fraction);
                SetActiveState(true);
                selectable.SetStatusItem(Db.Get().StatusItemCategories.Power, Db.Get().BuildingStatusItems.Wattage, this);
            }
            else
            {
                SetActiveState(false);
            }
        }

        private void SetActiveState(bool active)
        {
            if (_wasActive != active)
            {
                _wasActive = active;
                operational.SetActive(active);
                if (active)
                {
                    Machinery.BuildingAnimHelper.PlayWorkingAnim(_kbac);
                }
                else
                {
                    Machinery.BuildingAnimHelper.StopWorkingAnim(_kbac, operational);
                }
            }
        }

        // ================= ISingleSliderControl / ISliderControl =================

        public string SliderTitleKey => "STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.CAPACITY_TITLE";

        public string SliderUnits => " 粒子";

        public int SliderDecimalPlaces(int index) => 0;

        public float GetSliderMin(int index) => 0f;

        public float GetSliderMax(int index) => 1000f;

        public float GetSliderValue(int index) => targetStorageCapacity;

        public void SetSliderValue(float value, int index)
        {
            targetStorageCapacity = Mathf.Round(Mathf.Clamp(value, 0f, 1000f));
            if (_particleStorage != null)
            {
                _particleStorage.capacity = targetStorageCapacity;
            }
        }

        public string GetSliderTooltipKey(int index) => "STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.CAPACITY_TOOLTIP";

        public string GetSliderTooltip(int index) =>
            $"辐射粒子存储上限：{targetStorageCapacity:0} 粒子。\n" +
            $"当发电机储存的粒子达到此上限后将停止接收，后续射入的辐射粒子将自动无损穿透传导至后方同轴发电机。\n" +
            $"支持按 C 键复制设置到其他发电机。";
    }
}
