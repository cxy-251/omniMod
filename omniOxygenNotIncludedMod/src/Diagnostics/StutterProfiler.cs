using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace OmniMod.Diagnostics
{
    /// <summary>只计时调度批次与模组入口；不拦截逐物品、逐格子和逐预条件的热点。</summary>
    public static class StutterProfiler
    {
        public static bool Enabled = true;

        internal const float SpikeThresholdMs = 100f;

        private static readonly Dictionary<MethodBase, int> _index = new Dictionary<MethodBase, int>();
        private static readonly List<string> _names = new List<string>();
        private static long[] _frameTicks = new long[0];
        private static int[] _frameCalls = new int[0];
        private static long[] _windowTicks = new long[0];
        private static int[] _windowCalls = new int[0];
        private static long[] _windowMax = new long[0];

        private static readonly string[][] TimingTargets =
        {
            new[] { "Game", "Update" },
            new[] { "Game", "LateUpdate" },
            new[] { "Game", "SimEveryTick" },
            new[] { "Game", "StepTheSim" },
            new[] { "Global", "Update" },
            new[] { "StateMachineUpdater", "Render" },
            new[] { "StateMachineUpdater", "RenderEveryTick" },
            new[] { "StateMachineUpdater", "AdvanceOneSimSubTick" },
            new[] { "GlobalChoreProvider", "Render200ms" },
            new[] { "FetchManager", "Sim1000ms" },
            new[] { "BrainScheduler", "RenderEveryTick" },
            new[] { "FishOvercrowingManager", "Sim1000ms" },
            new[] { "SolidTransferArm", "BatchUpdate" },
            new[] { "SolidTransferArm", "Sim" },
            new[] { "OmniMod.Collection.GlobalItemCollector", "Sim1000ms" },
            new[] { "OmniMod.Collection.GlobalItemCollector", "Sim33ms" },
            new[] { "OmniMod.Collection.CollectionCandidateSnapshot", "Advance" },
            new[] { "OmniMod.Collection.WorldStorageIndex", "Advance" },
            new[] { "OmniMod.Pool.SharedMaterialSupply", "ForChore" },
            new[] { "OmniMod.Collection.BulkBufferManager", "MaterializeSolidFromPool" },
            new[] { "OmniMod.Logistics.RemoteFetch.SolidTransferArm_FindFetchTarget_Patch", "FindTargetInternal" },
            new[] { "OmniMod.Buildings.OmniPocketHabitat", "Sim1000ms" },
            new[] { "SaveLoader", "Save" },
        };

        public static void Install(Harmony harmony)
        {
            if (!Enabled)
            {
                return;
            }

            var targets = new HashSet<MethodBase>();

            // 采用固定的低频入口名单，避免对所有 Harmony 补丁和高频内层循环再加一层计时。
            foreach (string[] t in TimingTargets)
            {
                try
                {
                    Type type = AccessTools.TypeByName(t[0]);
                    MethodInfo m = type != null ? AccessTools.DeclaredMethod(type, t[1]) : null;
                    if (m != null && !m.IsAbstract)
                    {
                        targets.Add(m);
                    }
                }
                catch (Exception)
                {
                    // 有重载或不存在时直接跳过
                }
            }

            foreach (MethodBase m in targets)
            {
                _index[m] = _names.Count;
                _names.Add((m.DeclaringType != null ? m.DeclaringType.FullName : "?") + "." + m.Name);
            }
            int n = _names.Count;
            _frameTicks = new long[n];
            _frameCalls = new int[n];
            _windowTicks = new long[n];
            _windowCalls = new int[n];
            _windowMax = new long[n];

            var prefix = new HarmonyMethod(typeof(StutterProfiler).GetMethod(nameof(TimingPrefix), BindingFlags.Static | BindingFlags.NonPublic)) { priority = Priority.First };
            var postfix = new HarmonyMethod(typeof(StutterProfiler).GetMethod(nameof(TimingPostfix), BindingFlags.Static | BindingFlags.NonPublic)) { priority = Priority.Last };

            int ok = 0;
            foreach (MethodBase m in targets)
            {
                try
                {
                    harmony.Patch(m, prefix, postfix);
                    ok++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[OmniMod][卡顿诊断] 计时挂载失败: {m.DeclaringType?.FullName}.{m.Name} — {e.Message}");
                }
            }
            Debug.Log($"[OmniMod][卡顿诊断] 已挂载计时 {ok}/{targets.Count} 个方法，帧耗时阈值 {SpikeThresholdMs} ms；方法耗时包含子调用，不可直接相加。");
        }

        private static void TimingPrefix(out long __state)
        {
            __state = Game.IsOnMainThread() ? Stopwatch.GetTimestamp() : 0;
        }

        private static void TimingPostfix(long __state, MethodBase __originalMethod)
        {
            if (__state == 0) return;
            long dt = Stopwatch.GetTimestamp() - __state;
            if (__originalMethod == null || !_index.TryGetValue(__originalMethod, out int i))
            {
                return;
            }
            Interlocked.Add(ref _frameTicks[i], dt);
            Interlocked.Increment(ref _frameCalls[i]);
            Interlocked.Add(ref _windowTicks[i], dt);
            Interlocked.Increment(ref _windowCalls[i]);
            long max;
            while (dt > (max = Interlocked.Read(ref _windowMax[i])))
            {
                if (Interlocked.CompareExchange(ref _windowMax[i], dt, max) == max)
                {
                    break;
                }
            }
        }

        private static double ToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        internal static void ReportSpike(float frameMs, int gc0Delta, int gc1Delta, float sinceLastSpikeSec)
        {
            var order = new List<int>();
            for (int i = 0; i < _names.Count; i++)
            {
                if (_frameTicks[i] > 0) order.Add(i);
            }
            order.Sort((a, b) => _frameTicks[b].CompareTo(_frameTicks[a]));

            var sb = new StringBuilder();
            sb.Append($"[OmniMod][卡顿诊断] 卡顿帧 {frameMs:F0} ms | 距上次记录 {sinceLastSpikeSec:F2} s | GC0 +{gc0Delta} GC1 +{gc1Delta} | 速度 {Time.timeScale:F1} | 前台 {Application.isFocused}");
            for (int k = 0; k < order.Count && k < 15; k++)
            {
                int i = order[k];
                sb.Append($"\n    {ToMs(_frameTicks[i]),8:F1} ms  x{_frameCalls[i],-6} {_names[i]}");
            }
            Debug.Log(sb.ToString());
        }

        internal static void ResetFrame()
        {
            Array.Clear(_frameTicks, 0, _frameTicks.Length);
            Array.Clear(_frameCalls, 0, _frameCalls.Length);
        }

        internal static void ReportWindow(float windowSec, int frames, int spikes, float maxFrameMs, int gc0, int gc1, float runningSec)
        {
            var order = new List<int>();
            for (int i = 0; i < _names.Count; i++)
            {
                if (_windowTicks[i] > 0) order.Add(i);
            }
            order.Sort((a, b) => _windowTicks[b].CompareTo(_windowTicks[a]));

            var sb = new StringBuilder();
            sb.Append($"[OmniMod][卡顿诊断] {windowSec:F0}s 汇总 | 平均 {frames / Mathf.Max(windowSec, 0.001f):F1} FPS | >=100ms {spikes} 帧 | 最长帧 {maxFrameMs:F0}ms | 模拟运行 {runningSec:F1}s | GC0 +{gc0} GC1 +{gc1} | 堆 {GC.GetTotalMemory(false) / 1048576} MB");
            sb.Append($"\n    世界 {ClusterManager.Instance?.WorldContainers.Count ?? 0} | 物品 {Components.Pickupables.Count} | 建筑 {Components.BuildingCompletes.Count} | AI 个体 {Components.Brains.Count} | 复制人 {Components.LiveMinionIdentities.Count}");
            for (int k = 0; k < order.Count && k < 20; k++)
            {
                int i = order[k];
                sb.Append($"\n    总 {ToMs(_windowTicks[i]),9:F1} ms  峰值 {ToMs(_windowMax[i]),7:F1} ms  x{_windowCalls[i],-8} {_names[i]}");
            }
            Debug.Log(sb.ToString());

            Array.Clear(_windowTicks, 0, _windowTicks.Length);
            Array.Clear(_windowCalls, 0, _windowCalls.Length);
            Array.Clear(_windowMax, 0, _windowMax.Length);
        }
    }

    /// <summary>每帧末尾统计帧耗时，超过阈值即输出本帧耗时明细。</summary>
    public sealed class StutterMonitor : MonoBehaviour
    {
        private float _lastFrameTime;
        private float _lastSpikeTime;
        private int _lastGc0;
        private int _lastGc1;
        private float _windowStart;
        private int _windowFrames;
        private int _windowSpikes;
        private float _maxFrameMs;
        private int _windowGc0;
        private int _windowGc1;
        private float _runningSec;

        private void Start()
        {
            _lastFrameTime = Time.realtimeSinceStartup;
            _lastSpikeTime = _lastFrameTime;
            _windowStart = _lastFrameTime;
            _lastGc0 = GC.CollectionCount(0);
            _lastGc1 = GC.CollectionCount(1);
            _windowGc0 = _lastGc0;
            _windowGc1 = _lastGc1;
            StutterProfiler.ResetFrame();
        }

        private void LateUpdate()
        {
            float now = Time.realtimeSinceStartup;
            float frameMs = (now - _lastFrameTime) * 1000f;
            _lastFrameTime = now;
            _windowFrames++;

            int gc0 = GC.CollectionCount(0);
            int gc1 = GC.CollectionCount(1);
            _maxFrameMs = Mathf.Max(_maxFrameMs, frameMs);
            if (Time.timeScale > 0f) _runningSec += frameMs / 1000f;
            if (frameMs >= StutterProfiler.SpikeThresholdMs)
            {
                _windowSpikes++;
                // 极卡时最多每 5 秒一条明细；每 10 秒汇总保留全部卡顿次数。
                if (now - _lastSpikeTime >= 5f)
                {
                    StutterProfiler.ReportSpike(frameMs, gc0 - _lastGc0, gc1 - _lastGc1, now - _lastSpikeTime);
                    _lastSpikeTime = now;
                }
            }
            _lastGc0 = gc0;
            _lastGc1 = gc1;
            StutterProfiler.ResetFrame();

            float windowSec = now - _windowStart;
            if (windowSec >= 10f)
            {
                StutterProfiler.ReportWindow(windowSec, _windowFrames, _windowSpikes, _maxFrameMs, gc0 - _windowGc0, gc1 - _windowGc1, _runningSec);
                _windowStart = now;
                _windowFrames = 0;
                _windowSpikes = 0;
                _maxFrameMs = 0f;
                _windowGc0 = gc0;
                _windowGc1 = gc1;
                _runningSec = 0f;
            }
        }
    }

    [HarmonyPatch(typeof(Game), "OnSpawn")]
    internal static class Game_OnSpawn_StutterMonitor_Patch
    {
        private static void Postfix(Game __instance)
        {
            if (StutterProfiler.Enabled && __instance != null && __instance.gameObject.GetComponent<StutterMonitor>() == null)
            {
                __instance.gameObject.AddComponent<StutterMonitor>();
            }
        }
    }
}
