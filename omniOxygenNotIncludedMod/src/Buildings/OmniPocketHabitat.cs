using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using KSerialization;
using OmniMod.Core.Logging;
using STRINGS;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 【折叠空间生态巨舱】核心控制器：
    /// 1. 建造完成后自动开辟 32x32（内部 28x27 净空，外框为标准钢制火箭壁板）的独立空间。
    /// 2. 自动将舱外门与舱内门（ClustercraftInteriorDoor）进行双向导航（NavTeleporter）与通行权限（AssignmentGroupController）配对。
    /// 3. 舱外底部电源接口与舱内原版【电源插座】（RocketInteriorPowerPlug）通过虚拟电路键直连，实现舱内外电网无缝互通。
    /// 4. 舱外与舱内门均提供侧边栏一键切换视角按钮（【进入舱内】/【返回舱外】）。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class OmniPocketHabitat : KMonoBehaviour, ISaveLoadable, ISidescreenButtonControl
    {
        [Serialize]
        public int targetWorldId = -1;

#pragma warning disable CS0649
        [MyCmpReq]
        private NavTeleporter navTeleporter;

        [MyCmpGet]
        private AssignmentGroupController assignmentGroupController;
#pragma warning restore CS0649

        private ClustercraftInteriorDoor pairedInteriorDoor;
        private bool isInitializingWorld;

        public string SidescreenTitle => ModStrings.T("折叠空间生态巨舱", "Omni Pocket Hyper-Habitat").Resolve();
        public string SidescreenButtonText => ModStrings.T("进入舱内 (View Interior)", "View Interior").Resolve();
        public string SidescreenButtonTooltip => ModStrings.T("将镜头视角切换至该生态舱内部空间", "Switch camera view to the habitat interior").Resolve();

        public bool SidescreenEnabled() => targetWorldId >= 0;
        public bool SidescreenButtonInteractable() => targetWorldId >= 0;
        public int ButtonSideScreenSortOrder() => 20;
        public int HorizontalGroupID() => -1;
        public void SetButtonTextOverride(ButtonMenuTextOverride textOverride) { }

        public void OnSidescreenButtonPressed()
        {
            if (targetWorldId < 0 || ClusterManager.Instance == null)
            {
                return;
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
            EnsurePocketWorldInitialized();
        }

        /// <summary>
        /// 幂等初始化内部世界（确保无论 OnSpawn 顺序如何，WireUtilitySemiVirtualNetworkLink 都能立即拿到目标 WorldContainer）。
        /// </summary>
        public void EnsurePocketWorldInitialized()
        {
            if (isInitializingWorld)
            {
                return;
            }

            if (targetWorldId < 0)
            {
                isInitializingWorld = true;
                try
                {
                    CreatePocketWorld();
                }
                finally
                {
                    isInitializingWorld = false;
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
                }
                StartCoroutine(DelayedPairDoor());
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
                WorldContainer world = ClusterManager.Instance.CreateRocketInteriorWorld(
                    gameObject,
                    OmniPocketTemplate.TemplateName,
                    PairWithInteriorDoor
                );

                if (world != null)
                {
                    targetWorldId = world.id;
                    int parentWorldId = ClusterUtil.GetMyWorldId(this);
                    if (parentWorldId != -1)
                    {
                        world.SetParentIdx(parentWorldId);
                    }
                    ModLog.Info($"[折叠生态舱] 成功创建内部空间 (World ID: {targetWorldId})。");
                }
                else
                {
                    ModLog.Warn("[折叠生态舱] 创建内部空间失败：底层网格空间不足。");
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[折叠生态舱] 创建内部世界异常: {ex}");
            }
        }

        private IEnumerator DelayedPairDoor()
        {
            yield return null;
            PairWithInteriorDoor();
        }

        public void PairWithInteriorDoor()
        {
            if (targetWorldId < 0)
            {
                return;
            }

            foreach (ClustercraftInteriorDoor door in Components.ClusterCraftInteriorDoors.Items)
            {
                if (door != null && ClusterUtil.GetMyWorldId(door) == targetWorldId)
                {
                    pairedInteriorDoor = door;
                    break;
                }
            }

            if (pairedInteriorDoor == null)
            {
                return;
            }

            // 同步内外门的通行权限组（与原版 ClustercraftExteriorDoor.SetTarget 保持一致）
            if (assignmentGroupController != null)
            {
                var interiorAgc = pairedInteriorDoor.GetComponent<AssignmentGroupController>();
                if (interiorAgc != null && !string.IsNullOrEmpty(assignmentGroupController.AssignmentGroupID))
                {
                    interiorAgc.SetGroupID(assignmentGroupController.AssignmentGroupID);
                }
            }

            // 建立双向导航传送连接
            NavTeleporter interiorNav = pairedInteriorDoor.GetComponent<NavTeleporter>();
            if (navTeleporter != null && interiorNav != null)
            {
                navTeleporter.TwoWayTarget(interiorNav);
            }

            // 在舱内门上挂载【返回舱外】快捷视角切换按钮
            var exitBtn = pairedInteriorDoor.gameObject.AddOrGet<OmniPocketInteriorExitButton>();
            exitBtn.exteriorHabitat = this;

            WorldContainer targetWorld = GetTargetWorld();
            int parentWorldId = ClusterUtil.GetMyWorldId(this);
            if (targetWorld != null && parentWorldId != -1)
            {
                targetWorld.SetParentIdx(parentWorldId);
            }
        }

        protected override void OnCleanUp()
        {
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
                world.TransferResourcesToParentWorld(ejectPos + new Vector3(0f, 0.5f, 0f), noRefundTiles);

                ClusterManager.Instance.UnregisterWorldContainer(world);

                OrbitalMechanics orbital = GetComponent<OrbitalMechanics>();
                if (!Util.IsNullOrDestroyed(orbital))
                {
                    UnityEngine.Object.Destroy(orbital);
                }

                ModLog.Info($"[折叠生态舱] 建筑已拆除，内部空间 (World {targetWorldId}) 已安全回收并将物资退回舱外。");
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
    /// </summary>
    public static class OmniPocketHabitatPatches
    {
        [ThreadStatic]
        private static int suppressModuleInteriorDepth;

        private static bool IsPocketHabitatWorld(WorldContainer world)
        {
            return world != null && world.GetComponent<OmniPocketHabitat>() != null && world.GetComponent<Clustercraft>() == null;
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
                    return;
                }

                // 情况 B：建造在生态舱内部空间的原版【电源插座】(RocketInteriorPowerPlug)
                WorldContainer myWorld = ClusterUtil.GetMyWorld(__instance);
                if (IsPocketHabitatWorld(myWorld))
                {
                    VirtualCircuitKeyRef(__instance) = myWorld;
                }
            }
        }

        // ==========================================
        // 2. 屏蔽非火箭生态舱的 Clustercraft 强依赖逻辑
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

        [HarmonyPatch(typeof(ColonyDiagnosticUtility), nameof(ColonyDiagnosticUtility.AddWorld))]
        public static class ColonyDiagnosticUtility_AddWorld_Patch
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

        // 兜底保护：对旧存档中已注册的火箭专属诊断项做空引用防护
        [HarmonyPatch(typeof(FloatingRocketDiagnostic), nameof(FloatingRocketDiagnostic.Evaluate))]
        public static class FloatingRocketDiagnostic_Evaluate_Patch
        {
            public static bool Prefix(FloatingRocketDiagnostic __instance, ref ColonyDiagnostic.DiagnosticResult __result)
            {
                WorldContainer world = ClusterManager.Instance?.GetWorld(__instance.worldID);
                if (IsPocketHabitatWorld(world))
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
                if (IsPocketHabitatWorld(world))
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
                if (IsPocketHabitatWorld(world))
                {
                    __result = new ColonyDiagnostic.DiagnosticResult(ColonyDiagnostic.DiagnosticResult.Opinion.Normal, string.Empty);
                    return false;
                }
                return true;
            }
        }
    }
}
