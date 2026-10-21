using System;
using OmniMod.Core.Logging;

namespace OmniMod.Core.Safety
{
    /// <summary>
    /// Mod 全域异常熔断网：为所有自动化补丁、高频循环和生命周期方法提供统一的异常隔离。
    /// 
    /// 核心目的：
    /// 杜绝任何 Mod 代码缺陷（如反射字段不匹配、空引用、未处理枚举）外溢至游戏主循环，
    /// 避免游戏跳出崩溃弹窗或闪退。
    /// </summary>
    public static class ExceptionShield
    {
        /// <summary>
        /// 安全执行无返回值的逻辑。若发生异常则记录节流警告并安全跳过。
        /// </summary>
        /// <param name="moduleName">业务模块名称（如 "DesalinatorAutoEmpty"）</param>
        /// <param name="action">待执行的动作</param>
        public static void Execute(string moduleName, System.Action action)
        {
            if (action == null) return;

            try
            {
                action();
            }
            catch (Exception ex)
            {
                ModLog.Throttle(
                    $"shield_{moduleName}",
                    $"[异常熔断] 模块 [{moduleName}] 发生异常（已安全拦截，游戏正常运行）: {ex.Message}",
                    10f
                );
            }
        }

        /// <summary>
        /// 安全执行有返回值的逻辑。若发生异常则记录节流警告并返回默认值。
        /// </summary>
        public static T Execute<T>(string moduleName, Func<T> func, T fallback = default)
        {
            if (func == null) return fallback;

            try
            {
                return func();
            }
            catch (Exception ex)
            {
                ModLog.Throttle(
                    $"shield_{moduleName}",
                    $"[异常熔断] 模块 [{moduleName}] 发生异常（已安全返回默认值）: {ex.Message}",
                    10f
                );
                return fallback;
            }
        }
    }
}
