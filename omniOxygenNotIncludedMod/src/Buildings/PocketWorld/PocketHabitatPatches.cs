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
    /// 挂载在舱内门（ClustercraftInteriorDoor）上的侧边栏快捷按钮，支持在舱内一键返回舱外。
    /// </summary>
    public class OmniPocketInteriorExitButton : KMonoBehaviour, ISidescreenButtonControl
    {
        public OmniPocketHabitat exteriorHabitat;

        public string SidescreenTitle => ModStrings.T("折叠空间生态巨舱", "Omni Pocket Hyper-Habitat").Resolve();
        public string SidescreenButtonText => ModStrings.T("返回舱外 (View Exterior)", "View Exterior").Resolve();
        public string SidescreenButtonTooltip => ModStrings.T("将镜头视角切回舱外基地入口", "Return camera view to the exterior habitat entrance").Resolve();

        public bool SidescreenEnabled() => exteriorHabitat != null && !exteriorHabitat.IsClosing;
        public bool SidescreenButtonInteractable() => exteriorHabitat != null && !exteriorHabitat.IsClosing;
        public int ButtonSideScreenSortOrder() => 20;
        public int HorizontalGroupID() => -1;
        public void SetButtonTextOverride(ButtonMenuTextOverride textOverride) { }

        public void OnSidescreenButtonPressed()
        {
            if (exteriorHabitat == null || ClusterManager.Instance == null)
            {
                return;
            }

            WorldContainer interior = exteriorHabitat.GetTargetWorld();
            if (!exteriorHabitat.IsClosing && interior != null &&
                PocketWorldLifecycle.TryGetExterior(exteriorHabitat, interior, out WorldContainer exterior))
            {
                ClusterManager.Instance.SetActiveWorld(exterior.id);
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
    public static partial class OmniPocketHabitatPatches
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
            // 兼容层在读档或退出时也需识别组件类型；修改世界前另走严格的所有权校验。
            return world.GetComponent<OmniPocketHabitat>() != null;
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
                    bool inBounds = PocketWorldBounds.TryGet(myWorld, out PocketWorldBounds bounds) &&
                        bounds.Size.x <= OmniPocketSpaceManager.StandardSize.x && bounds.Size.y <= OmniPocketSpaceManager.StandardSize.y &&
                        bounds.Owns(Grid.PosToCell(__instance));
                    VirtualCircuitKeyRef(__instance) = inBounds ? (object)myWorld : __instance.gameObject;
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
            public static void Prefix(out bool __state)
            {
                __state = true;
                suppressModuleInteriorDepth++;
            }
            public static void Finalizer(bool __state)
            {
                if (__state && suppressModuleInteriorDepth > 0) suppressModuleInteriorDepth--;
            }
        }

        [HarmonyPatch(typeof(AmbienceManager.Quadrant), nameof(AmbienceManager.Quadrant.Update))]
        public static class AmbienceManager_Quadrant_Update_Patch
        {
            public static void Prefix(out bool __state)
            {
                __state = true;
                suppressModuleInteriorDepth++;
            }
            public static void Finalizer(bool __state)
            {
                if (__state && suppressModuleInteriorDepth > 0) suppressModuleInteriorDepth--;
            }
        }

        [HarmonyPatch(typeof(TerrainBG), "LateUpdate")]
        public static class TerrainBG_LateUpdate_Patch
        {
            public static void Prefix(out bool __state)
            {
                __state = true;
                suppressModuleInteriorDepth++;
            }
            public static void Finalizer(bool __state)
            {
                if (__state && suppressModuleInteriorDepth > 0) suppressModuleInteriorDepth--;
            }
        }

        [HarmonyPatch(typeof(ClusterManager), "UpdateWorldReverbSnapshot")]
        public static class ClusterManager_UpdateWorldReverbSnapshot_Patch
        {
            public static void Prefix(out bool __state)
            {
                __state = true;
                suppressModuleInteriorDepth++;
            }
            public static void Finalizer(bool __state)
            {
                if (__state && suppressModuleInteriorDepth > 0) suppressModuleInteriorDepth--;
            }
        }

        [HarmonyPatch(typeof(ClusterManager), nameof(ClusterManager.UpdateRocketInteriorAudio))]
        public static class ClusterManager_UpdateRocketInteriorAudio_Patch
        {
            public static void Prefix(out bool __state)
            {
                __state = true;
                suppressModuleInteriorDepth++;
            }
            public static void Finalizer(bool __state)
            {
                if (__state && suppressModuleInteriorDepth > 0) suppressModuleInteriorDepth--;
            }
        }

        [HarmonyPatch(typeof(ColonyAchievementTracker), "OnNewDay")]
        public static class ColonyAchievementTracker_OnNewDay_Patch
        {
            public static void Prefix(out bool __state)
            {
                __state = true;
                suppressModuleInteriorDepth++;
            }
            public static void Finalizer(bool __state)
            {
                if (__state && suppressModuleInteriorDepth > 0) suppressModuleInteriorDepth--;
            }
        }

        [HarmonyPatch(typeof(RocketConduitReceiver), "FindPartner")]
        public static class RocketConduitReceiver_FindPartner_Patch
        {
            public static bool Prefix(RocketConduitReceiver __instance, out bool __state)
            {
                __state = false;
                if (__instance == null) return true;
                int worldId = __instance.GetMyWorldId();
                OmniPocketHabitat habitat = OmniPocketHabitat.GetHabitatForWorld(worldId);
                if (habitat != null)
                {
                    if (habitat.IsClosing || !habitat.OwnsInteriorCell(Grid.PosToCell(__instance))) return false;
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
                __state = true;
                return true;
            }

            public static void Finalizer(bool __state)
            {
                if (__state && suppressModuleInteriorDepth > 0)
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

            public static bool Prefix(RocketConduitSender __instance, out bool __state)
            {
                __state = false;
                if (__instance == null) return true;
                int worldId = __instance.GetMyWorldId();
                OmniPocketHabitat habitat = OmniPocketHabitat.GetHabitatForWorld(worldId);
                if (habitat != null)
                {
                    if (habitat.IsClosing || !habitat.OwnsInteriorCell(Grid.PosToCell(__instance))) return false;
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
                __state = true;
                return true;
            }

            public static void Finalizer(bool __state)
            {
                if (__state && suppressModuleInteriorDepth > 0)
                {
                    suppressModuleInteriorDepth--;
                }
            }
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
                    PurgePocketWorldDiagnostics(worldID, force: true);
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

                if (habitat.IsClosing || !habitat.OwnsInteriorCell(Grid.PosToCell(__instance))) return false;
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
