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
    /// - 存储可调：存储容量默认 4000 粒子，侧边栏自由调节（0 ~ 4000 粒子）。
    /// - 穿透可调：内置粒子穿透传导机制，侧边栏自由设定穿透放行阈值（0 ~ 4000 粒子，默认 100 粒子）与穿透模式切换按钮。
    /// - 消耗可调：侧边栏自由微调满载粒子消耗速率（0.01 ~ 10.00 粒子/秒，默认 0.25 粒子/秒）。
    /// - 设置复制：全面支持 Copy Settings（快捷键 C），一键同步所有参数。
    /// - 自动化端口：带自动化输入端（可连智能电池，充满自动停机并直通穿透放行）与粒子储量满信号输出端。
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
            storage.autoStore = false; // 由 RadboltGenerator 自行接管穿透与接收判定
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
    public class RadboltGenerator : Generator, IMultiSliderControl, ISidescreenButtonControl
    {
        public const float DEFAULT_STORAGE_CAPACITY = 4000f; // 默认存储上限 4000 粒子
        public const float DEFAULT_PASSTHROUGH_THRESHOLD = 100f; // 默认穿透阈值 100 粒子
        public const float DEFAULT_CONSUMPTION_RATE = 0.25f; // 默认消耗速率 0.25 粒子/秒

        public enum PassThroughMode
        {
            SmartThreshold = 0, // 智能阈值穿透：存量 >= 穿透阈值时自动穿透放行给后方机器（默认）
            AlwaysPass = 1,     // 全额直通穿透：本机不截留，100% 粒子全部放行给后方机器
            NeverPass = 2       // 完全拦截：关闭提前穿透，优先充饱本机（直到上限 4000）后才放行
        }

        [MyCmpReq]
        private HighEnergyParticleStorage _particleStorage;

        [MyCmpReq]
        private KBatchedAnimController _kbac;

        [Serialize]
        [SerializeField]
        public float targetStorageCapacity = DEFAULT_STORAGE_CAPACITY; // 存储上限（默认 4000 粒子，可调 0 ~ 4000）

        [Serialize]
        [SerializeField]
        public float passThroughThreshold = DEFAULT_PASSTHROUGH_THRESHOLD; // 穿透阈值（默认 100 粒子，可调 0 ~ 4000）

        [Serialize]
        [SerializeField]
        public float particlesPerSecond = DEFAULT_CONSUMPTION_RATE; // 消耗速率（默认 0.25 粒子/秒，可调 0.01 ~ 10.00）

        [Serialize]
        [SerializeField]
        public PassThroughMode passThroughMode = PassThroughMode.SmartThreshold; // 穿透模式

        private bool _wasActive;
        private ISliderControl[] _sliderControls;

        private static readonly EventSystem.IntraObjectHandler<RadboltGenerator> OnCopySettingsDelegate =
            new EventSystem.IntraObjectHandler<RadboltGenerator>((component, data) => component.OnCopySettings(data));

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Subscribe((int)GameHashes.CopySettings, OnCopySettingsDelegate);

            _sliderControls = new ISliderControl[3]
            {
                new CapacityController(this),
                new PassThroughThresholdController(this),
                new ConsumptionRateController(this)
            };

            BindParticlePort();
        }

        private void BindParticlePort()
        {
            HighEnergyParticlePort port = GetComponent<HighEnergyParticlePort>();
            if (port != null)
            {
                port.onParticleCapture = OnCustomParticleCapture;
                port.onParticleCaptureAllowed = OnCustomParticleCaptureAllowed;
            }
        }

        private void OnCopySettings(object data)
        {
            GameObject source = data as GameObject;
            if (source == null) return;

            RadboltGenerator sourceGen = source.GetComponent<RadboltGenerator>();
            if (sourceGen == null) return;

            targetStorageCapacity = sourceGen.targetStorageCapacity;
            passThroughThreshold = sourceGen.passThroughThreshold;
            particlesPerSecond = sourceGen.particlesPerSecond;
            passThroughMode = sourceGen.passThroughMode;

            if (_particleStorage != null)
            {
                _particleStorage.capacity = targetStorageCapacity;
            }

            ModLog.Info($"[辐射粒子发电机] 已从 {source.name} 复制全部设置：存储上限={targetStorageCapacity:0}, 穿透阈值={passThroughThreshold:0}, 消耗速率={particlesPerSecond:0.00}/s, 穿透模式={passThroughMode}");
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            _particleStorage = GetComponent<HighEnergyParticleStorage>();
            _kbac = GetComponent<KBatchedAnimController>();

            // 升级与容错保护
            if (targetStorageCapacity <= 0f && targetStorageCapacity != 0f)
            {
                targetStorageCapacity = DEFAULT_STORAGE_CAPACITY;
            }

            if (particlesPerSecond <= 0f)
            {
                particlesPerSecond = DEFAULT_CONSUMPTION_RATE;
            }

            if (_particleStorage != null)
            {
                _particleStorage.capacity = targetStorageCapacity;
            }

            BindParticlePort();
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

        // ================= 粒子穿透与捕获逻辑 =================

        private bool OnCustomParticleCaptureAllowed(HighEnergyParticle particle)
        {
            if (_particleStorage == null) return false;

            // 自动化红信号停机时，自动穿透放行给后方机器
            if (operational != null && !operational.IsOperational)
            {
                return false;
            }

            // 全额直通穿透：本机绝不拦截，100% 穿透放行
            if (passThroughMode == PassThroughMode.AlwaysPass)
            {
                return false;
            }

            // 智能阈值穿透：存量达到设定阈值后，后续粒子穿透放行
            if (passThroughMode == PassThroughMode.SmartThreshold)
            {
                float limit = Mathf.Min(passThroughThreshold, targetStorageCapacity);
                return _particleStorage.Particles < limit;
            }

            // 完全拦截模式：存满目标存储上限之前全部吸收
            return _particleStorage.Particles < targetStorageCapacity;
        }

        private void OnCustomParticleCapture(HighEnergyParticle particle)
        {
            if (_particleStorage == null) return;

            float effectiveLimit;
            if (passThroughMode == PassThroughMode.SmartThreshold)
            {
                effectiveLimit = Mathf.Min(passThroughThreshold, targetStorageCapacity);
            }
            else if (passThroughMode == PassThroughMode.AlwaysPass)
            {
                effectiveLimit = 0f;
            }
            else
            {
                effectiveLimit = targetStorageCapacity;
            }

            float needed = Mathf.Max(effectiveLimit - _particleStorage.Particles, 0f);
            float toStore = Mathf.Min(particle.payload, needed);

            if (toStore > 0f)
            {
                _particleStorage.Store(toStore);
                particle.payload -= toStore;
            }

            // 如果光球有剩余粒子，触发 Uncapture 实现无损穿透传导至后方同轴发电机
            if (particle.payload > 0f)
            {
                HighEnergyParticlePort port = GetComponent<HighEnergyParticlePort>();
                if (port != null)
                {
                    port.Uncapture(particle);
                }
            }
        }

        // ================= IMultiSliderControl (三组参数控制滑块) =================

        string IMultiSliderControl.SidescreenTitleKey => "STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.PANEL_TITLE";

        bool IMultiSliderControl.SidescreenEnabled() => true;

        ISliderControl[] IMultiSliderControl.sliderControls => _sliderControls;

        // 1. 存储上限控制器（0 ~ 4000 粒子，默认 4000）
        protected class CapacityController : ISingleSliderControl, ISliderControl
        {
            private readonly RadboltGenerator _target;

            public CapacityController(RadboltGenerator target) => _target = target;

            public string SliderTitleKey => "STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.CAPACITY_TITLE";
            public string SliderUnits => " 粒子";
            public int SliderDecimalPlaces(int index) => 0;
            public float GetSliderMin(int index) => 0f;
            public float GetSliderMax(int index) => 4000f;
            public float GetSliderValue(int index) => _target.targetStorageCapacity;
            public void SetSliderValue(float val, int index)
            {
                _target.targetStorageCapacity = Mathf.Round(Mathf.Clamp(val, 0f, 4000f));
                if (_target._particleStorage != null)
                {
                    _target._particleStorage.capacity = _target.targetStorageCapacity;
                }
            }
            public string GetSliderTooltipKey(int index) => "STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.CAPACITY_TOOLTIP";
            public string GetSliderTooltip(int index) =>
                $"粒子存储上限：{_target.targetStorageCapacity:0} 粒子（默认 4000 粒子）。\n" +
                $"发电机允许储存的最大辐射粒子总量。可随时按需自由调节。";
        }

        // 2. 穿透放行阈值控制器（0 ~ 4000 粒子，默认 100）
        protected class PassThroughThresholdController : ISingleSliderControl, ISliderControl
        {
            private readonly RadboltGenerator _target;

            public PassThroughThresholdController(RadboltGenerator target) => _target = target;

            public string SliderTitleKey => "STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.PASSTHROUGH_TITLE";
            public string SliderUnits => " 粒子";
            public int SliderDecimalPlaces(int index) => 0;
            public float GetSliderMin(int index) => 0f;
            public float GetSliderMax(int index) => 4000f;
            public float GetSliderValue(int index) => _target.passThroughThreshold;
            public void SetSliderValue(float val, int index)
            {
                _target.passThroughThreshold = Mathf.Round(Mathf.Clamp(val, 0f, 4000f));
            }
            public string GetSliderTooltipKey(int index) => "STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.PASSTHROUGH_TOOLTIP";
            public string GetSliderTooltip(int index) =>
                $"穿透放行阈值：{_target.passThroughThreshold:0} 粒子（默认 100 粒子）。\n" +
                $"在智能穿透模式下，发电机存量达到此数值后，后续射入的辐射粒子将自动无损穿透传导至后方发电机。\n" +
                $"两台发电机串联时，设为 100 即可让第一台存够 100 粒子后将后续粒子全部放行给第二台！";
        }

        // 3. 消耗速率控制器（0.01 ~ 10.00 粒子/秒，默认 0.25）
        protected class ConsumptionRateController : ISingleSliderControl, ISliderControl
        {
            private readonly RadboltGenerator _target;

            public ConsumptionRateController(RadboltGenerator target) => _target = target;

            public string SliderTitleKey => "STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.CONSUMPTION_RATE_TITLE";
            public string SliderUnits => " 粒子/秒";
            public int SliderDecimalPlaces(int index) => 2;
            public float GetSliderMin(int index) => 0.01f;
            public float GetSliderMax(int index) => 10.0f;
            public float GetSliderValue(int index) => _target.particlesPerSecond;
            public void SetSliderValue(float val, int index)
            {
                _target.particlesPerSecond = (float)System.Math.Round(Mathf.Clamp(val, 0.01f, 10.0f), 2);
            }
            public string GetSliderTooltipKey(int index) => "STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.CONSUMPTION_RATE_TOOLTIP";
            public string GetSliderTooltip(int index) =>
                $"辐射粒子消耗速率：{_target.particlesPerSecond:0.00} 粒子/秒（每周期 {_target.particlesPerSecond * 600f:0} 粒子）。\n" +
                $"满载发电 20,000 W。可在 0.01 ~ 10.00 粒子/秒之间自由微调。";
        }

        // ================= ISidescreenButtonControl (穿透模式切换按钮) =================

        public string SidescreenTitle => "穿透模式控制";

        public int HorizontalGroupID() => -1;

        public string SidescreenButtonText
        {
            get
            {
                switch (passThroughMode)
                {
                    case PassThroughMode.SmartThreshold:
                        return "穿透模式：智能阈值放行 [点击切换]";
                    case PassThroughMode.AlwaysPass:
                        return "穿透模式：全额直通穿透 [点击切换]";
                    case PassThroughMode.NeverPass:
                        return "穿透模式：完全自身拦截 [点击切换]";
                    default:
                        return "穿透模式 [点击切换]";
                }
            }
        }

        public string SidescreenButtonTooltip
        {
            get
            {
                switch (passThroughMode)
                {
                    case PassThroughMode.SmartThreshold:
                        return $"【当前：智能阈值放行】\n机器存量达到设定阈值（{passThroughThreshold:0} 粒子）后，后续射入的粒子光球将自动 100% 穿透射给后方发电机。\n点击切换模式。";
                    case PassThroughMode.AlwaysPass:
                        return "【当前：全额直通穿透】\n本机不拦截任何粒子，所有射入的光球 100% 直通放行给下游建筑。\n点击切换模式。";
                    case PassThroughMode.NeverPass:
                        return $"【当前：完全自身拦截】\n关闭提前穿透，优先填满本机自身存储上限（最高 {targetStorageCapacity:0} 粒子）后才放行。\n点击切换模式。";
                    default:
                        return "点击切换穿透模式。";
                }
            }
        }

        public bool SidescreenEnabled() => true;

        public bool SidescreenButtonInteractable() => true;

        public int ButtonSideScreenSortOrder() => 20;

        public void SetButtonTextOverride(ButtonMenuTextOverride textOverride) { }

        public void OnSidescreenButtonPressed()
        {
            switch (passThroughMode)
            {
                case PassThroughMode.SmartThreshold:
                    passThroughMode = PassThroughMode.AlwaysPass;
                    break;
                case PassThroughMode.AlwaysPass:
                    passThroughMode = PassThroughMode.NeverPass;
                    break;
                case PassThroughMode.NeverPass:
                    passThroughMode = PassThroughMode.SmartThreshold;
                    break;
                default:
                    passThroughMode = PassThroughMode.SmartThreshold;
                    break;
            }
            ModLog.Info($"[辐射粒子发电机] 穿透模式已切换为: {passThroughMode}");
        }
    }
}
