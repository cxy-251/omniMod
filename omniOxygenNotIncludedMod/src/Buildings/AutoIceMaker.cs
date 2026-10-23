using KSerialization;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 全能冷冻造雪机主控逻辑：
    /// - 进水管道直接通入纯水（2.0 kg/s）；
    /// - 自动免人工将纯水急速冷冻为低温疏松雪花（Snow）或坚硬冰块（Ice）；
    /// - 侧边栏按钮切换产物形态（雪花 / 冰块）；
    /// - 侧边栏滑条自由调节产物结晶温度（-100°C ~ -1°C）；
    /// - 全面支持【复制设置】（Copy Settings）。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class AutoIceMaker : KMonoBehaviour, ISaveLoadable, ISidescreenButtonControl, ISingleSliderControl
    {
        public enum OutputMode
        {
            Snow = 0, // 疏松雪花（默认：直接供给糖心树等深冷作物施肥）
            Ice = 1   // 致密冰块（适合工业制冷、降温与固态储水）
        }

        [Serialize]
        public OutputMode SelectedMode = OutputMode.Snow;

        [Serialize]
        public float TargetTemperatureCelsius = -15f;

        private ElementConverter _converter;
        private Storage _storage;

        public static readonly Tag WaterTag = SimHashes.Water.CreateTag();

        public static readonly ElementConverter.ConsumedElement[] ConsumedWater = new[]
        {
            new ElementConverter.ConsumedElement(WaterTag, 2.0f)
        };

        private static readonly EventSystem.IntraObjectHandler<AutoIceMaker> OnCopySettingsDelegate =
            new EventSystem.IntraObjectHandler<AutoIceMaker>((component, data) => component.OnCopySettings(data));

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            _storage = GetComponent<Storage>();
            _converter = GetComponent<ElementConverter>();
            Subscribe((int)GameHashes.CopySettings, OnCopySettingsDelegate);
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            if (_storage == null) _storage = GetComponent<Storage>();
            if (_converter == null) _converter = GetComponent<ElementConverter>();
            ApplyRecipe();
        }

        public void SetMode(OutputMode mode)
        {
            SelectedMode = mode;
            ApplyRecipe();
            ModLog.Info($"[全能冷冻造雪机] 产物形态已切换为: {SelectedMode}");
        }

        public void ApplyRecipe()
        {
            if (_converter == null) return;

            float kelvin = Mathf.Clamp(TargetTemperatureCelsius, -100f, -1f) + 273.15f;
            SimHashes outputElement = SelectedMode == OutputMode.Snow ? SimHashes.Snow : SimHashes.Ice;

            _converter.consumedElements = ConsumedWater;
            _converter.outputElements = new[]
            {
                new ElementConverter.OutputElement(
                    kgPerSecond: 2.0f,
                    element: outputElement,
                    minOutputTemperature: kelvin,
                    useEntityTemperature: false,
                    storeOutput: false,
                    outputElementOffsetx: 1.0f,
                    outputElementOffsety: 0.5f)
            };
        }

        private void OnCopySettings(object data)
        {
            GameObject source = data as GameObject;
            if (source == null) return;

            AutoIceMaker sourceMaker = source.GetComponent<AutoIceMaker>();
            if (sourceMaker == null) return;

            SelectedMode = sourceMaker.SelectedMode;
            TargetTemperatureCelsius = sourceMaker.TargetTemperatureCelsius;
            ApplyRecipe();

            ModLog.Info($"[全能冷冻造雪机] 已从 {source.name} 复制冷冻设置：形态={SelectedMode}, 温度={TargetTemperatureCelsius:0}°C");
        }

        // ================= ISidescreenButtonControl =================

        public string SidescreenTitle => "冷冻产物形态配置";

        public int HorizontalGroupID() => -1;

        public string SidescreenButtonText
        {
            get
            {
                switch (SelectedMode)
                {
                    case OutputMode.Snow:
                        return "冷冻形态：疏松雪花 [点击切换为冰块]";
                    case OutputMode.Ice:
                        return "冷冻形态：致密冰块 [点击切换为雪花]";
                    default:
                        return "冷冻产物形态 [点击切换]";
                }
            }
        }

        public string SidescreenButtonTooltip
        {
            get
            {
                switch (SelectedMode)
                {
                    case OutputMode.Snow:
                        return "【当前模式：疏松雪花（Snow）】\n水以 2.0 kg/s 急速冷冻为指定温度的雪花直接吐出到地面！\n完美契合《冰封星球》糖心树施肥需求，小人或机械臂可直接抓取。\n点击切换为【致密冰块】。";
                    case OutputMode.Ice:
                        return "【当前模式：致密冰块（Ice）】\n水以 2.0 kg/s 急速冷冻为指定温度的致密冰块直接吐出到地面！\n适合工业制冷、降温工程或冰块储备。\n点击切换为【疏松雪花】。";
                    default:
                        return "点击切换冷冻产物形态。";
                }
            }
        }

        public bool SidescreenEnabled() => true;

        public bool SidescreenButtonInteractable() => true;

        public int ButtonSideScreenSortOrder() => 20;

        public void SetButtonTextOverride(ButtonMenuTextOverride textOverride) { }

        public void OnSidescreenButtonPressed()
        {
            SetMode(SelectedMode == OutputMode.Snow ? OutputMode.Ice : OutputMode.Snow);
        }

        // ================= ISingleSliderControl / ISliderControl =================

        public string SliderTitleKey => "STRINGS.UI.UISIDESCREENS.OMNIMOD_ICEMAKER.TARGET_TEMP_TITLE";

        public string SliderUnits => "°C";

        public int SliderDecimalPlaces(int index) => 0;

        public float GetSliderMin(int index) => -100f;

        public float GetSliderMax(int index) => -1f;

        public float GetSliderValue(int index) => TargetTemperatureCelsius;

        public void SetSliderValue(float value, int index)
        {
            TargetTemperatureCelsius = Mathf.Round(value);
            ApplyRecipe();
        }

        public string GetSliderTooltipKey(int index) => "STRINGS.UI.UISIDESCREENS.OMNIMOD_ICEMAKER.TARGET_TEMP_TOOLTIP";

        public string GetSliderTooltip(int index) => $"目标产物结晶温度：{TargetTemperatureCelsius:0}°C\n急速冷冻水并在地面直接生成该温度的疏松雪花或坚硬冰块。";
    }
}
