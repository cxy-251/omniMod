using System.Collections.Generic;
using KSerialization;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 装载浓缩铀作为永久反应核心，维持矩形定值辐射场；不消耗辐射粒子、无运行消耗、永久工作。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class RadboltIrradiator : KMonoBehaviour, ISim200ms, IMultiSliderControl
    {
        public const float RequiredEnrichedUranium = 10f; // 运送 10 kg 浓缩铀即可永久激活
        internal static readonly Tag FuelTag = SimHashes.EnrichedUranium.CreateTag();

        internal const string SliderKey = "STRINGS.UI.UISIDESCREENS.OMNIMOD_IRRADIATOR.";
        private const int DefaultReach = 30;
        private const int DefaultHeight = 5;
        private const float BreedingRads = 800f;
        private const float MaintenanceRads = 100f;
        private enum BeamDirection { Right, Left, Both }

        [Serialize] private int reach = DefaultReach;
        [Serialize] private int radiationHeight = DefaultHeight;
        [Serialize] private int verticalOffset;
        [Serialize] private float sourceRads = BreedingRads;
        // 保留字段名以兼容旧存档反序列化，不再使用
#pragma warning disable CS0414
        [Serialize] private float baseConsumption = 5f;
#pragma warning restore CS0414
        [Serialize] private BeamDirection direction;
        [Serialize] private bool switchedOn = true;

        private Storage storage;
        private RadiationEmitter emitter;
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

        public float FuelMass => storage != null ? storage.GetMassAvailable(FuelTag) : 0f;
        public bool HasFuel => FuelMass >= RequiredEnrichedUranium;

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Subscribe((int)GameHashes.CopySettings, CopySettings);
            Subscribe((int)GameHashes.RefreshUserMenu, RefreshMenu);
            sliders = new ISliderControl[]
            {
                new SettingSlider(this, 0), // 延伸距离
                new SettingSlider(this, 1), // 辐照高度
                new SettingSlider(this, 2)  // 目标辐射强度
            };
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            SanitizeSettings();
            storage = GetComponent<Storage>();
            if (storage == null)
            {
                storage = gameObject.AddOrGet<Storage>();
                storage.capacityKg = RequiredEnrichedUranium;
                storage.showInUI = true;
                storage.showDescriptor = true;
                storage.storageFilters = new List<Tag> { FuelTag };
                storage.SetDefaultStoredItemModifiers(Storage.StandardSealedStorage);
            }

            var delivery = gameObject.AddOrGet<ManualDeliveryKG>();
            delivery.SetStorage(storage);
            delivery.RequestedItemTag = FuelTag;
            delivery.capacity = RequiredEnrichedUranium;
            delivery.refillMass = RequiredEnrichedUranium;
            delivery.MinimumMass = 0.01f;
            delivery.choreTypeIDHash = Db.Get().ChoreTypes.MachineFetch.IdHash;
            delivery.operationalRequirement = Operational.State.None;
            delivery.allowPause = true;

            Prioritizable.AddRef(gameObject);

            if (!HasFuel)
            {
                delivery.UpdateDeliveryState();
                delivery.RequestDelivery();
            }

            emitter = GetComponent<RadiationEmitter>();
            operational = GetComponent<Operational>();
            anim = GetComponent<KBatchedAnimController>();
            range = GetComponent<RangeVisualizer>();
            if (emitter != null) emitter.SetEmitting(false);

            state = HasFuel ? Text("均匀辐照中", "Uniform irradiation active") : Text("等待运送浓缩铀", "Waiting for enriched uranium");
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
            base.OnCleanUp();
        }

        private static string Text(string zh, string en) => ModStrings.T(zh, en).Resolve();

        private void SanitizeSettings()
        {
            reach = Mathf.Clamp(reach, 2, 60) / 2 * 2;
            radiationHeight = (Mathf.Clamp(radiationHeight, 3, 15) - 1) / 2 * 2 + 1;
            verticalOffset = Mathf.Clamp(verticalOffset, -8, 8);
            if (float.IsNaN(sourceRads) || float.IsInfinity(sourceRads)) sourceRads = BreedingRads;
            sourceRads = Mathf.Clamp(sourceRads, 25f, 2000f);
            if ((int)direction < 0 || (int)direction > 2) direction = BeamDirection.Right;
        }

        private void CopySettings(object data)
        {
            var source = data as GameObject;
            var other = source != null ? source.GetComponent<RadboltIrradiator>() : null;
            if (other == null) return;
            reach = other.reach;
            radiationHeight = other.radiationHeight;
            verticalOffset = other.verticalOffset;
            sourceRads = other.sourceRads;
            direction = other.direction;
            switchedOn = other.switchedOn;
            SanitizeSettings();
            SettingsChanged(true);
        }

        private void SettingsChanged(bool geometry)
        {
            if (geometry) { layoutDirty = true; retryAt = 0f; }
            emitterDirty = true;
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

        public void Sim200ms(float dt)
        {
            if (App.isLoading || Game.Instance == null || dt <= 0f) return;
            bool hasBand = EnsureBand();
            if (!switchedOn)
            {
                state = Text("辐照已关闭", "Irradiation switched off");
                SetEmission(false, 0f);
                return;
            }
            if (operational == null || !operational.IsOperational)
            {
                state = Text("自动化或建筑状态已停机", "Disabled by automation or building state");
                SetEmission(false, 0f);
                return;
            }
            if (!hasBand)
            {
                state = Text("等待世界就绪或横向空间不足", "Waiting for valid world or horizontal space");
                SetEmission(false, 0f);
                return;
            }
            if (!HasFuel)
            {
                state = Text($"等待运送浓缩铀 ({FuelMass:0.0}/{RequiredEnrichedUranium:0.0} kg)",
                             $"Waiting for enriched uranium ({FuelMass:0.0}/{RequiredEnrichedUranium:0.0} kg)");
                SetEmission(false, 0f);
                return;
            }

            state = Text("均匀辐照中（浓缩铀核心永久工作）", "Uniform irradiation active (Enriched Uranium permanent core)");
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
            string fuelStatus = HasFuel
                ? Text($"浓缩铀反应核心已就绪 ({FuelMass:0.0} kg)，永久运行（零消耗）。",
                       $"Enriched uranium reaction core ready ({FuelMass:0.0} kg), permanent operation (zero consumption).")
                : Text($"等待运送浓缩铀 ({FuelMass:0.0}/{RequiredEnrichedUranium:0.0} kg)。",
                       $"Waiting for enriched uranium ({FuelMass:0.0}/{RequiredEnrichedUranium:0.0} kg).");

            return Text("普通建筑，尺寸 1×1，无电力需求。", "Normal 1×1 building; no electrical power required.") +
                $"\n{Text("方向", "Direction")}: {DirectionName}  |  {Text("实际范围", "Actual bounds")}: {width}×{(width > 0 ? verticalRadius * 2 + 1 : 0)}" +
                $"\n{Text("每格目标辐射", "Target radiation per tile")}: {sourceRads:0} rad" +
                $"\n{Text("浓缩铀装载", "Enriched Uranium")}: {FuelMass:0.0} / {RequiredEnrichedUranium:0.0} kg" +
                $"\n{Text("反应核心状态", "Core Status")}: {fuelStatus}" +
                "\n" + Text("运送满 10 kg 浓缩铀后即可作为永久核心工作，无需持续供给燃料，不产生任何运行消耗。",
                            "Delivering 10 kg of Enriched Uranium activates a permanent core that never depletes.") +
                "\n" + Text("工作时矩形内每格采用设定值，无距离或四角衰减；重叠取较高设定值，关闭恢复原环境辐射。",
                            "Every tile in the rectangle uses the target value, without distance or corner falloff. Overlaps use the higher target; disabling restores ambient radiation.");
        }

        private void RefreshMenu(object data)
        {
            if (Game.Instance?.userMenu == null) return;
            AddButton(Text("辐照：", "Irradiation: ") + (switchedOn ? Text("开", "On") : Text("关", "Off")),
                () => { switchedOn = !switchedOn; SettingsChanged(false); },
                Text("点击启停辐照。", "Toggle irradiation."));
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
            private static readonly string[] Keys = { "REACH", "HEIGHT", "STRENGTH" };
            internal SettingSlider(RadboltIrradiator owner, int setting) { this.owner = owner; this.setting = setting; }
            public string SliderTitleKey => SliderKey + Keys[setting] + "_TITLE";
            public string SliderUnits => setting <= 1 ? Text(" 格", " tiles") : " rad";
            public int SliderDecimalPlaces(int index) => 0;
            public float GetSliderMin(int index) => setting == 0 ? 2f : setting == 1 ? 3f : 25f;
            public float GetSliderMax(int index) => setting == 0 ? 60f : setting == 1 ? 15f : 2000f;
            public float GetSliderValue(int index) => setting == 0 ? owner.reach : setting == 1 ? owner.radiationHeight : owner.sourceRads;
            public void SetSliderValue(float value, int index)
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) return;
                if (setting == 0) owner.reach = Mathf.RoundToInt(Mathf.Clamp(value, 2f, 60f) / 2f) * 2;
                else if (setting == 1) owner.radiationHeight = Mathf.RoundToInt((Mathf.Clamp(value, 3f, 15f) - 1f) / 2f) * 2 + 1;
                else if (setting == 2) owner.sourceRads = Mathf.Round(Mathf.Clamp(value, 25f, 2000f) / 25f) * 25f;
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
                    default: return "";
                }
            }
        }
    }
}
