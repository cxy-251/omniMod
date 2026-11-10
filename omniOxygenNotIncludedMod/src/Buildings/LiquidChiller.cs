using System;
using System.Collections.Generic;
using KSerialization;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 全能恒温流体冷却机主控逻辑：
    /// - 管道输入任意液体（满管 10 kg/s 通量）；
    /// - 侧边栏滑条设定目标出液温度（-200°C ~ +400°C）；
    /// - 进管流体一步直接调节至设定温度出液；
    /// - 智能防爆管：若设定温度低于流体冰点，自动锁死在冰点 +3°C，高于沸点自动锁死在沸点 -3°C，永不相变爆管；
    /// - 超低自热与功耗，零废热外泄；
    /// - 全面支持【复制设置】（Copy Settings）；
    /// - 智能待机控制：无液体、出水管堵塞或流体已达标时自动停机并断电，管网断水绝不空转；
    /// - 侧边栏快捷按钮：一键为相连管网充满 20°C 超级冷却剂（Super Coolant）。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class LiquidChiller : KMonoBehaviour, ISim200ms, ISaveLoadable, ISingleSliderControl, ISidescreenButtonControl
    {
        [Serialize]
        public float TargetTemperatureCelsius = 20f;

        private Storage _storage;
        private Operational _operational;
        private float _activeCooldown = 0f;

        private static readonly EventSystem.IntraObjectHandler<LiquidChiller> OnStorageChangeDelegate =
            new EventSystem.IntraObjectHandler<LiquidChiller>((cmp, data) => cmp.ProcessStoredLiquids(0f));

        private static readonly EventSystem.IntraObjectHandler<LiquidChiller> OnCopySettingsDelegate =
            new EventSystem.IntraObjectHandler<LiquidChiller>((cmp, data) => cmp.OnCopySettings(data));

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            _storage = GetComponent<Storage>();
            _operational = GetComponent<Operational>();
            Subscribe((int)GameHashes.OnStorageChange, OnStorageChangeDelegate);
            Subscribe((int)GameHashes.CopySettings, OnCopySettingsDelegate);
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            if (_storage == null) _storage = GetComponent<Storage>();
            if (_operational == null) _operational = GetComponent<Operational>();
            _activeCooldown = 0f;
            ProcessStoredLiquids(0f);
        }

        protected override void OnCleanUp()
        {
            Unsubscribe((int)GameHashes.OnStorageChange, OnStorageChangeDelegate);
            Unsubscribe((int)GameHashes.CopySettings, OnCopySettingsDelegate);
            base.OnCleanUp();
        }

        public void Sim200ms(float dt)
        {
            ProcessStoredLiquids(dt);
        }

        public void ProcessStoredLiquids(float dt = 0f)
        {
            if (_storage == null) _storage = GetComponent<Storage>();
            if (_operational == null) _operational = GetComponent<Operational>();
            if (_storage == null || _operational == null) return;

            // 若建筑处于未通电、被信号关闭或失效状态，彻底关闭激活状态与动画
            if (!_operational.IsOperational)
            {
                _activeCooldown = 0f;
                _operational.SetActive(false);
                return;
            }

            float liquidMass = 0f;
            bool isModifyingTemp = false;

            if (_storage.items != null)
            {
                for (int i = 0; i < _storage.items.Count; i++)
                {
                    GameObject item = _storage.items[i];
                    if (item == null) continue;

                    PrimaryElement pe = item.GetComponent<PrimaryElement>();
                    if (pe == null || !pe.Element.IsLiquid || pe.Mass <= 0.001f) continue;

                    liquidMass += pe.Mass;

                    float lowTemp = pe.Element.lowTemp;
                    float highTemp = pe.Element.highTemp;

                    // 智能防爆管：出液温度最低不能低于冰点+3度，最高不能高于沸点-3度
                    float safeMin = lowTemp + 3.0f;
                    float safeMax = highTemp - 3.0f;
                    float targetKelvin = Mathf.Clamp(TargetTemperatureCelsius + 273.15f, safeMin, safeMax);

                    if (Mathf.Abs(pe.Temperature - targetKelvin) > 0.01f)
                    {
                        pe.Temperature = targetKelvin;
                        isModifyingTemp = true;
                    }
                }
            }

            // 状态机激活判定逻辑：
            // 1. 如果机器内有有效液体（Mass > 0.001kg）且正在调温，刷新冷却机激活维持时间（1.2 秒），确保连续过水时平滑运转不频繁闪烁；
            // 2. 如果机器内没有液体（或液体已排空仅剩 0kg 幽灵残留对象），或者液体已达到设定温度（如出管堵塞），则计时衰减；
            // 3. 倒计时结束后立即 SetActive(false)，停止动画与功耗。
            if (liquidMass > 0.001f && isModifyingTemp)
            {
                _activeCooldown = 1.2f;
            }
            else if (dt > 0f)
            {
                _activeCooldown = Mathf.Max(0f, _activeCooldown - dt);
            }

            bool shouldBeActive = _activeCooldown > 0f;
            _operational.SetActive(shouldBeActive);
        }

        private void OnCopySettings(object data)
        {
            GameObject source = data as GameObject;
            if (source == null) return;

            LiquidChiller sourceChiller = source.GetComponent<LiquidChiller>();
            if (sourceChiller == null) return;

            TargetTemperatureCelsius = sourceChiller.TargetTemperatureCelsius;
            ProcessStoredLiquids(0f);

            ModLog.Info($"[全能恒温流体冷却机] 已从 {source.name} 复制温控设置：目标出水={TargetTemperatureCelsius:0}°C");
        }

        // ================= ISingleSliderControl / ISliderControl =================

        public string SliderTitleKey => "STRINGS.UI.UISIDESCREENS.OMNIMOD_LIQUIDCHILLER.TARGET_TEMP_TITLE";

        public string SliderUnits => "°C";

        public int SliderDecimalPlaces(int index) => 0;

        public float GetSliderMin(int index) => -200f;

        public float GetSliderMax(int index) => 400f;

        public float GetSliderValue(int index) => TargetTemperatureCelsius;

        public void SetSliderValue(float value, int index)
        {
            TargetTemperatureCelsius = Mathf.Round(value);
            ProcessStoredLiquids(0f);
        }

        public string GetSliderTooltipKey(int index) => "STRINGS.UI.UISIDESCREENS.OMNIMOD_LIQUIDCHILLER.TARGET_TEMP_TOOLTIP";

        public string GetSliderTooltip(int index) =>
            $"目标出液温度：{TargetTemperatureCelsius:0}°C\n管道流体经过时一步直接调节至此温度出管。\n若设定温度低于流体冰点，系统将自动锁死在冰点+3°C以保护管道永不爆裂。";

        // ================= ISidescreenButtonControl =================

        public string SidescreenTitle => ModStrings.T("管道极速充填", "Conduit Rapid Fill").Resolve();

        public int HorizontalGroupID() => -1;

        public string SidescreenButtonText => ModStrings.T("充满 20°C 超级冷却剂", "Fill Pipe With 20°C Super Coolant").Resolve();

        public string SidescreenButtonTooltip => ModStrings.T(
            "一键将连接至本冷却机的所有液体管道（进管与出管循环闭环）瞬间充满满管（10 kg/格）20°C 超级冷却剂！\n彻底免除繁琐的手工灌装、排气与换液，即点即用，立即开启深冷循环。",
            "Instantly fill all connected liquid pipes (both input and output loops) with 10 kg/tile of 20°C Super Coolant!\nEliminates manual piping and purging. Ready to cool immediately.").Resolve();

        public bool SidescreenEnabled() => true;

        public bool SidescreenButtonInteractable() => true;

        public int ButtonSideScreenSortOrder() => 20;

        public void SetButtonTextOverride(ButtonMenuTextOverride textOverride) { }

        public void OnSidescreenButtonPressed()
        {
            FillConnectedPipesWithSuperCoolant();
        }

        /// <summary>
        /// 一键为相连管网与机器自身充满 20°C 超级冷却剂
        /// </summary>
        public void FillConnectedPipesWithSuperCoolant()
        {
            try
            {
                if (Game.Instance == null || Game.Instance.liquidConduitFlow == null || Game.Instance.liquidConduitSystem == null) return;

                ConduitFlow flow = Game.Instance.liquidConduitFlow;
                Building building = GetComponent<Building>();
                if (building == null) return;

                int inCell = building.GetUtilityInputCell();
                int outCell = building.GetUtilityOutputCell();

                var targetNetworks = new HashSet<FlowUtilityNetwork>();
                if (Grid.IsValidCell(inCell))
                {
                    if (Game.Instance.liquidConduitSystem.GetNetworkForCell(inCell) is FlowUtilityNetwork inNet)
                        targetNetworks.Add(inNet);
                }
                if (Grid.IsValidCell(outCell))
                {
                    if (Game.Instance.liquidConduitSystem.GetNetworkForCell(outCell) is FlowUtilityNetwork outNet)
                        targetNetworks.Add(outNet);
                }

                int filledCells = 0;
                const float tempKelvin = 293.15f; // 20°C
                const float fullMass = 10f; // 10 kg

                foreach (var net in targetNetworks)
                {
                    if (net == null || net.conduits == null) continue;
                    foreach (var item in net.conduits)
                    {
                        if (item == null) continue;
                        int cell = item.Cell;
                        if (!flow.HasConduit(cell)) continue;

                        flow.SetContents(cell, new ConduitFlow.ConduitContents(SimHashes.SuperCoolant, fullMass, tempKelvin, byte.MaxValue, 0));
                        filledCells++;
                    }
                }

                // 确保接口处单格管道（若未形成大管网）也被充填
                if (Grid.IsValidCell(inCell) && flow.HasConduit(inCell))
                {
                    flow.SetContents(inCell, new ConduitFlow.ConduitContents(SimHashes.SuperCoolant, fullMass, tempKelvin, byte.MaxValue, 0));
                }
                if (Grid.IsValidCell(outCell) && flow.HasConduit(outCell))
                {
                    flow.SetContents(outCell, new ConduitFlow.ConduitContents(SimHashes.SuperCoolant, fullMass, tempKelvin, byte.MaxValue, 0));
                }

                // 清理并充注机器内部 Storage，使冷却机即刻进入平滑循环
                if (_storage != null)
                {
                    _storage.DropAll(false, false, default, true);
                    _storage.AddLiquid(SimHashes.SuperCoolant, fullMass, tempKelvin, byte.MaxValue, 0, false, true);
                }

                ProcessStoredLiquids(0f);

                string msg = ModStrings.T($"已为连接管网充满 20°C 超级冷却剂 ({filledCells} 格)！", $"Filled {filledCells} pipes with 20°C Super Coolant!").Resolve();
                PopFXManager.Instance?.SpawnFX(PopFXManager.Instance.sprite_Plus, msg, transform);
                ModLog.Info($"[全能恒温流体冷却机] {name}: {msg}");
            }
            catch (Exception ex)
            {
                ModLog.Error($"[全能恒温流体冷却机] 充注超级冷却剂失败: {ex.Message}", ex);
            }
        }
    }
}
