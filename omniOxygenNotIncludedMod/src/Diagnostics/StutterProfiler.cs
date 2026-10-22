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
    /// <summary>
    /// 卡顿诊断器（临时）：
    /// - 给 OmniMod 打过补丁的所有原版方法、OmniMod 自身的 Sim/Update 方法、以及游戏主循环关键方法加计时；
    /// - 每帧结束统计，若帧耗时超过阈值，把本帧耗时最高的方法、GC 次数变化写入 Player.log；
    /// - 每 10 秒输出一次累计耗时排行与内存分配速率。
    /// 日志关键字：[OmniMod][卡顿诊断]
    /// </summary>
    public static class StutterProfiler
    {
        /// <summary>
        /// 性能探针总开关：默认关闭以保证零开销与无干扰纯净运行。需排查卡顿时开启。
        /// </summary>
        public static bool Enabled = false;

        internal const float SpikeThresholdMs = 100f;

        private static readonly Dictionary<MethodBase, int> _index = new Dictionary<MethodBase, int>();
        private static readonly List<string> _names = new List<string>();
        private static long[] _frameTicks = new long[0];
        private static int[] _frameCalls = new int[0];
        private static long[] _windowTicks = new long[0];
        private static int[] _windowCalls = new int[0];
        private static long[] _windowMax = new long[0];

        private static readonly string[] OwnMethodNames =
        {
            "Sim33ms", "Sim200ms", "Sim1000ms", "Sim4000ms", "SimEveryTick",
            "Update", "LateUpdate", "FixedUpdate",
            "RenderEveryTick", "Render200ms", "Render1000ms",
        };

        private static readonly string[][] VanillaTargets =
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
            new[] { "FetchManager", "Sim200ms" },
            new[] { "BrainScheduler", "RenderEveryTick" },
            new[] { "SolidTransferArm", "BatchUpdate" },
            new[] { "ChoreConsumer", "FindNextChore" },
            new[] { "SaveLoader", "Save" },
        };

        public static void Install(Harmony harmony)
        {
            if (!Enabled)
            {
                return;
            }

            var targets = new HashSet<MethodBase>();

            // 1. OmniMod 打过补丁的所有原版方法
            foreach (MethodBase m in Harmony.GetAllPatchedMethods())
            {
                Patches info = Harmony.GetPatchInfo(m);
                if (info != null && info.Owners.Contains(harmony.Id))
                {
                    targets.Add(m);
                }
            }

            // 2. OmniMod 自身组件的 Sim/Update 方法
            var ownNames = new HashSet<string>(OwnMethodNames);
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (type.IsDefined(typeof(HarmonyPatch), false) || type.Namespace == typeof(StutterProfiler).Namespace)
                {
                    continue;
                }
                foreach (MethodInfo m in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (ownNames.Contains(m.Name) && !m.IsAbstract && !m.ContainsGenericParameters)
                    {
                        targets.Add(m);
                    }
                }
            }

            // 3. 游戏主循环关键方法（用于判断卡顿是否发生在 OmniMod 之外）
            foreach (string[] t in VanillaTargets)
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
            Debug.Log($"[OmniMod][卡顿诊断] 已挂载计时 {ok}/{targets.Count} 个方法，帧耗时阈值 {SpikeThresholdMs} ms");
        }

        private static void TimingPrefix(out long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }

        private static void TimingPostfix(long __state, MethodBase __originalMethod)
        {
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
            sb.Append($"[OmniMod][卡顿诊断] 卡顿帧 {frameMs:F0} ms | 距上次卡顿 {sinceLastSpikeSec:F2} s | GC0 +{gc0Delta} GC1 +{gc1Delta} | 堆 {GC.GetTotalMemory(false) / 1048576} MB");
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

        internal static void ReportWindow(float windowSec, int frames, int spikes, double allocMbPerSec)
        {
            var order = new List<int>();
            for (int i = 0; i < _names.Count; i++)
            {
                if (_windowTicks[i] > 0) order.Add(i);
            }
            order.Sort((a, b) => _windowTicks[b].CompareTo(_windowTicks[a]));

            var sb = new StringBuilder();
            sb.Append($"[OmniMod][卡顿诊断] {windowSec:F0}s 汇总 | 平均 {frames / Mathf.Max(windowSec, 0.001f):F1} FPS | 卡顿帧 {spikes} | 约 {allocMbPerSec:F1} MB/s 内存分配");
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
        private long _lastHeap;
        private double _allocBytes;

        private void Start()
        {
            _lastFrameTime = Time.realtimeSinceStartup;
            _lastSpikeTime = _lastFrameTime;
            _windowStart = _lastFrameTime;
            _lastGc0 = GC.CollectionCount(0);
            _lastGc1 = GC.CollectionCount(1);
            _lastHeap = GC.GetTotalMemory(false);
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
            long heap = GC.GetTotalMemory(false);
            if (heap > _lastHeap)
            {
                _allocBytes += heap - _lastHeap;
            }
            _lastHeap = heap;

            if (frameMs >= StutterProfiler.SpikeThresholdMs)
            {
                _windowSpikes++;
                StutterProfiler.ReportSpike(frameMs, gc0 - _lastGc0, gc1 - _lastGc1, now - _lastSpikeTime);
                _lastSpikeTime = now;
            }
            _lastGc0 = gc0;
            _lastGc1 = gc1;
            StutterProfiler.ResetFrame();

            float windowSec = now - _windowStart;
            if (windowSec >= 10f)
            {
                StutterProfiler.ReportWindow(windowSec, _windowFrames, _windowSpikes, _allocBytes / 1048576.0 / windowSec);
                _windowStart = now;
                _windowFrames = 0;
                _windowSpikes = 0;
                _allocBytes = 0;
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
