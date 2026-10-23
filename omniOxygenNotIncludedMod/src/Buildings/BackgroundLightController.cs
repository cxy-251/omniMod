using KSerialization;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 全能背景灯主控逻辑：
    /// - 默认开启【糖心树日照强光】（12,000 Lux，照射范围 8 格）：完美覆盖冰封星球 DLC 糖心树（SpaceTree）≥ 10,000 Lux 的生长照度需求；
    /// - 支持侧边栏一键切换【糖心树强光 12000 Lux】、【超日照耀斑 40000 Lux】、【柔和作物补光 1800 Lux】三种模式；
    /// - 支持存档保存与实时动态刷新（Light2D.FullRefresh）。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class BackgroundLightController : KMonoBehaviour, ISaveLoadable, ISidescreenButtonControl
    {
        public enum LightMode
        {
            Sunlight = 0,   // 糖心树日照强光（12,000 Lux，半径 8 格）- 默认
            HyperSolar = 1, // 超级耀斑/超日照模式（40,000 Lux，半径 10 格）
            Soft = 2        // 柔和作物补光（1,800 Lux，半径 6 格）
        }

        [Serialize]
        public LightMode SelectedMode = LightMode.Sunlight;

        private Light2D _light;

        private static readonly EventSystem.IntraObjectHandler<BackgroundLightController> OnCopySettingsDelegate =
            new EventSystem.IntraObjectHandler<BackgroundLightController>((component, data) => component.OnCopySettings(data));

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            _light = GetComponent<Light2D>();
            Subscribe((int)GameHashes.CopySettings, OnCopySettingsDelegate);
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            if (_light == null) _light = GetComponent<Light2D>();
            ApplyMode();
        }

        private void OnCopySettings(object data)
        {
            GameObject source = data as GameObject;
            if (source == null) return;

            BackgroundLightController sourceController = source.GetComponent<BackgroundLightController>();
            if (sourceController == null) return;

            SetMode(sourceController.SelectedMode);
            ModLog.Info($"[全能背景灯] 已从 {source.name} 复制光照设置: {SelectedMode}");
        }

        public void SetMode(LightMode mode)
        {
            SelectedMode = mode;
            ApplyMode();
        }

        public void ApplyMode()
        {
            if (_light == null) _light = GetComponent<Light2D>();
            if (_light == null) return;

            switch (SelectedMode)
            {
                case LightMode.Sunlight:
                    _light.Lux = 12000;
                    _light.Range = 8f;
                    break;
                case LightMode.HyperSolar:
                    _light.Lux = 40000;
                    _light.Range = 10f;
                    break;
                case LightMode.Soft:
                    _light.Lux = 1800;
                    _light.Range = 6f;
                    break;
            }

            _light.FullRefresh();
        }

        // ================= ISidescreenButtonControl =================

        public string SidescreenTitle => "背景灯光照模式";

        public int HorizontalGroupID() => -1;

        public string SidescreenButtonText
        {
            get
            {
                switch (SelectedMode)
                {
                    case LightMode.Sunlight:
                        return "光照模式：糖心树强光 (12,000 Lux) [点击切换]";
                    case LightMode.HyperSolar:
                        return "光照模式：超日照耀斑 (40,000 Lux) [点击切换]";
                    case LightMode.Soft:
                        return "光照模式：柔和补光 (1,800 Lux) [点击切换]";
                    default:
                        return "光照模式 [点击切换]";
                }
            }
        }

        public string SidescreenButtonTooltip
        {
            get
            {
                switch (SelectedMode)
                {
                    case LightMode.Sunlight:
                        return "【当前：糖心树强光模式】\n亮度：12,000 Lux，照射范围：8格。\n完美满足《冰封星球》糖心树 ≥10,000 Lux 的生长照度需求！\n点击切换为【超日照耀斑模式】。";
                    case LightMode.HyperSolar:
                        return "【当前：超日照耀斑模式】\n亮度：40,000 Lux，照射范围：10格。\n极强光照，即使距离较远也能提供超万照度，适合大范围糖心树林！\n点击切换为【柔和补光模式】。";
                    case LightMode.Soft:
                        return "【当前：柔和补光模式】\n亮度：1,800 Lux，照射范围：6格。\n节能温和，适合毛刺花、夜幕花等常规作物的精准补光。\n点击切换为【糖心树强光模式】。";
                    default:
                        return "点击切换背景灯光照模式。";
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
                case LightMode.Sunlight:
                    SetMode(LightMode.HyperSolar);
                    break;
                case LightMode.HyperSolar:
                    SetMode(LightMode.Soft);
                    break;
                case LightMode.Soft:
                    SetMode(LightMode.Sunlight);
                    break;
            }

            ModLog.Info($"[全能背景灯] 光照模式已切换为: {SelectedMode}");
        }
    }
}
