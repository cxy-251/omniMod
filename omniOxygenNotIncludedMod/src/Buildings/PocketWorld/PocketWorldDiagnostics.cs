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
    public static partial class OmniPocketHabitatPatches
    {
        private static readonly HashSet<int> preparedWorlds = new HashSet<int>();
        private static ColonyDiagnosticUtility diagnosticContext;
        private static TrackerTool trackerContext;

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

        public static void RefreshTimeOfDaySunlight()
        {
            if (TimeOfDay.Instance != null)
            {
                UpdateSunlightIntensityDelegate(TimeOfDay.Instance);
            }
        }

        public static void PurgePocketWorldDiagnostics(int worldId, bool force = false)
        {
            if (worldId < 0 || PocketWorldRegistry.GetOwner(worldId) == null) return;
            if (diagnosticContext != ColonyDiagnosticUtility.Instance || trackerContext != TrackerTool.Instance)
            {
                preparedWorlds.Clear();
                diagnosticContext = ColonyDiagnosticUtility.Instance;
                trackerContext = TrackerTool.Instance;
            }
            if (!force && preparedWorlds.Contains(worldId)) return;

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
            preparedWorlds.Add(worldId);
        }

        public static void CleanupDestroyedWorldTrackersAndDiagnostics(int worldId)
        {
            if (worldId < 0 || (ClusterManager.Instance != null && ClusterManager.Instance.GetWorld(worldId) != null)) return;
            preparedWorlds.Remove(worldId);
            if (TrackerTool.Instance != null)
            {
                WorldTrackersRef(TrackerTool.Instance)?.RemoveAll(t => t != null && t.WorldID == worldId);
                UpdatingWorldTrackerRef(TrackerTool.Instance) = 0;
            }
            if (ColonyDiagnosticUtility.Instance != null)
            {
                var worlds = WorldDiagnosticsRef(ColonyDiagnosticUtility.Instance);
                if (worlds != null && worlds.TryGetValue(worldId, out List<ColonyDiagnostic> diagnostics))
                {
                    if (diagnostics != null)
                    {
                        foreach (ColonyDiagnostic diagnostic in diagnostics) diagnostic?.OnCleanUp();
                        diagnostics.Clear();
                    }
                }
                // 保留空的 UI 字典项供尚未刷新的界面收尾；不再为已销毁世界创建任何追踪器。
                if (ColonyDiagnosticUtility.Instance.diagnosticDisplaySettings != null &&
                    ColonyDiagnosticUtility.Instance.diagnosticDisplaySettings.TryGetValue(worldId, out var display)) display?.Clear();
                if (ColonyDiagnosticUtility.Instance.diagnosticCriteriaDisabled != null &&
                    ColonyDiagnosticUtility.Instance.diagnosticCriteriaDisabled.TryGetValue(worldId, out var criteria)) criteria?.Clear();
            }
        }

        internal static void ResetPocketDiagnostics()
        {
            preparedWorlds.Clear();
            diagnosticContext = null;
            trackerContext = null;
        }

    }
}
