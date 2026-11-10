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
    public partial class OmniPocketHabitat : KMonoBehaviour, ISaveLoadable, ISidescreenButtonControl, ISim1000ms
    {
        [Serialize]
        public int targetWorldId = -1;

        public Storage LiquidInStorage { get; private set; }
        public Storage LiquidOutStorage { get; private set; }
        public Storage GasInStorage { get; private set; }
        public Storage GasOutStorage { get; private set; }
        public ConduitConsumer LiquidConsumer { get; private set; }
        public ConduitConsumer GasConsumer { get; private set; }

        public static OmniPocketHabitat GetHabitatForWorld(int worldId) => PocketWorldRegistry.GetOwner(worldId);
        public static int GetExteriorWorldId(int worldId) => PocketWorldRegistry.GetExteriorWorldId(worldId);
        public static bool IsPocketHabitatWorld(WorldContainer world) => OmniPocketHabitatPatches.IsPocketHabitatWorld(world);
        public static bool IsPocketHabitatWorld(int worldId) => PocketWorldRegistry.GetOwner(worldId) != null;
        public static void CollectInteriorWorldsOf(int exteriorWorldId, List<int> result) => PocketWorldRegistry.CollectInteriors(exteriorWorldId, result);

#pragma warning disable CS0649
        [MyCmpReq]
        private NavTeleporter navTeleporter;

        [MyCmpGet]
        private AssignmentGroupController assignmentGroupController;
#pragma warning restore CS0649

        private ClustercraftInteriorDoor pairedInteriorDoor;
        private FlowUtilityNetwork.NetworkItem gasItemInput;
        private FlowUtilityNetwork.NetworkItem gasItemOutput;
        private int gasInputCell = Grid.InvalidCell;
        private int gasOutputCell = Grid.InvalidCell;
        private bool isInitializingWorld;
        private bool hasInitializedWorld;
        private bool isClosing;
        private bool pendingWorldAdded;
        internal bool IsClosing => isClosing;
        internal void MarkWorldCreated() => pendingWorldAdded = true;
        private float nextInitializationAttempt;
        [Serialize]
        private bool hasCleanedLegacyWalls;
        private OverlayScreen overlayScreen;
        private KBatchedAnimController habitatAnim;

        public string SidescreenTitle => ModStrings.T("折叠空间生态巨舱", "Omni Pocket Hyper-Habitat").Resolve();
        public string SidescreenButtonText => ModStrings.T("进入舱内 (View Interior)", "View Interior").Resolve();
        public string SidescreenButtonTooltip => ModStrings.T("将镜头视角切换至该生态舱内部空间", "Switch camera view to the habitat interior").Resolve();

        public bool SidescreenEnabled() => !isClosing;
        public bool SidescreenButtonInteractable() => !isClosing && hasInitializedWorld && GetTargetWorld() != null;
        public int ButtonSideScreenSortOrder() => 20;
        public int HorizontalGroupID() => -1;
        public void SetButtonTextOverride(ButtonMenuTextOverride textOverride) { }

        public void OnSidescreenButtonPressed()
        {
            if (isClosing) return;
            EnsurePocketWorldInitialized();
            if (!hasInitializedWorld || targetWorldId < 0 || ClusterManager.Instance == null)
            {
                return;
            }

            WorldContainer targetWorld = GetTargetWorld();
            if (targetWorld == null)
            {
                return;
            }

            if (!App.isLoading && !KMonoBehaviour.isLoadingScene)
            {
                ClustercraftInteriorDoor validDoor = PocketWorldLayout.FindAndRestoreDoor(this, targetWorld);
                if (validDoor != null && (validDoor != pairedInteriorDoor || !OwnsInteriorCell(Grid.PosToCell(validDoor))))
                {
                    if (pairedInteriorDoor != null && pairedInteriorDoor != validDoor)
                    {
                        navTeleporter?.Deregister();
                    }
                    pairedInteriorDoor = validDoor;
                    PairWithInteriorDoor();
                }
            }
            if (pairedInteriorDoor == null)
            {
                PairWithInteriorDoor();
            }

            if (PocketWorldBounds.TryGet(targetWorld, out var bounds))
            {
                for (int y = bounds.Offset.y; y < bounds.MaxY; y++)
                {
                    for (int x = bounds.Offset.x; x < bounds.MaxX; x++)
                    {
                        int cell = Grid.XYToCell(x, y);
                        Grid.PreventFogOfWarReveal[cell] = false;
                        Grid.Reveal(cell, byte.MaxValue, forceReveal: true);
                    }
                }
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

            // 关键：显式注册副端口（气体输入与输出）进原版 gasConduitSystem 网络系统（EndpointType.Sink 与 EndpointType.Source）
            // 解决生态舱出气管道直接直连另一生态舱进气管道时，因副端口未入网导致原版 ConduitFlow 无法建立流向矢量的根本问题
            Building building = GetComponent<Building>();
            if (building != null)
            {
                CellOffset inOffset = building.GetRotatedOffset(OmniPocketHabitatConfig.GasInputPort.offset);
                gasInputCell = Grid.OffsetCell(building.GetCell(), inOffset);
                gasItemInput = new FlowUtilityNetwork.NetworkItem(ConduitType.Gas, Endpoint.Sink, gasInputCell, gameObject);
                Conduit.GetNetworkManager(ConduitType.Gas)?.AddToNetworks(gasInputCell, gasItemInput, true);

                CellOffset outOffset = building.GetRotatedOffset(OmniPocketHabitatConfig.GasOutputPort.offset);
                gasOutputCell = Grid.OffsetCell(building.GetCell(), outOffset);
                gasItemOutput = new FlowUtilityNetwork.NetworkItem(ConduitType.Gas, Endpoint.Source, gasOutputCell, gameObject);
                Conduit.GetNetworkManager(ConduitType.Gas)?.AddToNetworks(gasOutputCell, gasItemOutput, true);
            }

            EnsurePocketWorldInitialized();

            PairWithInteriorDoor();

            RefreshConduitDemandState();
        }

        public void Sim1000ms(float dt)
        {
            if (!hasInitializedWorld || GetTargetWorld() == null) EnsurePocketWorldInitialized();

            // 运行时自动检测与归正错位或多余的舱内门（防止旧存档遗留多余/错位舱门）
            if (hasInitializedWorld && !App.isLoading && !KMonoBehaviour.isLoadingScene)
            {
                WorldContainer targetWorld = GetTargetWorld();
                if (targetWorld != null)
                {
                    ClustercraftInteriorDoor validDoor = PocketWorldLayout.FindAndRestoreDoor(this, targetWorld);
                    if (validDoor != null && (validDoor != pairedInteriorDoor || !OwnsInteriorCell(Grid.PosToCell(validDoor))))
                    {
                        if (pairedInteriorDoor != null && pairedInteriorDoor != validDoor)
                        {
                            navTeleporter?.Deregister();
                        }
                        pairedInteriorDoor = validDoor;
                        PairWithInteriorDoor();
                    }
                    if (PocketWorldBounds.TryGet(targetWorld, out PocketWorldBounds bounds))
                    {
                        PocketWorldLayout.ClearLeftoverDoorDebris(targetWorld, bounds);
                    }
                }
            }

            if (pairedInteriorDoor != null && !OwnsInteriorCell(Grid.PosToCell(pairedInteriorDoor)))
            {
                navTeleporter?.Deregister();
                pairedInteriorDoor = null;
            }
            if (hasInitializedWorld && pairedInteriorDoor == null)
            {
                PairWithInteriorDoor();
            }
            RefreshConduitDemandState();
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
            if (isClosing || PocketWorldRegistry.IsShuttingDown || isInitializingWorld ||
                (hasInitializedWorld && GetTargetWorld() != null) || Time.realtimeSinceStartup < nextInitializationAttempt) return;
            isInitializingWorld = true;
            nextInitializationAttempt = Time.realtimeSinceStartup + 2f;
            try
            {
                EnsureExteriorAssignmentGroupReady();
                OrbitalMechanics orbital = GetComponent<OrbitalMechanics>();
                if (!Util.IsNullOrDestroyed(orbital)) UnityEngine.Object.DestroyImmediate(orbital);
                WorldContainer world = PocketWorldLifecycle.ResolveOrCreate(this);
                if (world == null || !PocketWorldLifecycle.Prepare(this, world)) return;
                if (!hasCleanedLegacyWalls) hasCleanedLegacyWalls = PocketWorldLayout.CleanupLegacy(this, world);
                PairWithInteriorDoor();
                hasInitializedWorld = true;
                nextInitializationAttempt = 0f;
                OmniPocketHabitatPatches.PurgePocketWorldDiagnostics(world.id, force: true);
                if (pendingWorldAdded)
                {
                    pendingWorldAdded = false;
                    ClusterManager.Instance.BoxingTrigger((int)GameHashes.WorldAdded, world.id);
                }
            }
            catch (Exception ex)
            {
                ModLog.Throttle("pocket_initialize_" + GetInstanceID(), "[生态舱] 初始化未完成：" + ex, 10f);
            }
            finally { isInitializingWorld = false; }
        }

        public WorldContainer GetTargetWorld() => PocketWorldRegistry.Resolve(this);

        public static void ConfigurePocketWorldZoneAndSunlight(WorldContainer world)
        {
            if (world != null) PocketWorldLayout.Configure(world.GetComponent<OmniPocketHabitat>(), world);
        }

        internal bool OwnsInteriorCell(int cell)
        {
            return !isClosing && PocketWorldBounds.TryGet(GetTargetWorld(), out PocketWorldBounds bounds) && bounds.Owns(cell);
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
            if (isClosing) return;
            WorldContainer targetWorld = GetTargetWorld();
            if (targetWorld == null) return;
            pairedInteriorDoor = PocketWorldLayout.EnsureDoor(this, targetWorld);
            if (pairedInteriorDoor == null) return;

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
            isClosing = true;
            if (gasItemInput != null && Grid.IsValidCell(gasInputCell))
            {
                Conduit.GetNetworkManager(ConduitType.Gas)?.RemoveFromNetworks(gasInputCell, gasItemInput, true);
                gasItemInput = null;
                gasInputCell = Grid.InvalidCell;
            }
            if (gasItemOutput != null && Grid.IsValidCell(gasOutputCell))
            {
                Conduit.GetNetworkManager(ConduitType.Gas)?.RemoveFromNetworks(gasOutputCell, gasItemOutput, true);
                gasItemOutput = null;
                gasOutputCell = Grid.InvalidCell;
            }
            if (overlayScreen != null) overlayScreen.OnOverlayChanged -= OnUtilityOverlayChanged;
            overlayScreen = null;
            habitatAnim = null;
            int previousId = targetWorldId;
            try
            {
                if (!PocketWorldRegistry.IsShuttingDown && !App.IsExiting && !App.isLoading &&
                    !KMonoBehaviour.isLoadingScene && Game.Instance != null && !Game.IsQuitting())
                    PocketWorldLifecycle.Destroy(this, GetComponent<WorldContainer>(), navTeleporter);
            }
            catch (Exception ex) { ModLog.Warn("[生态舱] 世界回收未完成：" + ex); }
            finally
            {
                PocketWorldRegistry.Unbind(this, previousId);
                base.OnCleanUp();
            }
        }
    }
}
