using System;
using System.IO;
using HarmonyLib;
using OmniMod.Core.Logging;

namespace OmniMod.Core.Safety
{
    /// <summary>
    /// 崩溃防丢档容灾系统：在游戏捕获未处理异常、弹出崩溃窗口或即将强退前，
    /// 紧急将当前内存世界状态序列化为独立的 (CrashRecovery) 救命存档。
    /// 
    /// 关键原则：
    /// 1. 绝不覆盖原版正常的每日清晨自动存档（Cycle N.sav）或手动存档；
    /// 2. 不修改 SaveLoader 的当前活动存档指针（updateSavePointer = false），防止不完整状态成为默认档；
    /// 3. 全过程严格 try-catch，万一内存严重破坏无法保存也绝不产生二次崩溃。
    /// </summary>
    public static class CrashRecoverySaver
    {
        private static bool _hasSavedEmergency = false;
        private static readonly object SaveLock = new object();

        /// <summary>
        /// 当载入新存档或重新开始新游戏时重置保存标记。
        /// </summary>
        public static void Reset()
        {
            lock (SaveLock)
            {
                _hasSavedEmergency = false;
            }
        }

        /// <summary>
        /// 触发紧急自动保存。
        /// </summary>
        /// <param name="triggerSource">触发源说明（如 CrashReporter.ShowDialog）</param>
        /// <param name="details">附带的异常信息或原因</param>
        /// <returns>若成功保存返回 true，否则返回 false</returns>
        public static bool TriggerEmergencySave(string triggerSource, string details = null)
        {
            lock (SaveLock)
            {
                if (_hasSavedEmergency)
                {
                    return false;
                }

                try
                {
                    if (SaveLoader.Instance == null || Game.Instance == null)
                    {
                        ModLog.Warn($"[容灾防护] 收到退出/崩溃事件 ({triggerSource})，但当前不在游戏世界中，无需紧急保存。");
                        return false;
                    }

                    string activeSavePath = SaveLoader.GetActiveSaveFilePath();
                    string colonyName = "Colony";
                    string targetFolder = null;

                    if (!string.IsNullOrEmpty(activeSavePath))
                    {
                        colonyName = Path.GetFileNameWithoutExtension(activeSavePath);
                        targetFolder = Path.GetDirectoryName(activeSavePath);
                    }
                    else if (SaveLoader.Instance.GameInfo.baseName != null)
                    {
                        colonyName = SaveLoader.Instance.GameInfo.baseName;
                        targetFolder = SaveLoader.GetActiveSaveColonyFolder();
                    }

                    // 优先存入 auto_save 子目录，保持主目录清爽
                    string autoSaveDir = SaveLoader.GetActiveAutoSavePath();
                    if (!string.IsNullOrEmpty(autoSaveDir))
                    {
                        if (!Directory.Exists(autoSaveDir))
                        {
                            Directory.CreateDirectory(autoSaveDir);
                        }
                        targetFolder = autoSaveDir;
                    }

                    if (string.IsNullOrEmpty(targetFolder))
                    {
                        ModLog.Warn("[容灾防护] 未能解析到有效的存档目标目录，跳过紧急保存。");
                        return false;
                    }

                    int cycle = GameClock.Instance != null ? GameClock.Instance.GetCycle() + 1 : 0;
                    // 去除可能已存在的 " Cycle 123" 后缀，保持基础殖民地名称纯净
                    string baseName = colonyName.Split(new[] { " Cycle" }, StringSplitOptions.None)[0].Trim();
                    string recoveryFilename = $"{baseName} Cycle {cycle} (CrashRecovery).sav";
                    string recoveryFullPath = Path.Combine(targetFolder, recoveryFilename);

                    ModLog.Warn($"[容灾防护] ⚠️ 检测到游戏异常/即将退出 ({triggerSource})！正在紧急抢救内存进度...");
                    if (!string.IsNullOrEmpty(details))
                    {
                        ModLog.Warn($"[容灾防护] 异常原因简述: {details.Split('\n')[0]}");
                    }

                    // isAutoSave: false (不触发行数清理), updateSavePointer: false (不修改默认指针)
                    SaveLoader.Instance.Save(recoveryFullPath, isAutoSave: false, updateSavePointer: false);

                    _hasSavedEmergency = true;
                    ModLog.Info($"[容灾防护] ✅ 紧急抢救成功！已将最新进度安全写入: {recoveryFullPath}");
                    return true;
                }
                catch (Exception ex)
                {
                    ModLog.Error($"[容灾防护] ❌ 紧急抢救保存失败 (已安全拦截，未影响主进程退出): {ex.Message}", ex);
                    return false;
                }
            }
        }
    }

    /// <summary>
    /// 补丁 1：拦截原版崩溃弹窗（游戏抛出未捕获异常时触发）
    /// </summary>
    [HarmonyPatch(typeof(KCrashReporter), "ShowDialog")]
    internal static class KCrashReporter_ShowDialog_Patch
    {
        private static void Prefix(string error, string stack_trace)
        {
            CrashRecoverySaver.TriggerEmergencySave("KCrashReporter.ShowDialog", error);
        }
    }

    /// <summary>
    /// 补丁 2：拦截崩溃后玩家点击“退出桌面”
    /// </summary>
    [HarmonyPatch(typeof(KCrashReporter), "OnQuitToDesktopCrashed")]
    internal static class KCrashReporter_OnQuitToDesktopCrashed_Patch
    {
        private static void Prefix()
        {
            CrashRecoverySaver.TriggerEmergencySave("KCrashReporter.OnQuitToDesktopCrashed");
        }
    }

    /// <summary>
    /// 补丁 3：拦截退出代码（非正常退出或带错误码退出时最后一道防线）
    /// </summary>
    [HarmonyPatch(typeof(App), "QuitCode")]
    internal static class App_QuitCode_Patch
    {
        private static void Prefix(int exitCode)
        {
            if (exitCode != 0)
            {
                CrashRecoverySaver.TriggerEmergencySave($"App.QuitCode({exitCode})");
            }
        }
    }

    /// <summary>
    /// 补丁 4：每次进入新世界或载入存档时重置紧急保存标记
    /// </summary>
    [HarmonyPatch(typeof(Game), "OnSpawn")]
    internal static class Game_OnSpawn_ResetRecovery_Patch
    {
        private static void Postfix()
        {
            CrashRecoverySaver.Reset();
        }
    }
}
