using System;
using System.Collections;
using System.Collections.Generic;
using Delaunay.Geo;
using HarmonyLib;
using Klei;
using KSerialization;
using OmniMod.Core.Logging;
using ProcGen;
using STRINGS;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 【折叠空间生态巨舱】核心控制器：
    /// 1. 建造完成后自动开辟 32x32（内部 30x30 全真空净空，仅放置 1 个舱内门）的独立空间，供玩家自由铺设玻璃砖与顶部光伏板。
    /// 2. 自动配置内部区域为 RocketInterior（防太空暴露吞噬气液）并赋予高强度顶部阳光（80,000 Lux），支持舱内顶部光伏发电。
    /// 3. 自动将舱外门与舱内门（ClustercraftInteriorDoor）进行双向导航（NavTeleporter）、隐形落脚点（FakeFloorAdder）与通行权限（AssignmentGroupController）配对。
    /// 4. 舱外底部电源接口与舱内玩家建造的任意原版【电源插座】（RocketInteriorPowerPlug）通过虚拟电路键直连，实现舱内外电网无缝互通。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class OmniPocketHabitat : KMonoBehaviour, ISaveLoadable, ISidescreenButtonControl, ISim1000ms
    {
        [Serialize]
        public int targetWorldId = -1;

        public Storage LiquidInStorage { get; private set; }
        public Storage LiquidOutStorage { get; private set; }
        public Storage GasInStorage { get; private set; }
        public Storage GasOutStorage { get; private set; }
        public ConduitConsumer LiquidConsumer { get; private set; }
        public ConduitConsumer GasConsumer { get; private set; }

        private static readonly Dictionary<int, OmniPocketHabitat> HabitatsByWorld = new Dictionary<int, OmniPocketHabitat>();

        public static OmniPocketHabitat GetHabitatForWorld(int worldId)
        {
            if (worldId < 0) return null;
            if (HabitatsByWorld.TryGetValue(worldId, out var hab) && hab != null && !Util.IsNullOrDestroyed(hab))
            {
                return hab;
            }
            if (ClusterManager.Instance != null)
            {
                WorldContainer world = ClusterManager.Instance.GetWorld(worldId);
                if (world != null)
                {
                    hab = world.GetComponent<OmniPocketHabitat>();
                    if (hab != null && !Util.IsNullOrDestroyed(hab))
                    {
                        HabitatsByWorld[worldId] = hab;
                        return hab;
                    }
                }
            }
            return null;
        }

        /// <summary>生态舱内部世界 → 舱体建筑所在的外部世界 ID（不是生态舱则返回 -1）。</summary>
        public static int GetExteriorWorldId(int interiorWorldId)
        {
            OmniPocketHabitat hab = GetHabitatForWorld(interiorWorldId);
            if (hab == null)
            {
                return -1;
            }
            int exterior = ClusterUtil.GetMyWorldId(hab);
            if (exterior >= 0 && exterior != ClusterManager.INVALID_WORLD_IDX && exterior != interiorWorldId)
            {
                return exterior;
            }
            WorldContainer targetWorld = hab.GetTargetWorld();
            if (targetWorld != null && targetWorld.ParentWorldId >= 0 && targetWorld.ParentWorldId != interiorWorldId)
            {
                return targetWorld.ParentWorldId;
            }
            return (ClusterManager.Instance != null && ClusterManager.Instance.GetStartWorld() != null)
                ? ClusterManager.Instance.GetStartWorld().id
                : -1;
        }

        public static bool IsPocketHabitatWorld(WorldContainer world)
        {
            return OmniPocketHabitatPatches.IsPocketHabitatWorld(world);
        }

        public static bool IsPocketHabitatWorld(int worldId)
        {
            return OmniPocketHabitatPatches.IsPocketHabitatWorld(worldId);
        }

        /// <summary>收集建造在指定外部世界上的所有生态舱的内部世界 ID。</summary>
        public static void CollectInteriorWorldsOf(int exteriorWorldId, List<int> dst)
        {
            if (exteriorWorldId < 0 || dst == null)
            {
                return;
            }
            foreach (KeyValuePair<int, OmniPocketHabitat> kvp in HabitatsByWorld)
            {
                OmniPocketHabitat hab = kvp.Value;
                if (hab != null && !Util.IsNullOrDestroyed(hab) && kvp.Key != exteriorWorldId && ClusterUtil.GetMyWorldId(hab) == exteriorWorldId)
                {
                    dst.Add(kvp.Key);
                }
            }
        }

#pragma warning disable CS0649
        [MyCmpReq]
        private NavTeleporter navTeleporter;

        [MyCmpGet]
        private AssignmentGroupController assignmentGroupController;
#pragma warning restore CS0649

        private ClustercraftInteriorDoor pairedInteriorDoor;
        private bool isInitializingWorld;
        private bool hasInitializedWorld;
        private bool isStampingTemplate;
        [Serialize]
        private bool hasCleanedLegacyWalls;
        private OverlayScreen overlayScreen;
        private KBatchedAnimController habitatAnim;

        public string SidescreenTitle => ModStrings.T("折叠空间生态巨舱", "Omni Pocket Hyper-Habitat").Resolve();
        public string SidescreenButtonText => ModStrings.T("进入舱内 (View Interior)", "View Interior").Resolve();
        public string SidescreenButtonTooltip => ModStrings.T("将镜头视角切换至该生态舱内部空间", "Switch camera view to the habitat interior").Resolve();

        public bool SidescreenEnabled() => true;
        public bool SidescreenButtonInteractable() => true;
        public int ButtonSideScreenSortOrder() => 20;
        public int HorizontalGroupID() => -1;
        public void SetButtonTextOverride(ButtonMenuTextOverride textOverride) { }

        public void OnSidescreenButtonPressed()
        {
            if (targetWorldId < 0)
            {
                EnsurePocketWorldInitialized();
            }

            if (targetWorldId < 0 || ClusterManager.Instance == null)
            {
                return;
            }

            if (pairedInteriorDoor == null)
            {
                PairWithInteriorDoor();
            }

            WorldContainer targetWorld = GetTargetWorld();
            if (targetWorld == null)
            {
                return;
            }

            ClusterManager.Instance.SetActiveWorld(targetWorldId);
            ManagementMenu.Instance?.CloseAll();

            if (pairedInteriorDoor != null)
            {
                CameraController.Instance?.SetTargetPos(pairedInteriorDoor.transform.position, 8f, true);
            }
            else
            {
                Vector2 center = (targetWorld.minimumBounds + targetWorld.maximumBounds) * 0.5f;
                CameraController.Instance?.SetTargetPos(new Vector3(center.x, center.y, 0f), 12f, true);
            }
        }

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Ownable ownable = GetComponent<Ownable>();
            if (ownable != null && string.IsNullOrEmpty(ownable.slotID))
            {
                ownable.slotID = Db.Get().AssignableSlots.HabitatModule.Id;
                ownable.canBePublic = false;
            }
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();

            // 绑定外部液体/气体输入与输出储罐及管线消费者，供内部 RocketConduitStorageAccess 跨维管线传输液体（如咸乳）与气体
            foreach (ConduitConsumer consumer in GetComponents<ConduitConsumer>())
            {
                if (consumer == null || consumer.storage == null) continue;
                if (consumer.conduitType == ConduitType.Liquid)
                {
                    LiquidInStorage = consumer.storage;
                    LiquidConsumer = consumer;
                }
                else if (consumer.conduitType == ConduitType.Gas)
                {
                    GasInStorage = consumer.storage;
                    GasConsumer = consumer;
                }
            }
            foreach (ConduitDispenser dispenser in GetComponents<ConduitDispenser>())
            {
                if (dispenser == null || dispenser.storage == null) continue;
                if (dispenser.conduitType == ConduitType.Liquid) LiquidOutStorage = dispenser.storage;
                else if (dispenser.conduitType == ConduitType.Gas) GasOutStorage = dispenser.storage;
            }

            // 外部入口使用 1x3 窄舱门；读入旧存档时也覆盖旧的 grounded 播放状态。
            KBatchedAnimController kbac = GetComponent<KBatchedAnimController>();
            if (kbac != null)
            {
                OmniPocketHabitatConfig.ConfigureExteriorAnimation(gameObject);
                kbac.enabled = true;
                kbac.Play(OmniPocketHabitatConfig.ExteriorAnimState, KAnim.PlayMode.Loop);
            }

            habitatAnim = kbac;
            overlayScreen = OverlayScreen.Instance;
            if (overlayScreen != null)
            {
                overlayScreen.OnOverlayChanged += OnUtilityOverlayChanged;
                OnUtilityOverlayChanged(overlayScreen.GetMode());
            }

            EnsurePocketWorldInitialized();

            if (targetWorldId >= 0)
            {
                HabitatsByWorld[targetWorldId] = this;
            }

            RefreshConduitDemandState();
        }

        public void Sim1000ms(float dt)
        {
            if (targetWorldId < 0)
            {
                EnsurePocketWorldInitialized();
            }
            if (targetWorldId >= 0 && pairedInteriorDoor == null)
            {
                PairWithInteriorDoor();
            }
            RefreshConduitDemandState();
        }

        /// <summary>
        /// 检查生态舱内部是否建造了对应类型的流体出口（RocketConduitReceiver 或 RocketConduitStorageAccess）。
        /// 若舱内未建造流体出口，则外部管网消费者容量与速率归零，实现 100% 管道无损路过直通。
        /// </summary>
        public bool HasInteriorFluidOutlet(ConduitType type)
        {
            if (targetWorldId < 0 || ClusterManager.Instance == null) return false;

            // 1. 原版火箭管线接收器（RocketConduitReceiver，例如 RocketInteriorLiquidOutputPort）
            // 直接索引内部 Items 列表，零堆内存分配，瞬间完成
            if (Components.RocketConduitReceivers != null)
            {
                for (int i = 0; i < Components.RocketConduitReceivers.Count; i++)
                {
                    RocketConduitReceiver receiver = Components.RocketConduitReceivers[i];
                    if (receiver != null && !Util.IsNullOrDestroyed(receiver) &&
                        receiver.GetMyWorldId() == targetWorldId &&
                        receiver.conduitPortInfo != null && receiver.conduitPortInfo.conduitType == type)
                    {
                        return true;
                    }
                }
            }

            // 2. 舱内货仓存取接口（RocketConduitStorageAccess，例如 RocketInteriorLiquidOutput）
            // 从常驻静态集合检索，彻底告别每秒遍历全图上万建筑与高频 GC 垃圾堆积
            if (OmniPocketHabitatPatches.ActiveStorageAccesses.Count > 0)
            {
                var targetCargo = type == ConduitType.Liquid ? CargoBay.CargoType.Liquids : CargoBay.CargoType.Gasses;
                OmniPocketHabitatPatches.ActiveStorageAccesses.RemoveWhere(a => a == null || Util.IsNullOrDestroyed(a));
                foreach (RocketConduitStorageAccess access in OmniPocketHabitatPatches.ActiveStorageAccesses)
                {
                    if (access.GetMyWorldId() == targetWorldId && access.cargoType == targetCargo && access.targetLevel > 0.01f)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 动态刷新管道流体需求：
        /// 内部无出口需求时：禁用 Consumer，容量与速率置 0，零截留，外部液体/气体直接路过；
        /// 内部有出口需求时：启用 Consumer，对齐原版火箭标准（液 10kg，气 1kg），按需抽取引流。
        /// </summary>
        public void RefreshConduitDemandState()
        {
            if (LiquidConsumer != null)
            {
                bool hasLiquid = HasInteriorFluidOutlet(ConduitType.Liquid);
                if (LiquidConsumer.enabled != hasLiquid)
                {
                    LiquidConsumer.enabled = hasLiquid;
                }
                float targetRate = hasLiquid ? 10f : 0f;
                float targetCap = hasLiquid ? 10f : 0f;
                if (LiquidConsumer.consumptionRate != targetRate || LiquidConsumer.capacityKG != targetCap)
                {
                    LiquidConsumer.consumptionRate = targetRate;
                    LiquidConsumer.capacityKG = targetCap;
                }
                if (LiquidInStorage != null && LiquidInStorage.capacityKg != targetCap)
                {
                    LiquidInStorage.capacityKg = targetCap;
                }

                // 容灾回吐：如果舱内没有出水口，但 LiquidInStorage 滞留了旧版吞噬的液体，自动安全转移回吐至 LiquidOutStorage 排回管网
                if (!hasLiquid && LiquidInStorage != null && LiquidInStorage.MassStored() > 0.01f && LiquidOutStorage != null)
                {
                    for (int i = LiquidInStorage.items.Count - 1; i >= 0; i--)
                    {
                        GameObject go = LiquidInStorage.items[i];
                        if (go == null) continue;
                        Pickupable p = go.GetComponent<Pickupable>();
                        if (p != null)
                        {
                            Pickupable taken = p.Take(LiquidOutStorage.RemainingCapacity());
                            if (taken != null)
                            {
                                LiquidOutStorage.Store(taken.gameObject, hide_popups: true);
                            }
                        }
                    }
                }
            }

            if (GasConsumer != null)
            {
                bool hasGas = HasInteriorFluidOutlet(ConduitType.Gas);
                if (GasConsumer.enabled != hasGas)
                {
                    GasConsumer.enabled = hasGas;
                }
                float targetRate = hasGas ? 1f : 0f;
                float targetCap = hasGas ? 1f : 0f;
                if (GasConsumer.consumptionRate != targetRate || GasConsumer.capacityKG != targetCap)
                {
                    GasConsumer.consumptionRate = targetRate;
                    GasConsumer.capacityKG = targetCap;
                }
                if (GasInStorage != null && GasInStorage.capacityKg != targetCap)
                {
                    GasInStorage.capacityKg = targetCap;
                }

                if (!hasGas && GasInStorage != null && GasInStorage.MassStored() > 0.01f && GasOutStorage != null)
                {
                    for (int i = GasInStorage.items.Count - 1; i >= 0; i--)
                    {
                        GameObject go = GasInStorage.items[i];
                        if (go == null) continue;
                        Pickupable p = go.GetComponent<Pickupable>();
                        if (p != null)
                        {
                            Pickupable taken = p.Take(GasOutStorage.RemainingCapacity());
                            if (taken != null)
                            {
                                GasOutStorage.Store(taken.gameObject, hide_popups: true);
                            }
                        }
                    }
                }
            }
        }

        private void OnUtilityOverlayChanged(HashedString mode)
        {
            if (habitatAnim == null) return;
            bool utilities = mode == OverlayModes.Power.ID || mode == OverlayModes.LiquidConduits.ID ||
                             mode == OverlayModes.GasConduits.ID || mode == OverlayModes.Logic.ID ||
                             mode == OverlayModes.SolidConveyor.ID;
            // BuildingBack(18) 仍在气管(3)、水管(5)、电线(10) 前方。
            // 火箭贴图的 FG/SH 子动画也必须退层，单改根动画无法消除遮挡。
            Grid.SceneLayer body = utilities ? Grid.SceneLayer.Gas : Grid.SceneLayer.BuildingBack;
            Grid.SceneLayer front = utilities ? Grid.SceneLayer.Gas : Grid.SceneLayer.BuildingFront;
            if (habitatAnim.sceneLayer != body) habitatAnim.SetSceneLayer(body);
            habitatAnim.SetFGLayer(front);
            // 子动画的批次按深度分组，移动 Transform 后也要像原版管网视图一样重新排序。
            foreach (KBatchedAnimController child in habitatAnim.GetComponentsInChildren<KBatchedAnimController>())
            {
                if (child == habitatAnim) continue;
                child.animWidth = habitatAnim.animWidth;
                child.animHeight = habitatAnim.animHeight;
                if (!child.enabled) continue;
                child.enabled = false;
                child.enabled = true;
            }
        }

        /// <summary>
        /// 幂等初始化内部世界（确保无论 OnSpawn 顺序如何，WireUtilitySemiVirtualNetworkLink 与本体只执行一次初始化，绝不重复印章）。
        /// </summary>
        public void EnsurePocketWorldInitialized()
        {
            if (hasInitializedWorld || isInitializingWorld)
            {
                return;
            }

            isInitializingWorld = true;
            try
            {
                EnsureExteriorAssignmentGroupReady();

                // 清理旧版残留的 OrbitalMechanics 组件（防止读档时内部空引用崩溃）
                OrbitalMechanics orbital = GetComponent<OrbitalMechanics>();
                if (!Util.IsNullOrDestroyed(orbital))
                {
                    UnityEngine.Object.DestroyImmediate(orbital);
                }

                WorldContainer existingWorld = GetComponent<WorldContainer>();
                if (targetWorldId < 0)
                {
                    if (existingWorld != null && existingWorld.id >= 0)
                    {
                        // 容灾恢复：存档中已存在挂载于本建筑的 WorldContainer，直接接管
                        targetWorldId = existingWorld.id;
                        int parentWorldId = ClusterUtil.GetMyWorldId(this);
                        if (parentWorldId != -1)
                        {
                            existingWorld.SetParentIdx(parentWorldId);
                        }

                        OmniPocketHabitatPatches.PurgePocketWorldDiagnostics(targetWorldId);
                        ConfigurePocketWorldZoneAndSunlight(existingWorld);
                        CleanupLegacyWallsAndDuplicateDoors(existingWorld);
                        PairWithInteriorDoor();

                        if (pairedInteriorDoor == null)
                        {
                            StampPocketTemplate(existingWorld);
                        }

                        ModLog.Info($"[折叠生态舱] 已接管并修复存档中的内部空间 (World ID: {targetWorldId})。");
                    }
                    else
                    {
                        CreatePocketWorld();
                    }
                }
                else
                {
                    WorldContainer world = GetTargetWorld();
                    if (world != null)
                    {
                        int myWorldId = ClusterUtil.GetMyWorldId(this);
                        if (myWorldId != -1)
                        {
                            world.SetParentIdx(myWorldId);
                        }
                        OmniPocketHabitatPatches.PurgePocketWorldDiagnostics(world.id);
                        ConfigurePocketWorldZoneAndSunlight(world);
                        CleanupLegacyWallsAndDuplicateDoors(world);
                    }
                    StartCoroutine(DelayedPairDoor());
                }

                hasInitializedWorld = targetWorldId >= 0;
            }
            finally
            {
                isInitializingWorld = false;
            }
        }

        public WorldContainer GetTargetWorld()
        {
            if (targetWorldId >= 0 && ClusterManager.Instance != null)
            {
                return ClusterManager.Instance.GetWorld(targetWorldId);
            }
            return GetComponent<WorldContainer>();
        }

        private void CreatePocketWorld()
        {
            try
            {
                if (!OmniPocketSpaceManager.TryAllocatePocketSpace(out Vector2I size, out Vector2I offset))
                {
                    ModLog.Warn("[折叠生态舱] 创建内部空间失败：星系底层网格空间已彻底耗尽。");
                    return;
                }

                int newWorldId = OmniPocketHabitatPatches.GetNextWorldId(ClusterManager.Instance);
                gameObject.AddOrGet<WorldInventory>();
                WorldContainer world = gameObject.AddOrGet<WorldContainer>();
                world.SetRocketInteriorWorldDetails(newWorldId, size, offset);

                Vector2I max = offset + size;
                for (int y = offset.y; y < max.y; y++)
                {
                    for (int x = offset.x; x < max.x; x++)
                    {
                        int cell = Grid.XYToCell(x, y);
                        Grid.WorldIdx[cell] = (byte)newWorldId;
                        Pathfinding.Instance?.AddDirtyNavGridCell(cell);
                    }
                }

                targetWorldId = world.id;
                HabitatsByWorld[targetWorldId] = this;
                int parentWorldId = ClusterUtil.GetMyWorldId(this);
                if (parentWorldId != -1)
                {
                    world.SetParentIdx(parentWorldId);
                }

                StampPocketTemplate(world);

                ClusterManager.Instance.BoxingTrigger((int)GameHashes.WorldAdded, world.id);
                OmniPocketHabitatPatches.PurgePocketWorldDiagnostics(world.id);

                ModLog.Info($"[折叠生态舱] 成功创建内部纯净空间 (World ID: {targetWorldId}, Offset: {offset}, Size: {size})。");
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[折叠生态舱] 创建内部世界异常: {ex}");
            }
        }

        private void StampPocketTemplate(WorldContainer world)
        {
            if (world == null || isStampingTemplate)
            {
                return;
            }

            isStampingTemplate = true;
            world.PlaceInteriorTemplate(OmniPocketTemplate.TemplateName, OnTemplateStamped);
            ConfigurePocketWorldZoneAndSunlight(world);
        }

        private void OnTemplateStamped()
        {
            isStampingTemplate = false;
            WorldContainer world = GetTargetWorld();
            if (world != null)
            {
                ConfigurePocketWorldZoneAndSunlight(world);
                CleanupLegacyWallsAndDuplicateDoors(world);
            }
            PairWithInteriorDoor();
        }

        /// <summary>
        /// 将内部 30x30 净空区域注册为 SubWorld.ZoneType.RocketInterior（防止无背板时气液因太空暴露消失），
        /// 同时开启顶部高强度阳光（80,000 Lux），支持玩家在顶部铺设玻璃砖与光伏发电板。
        /// </summary>
        public static void ConfigurePocketWorldZoneAndSunlight(WorldContainer world)
        {
            if (world == null)
            {
                return;
            }

            // 1. 赋予顶部充足阳光（80,000 Lux，满足原版光伏板满功率发电需求）
            world.sunlight = TUNING.FIXEDTRAITS.SUNLIGHT.VERY_HIGH;
            world.sunlightFixedTrait = TUNING.FIXEDTRAITS.SUNLIGHT.NAME.VERY_HIGH;
            OmniPocketHabitatPatches.RefreshTimeOfDaySunlight();

            // 2. 构建覆盖内部 30x30 可建造区域的 OverworldCell 多边形（避免因去除 RocketWallTile 导致全图变成太空暴露）
            Vector2I offset = world.WorldOffset;
            Vector2I size = world.WorldSize;
            var corners = new List<Vector2>(4)
            {
                new Vector2(offset.x + 1f, offset.y + 1f),
                new Vector2(offset.x + size.x - 1f, offset.y + 1f),
                new Vector2(offset.x + size.x - 1f, offset.y + size.y - 1f),
                new Vector2(offset.x + 1f, offset.y + size.y - 1f)
            };
            var poly = new Polygon(corners);

            ref WorldDetailSave.OverworldCell overworldCell = ref OmniPocketHabitatPatches.OverworldCellRef(world);
            if (overworldCell == null)
            {
                overworldCell = new WorldDetailSave.OverworldCell();
            }

            overworldCell.poly = poly;
            overworldCell.zoneType = SubWorld.ZoneType.RocketInterior;
            if (overworldCell.tags == null)
            {
                overworldCell.tags = new TagSet { WorldGenTags.RocketInterior };
            }
            else if (!overworldCell.tags.Contains(WorldGenTags.RocketInterior))
            {
                overworldCell.tags.Add(WorldGenTags.RocketInterior);
            }

            var overworldCells = SaveLoader.Instance?.clusterDetailSave?.overworldCells;
            if (overworldCells != null && !overworldCells.Contains(overworldCell))
            {
                overworldCells.Add(overworldCell);
            }

            // 3. 将内部 30x30 格设为 RocketInterior，外圈 1 格边界保持 Space
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    int cell = Grid.XYToCell(offset.x + x, offset.y + y);
                    if (!Grid.IsValidCell(cell))
                    {
                        continue;
                    }

                    bool isInterior = x >= 1 && x < size.x - 1 && y >= 1 && y < size.y - 1;
                    byte simZone = isInterior ? (byte)SubWorld.ZoneType.RocketInterior : byte.MaxValue;
                    SubWorld.ZoneType renderZone = isInterior ? SubWorld.ZoneType.RocketInterior : SubWorld.ZoneType.Space;

                    SimMessages.ModifyCellWorldZone(cell, simZone);
                    if (World.Instance?.zoneRenderData?.worldZoneTypes != null && cell < World.Instance.zoneRenderData.worldZoneTypes.Length)
                    {
                        World.Instance.zoneRenderData.worldZoneTypes[cell] = renderZone;
                    }
                }
            }
        }

        /// <summary>
        /// 清理旧版残留的火箭壁板（RocketWallTile）与重复印章产生的多余舱内门，确保舱内只有 1 个门且无预置壁板。
        /// </summary>
        private void CleanupLegacyWallsAndDuplicateDoors(WorldContainer world)
        {
            if (world == null || hasCleanedLegacyWalls)
            {
                return;
            }

            int worldId = world.id;

            // 1. 移除旧版模板遗留的 RocketWallTile，将空间完全归还给玩家铺设玻璃砖
            var wallsToDestroy = new List<BuildingComplete>();
            foreach (BuildingComplete building in Components.BuildingCompletes.Items)
            {
                if (building != null && ClusterUtil.GetMyWorldId(building) == worldId && building.PrefabID() == "RocketWallTile")
                {
                    wallsToDestroy.Add(building);
                }
            }

            foreach (BuildingComplete wall in wallsToDestroy)
            {
                int cell = Grid.PosToCell(wall);
                Util.KDestroyGameObject(wall.gameObject);
                if (Grid.IsValidCell(cell))
                {
                    SimMessages.ReplaceElement(cell, SimHashes.Vacuum, CellEventLogger.Instance.Scenario, 0f);
                }
            }

            // 2. 移除因旧版重复印章产生的多余 ClustercraftInteriorDoor（只保留第 1 个）
            ClustercraftInteriorDoor primaryDoor = null;
            var extraDoors = new List<ClustercraftInteriorDoor>();
            foreach (ClustercraftInteriorDoor door in Components.ClusterCraftInteriorDoors.Items)
            {
                if (door != null && !Util.IsNullOrDestroyed(door) && ClusterUtil.GetMyWorldId(door) == worldId)
                {
                    if (primaryDoor == null)
                    {
                        primaryDoor = door;
                    }
                    else
                    {
                        extraDoors.Add(door);
                    }
                }
            }

            foreach (ClustercraftInteriorDoor extra in extraDoors)
            {
                Components.ClusterCraftInteriorDoors.Remove(extra);
                Util.KDestroyGameObject(extra.gameObject);
            }

            hasCleanedLegacyWalls = true;
        }

        private IEnumerator DelayedPairDoor()
        {
            yield return null;
            if (isStampingTemplate)
            {
                yield break;
            }

            PairWithInteriorDoor();
            if (pairedInteriorDoor == null && !isStampingTemplate)
            {
                WorldContainer world = GetTargetWorld();
                if (world != null)
                {
                    ModLog.Info($"[折叠生态舱] 检测到内部空间 (World {targetWorldId}) 缺少舱内门，正在自动补齐标准模板...");
                    StampPocketTemplate(world);
                }
            }
        }

        public void EnsureExteriorAssignmentGroupReady()
        {
            if (assignmentGroupController == null)
            {
                assignmentGroupController = GetComponent<AssignmentGroupController>();
            }

            if (assignmentGroupController == null || Game.Instance?.assignmentManager == null)
            {
                return;
            }

            assignmentGroupController.generateGroupOnStart = true;
            if (string.IsNullOrEmpty(assignmentGroupController.AssignmentGroupID))
            {
                KPrefabID kpid = GetComponent<KPrefabID>();
                string groupId = $"{kpid.PrefabID()}_{kpid.InstanceID}_assignmentGroup";
                assignmentGroupController.SetGroupID(groupId);
            }

            Game.Instance.assignmentManager.TryCreateAssignmentGroup(
                assignmentGroupController.AssignmentGroupID,
                Array.Empty<IAssignableIdentity>(),
                gameObject.GetProperName());
        }

        public void PairWithInteriorDoor()
        {
            if (targetWorldId < 0)
            {
                return;
            }

            WorldContainer targetWorld = GetTargetWorld();
            if (targetWorld != null)
            {
                CleanupLegacyWallsAndDuplicateDoors(targetWorld);
            }

            pairedInteriorDoor = null;
            foreach (ClustercraftInteriorDoor door in Components.ClusterCraftInteriorDoors.Items)
            {
                if (door != null && !Util.IsNullOrDestroyed(door) && ClusterUtil.GetMyWorldId(door) == targetWorldId)
                {
                    pairedInteriorDoor = door;
                    break;
                }
            }

            if (pairedInteriorDoor == null)
            {
                return;
            }

            // 1. 同步内外门的通行权限组（确保 AssignmentGroupID 绝不为空且已在 AssignmentManager 中注册）
            EnsureExteriorAssignmentGroupReady();
            var interiorAgc = pairedInteriorDoor.GetComponent<AssignmentGroupController>();
            if (interiorAgc != null && assignmentGroupController != null && !string.IsNullOrEmpty(assignmentGroupController.AssignmentGroupID))
            {
                interiorAgc.SetGroupID(assignmentGroupController.AssignmentGroupID);
            }

            // 2. 为悬空舱内门底部挂载隐形落脚点（FakeFloorAdder），使复制人在玩家未铺玻璃砖前也能直接进出站立，同时不阻碍在门下建造砖块
            FakeFloorAdder fakeFloor = pairedInteriorDoor.gameObject.AddOrGet<FakeFloorAdder>();
            fakeFloor.floorOffsets = new[]
            {
                new CellOffset(0, -1),
                new CellOffset(1, -1)
            };
            fakeFloor.initiallyActive = true;
            fakeFloor.SetFloor(true);

            int doorCell = Grid.PosToCell(pairedInteriorDoor);
            if (Grid.IsValidCell(doorCell))
            {
                Pathfinding.Instance?.AddDirtyNavGridCell(doorCell);
                int exitCell = Grid.OffsetCell(doorCell, new CellOffset(1, 0));
                if (Grid.IsValidCell(exitCell))
                {
                    Pathfinding.Instance?.AddDirtyNavGridCell(exitCell);
                }
            }

            // 3. 建立双向导航传送连接
            NavTeleporter interiorNav = pairedInteriorDoor.GetComponent<NavTeleporter>();
            if (navTeleporter != null && interiorNav != null)
            {
                navTeleporter.TwoWayTarget(interiorNav);
            }

            // 4. 在舱内门上挂载【返回舱外】快捷视角切换按钮
            var exitBtn = pairedInteriorDoor.gameObject.AddOrGet<OmniPocketInteriorExitButton>();
            exitBtn.exteriorHabitat = this;

            int parentWorldId = ClusterUtil.GetMyWorldId(this);
            if (targetWorld != null && parentWorldId != -1)
            {
                targetWorld.SetParentIdx(parentWorldId);
            }
        }

        protected override void OnCleanUp()
        {
            if (overlayScreen != null) overlayScreen.OnOverlayChanged -= OnUtilityOverlayChanged;
            overlayScreen = null;
            habitatAnim = null;
            if (targetWorldId >= 0)
            {
                HabitatsByWorld.Remove(targetWorldId);
            }

            // 仅在游戏正常运行期间拆除建筑时销毁口袋世界；退出游戏或读档销毁场景对象时不执行
            if (!App.IsExiting && Game.Instance != null && !Game.IsQuitting() && targetWorldId >= 0 && ClusterManager.Instance != null)
            {
                DestroyPocketWorldSafely();
            }

            base.OnCleanUp();
        }

        private void DestroyPocketWorldSafely()
        {
            try
            {
                WorldContainer world = ClusterManager.Instance.GetWorld(targetWorldId);
                if (world == null)
                {
                    return;
                }

                int parentWorldId = ClusterUtil.GetMyWorldId(this);
                if (ClusterManager.Instance.activeWorldId == targetWorldId)
                {
                    int fallbackWorldId = (parentWorldId >= 0 && parentWorldId != targetWorldId)
                        ? parentWorldId
                        : ClusterManager.Instance.GetStartWorld().id;
                    ClusterManager.Instance.SetActiveWorld(fallbackWorldId);
                }

                if (navTeleporter != null)
                {
                    navTeleporter.Deregister();
                }

                Vector3 ejectPos = transform.position;
                world.EjectAllDupes(ejectPos);
                world.CancelChores();
                world.DestroyWorldBuildings(out HashSet<int> noRefundTiles);
                world.ClearWorldZones();

                if (noRefundTiles == null)
                {
                    noRefundTiles = new HashSet<int>();
                }

                // 将内部空间所有中子物质（Unobtanium）格全部加入不退还列表，
                // 彻底阻止 TransferLiquidsSolidsAndGases 将世界边界中子质转为成千上万公斤碎片吐出舱外
                int minX = (int)world.minimumBounds.x;
                int minY = (int)world.minimumBounds.y;
                int maxX = (int)world.maximumBounds.x;
                int maxY = (int)world.maximumBounds.y;
                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        int cell = Grid.XYToCell(x, y);
                        if (!Grid.IsValidCell(cell)) continue;
                        Element elem = Grid.Element[cell];
                        if (elem != null && elem.id == SimHashes.Unobtanium)
                        {
                            noRefundTiles.Add(cell);
                        }
                    }
                }

                // 清理内部可能遗留的中子物质碎片，确保绝不转移到外部世界
                var pickupables = Components.Pickupables.GetWorldItems(targetWorldId);
                if (pickupables != null)
                {
                    for (int i = pickupables.Count - 1; i >= 0; i--)
                    {
                        Pickupable p = pickupables[i];
                        if (p != null && p.PrimaryElement != null && p.PrimaryElement.ElementID == SimHashes.Unobtanium)
                        {
                            Util.KDestroyGameObject(p.gameObject);
                        }
                    }
                }

                world.TransferResourcesToParentWorld(ejectPos + new Vector3(0f, 0.5f, 0f), noRefundTiles);

                var overworldCells = SaveLoader.Instance?.clusterDetailSave?.overworldCells;
                ref WorldDetailSave.OverworldCell overworldCell = ref OmniPocketHabitatPatches.OverworldCellRef(world);
                if (overworldCells != null && overworldCell != null)
                {
                    overworldCells.Remove(overworldCell);
                }

                int worldIdToClean = targetWorldId;
                Grid.FreeGridSpace(world.WorldSize, world.WorldOffset);
                ClusterManager.Instance.UnregisterWorldContainer(world);
                OmniPocketHabitatPatches.CleanupDestroyedWorldTrackersAndDiagnostics(worldIdToClean);

                OrbitalMechanics orbital = GetComponent<OrbitalMechanics>();
                if (!Util.IsNullOrDestroyed(orbital))
                {
                    UnityEngine.Object.Destroy(orbital);
                }

                ModLog.Info($"[折叠生态舱] 建筑已拆除，内部空间 (World {worldIdToClean}) 已安全回收并将物资退回舱外。");
                targetWorldId = -1;
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[折叠生态舱] 回收内部空间异常: {ex}");
            }
        }
    }

    /// <summary>
    /// 挂载在舱内门（ClustercraftInteriorDoor）上的侧边栏快捷按钮，支持在舱内一键返回舱外。
    /// </summary>
    public class OmniPocketInteriorExitButton : KMonoBehaviour, ISidescreenButtonControl
    {
        public OmniPocketHabitat exteriorHabitat;

        public string SidescreenTitle => ModStrings.T("折叠空间生态巨舱", "Omni Pocket Hyper-Habitat").Resolve();
        public string SidescreenButtonText => ModStrings.T("返回舱外 (View Exterior)", "View Exterior").Resolve();
        public string SidescreenButtonTooltip => ModStrings.T("将镜头视角切回舱外基地入口", "Return camera view to the exterior habitat entrance").Resolve();

        public bool SidescreenEnabled() => exteriorHabitat != null;
        public bool SidescreenButtonInteractable() => exteriorHabitat != null;
        public int ButtonSideScreenSortOrder() => 20;
        public int HorizontalGroupID() => -1;
        public void SetButtonTextOverride(ButtonMenuTextOverride textOverride) { }

        public void OnSidescreenButtonPressed()
        {
            if (exteriorHabitat == null || ClusterManager.Instance == null)
            {
                return;
            }

            int exteriorWorldId = ClusterUtil.GetMyWorldId(exteriorHabitat);
            if (exteriorWorldId >= 0)
            {
                ClusterManager.Instance.SetActiveWorld(exteriorWorldId);
                ManagementMenu.Instance?.CloseAll();
                CameraController.Instance?.SetTargetPos(exteriorHabitat.transform.position, 8f, true);
            }
        }
    }

    /// <summary>
    /// 生态巨舱底层兼容与跨维电网桥接补丁集：
    /// 1. 将舱外建筑电口与舱内【电源插座】（RocketInteriorPowerPlug）绑定至同一 VirtualCircuitKey（WorldContainer），实现舱内外电力直通。
    /// 2. 屏蔽原版针对真火箭（Clustercraft）的专属音效、背景星图飞行渲染、火箭燃料诊断及舱内门侧边栏，彻底消除空引用异常。
    /// 3. 对 AssignmentGroupController.OnSerialize 做防御性群组注册保底，防止任何未配对舱门导致存档崩溃。
    /// </summary>
    public static class OmniPocketHabitatPatches
    {
        [ThreadStatic]
        private static int suppressModuleInteriorDepth;

        public static readonly HashSet<RocketConduitStorageAccess> ActiveStorageAccesses =
            new HashSet<RocketConduitStorageAccess>();

        public static bool IsPocketHabitatWorld(int worldId)
        {
            if (worldId < 0) return false;
            return OmniPocketHabitat.GetHabitatForWorld(worldId) != null;
        }

        public static bool IsPocketHabitatWorld(WorldContainer world)
        {
            if (world == null) return false;
            if (world.GetComponent<Clustercraft>() != null) return false;
            return world.GetComponent<OmniPocketHabitat>() != null || OmniPocketHabitat.GetHabitatForWorld(world.id) != null;
        }

        // ==========================================
        // 1. 舱内外跨维电网直通补丁
        // ==========================================
        private static readonly AccessTools.FieldRef<WireUtilitySemiVirtualNetworkLink, object> VirtualCircuitKeyRef =
            AccessTools.FieldRefAccess<WireUtilitySemiVirtualNetworkLink, object>("<VirtualCircuitKey>k__BackingField");

        [HarmonyPatch(typeof(WireUtilitySemiVirtualNetworkLink), "OnSpawn")]
        public static class WireUtilitySemiVirtualNetworkLink_OnSpawn_Patch
        {
            public static void Prefix(WireUtilitySemiVirtualNetworkLink __instance)
            {
                if (__instance == null)
                {
                    return;
                }

                // 情况 A：挂载在舱外【折叠空间生态巨舱】本体上的电源接口
                OmniPocketHabitat habitat = __instance.GetComponent<OmniPocketHabitat>();
                if (habitat != null)
                {
                    habitat.EnsurePocketWorldInitialized();
                    WorldContainer targetWorld = habitat.GetTargetWorld();
                    if (targetWorld != null)
                    {
                        VirtualCircuitKeyRef(__instance) = targetWorld;
                    }
                    else
                    {
                        // 核心防线：若内部世界未就绪，使用自身建筑实例作为安全非空 key，杜绝 ArgumentNullException
                        VirtualCircuitKeyRef(__instance) = __instance.gameObject;
                    }
                    return;
                }

                // 情况 B：建造在生态舱内部空间的原版【电源插座】(RocketInteriorPowerPlug)
                WorldContainer myWorld = ClusterUtil.GetMyWorld(__instance);
                if (IsPocketHabitatWorld(myWorld))
                {
                    VirtualCircuitKeyRef(__instance) = (object)myWorld ?? __instance.gameObject;
                }
            }
        }

        // ==========================================
        // 2. 存档序列化权限组保底防护（防止 AssignmentGroupID 为空导致保存失败）
        // ==========================================
        [HarmonyPatch(typeof(AssignmentGroupController), "OnSerialize")]
        public static class AssignmentGroupController_OnSerialize_Patch
        {
            public static void Prefix(AssignmentGroupController __instance)
            {
                if (__instance == null || Game.Instance?.assignmentManager == null)
                {
                    return;
                }

                if (string.IsNullOrEmpty(__instance.AssignmentGroupID))
                {
                    WorldContainer myWorld = ClusterUtil.GetMyWorld(__instance);
                    OmniPocketHabitat habitat = myWorld != null ? myWorld.GetComponent<OmniPocketHabitat>() : __instance.GetComponent<OmniPocketHabitat>();
                    if (habitat != null)
                    {
                        habitat.EnsureExteriorAssignmentGroupReady();
                        var extAgc = habitat.GetComponent<AssignmentGroupController>();
                        if (extAgc != null && !string.IsNullOrEmpty(extAgc.AssignmentGroupID))
                        {
                            __instance.SetGroupID(extAgc.AssignmentGroupID);
                        }
                    }

                    if (string.IsNullOrEmpty(__instance.AssignmentGroupID))
                    {
                        KPrefabID kpid = __instance.GetComponent<KPrefabID>();
                        string fallbackId = kpid != null
                            ? $"{kpid.PrefabID()}_{kpid.InstanceID}_assignmentGroup"
                            : $"OmniFallback_{__instance.GetInstanceID()}_assignmentGroup";
                        __instance.SetGroupID(fallbackId);
                    }
                }

                if (!Game.Instance.assignmentManager.assignment_groups.ContainsKey(__instance.AssignmentGroupID))
                {
                    Game.Instance.assignmentManager.TryCreateAssignmentGroup(
                        __instance.AssignmentGroupID,
                        Array.Empty<IAssignableIdentity>(),
                        __instance.gameObject.GetProperName());
                }
            }
        }

        // ==========================================
        // 3. 屏蔽非火箭生态舱的 Clustercraft 强依赖逻辑
        // ==========================================
        [HarmonyPatch(typeof(WorldContainer), nameof(WorldContainer.IsModuleInterior), MethodType.Getter)]
        public static class WorldContainer_IsModuleInterior_Patch
        {
            public static void Postfix(WorldContainer __instance, ref bool __result)
            {
                if (suppressModuleInteriorDepth > 0 && __result && IsPocketHabitatWorld(__instance))
                {
                    __result = false;
                }
            }
        }

        [HarmonyPatch(typeof(PassengerRocketModule), "RefreshClusterStateForAudio")]
        public static class PassengerRocketModule_RefreshClusterStateForAudio_Patch
        {
            public static void Prefix() => suppressModuleInteriorDepth++;
            public static void Finalizer() => suppressModuleInteriorDepth--;
        }

        [HarmonyPatch(typeof(AmbienceManager.Quadrant), nameof(AmbienceManager.Quadrant.Update))]
        public static class AmbienceManager_Quadrant_Update_Patch
        {
            public static void Prefix() => suppressModuleInteriorDepth++;
            public static void Finalizer() => suppressModuleInteriorDepth--;
        }

        [HarmonyPatch(typeof(TerrainBG), "LateUpdate")]
        public static class TerrainBG_LateUpdate_Patch
        {
            public static void Prefix() => suppressModuleInteriorDepth++;
            public static void Finalizer() => suppressModuleInteriorDepth--;
        }

        [HarmonyPatch(typeof(ClusterManager), "UpdateWorldReverbSnapshot")]
        public static class ClusterManager_UpdateWorldReverbSnapshot_Patch
        {
            public static void Prefix() => suppressModuleInteriorDepth++;
            public static void Finalizer() => suppressModuleInteriorDepth--;
        }

        [HarmonyPatch(typeof(ClusterManager), nameof(ClusterManager.UpdateRocketInteriorAudio))]
        public static class ClusterManager_UpdateRocketInteriorAudio_Patch
        {
            public static void Prefix() => suppressModuleInteriorDepth++;
            public static void Finalizer() => suppressModuleInteriorDepth--;
        }

        [HarmonyPatch(typeof(ColonyAchievementTracker), "OnNewDay")]
        public static class ColonyAchievementTracker_OnNewDay_Patch
        {
            public static void Prefix() => suppressModuleInteriorDepth++;
            public static void Finalizer() => suppressModuleInteriorDepth--;
        }

        [HarmonyPatch(typeof(RocketConduitReceiver), "FindPartner")]
        public static class RocketConduitReceiver_FindPartner_Patch
        {
            public static bool Prefix(RocketConduitReceiver __instance)
            {
                if (__instance == null) return true;
                int worldId = __instance.GetMyWorldId();
                OmniPocketHabitat habitat = OmniPocketHabitat.GetHabitatForWorld(worldId);
                if (habitat != null)
                {
                    bool isLiquid = __instance.conduitPortInfo != null && __instance.conduitPortInfo.conduitType == ConduitType.Liquid;
                    Storage storage = isLiquid ? habitat.LiquidInStorage : habitat.GasInStorage;
                    if (storage != null)
                    {
                        __instance.SetStorage(storage);
                        habitat.RefreshConduitDemandState();
                        return false;
                    }
                }
                suppressModuleInteriorDepth++;
                return true;
            }

            public static void Finalizer()
            {
                if (suppressModuleInteriorDepth > 0)
                {
                    suppressModuleInteriorDepth--;
                }
            }
        }

        [HarmonyPatch(typeof(RocketConduitSender), "FindPartner")]
        public static class RocketConduitSender_FindPartner_Patch
        {
            private static readonly Type NestedPortType =
                AccessTools.Inner(typeof(RocketConduitSender), "ConduitPort");
            private static readonly System.Reflection.FieldInfo ConduitPortField =
                AccessTools.Field(typeof(RocketConduitSender), "conduitPort");

            public static bool Prefix(RocketConduitSender __instance)
            {
                if (__instance == null) return true;
                int worldId = __instance.GetMyWorldId();
                OmniPocketHabitat habitat = OmniPocketHabitat.GetHabitatForWorld(worldId);
                if (habitat != null)
                {
                    bool isLiquid = __instance.conduitPortInfo != null && __instance.conduitPortInfo.conduitType == ConduitType.Liquid;
                    Storage storage = isLiquid ? habitat.LiquidOutStorage : habitat.GasOutStorage;
                    if (storage != null && NestedPortType != null && ConduitPortField != null)
                    {
                        object port = Activator.CreateInstance(NestedPortType, __instance.gameObject, __instance.conduitPortInfo, storage);
                        ConduitPortField.SetValue(__instance, port);
                        __instance.AddConduitPortToNetwork();
                        return false;
                    }
                    return false;
                }
                suppressModuleInteriorDepth++;
                return true;
            }

            public static void Finalizer()
            {
                if (suppressModuleInteriorDepth > 0)
                {
                    suppressModuleInteriorDepth--;
                }
            }
        }

        private static readonly Func<ClusterManager, int> GetNextWorldIdDelegate =
            AccessTools.MethodDelegate<Func<ClusterManager, int>>(AccessTools.Method(typeof(ClusterManager), "GetNextWorldId"));

        private static readonly Func<TimeOfDay, float> UpdateSunlightIntensityDelegate =
            AccessTools.MethodDelegate<Func<TimeOfDay, float>>(AccessTools.Method(typeof(TimeOfDay), "UpdateSunlightIntensity"));

        public static readonly AccessTools.FieldRef<WorldContainer, WorldDetailSave.OverworldCell> OverworldCellRef =
            AccessTools.FieldRefAccess<WorldContainer, WorldDetailSave.OverworldCell>("overworldCell");

        private static readonly AccessTools.FieldRef<ColonyDiagnosticUtility, Dictionary<int, List<ColonyDiagnostic>>> WorldDiagnosticsRef =
            AccessTools.FieldRefAccess<ColonyDiagnosticUtility, Dictionary<int, List<ColonyDiagnostic>>>("worldDiagnostics");

        private static readonly AccessTools.FieldRef<TrackerTool, List<WorldTracker>> WorldTrackersRef =
            AccessTools.FieldRefAccess<TrackerTool, List<WorldTracker>>("worldTrackers");

        private static readonly AccessTools.FieldRef<TrackerTool, int> UpdatingWorldTrackerRef =
            AccessTools.FieldRefAccess<TrackerTool, int>("updatingWorldTracker");

        public static int GetNextWorldId(ClusterManager clusterManager) => GetNextWorldIdDelegate(clusterManager);

        public static void RefreshTimeOfDaySunlight()
        {
            if (TimeOfDay.Instance != null)
            {
                UpdateSunlightIntensityDelegate(TimeOfDay.Instance);
            }
        }

        public static void PurgePocketWorldDiagnostics(int worldId)
        {
            if (worldId < 0)
            {
                return;
            }

            // 1. 清理 TrackerTool 中的高负载世界追踪器，仅保留 StressTracker 和 KCalTracker 支撑顶部 HUD（防止报空引用）
            if (TrackerTool.Instance != null)
            {
                List<WorldTracker> trackers = WorldTrackersRef(TrackerTool.Instance);
                if (trackers != null)
                {
                    trackers.RemoveAll(t => t != null && t.WorldID == worldId && !(t is StressTracker) && !(t is KCalTracker));
                    if (TrackerTool.Instance.GetWorldTracker<StressTracker>(worldId) == null)
                    {
                        trackers.Add(new StressTracker(worldId));
                    }
                    if (TrackerTool.Instance.GetWorldTracker<KCalTracker>(worldId) == null)
                    {
                        trackers.Add(new KCalTracker(worldId));
                    }
                    UpdatingWorldTrackerRef(TrackerTool.Instance) = 0;
                }
            }

            // 2. 彻底注销并移除调度器中的所有诊断实例，保持 worldDiagnostics 为纯空列表
            // （绝不能删除字典 key，否则外部 UI 如 ColonyDiagnosticScreen 会因缺少 key 抛出 KeyNotFoundException）
            if (ColonyDiagnosticUtility.Instance != null)
            {
                Dictionary<int, List<ColonyDiagnostic>> worldDiagnostics = WorldDiagnosticsRef(ColonyDiagnosticUtility.Instance);
                if (worldDiagnostics != null)
                {
                    if (worldDiagnostics.TryGetValue(worldId, out List<ColonyDiagnostic> diags) && diags != null)
                    {
                        foreach (ColonyDiagnostic diag in diags)
                        {
                            if (diag != null)
                            {
                                diag.OnCleanUp();
                            }
                        }
                    }
                    worldDiagnostics[worldId] = new List<ColonyDiagnostic>();
                }

                if (ColonyDiagnosticUtility.Instance.diagnosticDisplaySettings != null &&
                    !ColonyDiagnosticUtility.Instance.diagnosticDisplaySettings.ContainsKey(worldId))
                {
                    ColonyDiagnosticUtility.Instance.diagnosticDisplaySettings[worldId] =
                        new Dictionary<string, ColonyDiagnosticUtility.DisplaySetting>();
                }

                if (ColonyDiagnosticUtility.Instance.diagnosticCriteriaDisabled != null &&
                    !ColonyDiagnosticUtility.Instance.diagnosticCriteriaDisabled.ContainsKey(worldId))
                {
                    ColonyDiagnosticUtility.Instance.diagnosticCriteriaDisabled[worldId] =
                        new Dictionary<string, List<string>>();
                }
            }
        }

        public static void CleanupDestroyedWorldTrackersAndDiagnostics(int worldId)
        {
            PurgePocketWorldDiagnostics(worldId);
        }

        // 防御性拦截：若诊断所属世界是生态舱或已被销毁，直接注销并跳过，防止任何诊断实例在生态舱内执行计算
        [HarmonyPatch(typeof(ColonyDiagnostic), nameof(ColonyDiagnostic.Sim4000ms))]
        public static class ColonyDiagnostic_Sim4000ms_Patch
        {
            public static bool Prefix(ColonyDiagnostic __instance)
            {
                if (__instance == null || ClusterManager.Instance == null)
                {
                    return false;
                }
                if (IsPocketHabitatWorld(__instance.worldID))
                {
                    __instance.OnCleanUp();
                    return false;
                }
                WorldContainer world = ClusterManager.Instance.GetWorld(__instance.worldID);
                if (world == null)
                {
                    __instance.OnCleanUp();
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(ColonyDiagnosticUtility), nameof(ColonyDiagnosticUtility.AddWorld))]
        public static class ColonyDiagnosticUtility_AddWorld_Patch
        {
            public static bool Prefix(int worldID)
            {
                if (IsPocketHabitatWorld(worldID))
                {
                    PurgePocketWorldDiagnostics(worldID);
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(ColonyDiagnosticUtility), nameof(ColonyDiagnosticUtility.GetWorldDiagnosticResult))]
        public static class ColonyDiagnosticUtility_GetWorldDiagnosticResult_Patch
        {
            public static bool Prefix(int worldID, ref ColonyDiagnostic.DiagnosticResult.Opinion __result)
            {
                if (IsPocketHabitatWorld(worldID))
                {
                    PurgePocketWorldDiagnostics(worldID);
                    __result = ColonyDiagnostic.DiagnosticResult.Opinion.Normal;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(ColonyDiagnosticUtility), nameof(ColonyDiagnosticUtility.GetWorldDiagnosticResultStatus))]
        public static class ColonyDiagnosticUtility_GetWorldDiagnosticResultStatus_Patch
        {
            public static bool Prefix(int worldID, ref string __result)
            {
                if (IsPocketHabitatWorld(worldID))
                {
                    PurgePocketWorldDiagnostics(worldID);
                    __result = string.Empty;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(ColonyDiagnosticUtility), nameof(ColonyDiagnosticUtility.GetWorldDiagnosticResultTooltip))]
        public static class ColonyDiagnosticUtility_GetWorldDiagnosticResultTooltip_Patch
        {
            public static bool Prefix(int worldID, ref string __result)
            {
                if (IsPocketHabitatWorld(worldID))
                {
                    PurgePocketWorldDiagnostics(worldID);
                    __result = string.Empty;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(ColonyDiagnosticUtility), nameof(ColonyDiagnosticUtility.GetDiagnostic), new Type[] { typeof(string), typeof(int) })]
        public static class ColonyDiagnosticUtility_GetDiagnostic_Patch
        {
            public static bool Prefix(string id, int worldID, ref ColonyDiagnostic __result)
            {
                if (IsPocketHabitatWorld(worldID))
                {
                    PurgePocketWorldDiagnostics(worldID);
                    __result = null;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(ColonyDiagnosticUtility), nameof(ColonyDiagnosticUtility.IsCriteriaEnabled))]
        public static class ColonyDiagnosticUtility_IsCriteriaEnabled_Patch
        {
            public static bool Prefix(int worldID, ref bool __result)
            {
                if (IsPocketHabitatWorld(worldID))
                {
                    PurgePocketWorldDiagnostics(worldID);
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(ColonyDiagnosticUtility), nameof(ColonyDiagnosticUtility.SetCriteriaEnabled))]
        public static class ColonyDiagnosticUtility_SetCriteriaEnabled_Patch
        {
            public static bool Prefix(int worldID)
            {
                if (IsPocketHabitatWorld(worldID))
                {
                    PurgePocketWorldDiagnostics(worldID);
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(ColonyDiagnosticUtility), nameof(ColonyDiagnosticUtility.GetChoreGroupDiagnostic))]
        public static class ColonyDiagnosticUtility_GetChoreGroupDiagnostic_Patch
        {
            public static bool Prefix(int worldID, ref ChoreGroupDiagnostic __result)
            {
                if (IsPocketHabitatWorld(worldID))
                {
                    PurgePocketWorldDiagnostics(worldID);
                    __result = null;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(ColonyDiagnosticUtility), nameof(ColonyDiagnosticUtility.GetWorkTimeDiagnostic))]
        public static class ColonyDiagnosticUtility_GetWorkTimeDiagnostic_Patch
        {
            public static bool Prefix(int worldID, ref WorkTimeDiagnostic __result)
            {
                if (IsPocketHabitatWorld(worldID))
                {
                    PurgePocketWorldDiagnostics(worldID);
                    __result = null;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(ColonyDiagnosticScreen), "OnSpawn")]
        public static class ColonyDiagnosticScreen_OnSpawn_Patch
        {
            public static void Prefix()
            {
                if (ClusterManager.Instance != null && ColonyDiagnosticUtility.Instance != null)
                {
                    foreach (WorldContainer world in ClusterManager.Instance.WorldContainers)
                    {
                        if (world != null && IsPocketHabitatWorld(world.id))
                        {
                            PurgePocketWorldDiagnostics(world.id);
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(ColonyDiagnosticScreen), "SpawnTrackerLines")]
        public static class ColonyDiagnosticScreen_SpawnTrackerLines_Patch
        {
            public static bool Prefix(int world)
            {
                if (IsPocketHabitatWorld(world))
                {
                    PurgePocketWorldDiagnostics(world);
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(TrackerTool), "AddNewWorldTrackers")]
        public static class TrackerTool_AddNewWorldTrackers_Patch
        {
            public static bool Prefix(TrackerTool __instance, int worldID)
            {
                if (IsPocketHabitatWorld(worldID))
                {
                    // 仅保留 StressTracker 和 KCalTracker 支撑顶部 HUD，杜绝 NullReferenceException；
                    // 彻底跳过后续 500+ 个高消耗资源追踪器
                    List<WorldTracker> trackers = WorldTrackersRef(__instance);
                    if (trackers != null)
                    {
                        if (__instance.GetWorldTracker<StressTracker>(worldID) == null)
                        {
                            trackers.Add(new StressTracker(worldID));
                        }
                        if (__instance.GetWorldTracker<KCalTracker>(worldID) == null)
                        {
                            trackers.Add(new KCalTracker(worldID));
                        }
                    }
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(MeterScreen_Stress), "InternalRefresh")]
        public static class MeterScreen_Stress_InternalRefresh_Patch
        {
            public static bool Prefix(MeterScreen_Stress __instance)
            {
                int activeWorldId = ClusterManager.Instance.activeWorldId;
                if (IsPocketHabitatWorld(activeWorldId))
                {
                    int parentId = OmniPocketHabitat.GetExteriorWorldId(activeWorldId);
                    if (parentId < 0) parentId = 0;
                    float maxStress = GameUtil.GetMaxStressInActiveWorld();
                    __instance.Label?.SetText(Mathf.Round(maxStress).ToString());
                    WorldTracker worldTracker = TrackerTool.Instance?.GetWorldTracker<StressTracker>(activeWorldId)
                        ?? TrackerTool.Instance?.GetWorldTracker<StressTracker>(parentId);
                    if (worldTracker != null && __instance.diagnosticGraph != null)
                    {
                        SparkLayer spark = __instance.diagnosticGraph.GetComponentInChildren<SparkLayer>();
                        if (spark != null)
                        {
                            spark.SetColor((worldTracker.GetCurrentValue() >= TUNING.STRESS.ACTING_OUT_RESET)
                                ? Constants.NEGATIVE_COLOR : Constants.NEUTRAL_COLOR);
                        }
                        LineLayer line = __instance.diagnosticGraph.GetComponentInChildren<LineLayer>();
                        if (line != null)
                        {
                            line.RefreshLine(worldTracker.ChartableData(600f), "stressData");
                        }
                    }
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(MeterScreen_Rations), "InternalRefresh")]
        public static class MeterScreen_Rations_InternalRefresh_Patch
        {
            public static bool Prefix(MeterScreen_Rations __instance)
            {
                int activeWorldId = ClusterManager.Instance.activeWorldId;
                if (IsPocketHabitatWorld(activeWorldId))
                {
                    int parentId = OmniPocketHabitat.GetExteriorWorldId(activeWorldId);
                    if (parentId < 0) parentId = 0;
                    WorldContainer parentWorld = ClusterManager.Instance?.GetWorld(parentId);
                    if (__instance.Label != null && WorldResourceAmountTracker<RationTracker>.Get() != null && parentWorld != null)
                    {
                        long num = (long)WorldResourceAmountTracker<RationTracker>.Get().CountAmount(null, parentWorld.worldInventory);
                        __instance.Label.SetText(GameUtil.GetFormattedCalories(num));
                    }
                    WorldTracker worldTracker = TrackerTool.Instance?.GetWorldTracker<KCalTracker>(activeWorldId)
                        ?? TrackerTool.Instance?.GetWorldTracker<KCalTracker>(parentId);
                    if (worldTracker != null && __instance.diagnosticGraph != null)
                    {
                        LineLayer line = __instance.diagnosticGraph.GetComponentInChildren<LineLayer>();
                        if (line != null)
                        {
                            line.RefreshLine(worldTracker.ChartableData(600f), "kcal");
                        }
                    }
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(SkillsScreen), "AddWorldDivider")]
        public static class SkillsScreen_AddWorldDivider_Patch
        {
            public static bool Prefix(int worldId)
            {
                if (IsPocketHabitatWorld(worldId))
                {
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(ClusterManager), "MinionsByWorld", MethodType.Getter)]
        public static class ClusterManager_MinionsByWorld_Patch
        {
            public static void Postfix(Dictionary<int, List<IAssignableIdentity>> __result)
            {
                if (__result == null || __result.Count == 0) return;

                List<int> pocketWorldIds = null;
                foreach (int worldId in __result.Keys)
                {
                    if (IsPocketHabitatWorld(worldId))
                    {
                        if (pocketWorldIds == null) pocketWorldIds = new List<int>();
                        pocketWorldIds.Add(worldId);
                    }
                }

                if (pocketWorldIds != null)
                {
                    foreach (int pocketId in pocketWorldIds)
                    {
                        List<IAssignableIdentity> minions = __result[pocketId];
                        __result.Remove(pocketId);

                        WorldContainer world = ClusterManager.Instance?.GetWorld(pocketId);
                        int parentId = world != null ? world.ParentWorldId : -1;
                        if (parentId < 0) parentId = 0;

                        if (!__result.ContainsKey(parentId))
                        {
                            __result[parentId] = new List<IAssignableIdentity>();
                        }
                        __result[parentId].AddRange(minions);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(RocketConduitStorageAccess), "OnSpawn")]
        public static class RocketConduitStorageAccess_OnSpawn_Patch
        {
            public static void Postfix(RocketConduitStorageAccess __instance)
            {
                if (__instance != null)
                {
                    ActiveStorageAccesses.Add(__instance);
                }
            }
        }

        // 防止点击生态舱内部门时触发原版 RocketInteriorSectionSideScreen（该界面强依赖 Clustercraft）
        [HarmonyPatch(typeof(RocketInteriorSectionSideScreen), nameof(RocketInteriorSectionSideScreen.IsValidForTarget))]
        public static class RocketInteriorSectionSideScreen_IsValidForTarget_Patch
        {
            public static void Postfix(GameObject target, ref bool __result)
            {
                if (__result && target != null && target.GetComponent<ClustercraftInteriorDoor>() != null)
                {
                    WorldContainer myWorld = ClusterUtil.GetMyWorld(target);
                    if (IsPocketHabitatWorld(myWorld))
                    {
                        __result = false;
                    }
                }
            }
        }

        // 兜底保护：对火箭专属诊断项做空引用与非火箭世界全面防护
        [HarmonyPatch(typeof(FloatingRocketDiagnostic), nameof(FloatingRocketDiagnostic.Evaluate))]
        public static class FloatingRocketDiagnostic_Evaluate_Patch
        {
            public static bool Prefix(FloatingRocketDiagnostic __instance, ref ColonyDiagnostic.DiagnosticResult __result)
            {
                WorldContainer world = ClusterManager.Instance?.GetWorld(__instance.worldID);
                if (world == null || world.GetComponent<Clustercraft>() == null || IsPocketHabitatWorld(world))
                {
                    __result = new ColonyDiagnostic.DiagnosticResult(ColonyDiagnostic.DiagnosticResult.Opinion.Normal, string.Empty);
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(RocketFuelDiagnostic), nameof(RocketFuelDiagnostic.Evaluate))]
        public static class RocketFuelDiagnostic_Evaluate_Patch
        {
            public static bool Prefix(RocketFuelDiagnostic __instance, ref ColonyDiagnostic.DiagnosticResult __result)
            {
                WorldContainer world = ClusterManager.Instance?.GetWorld(__instance.worldID);
                if (world == null || world.GetComponent<Clustercraft>() == null || IsPocketHabitatWorld(world))
                {
                    __result = new ColonyDiagnostic.DiagnosticResult(ColonyDiagnostic.DiagnosticResult.Opinion.Normal, string.Empty);
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(RocketOxidizerDiagnostic), nameof(RocketOxidizerDiagnostic.Evaluate))]
        public static class RocketOxidizerDiagnostic_Evaluate_Patch
        {
            public static bool Prefix(RocketOxidizerDiagnostic __instance, ref ColonyDiagnostic.DiagnosticResult __result)
            {
                WorldContainer world = ClusterManager.Instance?.GetWorld(__instance.worldID);
                if (world == null || world.GetComponent<Clustercraft>() == null || IsPocketHabitatWorld(world))
                {
                    __result = new ColonyDiagnostic.DiagnosticResult(ColonyDiagnostic.DiagnosticResult.Opinion.Normal, string.Empty);
                    return false;
                }
                return true;
            }
        }

        // 允许玩家在生态舱内任意位置（玻璃砖、普通砖块、或真空）自由建造【电源插座】(RocketInteriorPowerPlug) 及各类舱内接口建筑
        [HarmonyPatch(typeof(BuildingDef), nameof(BuildingDef.CheckFoundation))]
        public static class BuildingDef_CheckFoundation_Patch
        {
            public static bool Prefix(
                int cell,
                BuildLocationRule location_rule,
                ref bool __result)
            {
                if (location_rule == BuildLocationRule.OnRocketEnvelope && Grid.IsValidCell(cell))
                {
                    int worldIdx = Grid.WorldIdx[cell];
                    if (worldIdx != (int)ClusterManager.INVALID_WORLD_IDX && ClusterManager.Instance != null)
                    {
                        WorldContainer world = ClusterManager.Instance.GetWorld(worldIdx);
                        if (IsPocketHabitatWorld(world))
                        {
                            __result = true;
                            return false;
                        }
                    }
                }
                return true;
            }
        }

        // ==========================================
        // 4. 舱内外液体管网直通补丁（RocketConduitStorageAccess 跨维管线桥接）
        // ==========================================
        [HarmonyPatch(typeof(RocketConduitStorageAccess), nameof(RocketConduitStorageAccess.Sim200ms))]
        public static class RocketConduitStorageAccess_Sim200ms_Patch
        {
            public static bool Prefix(RocketConduitStorageAccess __instance, float dt)
            {
                if (__instance == null) return false;

                int worldId = __instance.GetMyWorldId();
                OmniPocketHabitat habitat = OmniPocketHabitat.GetHabitatForWorld(worldId);
                if (habitat == null)
                {
                    WorldContainer myWorld = __instance.GetMyWorld();
                    if (myWorld != null && myWorld.GetComponent<CraftModuleInterface>() == null)
                    {
                        return false; // 防止非火箭世界因缺失 CraftModuleInterface 抛出 NRE
                    }
                    return true;
                }

                Operational op = __instance.GetComponent<Operational>();
                if (op != null && !op.IsOperational)
                {
                    op.SetActive(false);
                    return false;
                }

                // 该桥接只处理气液管网，不能把固体接口的物资塞进液体储罐。
                if (__instance.cargoType != CargoBay.CargoType.Gasses &&
                    __instance.cargoType != CargoBay.CargoType.Liquids) return false;
                Storage cabinPortStorage = __instance.storage;
                if (cabinPortStorage == null) return false;

                float currentMass = cabinPortStorage.MassStored();
                float targetLevel = __instance.targetLevel;
                if (Mathf.Abs(currentMass - targetLevel) <= 0.01f)
                {
                    op?.SetActive(false);
                    return false;
                }

                float transferred = 0f;
                float needed = targetLevel - currentMass;
                Filterable filterable = __instance.GetComponent<Filterable>();
                Tag selectedTag = filterable != null ? filterable.SelectedTag : Tag.Invalid;

                bool isGas = __instance.cargoType == CargoBay.CargoType.Gasses;
                Storage extIn = isGas ? habitat.GasInStorage : habitat.LiquidInStorage;
                Storage extOut = isGas ? habitat.GasOutStorage : habitat.LiquidOutStorage;

                if (needed > 0f)
                {
                    needed = Mathf.Min(needed, cabinPortStorage.RemainingCapacity());
                    // 舱内接口 RocketInteriorLiquidOutput / RocketInteriorGasOutput：
                    // 从舱外建筑输入储罐 extIn 提取流体（支持过滤），送入舱内管道接口
                    if (extIn != null && extIn.MassStored() > 0f)
                    {
                        for (int i = extIn.items.Count - 1; i >= 0; i--)
                        {
                            GameObject go = extIn.items[i];
                            if (go == null || needed <= 0.01f) continue;
                            PrimaryElement element = go.GetComponent<PrimaryElement>();
                            if (element == null || (isGas ? !element.Element.IsGas : !element.Element.IsLiquid)) continue;
                            if (selectedTag.IsValid && selectedTag != GameTags.Void && go.PrefabID() != selectedTag)
                            {
                                continue;
                            }

                            Pickupable p = go.GetComponent<Pickupable>();
                            if (p != null)
                            {
                                Pickupable taken = p.Take(needed);
                                if (taken != null)
                                {
                                    float mass = taken.PrimaryElement.Mass;
                                    needed -= mass;
                                    transferred += mass;
                                    cabinPortStorage.Store(taken.gameObject, hide_popups: true);
                                }
                            }
                            if (needed <= 0f) break;
                        }
                    }
                }
                else if (needed < 0f)
                {
                    // 舱内接口 RocketInteriorLiquidInput / RocketInteriorGasInput（targetLevel = 0f）：
                    // 将舱内回收的流体送入舱外输出储罐 extOut，供外部管网排出
                    float toTransfer = -needed;
                    if (extOut != null && extOut.RemainingCapacity() > 0f)
                    {
                        float availableCap = Mathf.Min(toTransfer, extOut.RemainingCapacity());
                        for (int i = cabinPortStorage.items.Count - 1; i >= 0; i--)
                        {
                            GameObject go = cabinPortStorage.items[i];
                            if (go == null) continue;
                            PrimaryElement element = go.GetComponent<PrimaryElement>();
                            if (element == null || (isGas ? !element.Element.IsGas : !element.Element.IsLiquid)) continue;
                            Pickupable p = go.GetComponent<Pickupable>();
                            if (p != null)
                            {
                                Pickupable taken = p.Take(availableCap);
                                if (taken != null)
                                {
                                    float mass = taken.PrimaryElement.Mass;
                                    availableCap -= mass;
                                    transferred += mass;
                                    extOut.Store(taken.gameObject, hide_popups: true);
                                }
                            }
                            if (availableCap <= 0f) break;
                        }
                    }
                }

                op?.SetActive(transferred > 0f);
                return false; // 跳过原版依赖 CraftModuleInterface 的逻辑
            }
        }
    }
}
