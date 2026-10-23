using System;
using KSerialization;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 生物发酵反应罐主控逻辑：
    /// - 默认开启【纯水直接发酵】：仅需通入纯水与供电，即可全自动连续生产乙醇与二氧化碳，无需任何固体原料；
    /// - 支持侧边栏一键切换【纯水发酵】、【蔗糖催化发酵】、【汗甜玉米发酵】、【木材水解发酵】四种模式；
    /// - 自动管理进料（ManualDeliveryKG），避免无物料时卡死建筑。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class BioFermenter : KMonoBehaviour, ISaveLoadable, ISidescreenButtonControl
    {
        public enum FermentMode
        {
            WaterOnly = 0, // 纯水直接发酵（默认：水 2.0 kg/s → 乙醇 2.0 kg/s + CO2 0.5 kg/s）
            Sucrose = 1,   // 蔗糖催化发酵（水 2.0 kg/s + 蔗糖 0.5 kg/s → 乙醇 2.5 kg/s + CO2 0.5 kg/s）
            Sweatcorn = 2, // 汗甜玉米发酵（水 2.0 kg/s + 汗甜玉米 0.5 kg/s → 乙醇 2.2 kg/s + CO2 0.5 kg/s）
            Wood = 3       // 木材生物发酵（水 2.0 kg/s + 木材 0.5 kg/s → 乙醇 2.0 kg/s + 泥土 0.2 kg/s + CO2 0.5 kg/s）
        }

        [Serialize]
        public FermentMode SelectedMode = FermentMode.WaterOnly;

        private Storage _storage;
        private ElementConverter _converter;
        private ManualDeliveryKG _delivery;

        public static readonly Tag WaterTag = SimHashes.Water.CreateTag();
        public static readonly Tag SucroseTag = SimHashes.Sucrose.CreateTag();
        public static readonly Tag SweatcornTag = TagManager.Create("GardenFoodPlantFood");
        public static readonly Tag WoodTag = TagManager.Create("WoodLog");

        public static readonly ElementConverter.ConsumedElement[] ConsumedWaterOnly = new[]
        {
            new ElementConverter.ConsumedElement(WaterTag, 2.0f),
        };

        public static readonly ElementConverter.OutputElement[] OutputWaterOnly = new[]
        {
            new ElementConverter.OutputElement(2.0f, SimHashes.Ethanol, 313.15f, useEntityTemperature: false, storeOutput: true),
            new ElementConverter.OutputElement(0.5f, SimHashes.CarbonDioxide, 313.15f, useEntityTemperature: false, storeOutput: false, outputElementOffsety: 1.5f),
        };

        public static readonly ElementConverter.ConsumedElement[] ConsumedSucrose = new[]
        {
            new ElementConverter.ConsumedElement(WaterTag, 2.0f),
            new ElementConverter.ConsumedElement(SucroseTag, 0.5f),
        };

        public static readonly ElementConverter.OutputElement[] OutputSucrose = new[]
        {
            new ElementConverter.OutputElement(2.5f, SimHashes.Ethanol, 313.15f, useEntityTemperature: false, storeOutput: true),
            new ElementConverter.OutputElement(0.5f, SimHashes.CarbonDioxide, 313.15f, useEntityTemperature: false, storeOutput: false, outputElementOffsety: 1.5f),
        };

        public static readonly ElementConverter.ConsumedElement[] ConsumedSweatcorn = new[]
        {
            new ElementConverter.ConsumedElement(WaterTag, 2.0f),
            new ElementConverter.ConsumedElement(SweatcornTag, 0.5f),
        };

        public static readonly ElementConverter.OutputElement[] OutputSweatcorn = new[]
        {
            new ElementConverter.OutputElement(2.2f, SimHashes.Ethanol, 313.15f, useEntityTemperature: false, storeOutput: true),
            new ElementConverter.OutputElement(0.5f, SimHashes.CarbonDioxide, 313.15f, useEntityTemperature: false, storeOutput: false, outputElementOffsety: 1.5f),
        };

        public static readonly ElementConverter.ConsumedElement[] ConsumedWood = new[]
        {
            new ElementConverter.ConsumedElement(WaterTag, 2.0f),
            new ElementConverter.ConsumedElement(WoodTag, 0.5f),
        };

        public static readonly ElementConverter.OutputElement[] OutputWood = new[]
        {
            new ElementConverter.OutputElement(2.0f, SimHashes.Ethanol, 313.15f, useEntityTemperature: false, storeOutput: true),
            new ElementConverter.OutputElement(0.2f, SimHashes.Dirt, 313.15f, useEntityTemperature: false, storeOutput: true),
            new ElementConverter.OutputElement(0.5f, SimHashes.CarbonDioxide, 313.15f, useEntityTemperature: false, storeOutput: false, outputElementOffsety: 1.5f),
        };

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            _storage = GetComponent<Storage>();
            _converter = GetComponent<ElementConverter>();
            _delivery = GetComponent<ManualDeliveryKG>();
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            if (_storage == null) _storage = GetComponent<Storage>();
            if (_converter == null) _converter = GetComponent<ElementConverter>();
            if (_delivery == null) _delivery = GetComponent<ManualDeliveryKG>();
            ApplyMode();
        }

        public void SetMode(FermentMode mode)
        {
            SelectedMode = mode;
            ApplyMode();
        }

        public void ApplyMode()
        {
            if (_converter == null || _storage == null) return;

            switch (SelectedMode)
            {
                case FermentMode.WaterOnly:
                    if (_delivery != null)
                    {
                        _delivery.AbortDelivery("Switched to Water Only");
                        _delivery.Pause(true, "Water Only mode active");
                    }
                    _converter.consumedElements = ConsumedWaterOnly;
                    _converter.outputElements = OutputWaterOnly;
                    break;

                case FermentMode.Sucrose:
                    if (_delivery != null)
                    {
                        _delivery.RequestedItemTag = SucroseTag;
                        _delivery.capacity = 500f;
                        _delivery.refillMass = 100f;
                        _delivery.Pause(false, "Sucrose mode active");
                    }
                    _converter.consumedElements = ConsumedSucrose;
                    _converter.outputElements = OutputSucrose;
                    break;

                case FermentMode.Sweatcorn:
                    if (_delivery != null)
                    {
                        _delivery.RequestedItemTag = SweatcornTag;
                        _delivery.capacity = 500f;
                        _delivery.refillMass = 100f;
                        _delivery.Pause(false, "Sweatcorn mode active");
                    }
                    _converter.consumedElements = ConsumedSweatcorn;
                    _converter.outputElements = OutputSweatcorn;
                    break;

                case FermentMode.Wood:
                    if (_delivery != null)
                    {
                        _delivery.RequestedItemTag = WoodTag;
                        _delivery.capacity = 500f;
                        _delivery.refillMass = 100f;
                        _delivery.Pause(false, "Wood mode active");
                    }
                    _converter.consumedElements = ConsumedWood;
                    _converter.outputElements = OutputWood;
                    break;
            }
        }

        // ================= ISidescreenButtonControl =================

        public string SidescreenTitle => "生物发酵原料配置";

        public int HorizontalGroupID() => -1;

        public string SidescreenButtonText
        {
            get
            {
                switch (SelectedMode)
                {
                    case FermentMode.WaterOnly:
                        return "发酵原料：纯水直接发酵 [点击切换]";
                    case FermentMode.Sucrose:
                        return "发酵原料：蔗糖催化发酵 [点击切换]";
                    case FermentMode.Sweatcorn:
                        return "发酵原料：汗甜玉米发酵 [点击切换]";
                    case FermentMode.Wood:
                        return "发酵原料：木材生物水解 [点击切换]";
                    default:
                        return "发酵模式 [点击切换]";
                }
            }
        }

        public string SidescreenButtonTooltip
        {
            get
            {
                switch (SelectedMode)
                {
                    case FermentMode.WaterOnly:
                        return "【当前模式：纯水直接发酵（免固体料）】\n配方：水 2.0 kg/s → 乙醇 2.0 kg/s + 二氧化碳 0.5 kg/s。\n通水通电即全自动开工，无需任何固体物料！\n点击切换为【蔗糖催化发酵】。";
                    case FermentMode.Sucrose:
                        return "【当前模式：蔗糖催化发酵（高产模式）】\n配方：水 2.0 kg/s + 蔗糖 0.5 kg/s → 乙醇 2.5 kg/s + 二氧化碳 0.5 kg/s。\n点击切换为【汗甜玉米发酵】。";
                    case FermentMode.Sweatcorn:
                        return "【当前模式：汗甜玉米直接发酵】\n配方：水 2.0 kg/s + 汗甜玉米 0.5 kg/s → 乙醇 2.2 kg/s + 二氧化碳 0.5 kg/s。\n点击切换为【木材生物水解】。";
                    case FermentMode.Wood:
                        return "【当前模式：木材生物水解】\n配方：水 2.0 kg/s + 木材 0.5 kg/s → 乙醇 2.0 kg/s + 泥土 0.2 kg/s + 二氧化碳 0.5 kg/s。\n点击切换为【纯水直接发酵】。";
                    default:
                        return "点击切换生物发酵配方。";
                }
            }
        }

        public bool SidescreenEnabled() => true;

        public bool SidescreenButtonInteractable() => true;

        public int ButtonSideScreenSortOrder() => 20;

        public void SetButtonTextOverride(ButtonMenuTextOverride textOverride) { }

        public void OnSidescreenButtonPressed()
        {
            switch (SelectedMode)
            {
                case FermentMode.WaterOnly:
                    SetMode(FermentMode.Sucrose);
                    break;
                case FermentMode.Sucrose:
                    SetMode(FermentMode.Sweatcorn);
                    break;
                case FermentMode.Sweatcorn:
                    SetMode(FermentMode.Wood);
                    break;
                case FermentMode.Wood:
                    SetMode(FermentMode.WaterOnly);
                    break;
            }

            ModLog.Info($"[生物发酵反应罐] 发酵模式已切换为: {SelectedMode}");
        }
    }
}
