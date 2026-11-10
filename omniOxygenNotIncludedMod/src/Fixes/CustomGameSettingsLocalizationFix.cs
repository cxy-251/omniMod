using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Klei.CustomSettings;
using OmniMod.Core.Logging;

namespace OmniMod.Fixes
{
    /// <summary>
    /// 修复当模组提前初始化导致 Klei.CustomSettings.CustomGameSettingConfigs 在语言包加载前
    /// 被静态构造（.cctor），从而导致开局“游戏设置 / 自定义设置”界面全部回退显示英文的官方原生缓存问题。
    /// 在语言包就绪后、以及新游戏设置面板打开前，重新将全部设置项、子选项及特质描述刷新为正确的官方简体中文。
    /// </summary>
    internal static class CustomGameSettingsLocalizationFix
    {
        private static bool _alreadyApplied = false;

        private static readonly FieldInfo ConfigLabelField = AccessTools.Field(typeof(SettingConfig), "<label>k__BackingField");
        private static readonly FieldInfo ConfigTooltipField = AccessTools.Field(typeof(SettingConfig), "<tooltip>k__BackingField");
        private static readonly FieldInfo LevelLabelField = AccessTools.Field(typeof(SettingLevel), "<label>k__BackingField");
        private static readonly FieldInfo LevelTooltipField = AccessTools.Field(typeof(SettingLevel), "<tooltip>k__BackingField");

        private static void SetConfigText(SettingConfig config, string label, string tooltip = null)
        {
            if (config == null) return;
            if (label != null) ConfigLabelField?.SetValue(config, label);
            if (tooltip != null) ConfigTooltipField?.SetValue(config, tooltip);
        }

        private static void SetLevelText(SettingLevel level, string label, string tooltip = null)
        {
            if (level == null) return;
            if (label != null) LevelLabelField?.SetValue(level, label);
            if (tooltip != null) LevelTooltipField?.SetValue(level, tooltip);
        }

        private static readonly Dictionary<string, string> SettingIdToKeyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CalorieBurn"] = "CALORIE_BURN",
            ["ImmuneSystem"] = "IMMUNESYSTEM",
            ["Morale"] = "MORALE",
            ["Stress"] = "STRESS",
            ["StressBreaks"] = "STRESS_BREAKS",
            ["CarePackages"] = "CAREPACKAGES",
            ["Durability"] = "DURABILITY",
            ["Radiation"] = "RADIATION",
            ["MeteorShowers"] = "METEORSHOWERS",
            ["SandboxMode"] = "SANDBOXMODE",
            ["FastWorkersMode"] = "FASTWORKERSMODE",
            ["BionicPowerUse"] = "BIONICPOWERUSE",
            ["DemoliorDifficulty"] = "DEMOLIORDIFFICULTY",
            ["StoryTraitCount"] = "STORY_TRAIT_COUNT",
            ["Teleporters"] = "TELEPORTERS",
            ["SaveToCloud"] = "SAVETOCLOUD",
            ["WorldgenSeed"] = "WORLDGEN_SEED",
            ["ClusterLayout"] = "CLUSTER_CHOICE",
            ["Expansion1Active"] = "EXPANSION1ACTIVE",
            ["DlcMixing"] = "DLC_MIXING",
            ["SubworldMixing"] = "SUBWORLD_MIXING",
            ["WorldMixing"] = "WORLD_MIXING"
        };

