using System;
using System.IO;
using HarmonyLib;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Core.Safety
{
    /// <summary>
    /// 现实时间定时防丢备份系统：
    /// 解决缺氧“仅在天亮才自动保存”的致命痛点。当玩家暂停游戏进行长时间复杂基地规划（例如规划 30~50 分钟），
    /// 如果现实时间超过 15 分钟未发生任何存档，本系统会在后台自动生成一份备份快照：
    /// auto_save/[殖民地名] Cycle [N] (AutoBackup).sav
    /// 
    /// 规则：
    /// 1. 若游戏正常在天亮存档或玩家手动存档，计时器会自动清零重置，绝不频繁产生多余文件；
    /// 2. 仅在现实时间距上次存档超过 15 分钟时才触发一次；
    /// 3. 保存时不改变默认存档指针，不干扰正常游戏节奏。
    /// </summary>
    public static class TimedEmergencyBackup
    {
        /// <summary>
        /// 现实时间备份间隔（秒）：默认 15 分钟 (900 秒)
        /// </summary>
        public const float BackupIntervalSeconds = 900f;

        private static float _lastSaveRealtime = 0f;
        private static bool _isEnabled = true;

        public static bool IsEnabled
        {
            get => _isEnabled;
            set => _isEnabled = value;
        }

        public static void RecordSaveTime()
        {
            _lastSaveRealtime = Time.unscaledTime;
        }

        public static void CheckAndPerformBackup()
        {
            if (!_isEnabled) return;
            if (SaveLoader.Instance == null || Game.Instance == null) return;
            if (App.isLoading) return;

            float now = Time.unscaledTime;
            if (_lastSaveRealtime <= 0f)
            {
                _lastSaveRealtime = now;
                return;
            }

            if (now - _lastSaveRealtime >= BackupIntervalSeconds)
            {
                PerformBackup();
            }
        }

        private static void PerformBackup()
        {
            try
            {
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

                string autoSaveDir = SaveLoader.GetActiveAutoSavePath();
                if (!string.IsNullOrEmpty(autoSaveDir))
                {
                    if (!Directory.Exists(autoSaveDir))
                    {
                        Directory.CreateDirectory(autoSaveDir);
                    }
                    targetFolder = autoSaveDir;
                }

                if (string.IsNullOrEmpty(targetFolder)) return;

                int cycle = GameClock.Instance != null ? GameClock.Instance.GetCycle() + 1 : 0;
                string baseName = colonyName.Split(new[] { " Cycle" }, StringSplitOptions.None)[0].Trim();
                string backupFilename = $"{baseName} Cycle {cycle} (AutoBackup).sav";
                string backupFullPath = Path.Combine(targetFolder, backupFilename);

                ModLog.Info($"[容灾防护] 现实时间已达 {BackupIntervalSeconds / 60:F0} 分钟未存档，正在后台生成防丢备份...");
                SaveLoader.Instance.Save(backupFullPath, isAutoSave: false, updateSavePointer: false);
                _lastSaveRealtime = Time.unscaledTime;
                ModLog.Info($"[容灾防护] ✅ 防丢备份已成功保存: {backupFullPath}");
            }
            catch (Exception ex)
            {
                _lastSaveRealtime = Time.unscaledTime; // 失败也推迟，避免反复重试卡顿
                ModLog.Warn($"[容灾防护] 定时防丢备份失败 (已跳过): {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 补丁：在 Game.LateUpdate 中轻量检测现实时间
    /// </summary>
    [HarmonyPatch(typeof(Game), "LateUpdate")]
    internal static class Game_LateUpdate_TimedBackup_Patch
    {
        private static float _lastCheck = 0f;

        private static void Postfix()
        {
            // 每 5 秒现实时间检查一次是否超时，避免每帧执行开销
            float now = Time.unscaledTime;
            if (now - _lastCheck >= 5f)
            {
                _lastCheck = now;
                TimedEmergencyBackup.CheckAndPerformBackup();
            }
        }
    }

    /// <summary>
    /// 补丁：任何正常存档（每日自动存档或手动存档）触发后，重置计时器
    /// </summary>
    [HarmonyPatch(typeof(SaveLoader), "Save", new[] { typeof(string), typeof(bool), typeof(bool) })]
    internal static class SaveLoader_Save_ResetTimedBackup_Patch
    {
        private static void Postfix()
        {
            TimedEmergencyBackup.RecordSaveTime();
        }
    }

    /// <summary>
    /// 补丁：世界加载后初始化计时基准
    /// </summary>
    [HarmonyPatch(typeof(Game), "OnSpawn")]
    internal static class Game_OnSpawn_ResetTimedBackup_Patch
    {
        private static void Postfix()
        {
            TimedEmergencyBackup.RecordSaveTime();
        }
    }
}
