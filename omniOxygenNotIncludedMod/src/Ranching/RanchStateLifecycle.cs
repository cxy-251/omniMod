using System;
using HarmonyLib;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Ranching
{
    /// <summary>照料状态、目标参数和站点队列必须作为同一份归属清理；状态切换回调内只安排恢复。</summary>
    internal static class RanchStateLifecycle
    {
        private static readonly AccessTools.FieldRef<RanchedStates, RanchedStates.TargetParameter> Target =
            AccessTools.FieldRefAccess<RanchedStates, RanchedStates.TargetParameter>("ranchTarget");
        private static readonly AccessTools.FieldRef<RanchedStates, RanchedStates.RanchStates> Ranch =
            AccessTools.FieldRefAccess<RanchedStates, RanchedStates.RanchStates>("ranch");

        internal static bool HasValidTarget(RanchedStates.Instance critter)
        {
            if (critter == null || critter.gameObject == null || critter.Monitor == null || critter.sm == null) return false;
            RanchStation.Instance station = critter.Monitor.TargetRanchStation;
            return !station.IsNullOrDestroyed() && station.IsRunning() &&
                Target(critter.sm).Get(critter) == station.gameObject;
        }
        internal static bool IsAtWorkState(RanchedStates.Instance critter) =>
            critter != null && !critter.IsNullOrStopped() && critter.IsInsideState(Ranch(critter.sm).Ranching);

        internal static void RequestRecovery(RanchedStates.Instance state)
        {
            if (state == null || state.gameObject == null) return;
            state.gameObject.AddOrGet<RanchTargetRecovery>().RequestInvalidState(state);
        }
        internal static void RequestRelease(RanchableMonitor.Instance monitor, RanchStation.Instance station)
        {
            if (monitor == null || monitor.gameObject == null) return;
            monitor.gameObject.AddOrGet<RanchTargetRecovery>().RequestRelease(monitor, station);
        }
        internal static bool IsOwnedByStation(RanchableMonitor.Instance monitor, RanchStation.Instance station) =>
            !station.IsNullOrDestroyed() && station.IsRunning() &&
            (station.IsCritterInQueue(monitor) ||
                (station.ActiveRanchable != null && station.ActiveRanchable == monitor.States));
    }

    [SkipSaveFileSerialization]
    public sealed class RanchTargetRecovery : KMonoBehaviour, ISim200ms
    {
        private RanchedStates.Instance invalidState;
        private RanchableMonitor.Instance releaseMonitor;
        private RanchStation.Instance releaseStation;

        internal void RequestInvalidState(RanchedStates.Instance state) { invalidState = state; }
        internal void RequestRelease(RanchableMonitor.Instance monitor, RanchStation.Instance station)
        { releaseMonitor = monitor; releaseStation = station; }

        public void Sim200ms(float dt)
        {
            if (App.isLoading || Game.Instance == null) return;
            RanchedStates.Instance state = invalidState;
            RanchableMonitor.Instance monitor = releaseMonitor;
            RanchStation.Instance station = releaseStation;
            invalidState = null; releaseMonitor = null; releaseStation = null;
            try
            {
                if (state != null && !state.IsNullOrStopped() && !RanchStateLifecycle.HasValidTarget(state))
                {
                    // 离开原 GoTo/Enter 栈后再停任务，让原版取消移动、恢复速度、清队列和目标参数。
                    state.StopSM("OmniMod invalid ranch target");
                    AutoRanchTracker.Clear(gameObject.GetInstanceID());
                    ModLog.Throttle("ranch_invalid_target", "[自动照料] 已取消目标失效的照料任务，动物将重新选择有效站点。", 20f);
                }
                if (monitor == null || !ReferenceEquals(monitor.TargetRanchStation, station) ||
                    RanchStateLifecycle.IsOwnedByStation(monitor, station)) return;
                RanchedStates.Instance running = monitor.States;
                if (!running.IsNullOrStopped()) running.StopSM("OmniMod stale ranch assignment");
                if (ReferenceEquals(monitor.TargetRanchStation, station))
                {
                    if (!station.IsNullOrDestroyed()) station.Abandon(monitor);
                    else monitor.TargetRanchStation = null;
                }
                AutoRanchTracker.Clear(gameObject.GetInstanceID());
            }
            catch (Exception ex)
            {
                ModLog.Throttle("ranch_recovery_failed", "[自动照料] 目标恢复稍后重试：" + ex.Message, 20f);
                if (state != null && !state.IsNullOrStopped()) invalidState = state;
                if (monitor != null && ReferenceEquals(monitor.TargetRanchStation, station))
                { releaseMonitor = monitor; releaseStation = station; }
            }
        }
    }
}
