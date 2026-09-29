using HarmonyLib;
using UnityEngine;

namespace OmniMod.Fixes
{
    /// <summary>
    /// 修复原版 Bug：能量组充电站（ElectrobankCharger）进入 charging 状态时，把当前
    /// 正在充的那块空电池缓存成一个 GameObject 引用（targetElectrobank）。这个字段没有
    /// 标 [Serialize]，充电站真正认的是另外两个已序列化的状态机参数（是否有电池 / 内部电量）。
    ///
    /// 只要充电站还处在 charging 状态，它就不再监听箱子内容变化——期间如果那块被缓存的
    /// 电池被挪走（例如往充电站里又塞了一块满电池、手动搬走等），或者读档后
    /// targetElectrobank 没能正确恢复，充满电触发 full 状态时都会拿着一个空引用去调用
    /// Electrobank.Replace，直接 electrobank.transform 空引用崩溃，游戏只能退出/禁用模组。
    ///
    /// 这里给原版这个私有静态方法打 Prefix：传进来的对象是 null 就跳过原方法、安静返回
    /// null，这次换电池操作当空跑一次，充电站下个 tick 会自己重新扫描箱子找目标，不再崩溃。
    /// </summary>
    [HarmonyPatch(typeof(Electrobank), "Replace")]
    internal static class Electrobank_Replace_NullGuard_Patch
    {
        private static int _logged;

        private static bool Prefix(GameObject electrobank, ref GameObject __result)
        {
            if (electrobank != null)
            {
                return true;   // 正常情况：放行原逻辑
            }

            __result = null;
            if (_logged < 10)
            {
                _logged++;
                Debug.LogWarning("[OmniMod] Electrobank.Replace 收到 null 对象，已跳过（原版空引用崩溃的防护，见 Fixes/ElectrobankCrashFixPatch.cs）");
            }

            return false;   // 跳过原方法，不再抛 NullReferenceException
        }
    }
}
