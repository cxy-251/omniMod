using System;
using KSerialization;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 生物发酵反应罐主控逻辑：
    /// - 默认开启【蔗糖催化发酵】：水 2.0 kg/s + 蔗糖 0.5 kg/s → 乙醇 2.0 kg/s + CO2 0.5 kg/s；
    /// - 支持侧边栏一键切换【蔗糖催化发酵】、【纯水直接发酵】、【汗甜玉米发酵】、【木材水解发酵】四种模式；
    /// - 实现 ISim200ms 动态状态机控制：原料齐全且储罐未满时自动 SetActive(true) 驱动全套动画、功耗与转化，原料不足或储罐满时自动休眠 SetActive(false)；
    /// - 自动管理进料调度（ManualDeliveryKG）与 ElementConverter 转换器状态/Accumulators 刷新；
    /// - 智能自适应容错：若在纯水模式下检测到储罐内备有蔗糖，自动切换为蔗糖催化配方消化。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class BioFermenter : KMonoBehaviour, ISim200ms, ISaveLoadable, ISidescreenButtonControl
    {
        public enum FermentMode
        {
            Sucrose = 0,   // 蔗糖催化发酵（默认：水 2.0 kg/s + 蔗糖 0.5 kg/s → 乙醇 2.0 kg/s + CO2 0.5 kg/s）
            WaterOnly = 1, // 纯水直接发酵（免固体料：水 2.0 kg/s → 乙醇 2.0 kg/s + CO2 0.5 kg/s）
            Sweatcorn = 2, // 汗甜玉米发酵（水 2.0 kg/s + 汗甜玉米 0.5 kg/s → 乙醇 2.0 kg/s + CO2 0.5 kg/s）
            Wood = 3       // 木材生物水解（水 2.0 kg/s + 木材 0.5 kg/s → 乙醇 2.0 kg/s + 泥土 0.2 kg/s + CO2 0.5 kg/s）
        }

        [Serialize]
        public FermentMode SelectedMode = FermentMode.Sucrose;

        private Storage _storage;
        private Operational _operational;
        private ElementConverter _converter;
        private ManualDeliveryKG _delivery;

        private static readonly EventSystem.IntraObjectHandler<BioFermenter> OnCopySettingsDelegate =
            new EventSystem.IntraObjectHandler<BioFermenter>((component, data) => component.OnCopySettings(data));

        public static readonly Tag WaterTag = SimHashes.Water.CreateTag();
        public static readonly Tag SucroseTag = SimHashes.Sucrose.CreateTag();
        public static readonly Tag SweatcornTag = TagManager.Create("GardenFoodPlantFood");
        public static readonly Tag WoodTag = TagManager.Create("WoodLog");

        public static readonly ElementConverter.ConsumedElement[] ConsumedSucrose = new[]
        {
            new ElementConverter.ConsumedElement(WaterTag, 2.0f),
            new ElementConverter.ConsumedElement(SucroseTag, 0.5f),
        };

        public static readonly ElementConverter.OutputElement[] OutputSucrose = new[]
        {
            new ElementConverter.OutputElement(2.0f, SimHashes.Ethanol, 313.15f, useEntityTemperature: false, storeOutput: true),
            new ElementConverter.OutputElement(0.5f, SimHashes.CarbonDioxide, 313.15f, useEntityTemperature: false, storeOutput: false, outputElementOffsety: 1.5f),
        };

        public static readonly ElementConverter.ConsumedElement[] ConsumedWaterOnly = new[]
        {
            new ElementConverter.ConsumedElement(WaterTag, 2.0f),
        };

        public static readonly ElementConverter.OutputElement[] OutputWaterOnly = new[]
        {
            new ElementConverter.OutputElement(2.0f, SimHashes.Ethanol, 313.15f, useEntityTemperature: false, storeOutput: true),
            new ElementConverter.OutputElement(0.5f, SimHashes.CarbonDioxide, 313.15f, useEntityTemperature: false, storeOutput: false, outputElementOffsety: 1.5f),
        };

        public static readonly ElementConverter.ConsumedElement[] ConsumedSweatcorn = new[]
        {
            new ElementConverter.ConsumedElement(WaterTag, 2.0f),
            new ElementConverter.ConsumedElement(SweatcornTag, 0.5f),
        };

        public static readonly ElementConverter.OutputElement[] OutputSweatcorn = new[]
        {
            new ElementConverter.OutputElement(2.0f, SimHashes.Ethanol, 313.15f, useEntityTemperature: false, storeOutput: true),
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
            _operational = GetComponent<Operational>();
            _converter = GetComponent<ElementConverter>();
            _delivery = GetComponent<ManualDeliveryKG>();
            Subscribe((int)GameHashes.CopySettings, OnCopySettingsDelegate);
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            if (_storage == null) _storage = GetComponent<Storage>();
            if (_operational == null) _operational = GetComponent<Operational>();
            if (_converter == null) _converter = GetComponent<ElementConverter>();
            if (_delivery == null) _delivery = GetComponent<ManualDeliveryKG>();

            ApplyMode();
            UpdateOperationalActive();
        }

        protected override void OnCleanUp()
        {
            Unsubscribe((int)GameHashes.CopySettings, OnCopySettingsDelegate);
            base.OnCleanUp();
        }

        private void OnCopySettings(object data)
        {
            GameObject source = data as GameObject;
            if (source == null) return;

            BioFermenter sourceFermenter = source.GetComponent<BioFermenter>();
            if (sourceFermenter == null) return;

            SetMode(sourceFermenter.SelectedMode);
            ModLog.Info($"[生物发酵反应罐] 已从 {source.name} 复制发酵配方设置: {SelectedMode}");
        }

        public void SetMode(FermentMode mode)
        {
            SelectedMode = mode;
            ApplyMode();
            UpdateOperationalActive();
            ModLog.Info($"[生物发酵反应罐] 发酵配方已切换为: {SelectedMode}");
        }

        public void Sim200ms(float dt)
        {
            UpdateOperationalActive();
        }

        public void UpdateOperationalActive()
        {
            if (_storage == null || _operational == null || _converter == null) return;

            // 1. 若没有电力、或被自动化红信号/玩家禁用关闭：
            if (!_operational.IsOperational)
            {
                if (_operational.IsActive)
                {
                    _operational.SetActive(false);
                }
                return;
            }

            // 2. 智能容错：如果在纯水模式下，检测到储罐内已有蔗糖且有纯水，自动无缝升级为高产蔗糖催化配方
            if (SelectedMode == FermentMode.WaterOnly)
            {
                if (_storage.GetMassAvailable(SucroseTag) >= 0.5f && _storage.GetMassAvailable(WaterTag) >= 2.0f)
                {
                    SetMode(FermentMode.Sucrose);
                    return;
                }
            }

            // 3. 防储物箱溢满堵塞（留出 50kg 安全余量）：当排液受阻或储物箱将满时暂缓运转
            if (_storage.MassStored() >= _storage.capacityKg - 50f)
            {
                if (_operational.IsActive)
                {
                    _operational.SetActive(false);
                }
                return;
            }

            // 4. 驱动 ElementConverter 与 PoweredActiveController 状态机启停
            if (_operational.IsActive)
            {
                if (!_converter.CanConvertAtAll())
                {
                    _operational.SetActive(false);
                }
            }
            else
            {
                if (_converter.HasEnoughMassToStartConverting(false))
                {
                    _operational.SetActive(true);
                }
            }
        }

        public void ApplyMode()
        {
            if (_converter == null || _storage == null) return;

            ElementConverter.ConsumedElement[] consumed;
            ElementConverter.OutputElement[] output;

            switch (SelectedMode)
            {
                case FermentMode.Sucrose:
                    if (_delivery != null)
                    {
                        _delivery.RequestedItemTag = SucroseTag;
                        _delivery.capacity = 500f;
                        _delivery.refillMass = 100f;
                        _delivery.Pause(false, "Sucrose mode active");
                    }
                    consumed = ConsumedSucrose;
                    output = OutputSucrose;
                    break;

                case FermentMode.WaterOnly:
                    if (_delivery != null)
                    {
                        _delivery.AbortDelivery("Switched to Water Only");
                        _delivery.Pause(true, "Water Only mode active");
                    }
                    consumed = ConsumedWaterOnly;
                    output = OutputWaterOnly;
                    break;

                case FermentMode.Sweatcorn:
                    if (_delivery != null)
                    {
                        _delivery.RequestedItemTag = SweatcornTag;
                        _delivery.capacity = 500f;
                        _delivery.refillMass = 100f;
                        _delivery.Pause(false, "Sweatcorn mode active");
                    }
                    consumed = ConsumedSweatcorn;
                    output = OutputSweatcorn;
                    break;

                case FermentMode.Wood:
                    if (_delivery != null)
                    {
                        _delivery.RequestedItemTag = WoodTag;
                        _delivery.capacity = 500f;
                        _delivery.refillMass = 100f;
                        _delivery.Pause(false, "Wood mode active");
                    }
                    consumed = ConsumedWood;
                    output = OutputWood;
                    break;

                default:
                    consumed = ConsumedSucrose;
                    output = OutputSucrose;
                    break;
            }

            RefreshConverterArrays(consumed, output);
        }

        private void RefreshConverterArrays(ElementConverter.ConsumedElement[] consumed, ElementConverter.OutputElement[] output)
        {
            if (_converter == null) return;

            // 1. 清理旧的 Accumulator 句柄
            if (Game.Instance != null && Game.Instance.accumulators != null)
            {
                if (_converter.consumedElements != null)
                {
                    for (int i = 0; i < _converter.consumedElements.Length; i++)
                    {
                        if (_converter.consumedElements[i].Accumulator.IsValid())
                        {
                            Game.Instance.accumulators.Remove(_converter.consumedElements[i].Accumulator);
                        }
                    }
                }
                if (_converter.outputElements != null)
                {
                    for (int i = 0; i < _converter.outputElements.Length; i++)
                    {
                        if (_converter.outputElements[i].accumulator.IsValid())
                        {
                            Game.Instance.accumulators.Remove(_converter.outputElements[i].accumulator);
                        }
                    }
                }
            }

            // 2. 复制新配方元素
            ElementConverter.ConsumedElement[] newConsumed = new ElementConverter.ConsumedElement[consumed.Length];
            for (int i = 0; i < consumed.Length; i++)
            {
                newConsumed[i] = new ElementConverter.ConsumedElement(consumed[i].Tag, consumed[i].MassConsumptionRate);
            }

            ElementConverter.OutputElement[] newOutput = new ElementConverter.OutputElement[output.Length];
            for (int i = 0; i < output.Length; i++)
            {
                newOutput[i] = new ElementConverter.OutputElement(
                    output[i].massGenerationRate,
                    output[i].elementHash,
                    output[i].minOutputTemperature,
                    output[i].useEntityTemperature,
                    output[i].storeOutput,
                    output[i].outputElementOffset.x,
                    output[i].outputElementOffset.y,
                    output[i].diseaseWeight);
            }

            _converter.consumedElements = newConsumed;
            _converter.outputElements = newOutput;

            // 3. 若世界已就绪，为新配方注册 Accumulator 句柄
            if (Game.Instance != null && Game.Instance.accumulators != null)
            {
                for (int i = 0; i < _converter.consumedElements.Length; i++)
                {
                    _converter.consumedElements[i].Accumulator = Game.Instance.accumulators.Add("ElementsConsumed", _converter);
                }
                for (int i = 0; i < _converter.outputElements.Length; i++)
                {
                    _converter.outputElements[i].accumulator = Game.Instance.accumulators.Add("OutputElements", _converter);
                }
            }

            // 4. 刷新建筑界面详情提示
            var smi = _converter.GetSMI<ElementConverter.StatesInstance>();
            if (smi != null)
            {
                smi.RemoveStatusItems();
                smi.AddStatusItems();
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
                    case FermentMode.Sucrose:
                        return "发酵配方：蔗糖催化发酵 [点击切换]";
                    case FermentMode.WaterOnly:
                        return "发酵配方：纯水直接发酵 (免固态料) [点击切换]";
                    case FermentMode.Sweatcorn:
                        return "发酵配方：汗甜玉米发酵 [点击切换]";
                    case FermentMode.Wood:
                        return "发酵配方：木材生物水解 [点击切换]";
                    default:
                        return "发酵配方 [点击切换]";
                }
            }
        }

        public string SidescreenButtonTooltip
        {
            get
            {
                switch (SelectedMode)
                {
                    case FermentMode.Sucrose:
                        return "【当前模式：蔗糖催化发酵】\n配方：纯水 2.0 kg/s + 蔗糖 0.5 kg/s → 乙醇 2.0 kg/s + 二氧化碳 0.5 kg/s。\n小人/自动运载器会自动补充蔗糖。\n点击切换为【纯水直接发酵】。";
                    case FermentMode.WaterOnly:
                        return "【当前模式：纯水直接发酵（免固体料）】\n配方：纯水 2.0 kg/s → 乙醇 2.0 kg/s + 二氧化碳 0.5 kg/s。\n通水通电即全自动开工，无需任何固体物料！\n点击切换为【汗甜玉米发酵】。";
                    case FermentMode.Sweatcorn:
                        return "【当前模式：汗甜玉米直接发酵】\n配方：纯水 2.0 kg/s + 汗甜玉米 0.5 kg/s → 乙醇 2.0 kg/s + 二氧化碳 0.5 kg/s。\n点击切换为【木材生物水解】。";
                    case FermentMode.Wood:
                        return "【当前模式：木材生物水解】\n配方：纯水 2.0 kg/s + 木材 0.5 kg/s → 乙醇 2.0 kg/s + 泥土 0.2 kg/s + 二氧化碳 0.5 kg/s。\n点击切换为【蔗糖催化发酵】。";
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
                case FermentMode.Sucrose:
                    SetMode(FermentMode.WaterOnly);
                    break;
                case FermentMode.WaterOnly:
                    SetMode(FermentMode.Sweatcorn);
                    break;
                case FermentMode.Sweatcorn:
                    SetMode(FermentMode.Wood);
                    break;
                case FermentMode.Wood:
                    SetMode(FermentMode.Sucrose);
                    break;
                default:
                    SetMode(FermentMode.Sucrose);
                    break;
            }
        }
    }
}
