using KSerialization;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>消耗真实粒子，维持矩形定值辐射场；不扫描植物、不改写种子。</summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class RadboltIrradiator : KMonoBehaviour, ISim200ms, IMultiSliderControl
    {
        internal const float BufferCapacity = 100f;
        internal const string SliderKey = "STRINGS.UI.UISIDESCREENS.OMNIMOD_IRRADIATOR.";
        private const int DefaultReach = 30;
        private const int DefaultHeight = 5;
        private const float BreedingRads = 800f;
        private const float MaintenanceRads = 100f;
        private const float DefaultConsumption = 5f;
        private enum BeamDirection { Right, Left, Both }

        [Serialize] private int reach = DefaultReach;
        [Serialize] private int radiationHeight = DefaultHeight;
        [Serialize] private int verticalOffset;
        // 保留字段名以兼容旧存档；现在表示每格实际目标值，不再是点源强度。
        [Serialize] private float sourceRads = BreedingRads;
        // 沿用旧存档字段名，现在直接表示整机每游戏秒消耗，不再乘范围或辐射值。
        [Serialize] private float baseConsumption = DefaultConsumption;
        [Serialize] private BeamDirection direction;
        [Serialize] private bool switchedOn = true;

        private HighEnergyParticleStorage storage;
        private RadiationEmitter emitter;
        private HighEnergyParticlePort particlePort;
        private Operational operational;
        private KBatchedAnimController anim;
        private RangeVisualizer range;
        private ISliderControl[] sliders;
        private static StatusItem status;
        private bool active;
        private bool layoutDirty = true;
        private bool emitterDirty = true;
        private int radius;
        private int centerOffset;
        private int verticalRadius;
        private int verticalCenterOffset;
        private int lastCell = -1;
        private int lastWorld = -1;
        private Vector2I lastOffset;
        private Vector2I lastSize;
        private float retryAt;
        private float appliedRads;
        private string state;

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Subscribe((int)GameHashes.CopySettings, CopySettings);
            Subscribe((int)GameHashes.RefreshUserMenu, RefreshMenu);
            sliders = new ISliderControl[]
            {
                new SettingSlider(this, 0), new SettingSlider(this, 1),
                new SettingSlider(this, 2), new SettingSlider(this, 3)
            };
            BindParticlePort();
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            SanitizeSettings();
            storage = GetComponent<HighEnergyParticleStorage>();
            emitter = GetComponent<RadiationEmitter>();
            operational = GetComponent<Operational>();
            anim = GetComponent<KBatchedAnimController>();
            range = GetComponent<RangeVisualizer>();
            // 不截断旧存量；超过 100 的旧缓存先消耗，新输入会全额穿透。
            storage.capacity = BufferCapacity;
            storage.autoStore = false;
            BindParticlePort();
            if (emitter != null) emitter.SetEmitting(false);
            state = Text("等待辐射粒子", "Waiting for radbolts");
            if (status == null)
            {
                status = new StatusItem("OmniRadboltIrradiator", "BUILDING", "", StatusItem.IconType.Info,
                    NotificationType.Neutral, false, OverlayModes.Radiation.ID);
                status.SetResolveStringCallback((_, data) => ((RadboltIrradiator)data).state);
                status.resolveTooltipCallback = (_, data) => ((RadboltIrradiator)data).StatusTooltip();
            }
            GetComponent<KSelectable>().SetStatusItem(Db.Get().StatusItemCategories.Main, status, this);
        }

        protected override void OnCleanUp()
        {
            UniformRadiationField.Remove(this);
            if (emitter != null) emitter.SetEmitting(false);
            if (particlePort != null)
            {
                particlePort.onParticleCaptureAllowed -= CanCaptureParticle;
            }
            base.OnCleanUp();
        }

        private void BindParticlePort()
        {
            particlePort = GetComponent<HighEnergyParticlePort>();
            if (particlePort == null) return;
            particlePort.requireOperational = false;
            // 由具体 HighEnergyParticle.Capture 补丁接收，避免原版部分捕获暂停飞行动画。
            particlePort.onParticleCapture = null;
            particlePort.onParticleCaptureAllowed = CanCaptureParticle;
        }

        private bool CanCaptureParticle(HighEnergyParticle particle) => particle != null &&
            particle.payload > 0f && !float.IsNaN(particle.payload) && !float.IsInfinity(particle.payload) &&
            storage != null && storage.receiverOpen && storage.Particles < BufferCapacity &&
            switchedOn && operational != null && operational.IsOperational &&
            !IrradiatorParticleTransit.IsPassing(particle);

        internal void ReceiveParticle(HighEnergyParticle particle)
        {
            if (particle == null || particlePort == null) return;
            float incoming = particle.payload;
            float kept = 0f;
            if (storage != null && storage.receiverOpen && switchedOn && operational != null && operational.IsOperational)
            {
                float space = Mathf.Max(0f, BufferCapacity - storage.Particles);
                if (space > 0f) kept = storage.Store(Mathf.Min(space, incoming));
            }
            particle.payload = Mathf.Max(0f, incoming - kept);
            if (particle.payload > 0f)
            {
                // 原粒子继续正常飞行，离开当前格前不再捕获；不走会原地停顿的 CaptureAndRelease。
                IrradiatorParticleTransit.Begin(particle);
                particle.Collide(HighEnergyParticle.CollisionType.PassThrough);
            }
            else particle.Collide(HighEnergyParticle.CollisionType.Captured);
        }

        private static string Text(string zh, string en) => ModStrings.T(zh, en).Resolve();

        private void SanitizeSettings()
        {
            reach = Mathf.Clamp(reach, 2, 60) / 2 * 2;
            radiationHeight = (Mathf.Clamp(radiationHeight, 3, 15) - 1) / 2 * 2 + 1;
            verticalOffset = Mathf.Clamp(verticalOffset, -8, 8);
            if (float.IsNaN(sourceRads) || float.IsInfinity(sourceRads)) sourceRads = BreedingRads;
            if (float.IsNaN(baseConsumption) || float.IsInfinity(baseConsumption)) baseConsumption = DefaultConsumption;
            sourceRads = Mathf.Clamp(sourceRads, 25f, 2000f);
            baseConsumption = Mathf.Clamp(baseConsumption, 0.5f, 20f);
            if ((int)direction < 0 || (int)direction > 2) direction = BeamDirection.Right;
        }

        private void CopySettings(object data)
        {
            var source = data as GameObject;
            var other = source != null ? source.GetComponent<RadboltIrradiator>() : null;
            if (other == null) return;
            reach = other.reach; radiationHeight = other.radiationHeight; verticalOffset = other.verticalOffset;
            sourceRads = other.sourceRads; baseConsumption = other.baseConsumption;
            direction = other.direction; switchedOn = other.switchedOn;
            SanitizeSettings();
            SettingsChanged(true);
        }

        private void SettingsChanged(bool geometry)
        {
            if (geometry) { layoutDirty = true; retryAt = 0f; }
            emitterDirty = true;
            // 暂停或尚未供料时，点击偏移按钮也应立即更新选中范围。
            if (isSpawned && geometry) EnsureBand();
            if (!switchedOn) SetEmission(false, 0f);
            else if (isSpawned && active) SetEmission(EnsureBand(), sourceRads);
            if (isSpawned) Game.Instance?.userMenu?.Refresh(gameObject);
        }

        private static bool OwnsColumn(PocketWorldBounds bounds, int x, int minY, int maxY)
        {
            if (x < bounds.Offset.x || x >= bounds.MaxX || minY < bounds.Offset.y || maxY >= bounds.MaxY) return false;
            for (int y = minY; y <= maxY; y++)
                if (!bounds.Owns(Grid.XYToCell(x, y))) return false;
            return true;
        }

        private bool EnsureBand()
        {
            int cell = Grid.PosToCell(this);
            WorldContainer world = ClusterUtil.GetMyWorld(this);
            if (!PocketWorldBounds.TryGet(world, out PocketWorldBounds bounds) || !bounds.Owns(cell)) return false;
            if (cell != lastCell || world.id != lastWorld || world.WorldOffset != lastOffset || world.WorldSize != lastSize)
            {
                lastCell = cell; lastWorld = world.id; lastOffset = world.WorldOffset; lastSize = world.WorldSize;
                layoutDirty = true; retryAt = 0f;
            }
            if (!layoutDirty) return radius > 0;
            if (Time.time < retryAt) return false;
            int x = cell % Grid.WidthInCells;
            int y = cell / Grid.WidthInCells;
            radius = 0;
            centerOffset = 0;
            verticalRadius = 0;
            verticalCenterOffset = 0;
            int minY = Mathf.Max(bounds.Offset.y, y + verticalOffset - radiationHeight / 2);
            int maxY = Mathf.Min(bounds.MaxY - 1, y + verticalOffset + radiationHeight / 2);
            if ((maxY - minY + 1) % 2 == 0) maxY--;
            if (maxY - minY >= 2 && OwnsColumn(bounds, x, minY, maxY))
            {
                verticalRadius = (maxY - minY) / 2;
                verticalCenterOffset = (minY + maxY) / 2 - y;
                if (direction == BeamDirection.Both)
                {
                    for (int step = 1; step <= reach / 2; step++)
                    {
                        if (!OwnsColumn(bounds, x - step, minY, maxY) || !OwnsColumn(bounds, x + step, minY, maxY)) break;
                        radius = step;
                    }
                }
                else
                {
                    int sign = direction == BeamDirection.Right ? 1 : -1;
                    int distance = 0;
                    for (int step = 1; step <= reach; step++)
                    {
                        if (!OwnsColumn(bounds, x + sign * step, minY, maxY)) break;
                        distance = step;
                    }
                    // 保持既有整数中心与奇数宽度设置，整个矩形必须属于本世界。
                    radius = distance / 2;
                    centerOffset = sign * radius;
                }
            }
            if (range != null)
            {
                range.OriginOffset = new Vector2I(centerOffset, verticalCenterOffset);
                range.RangeMin = new Vector2I(-radius, radius > 0 ? -verticalRadius : 0);
                range.RangeMax = new Vector2I(radius, radius > 0 ? verticalRadius : 0);
            }
            if (radius < 1) { retryAt = Time.time + 1f; return false; }
            layoutDirty = false;
            emitterDirty = true;
            return true;
        }

        private float ConsumptionRate() => baseConsumption;

        public void Sim200ms(float dt)
        {
            if (App.isLoading || Game.Instance == null || dt <= 0f) return;
            bool hasBand = EnsureBand();
            if (!switchedOn)
            { state = Text("辐照已关闭", "Irradiation switched off"); SetEmission(false, 0f); return; }
            if (operational == null || !operational.IsOperational)
            { state = Text("自动化或建筑状态已停机", "Disabled by automation or building state"); SetEmission(false, 0f); return; }
            if (!hasBand)
            { state = Text("等待世界就绪或横向空间不足", "Waiting for valid world or horizontal space"); SetEmission(false, 0f); return; }
            if (storage == null || storage.Particles <= 0f)
            { state = Text("等待辐射粒子", "Waiting for radbolts"); SetEmission(false, 0f); return; }
            float rate = ConsumptionRate();
            float required = rate * dt;
            // 重启需积攒至少两秒用量或 20 粒子（上限 100），运行时只提供完整设定值。
            // 缺料时暂停并补足缓存，不再每 200ms 用少量入料反复点亮或压低辐射值。
            float startup = Mathf.Max(required, Mathf.Min(BufferCapacity, Mathf.Max(20f, rate * 2f)));
            if (storage.Particles < (active ? required : startup))
            {
                state = Text("积攒粒子缓存（供给需达到消耗速率）", "Charging buffer (supply must meet consumption)");
                SetEmission(false, 0f);
                return;
            }
            storage.ConsumeAndGet(required);
            state = Text("均匀辐照中", "Uniform irradiation active");
            SetEmission(true, sourceRads);
        }

        private void SetEmission(bool value, float rads)
        {
            if (value != active || emitterDirty || !Mathf.Approximately(appliedRads, rads))
            {
                if (value)
                {
                    int x = lastCell % Grid.WidthInCells + centerOffset;
                    int y = lastCell / Grid.WidthInCells + verticalCenterOffset;
                    UniformRadiationField.Set(this, lastWorld, x - radius, x + radius,
                        y - verticalRadius, y + verticalRadius, rads);
                }
                else UniformRadiationField.Remove(this);
                appliedRads = rads;
                emitterDirty = false;
            }
            if (active != value)
            {
                active = value;
                operational?.SetActive(value);
                anim?.Play(value ? "on" : "off");
            }
        }

        private string DirectionName => direction == BeamDirection.Right ? Text("向右", "Right") :
            direction == BeamDirection.Left ? Text("向左", "Left") : Text("左右双向", "Both");

        private string StatusTooltip()
        {
            int width = radius > 0 ? radius * 2 + 1 : 0;
            return Text("普通建筑，尺寸 1×1，无电力需求。", "Normal 1×1 building; no electrical power required.") +
                $"\n{Text("方向", "Direction")}: {DirectionName}  |  {Text("实际范围", "Actual bounds")}: {width}×{(width > 0 ? verticalRadius * 2 + 1 : 0)}" +
                $"\n{Text("每格目标辐射", "Target radiation per tile")}: {sourceRads:0} rad" +
                $"\n{Text("整机运行消耗", "Total running consumption")}: {ConsumptionRate():0.0} {Text("粒子/游戏秒", "radbolts/game second")}" +
                $"\n{Text("缓存", "Buffer")}: {(storage != null ? storage.Particles : 0f):0.0}/{BufferCapacity:0}" +
                $"\n{Text("库存可用时长", "Stored run time")}: {(storage != null ? storage.Particles / ConsumptionRate() : 0f):0.0} {Text("游戏秒", "game seconds")}" +
                "\n" + Text("运行消耗等于设定值，不随范围或辐射强度增加；游戏倍速会同比加快现实时间中的消耗。", "Running use equals the setting, independent of field size or radiation. Fast-forward speeds up consumption in real time.") +
                "\n" + Text("先存满 100 粒子，满仓余量沿原方向继续；不按比例分流。", "Fills the 100-radbolt buffer first; overflow continues in its original direction.") +
                (storage != null && storage.Particles > BufferCapacity ? "\n" + Text("旧缓存超限，输入全额穿透；原有粒子保留并继续消耗。", "Legacy buffer exceeds capacity: incoming radbolts pass through while stored particles are consumed.") : "") +
                "\n" + Text("工作时矩形内每格采用设定值，无距离或四角衰减；重叠取较高设定值，关闭恢复原环境辐射。",
                    "Every tile in the rectangle uses the target value, without distance or corner falloff. Overlaps use the higher target; disabling restores ambient radiation.");
        }

        private void RefreshMenu(object data)
        {
            if (Game.Instance?.userMenu == null) return;
            AddButton(Text("辐照：", "Irradiation: ") + (switchedOn ? Text("开", "On") : Text("关", "Off")),
                () => { switchedOn = !switchedOn; SettingsChanged(false); },
                Text("点击启停辐照。关闭时不消耗粒子，输入全额穿透。", "Toggle irradiation. When off, incoming radbolts pass through."));
            AddButton(Text("辐照方向：", "Direction: ") + DirectionName,
                () => { direction = (BeamDirection)(((int)direction + 1) % 3); SettingsChanged(true); },
                Text("依次切换向右、向左、左右双向；长度由侧边栏设置。", "Cycle right, left and both directions; set reach in the side screen."));
            string mode = Mathf.Approximately(sourceRads, BreedingRads) ? Text("育种", "Breeding") :
                Mathf.Approximately(sourceRads, MaintenanceRads) ? Text("维持", "Maintenance") : Text("自定义", "Custom");
            AddButton(Text("辐照档位：", "Preset: ") + mode,
                () => { sourceRads = Mathf.Approximately(sourceRads, BreedingRads) ? MaintenanceRads : BreedingRads; SettingsChanged(false); },
                Text("切换每格 800 rad 育种和每格 100 rad 维持，也可直接拖动目标值滑块。",
                    "Toggle an 800 rad breeding field or 100 rad maintenance field per tile, or adjust the target slider."));
        }

        internal int VerticalOffset => verticalOffset;

        internal void SetVerticalOffset(int value)
        {
            int next = Mathf.Clamp(value, -8, 8);
            if (next == verticalOffset) return;
            verticalOffset = next;
            SettingsChanged(true);
        }

        private void AddButton(string label, System.Action callback, string tooltip) =>
            Game.Instance.userMenu.AddButton(gameObject, new KIconButtonMenu.ButtonInfo(
                "action_move_to_storage", label, callback, Action.NumActions, null, null, null, tooltip));

        string IMultiSliderControl.SidescreenTitleKey => SliderKey + "TITLE";
        bool IMultiSliderControl.SidescreenEnabled() => true;
        ISliderControl[] IMultiSliderControl.sliderControls => sliders;

        private sealed class SettingSlider : ISingleSliderControl, ISliderControl
        {
            private readonly RadboltIrradiator owner;
            private readonly int setting;
            private static readonly string[] Keys = { "REACH", "HEIGHT", "STRENGTH", "CONSUMPTION" };
            internal SettingSlider(RadboltIrradiator owner, int setting) { this.owner = owner; this.setting = setting; }
            public string SliderTitleKey => SliderKey + Keys[setting] + "_TITLE";
            public string SliderUnits => setting <= 1 ? Text(" 格", " tiles") : setting == 2 ? " rad" : Text(" 粒子/s", " radbolts/s");
            public int SliderDecimalPlaces(int index) => setting == 3 ? 1 : 0;
            public float GetSliderMin(int index) => setting == 0 ? 2f : setting == 1 ? 3f : setting == 2 ? 25f : 0.5f;
            public float GetSliderMax(int index) => setting == 0 ? 60f : setting == 1 ? 15f : setting == 2 ? 2000f : 20f;
            public float GetSliderValue(int index) => setting == 0 ? owner.reach : setting == 1 ? owner.radiationHeight : setting == 2 ? owner.sourceRads : owner.baseConsumption;
            public void SetSliderValue(float value, int index)
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) return;
                if (setting == 0) owner.reach = Mathf.RoundToInt(Mathf.Clamp(value, 2f, 60f) / 2f) * 2;
                else if (setting == 1) owner.radiationHeight = Mathf.RoundToInt((Mathf.Clamp(value, 3f, 15f) - 1f) / 2f) * 2 + 1;
                else if (setting == 2) owner.sourceRads = Mathf.Round(Mathf.Clamp(value, 25f, 2000f) / 25f) * 25f;
                else owner.baseConsumption = Mathf.Round(Mathf.Clamp(value, 0.5f, 20f) * 2f) / 2f;
                owner.SettingsChanged(setting <= 1);
            }
            public string GetSliderTooltipKey(int index) => SliderKey + Keys[setting] + "_TOOLTIP";
            public string GetSliderTooltip(int index)
            {
                switch (setting)
                {
                    case 0: return Text("单向延伸距离，双向平均分配；含本机，总宽最多为距离＋1。范围到世界边界自动缩短。", "One-sided reach, split evenly in both directions. Clipped at world borders.");
                    case 1: return Text("辐照带总高度：3、5、7…15 格。会按当前世界边界缩短。", "Total band height: 3, 5, 7…15 tiles, clipped to this world.");
                    case 2: return Text("工作时矩形内每格的实际环境辐射值。无距离、遮挡或四角衰减；重叠区域取较高值。", "Actual environmental radiation on every tile while running. No distance, shielding or corner falloff; overlaps use the higher target.");
                    case 3: return Text("整台设备每游戏秒实际消耗的粒子数，不随范围或辐射强度放大。设为 5 时，每 0.2 游戏秒扣 1 粒子，50 粒子约可运行 10 游戏秒；游戏倍速同比加快现实时间中的消耗。", "Total actual radbolts per game second, independent of field size or radiation. At 5/s, each 0.2-second tick uses 1 and 50 lasts about 10 game seconds. Fast-forward speeds up real-time use.");
                    default: return "";
                }
            }
        }
    }
}
