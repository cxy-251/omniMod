using System.Collections.Concurrent;
using UnityEngine;

namespace OmniMod.Core.Logging
{
    /// <summary>
    /// 日志节流器：防止在游戏 Update/Sim 循环中大量打印重复日志导致 I/O 卡顿与 Player.log 膨胀。
    /// </summary>
    public static class LogThrottle
    {
        private static readonly ConcurrentDictionary<string, float> LastLoggedTime = new ConcurrentDictionary<string, float>();
        private static readonly ConcurrentDictionary<string, byte> LoggedOnceKeys = new ConcurrentDictionary<string, byte>();

        /// <summary>
        /// 判定指定 key 是否已过节流间隔时间。若已过则更新记录并返回 true。
        /// </summary>
        /// <param name="key">日志唯一标识</param>
        /// <param name="intervalSeconds">最小输出间隔（秒）</param>
        public static bool ShouldLog(string key, float intervalSeconds)
        {
            if (string.IsNullOrEmpty(key)) return true;

            float now = Time.realtimeSinceStartup;
            if (LastLoggedTime.TryGetValue(key, out float lastTime))
            {
                if (now - lastTime < intervalSeconds)
                {
                    return false;
                }
            }

            LastLoggedTime[key] = now;
            return true;
        }

        /// <summary>
        /// 判定指定 key 是否为首次输出。仅首次返回 true，后续调用均返回 false。
        /// </summary>
        public static bool ShouldLogOnce(string key)
        {
            if (string.IsNullOrEmpty(key)) return true;
            return LoggedOnceKeys.TryAdd(key, 0);
        }

        /// <summary>
        /// 清理所有节流记录。
        /// </summary>
        public static void Reset()
        {
            LastLoggedTime.Clear();
            LoggedOnceKeys.Clear();
        }
    }
}
