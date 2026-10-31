using System;
using UnityEngine;

namespace OmniMod.Core.Logging
{
    /// <summary>
    /// OmniMod 统一日志门面：提供分级、节流和开关控制的日志输出。
    /// </summary>
    public static class ModLog
    {
        public const string Tag = "OmniMod";
        public const string Prefix = "[" + Tag + "] ";

        /// <summary>
        /// 调试日志开关：生产环境下默认关闭，避免高频日志消耗性能。
        /// </summary>
        public static bool EnableDebug = false;

        public static void Info(string msg)
        {
            Debug.Log(Prefix + msg);
        }

        public static void Warn(string msg)
        {
            Debug.LogWarning(Prefix + msg);
        }

        public static void Error(string msg, Exception ex = null)
        {
            if (ex != null)
            {
                Debug.LogError($"{Prefix}{msg}\n{ex}");
            }
            else
            {
                Debug.LogError(Prefix + msg);
            }
        }

        public static void DebugLog(string msg)
        {
            if (EnableDebug)
            {
                Debug.Log($"{Prefix}[Debug] {msg}");
            }
        }

        internal readonly struct CallTimer
        {
            internal readonly long Started;
            internal readonly int FullCollections;

            internal CallTimer(long started, int fullCollections)
            {
                Started = started;
                FullCollections = fullCollections;
            }
        }

        internal static CallTimer StartCall() => new CallTimer(System.Diagnostics.Stopwatch.GetTimestamp(),
            GC.CollectionCount(GC.MaxGeneration));

        internal static void SlowCall(string operation, CallTimer timer, int items = 0)
        {
            double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - timer.Started) * 1000.0 /
                               System.Diagnostics.Stopwatch.Frequency;
            if (elapsedMs < 100.0) return;
            int collections = GC.CollectionCount(GC.MaxGeneration) - timer.FullCollections;
            Throttle("slow_" + operation, $"[慢调用] {operation} 耗时 {elapsedMs:F0} ms，物品数 {items}，完整 GC +{collections}", 10f);
        }

        /// <summary>
        /// 节流日志：同一 key 至少间隔 intervalSeconds 秒才会再次输出，防止高频刷屏。
        /// </summary>
        public static void Throttle(string key, string msg, float intervalSeconds = 5f)
        {
            if (LogThrottle.ShouldLog(key, intervalSeconds))
            {
                Debug.Log(Prefix + msg);
            }
        }

        /// <summary>
        /// 单次日志：整个游戏生命周期内仅打印一次。
        /// </summary>
        public static void Once(string key, string msg)
        {
            if (LogThrottle.ShouldLogOnce(key))
            {
                Debug.Log(Prefix + msg);
            }
        }
    }
}