        private static readonly Dictionary<string, string> StoryIdToKeyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CritterManipulator"] = "CRITTER_MANIPULATOR",
            ["FossilHunt"] = "FOSSIL_HUNT",
            ["Borb"] = "BORB",
            ["MegaBrainTank"] = "MEGA_BRAIN_TANK",
            ["SomniumSynthesizer"] = "SOMNIUM_SYNTHESIZER",
            ["LonelyMinion"] = "LONELY_MINION"
        };

        private static readonly Dictionary<string, string> CommonLevelTranslations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Disabled"] = "禁用",
            ["Enabled"] = "启用",
            ["Default"] = "默认",
            ["Guaranteed"] = "确保",
            ["Likely"] = "可能",
            ["TryMixing"] = "尝试融入",
            ["GuaranteeMixing"] = "确保融入",
            ["Normal"] = "正常",
            ["All"] = "全部",
            ["DuplicantsOnly"] = "仅限复制人",
            ["Indestructible"] = "永不磨损",
            ["ClearSkies"] = "晴空万里",
            ["Off"] = "关闭"
        };

        public static void ApplyTranslations(bool force = false)
        {
            if (!ModStrings.PreferChinese()) return;
            if (_alreadyApplied && !force) return;

            int refreshedCount = 0;

            // 1. 刷新 CustomGameSettingConfigs 上的所有静态实例
            try
            {
                var configFields = typeof(CustomGameSettingConfigs).GetFields(BindingFlags.Public | BindingFlags.Static);
                foreach (var f in configFields)
                {
                    if (f.GetValue(null) is SettingConfig config)
                    {
                        TranslateSettingConfig(config.id ?? f.Name, config);
                        refreshedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[中文补全] 刷新静态设置字段异常: {ex.Message}");
            }

            // 2. 刷新 CustomGameSettings.Instance 运行时字典
            try
            {
                var instance = CustomGameSettings.Instance;
                if (instance != null)
                {
                    if (instance.QualitySettings != null)
                    {
                        foreach (var kvp in instance.QualitySettings)
                        {
                            TranslateSettingConfig(kvp.Key, kvp.Value);
                            refreshedCount++;
                        }
                    }

                    if (instance.StorySettings != null)
                    {
                        foreach (var kvp in instance.StorySettings)
                        {
                            TranslateStoryConfig(kvp.Key, kvp.Value);
                            refreshedCount++;
                        }
                    }

                    if (instance.MixingSettings != null)
                    {
                        foreach (var kvp in instance.MixingSettings)
                        {
                            TranslateMixingConfig(kvp.Key, kvp.Value);
                            refreshedCount++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[中文补全] 刷新运行时设置字典异常: {ex.Message}");
            }

            if (refreshedCount > 0)
            {
                _alreadyApplied = true;
                ModLog.Info($"[中文补全] 已刷新 {refreshedCount} 项开局游戏设置与选项的中文本地化文本。");
            }
        }

        private static void TranslateSettingConfig(string id, SettingConfig config)
        {
            if (config == null || string.IsNullOrEmpty(id)) return;

            if (!SettingIdToKeyMap.TryGetValue(id, out string keyName))
            {
                keyName = id.ToUpperInvariant();
            }

            string prefix = "STRINGS.UI.FRONTEND.CUSTOMGAMESETTINGSSCREEN.SETTINGS." + keyName;
            string newLabel = null;
            string newTooltip = null;

            if (Strings.TryGet(prefix + ".NAME", out var nameEntry) && !string.IsNullOrEmpty(nameEntry.String))
            {
                newLabel = nameEntry.String;
            }
            if (Strings.TryGet(prefix + ".TOOLTIP", out var tipEntry) && !string.IsNullOrEmpty(tipEntry.String))
            {
                newTooltip = tipEntry.String;
            }

            if (newLabel != null || newTooltip != null)
            {
                SetConfigText(config, newLabel, newTooltip);
            }

            if (config is ListSettingConfig listConfig && listConfig.levels != null)
            {
                foreach (var level in listConfig.levels)
                {
                    TranslateLevel(prefix, level);
                }
            }
            else if (config is ToggleSettingConfig toggleConfig)
            {
                if (toggleConfig.on_level != null) TranslateLevel(prefix, toggleConfig.on_level);
                if (toggleConfig.off_level != null) TranslateLevel(prefix, toggleConfig.off_level);
            }
        }

        private static void TranslateStoryConfig(string storyId, SettingConfig config)
        {
            if (config == null || string.IsNullOrEmpty(storyId)) return;

            if (StoryIdToKeyMap.TryGetValue(storyId, out string codexKey))
            {
                string codexPrefix = "STRINGS.CODEX.STORY_TRAITS." + codexKey;
                string newLabel = null;
                string newTooltip = null;

                if (Strings.TryGet(codexPrefix + ".NAME", out var storyName) && !string.IsNullOrEmpty(storyName.String))
                {
                    newLabel = storyName.String;
                }
                if (Strings.TryGet(codexPrefix + ".DESCRIPTION", out var storyDesc) && !string.IsNullOrEmpty(storyDesc.String))
                {
                    newTooltip = storyDesc.String;
                }

                if (newLabel != null || newTooltip != null)
                {
                    SetConfigText(config, newLabel, newTooltip);
                }
            }

            TranslateLevelsGeneral(config);
        }

        private static void TranslateMixingConfig(string mixingId, SettingConfig config)
        {
            if (config == null) return;
            TranslateSettingConfig(mixingId, config);
            TranslateLevelsGeneral(config);
        }

        private static void TranslateLevelsGeneral(SettingConfig config)
        {
            if (config is ListSettingConfig listConfig && listConfig.levels != null)
            {
                foreach (var level in listConfig.levels)
                {
                    TranslateLevelFallback(level);
                }
            }
            else if (config is ToggleSettingConfig toggleConfig)
            {
                if (toggleConfig.on_level != null) TranslateLevelFallback(toggleConfig.on_level);
                if (toggleConfig.off_level != null) TranslateLevelFallback(toggleConfig.off_level);
            }
        }

        private static void TranslateLevel(string prefix, SettingLevel level)
        {
            if (level == null || string.IsNullOrEmpty(level.id)) return;

            string lvlPrefix = prefix + ".LEVELS." + level.id.ToUpperInvariant();

            bool found = false;
            string newLabel = null;
            string newTooltip = null;

            if (Strings.TryGet(lvlPrefix + ".NAME", out var lvlName) && !string.IsNullOrEmpty(lvlName.String))
            {
                newLabel = lvlName.String;
                found = true;
            }
            if (Strings.TryGet(lvlPrefix + ".TOOLTIP", out var lvlTip) && !string.IsNullOrEmpty(lvlTip.String))
            {
                newTooltip = lvlTip.String;
            }

            if (newLabel != null || newTooltip != null)
            {
                SetLevelText(level, newLabel, newTooltip);
            }

            if (!found)
            {
                TranslateLevelFallback(level);
            }
        }

        private static void TranslateLevelFallback(SettingLevel level)
        {
            if (level == null || string.IsNullOrEmpty(level.id)) return;

            if (CommonLevelTranslations.TryGetValue(level.id, out string cnLabel))
            {
                SetLevelText(level, cnLabel);
            }
            else if (CommonLevelTranslations.TryGetValue(level.id.Replace("_", ""), out string cnLabel2))
            {
                SetLevelText(level, cnLabel2);
            }
        }
    }

    [HarmonyPatch(typeof(NewGameSettingsPanel), "Init")]
    internal static class NewGameSettingsPanel_Init_Patch
    {
        private static void Prefix()
        {
            CustomGameSettingsLocalizationFix.ApplyTranslations(force: true);
        }
    }

    [HarmonyPatch(typeof(ColonyDestinationSelectScreen), "OnPrefabInit")]
    internal static class ColonyDestinationSelectScreen_OnPrefabInit_Patch
    {
        private static void Postfix()
        {
            CustomGameSettingsLocalizationFix.ApplyTranslations(force: false);
        }
    }

    [HarmonyPatch(typeof(CustomGameSettings), "OnPrefabInit")]
    internal static class CustomGameSettings_OnPrefabInit_Patch
    {
        private static void Postfix()
        {
            CustomGameSettingsLocalizationFix.ApplyTranslations(force: false);
        }
    }
}
