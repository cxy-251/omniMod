using System;
using System.IO;
using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Fixes
{
    /// <summary>
    /// 存档读取与启动链路终极防护补丁：
    /// 1. 过滤损坏/空壳的应急存档（如体积过小、实体缺失的坏档），自动回退至最新健康自动存档，确保玩家启动游戏绝不陷入坏档循环；
    /// 2. 拦截选人界面、未解锁条目、相机初始化、打印仓检索等组件在异常回退时的空指针雪崩。
    /// </summary>
    public static class SaveLoadSafetyPatch
    {
        private const long MIN_HEALTHY_SAVE_SIZE = 2000000; // 2MB，完整存档通常在 3MB~15MB 之间

        /// <summary>
        /// 检查指定路径的存档文件是否为有效且完整的存档。
        /// </summary>
        public static bool IsValidSaveFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            try
            {
                FileInfo fi = new FileInfo(path);
                // 体积过小（如仅包含 Sim 网格而无实体的 1.6MB 残卷）直接判定为坏档
                if (fi.Length < MIN_HEALTHY_SAVE_SIZE)
                {
                    return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 补丁 1：拦截 SaveLoader.GetLatestSaveForCurrentDLC，确保“继续游戏”绝不命中残卷坏档。
        /// </summary>
        [HarmonyPatch(typeof(SaveLoader), nameof(SaveLoader.GetLatestSaveForCurrentDLC))]
        public static class SaveLoader_GetLatestSaveForCurrentDLC_Patch
        {
            public static void Postfix(ref string __result)
            {
                if (!string.IsNullOrEmpty(__result) && IsValidSaveFile(__result))
                {
                    return;
                }

                string badFile = __result;
                if (!string.IsNullOrEmpty(badFile))
                {
                    ModLog.Warn($"[SaveLoadSafety] ⚠️ 检测到最新的候选存档为不完整或损坏的残卷 ({Path.GetFileName(badFile)})，正在寻找最近的有效完好存档...");
                }

                __result = null;

                try
                {
                    List<SaveLoader.SaveFileEntry> allFiles = SaveLoader.GetAllFiles(true, SaveLoader.SaveType.both);
                    if (allFiles != null)
                    {
                        foreach (SaveLoader.SaveFileEntry entry in allFiles)
                        {
                            if (string.IsNullOrEmpty(entry.path) || entry.path.Equals(badFile, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (!IsValidSaveFile(entry.path))
                            {
                                continue;
                            }

                            Tuple<SaveGame.Header, SaveGame.GameInfo> fileInfo = SaveGame.GetFileInfo(entry.path);
                            if (fileInfo != null && fileInfo.second.saveMajorVersion >= 7)
                            {
                                HashSet<string> dlcIds, currentDlcs;
                                if (fileInfo.second.IsCompatableWithCurrentDlcConfiguration(out dlcIds, out currentDlcs))
                                {
                                    __result = entry.path;
                                    ModLog.Info($"[SaveLoadSafety] ✅ 成功回退并切换至最新完好存档: {Path.GetFileName(__result)} ({new FileInfo(__result).Length / 1024 / 1024:F2} MB)");
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModLog.Error($"[SaveLoadSafety] 筛选完好存档时异常: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 补丁 2：GameUtil.GetActiveTelepad 空引用防护（防止 ClusterManager.Instance 为 null 时引发相机崩溃）
        /// </summary>
        [HarmonyPatch(typeof(GameUtil), nameof(GameUtil.GetActiveTelepad))]
        public static class GameUtil_GetActiveTelepad_SafePatch
        {
            public static bool Prefix(ref GameObject __result)
            {
                if (ClusterManager.Instance == null)
                {
                    __result = null;
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// 补丁 3：选人界面殖民地命名空引用保护
        /// </summary>
        [HarmonyPatch(typeof(BaseNaming), "OnEndEdit")]
        public static class BaseNaming_OnEndEdit_SafePatch
        {
            public static bool Prefix(string newName)
            {
                if (SaveGame.Instance == null)
                {
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// 补丁 4：Codex 解锁周期检查空引用保护
        /// </summary>
        [HarmonyPatch(typeof(Unlocks), "UnlockCycleCodexes")]
        public static class Unlocks_UnlockCycleCodexes_SafePatch
        {
            public static bool Prefix()
            {
                if (GameClock.Instance == null)
                {
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// 补丁 5：大型冲击物特效空引用保护
        /// </summary>
        [HarmonyPatch(typeof(LargeImpactorVisualizerEffect), "OnSpawn")]
        public static class LargeImpactorVisualizerEffect_OnSpawn_SafePatch
        {
            public static bool Prefix()
            {
                if (GameplayEventManager.Instance == null || Db.Get() == null || ClusterManager.Instance == null)
                {
                    return false;
                }
                return true;
            }
        }
    }
}
