using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Core.Logging;

namespace OmniMod.Fixes
{
    /// <summary>补齐本机 U59 官方中文包的明确漏项，在原版占位符／链接校验之前合并。</summary>
    [HarmonyPatch(typeof(Localization), nameof(Localization.OverloadStrings), new[] { typeof(Dictionary<string, string>) })]
    internal static class ChineseTranslationFallback
    {
        // 与当前程序集 21,330 个 LocString 字段、官方中文包和英文模板逐项对照所得的 19 个漏项。
        private static readonly Dictionary<string, string> Missing = new Dictionary<string, string>
        {
            ["STRINGS.BLUEPRINTS.PERMIT_GLOVES_SWIM_MERM.DESC"] = "蹼状手指和手腕处张开的“鳃”，让这双手套成为海洋爱好者的心头好。",
            ["STRINGS.BLUEPRINTS.PERMIT_GLOVES_SWIM_MERM.NAME"] = "人鱼手套",
            ["STRINGS.BLUEPRINTS.PERMIT_GLOVES_SWIM_WAVECRESTS.DESC"] = "让佩戴者的每一次划水都更显灵动。",
            ["STRINGS.BLUEPRINTS.PERMIT_GLOVES_SWIM_WAVECRESTS.NAME"] = "浪冠手套",
            ["STRINGS.BLUEPRINTS.PERMIT_SHOES_SWIM_MERM.DESC"] = "用加工过的鱼皮、干海藻和大量剪贴簿胶水制成的环保长靴。",
            ["STRINGS.BLUEPRINTS.PERMIT_SHOES_SWIM_MERM.NAME"] = "人鱼长靴",
            ["STRINGS.BLUEPRINTS.PERMIT_SHOES_SWIM_WAVECRESTS.DESC"] = "每走一步，都像踏上了一道大浪。",
            ["STRINGS.BLUEPRINTS.PERMIT_SHOES_SWIM_WAVECRESTS.NAME"] = "破浪长靴",
            ["STRINGS.CODEX.CREATURE_DESCRIPTORS.CONFINED_AQUATIC"] = "水池小于 <b>{0} 格</b>时，会感到<link=\"CREATURES::GUIDE::MOOD\">封闭</link>。",
            ["STRINGS.CODEX.CREATURE_DESCRIPTORS.OVERCROWDING_AQUATIC"] = "水池中每只小动物可用空间不足 <b>{0} 格</b>时，会感到<link=\"CREATURES::GUIDE::MOOD\">拥挤</link>。",
            ["STRINGS.CODEX.PACU.BODY.CONTAINER2"] = "喂给帕库鱼它喜欢的<link=\"PACUTREAT\">帕库鱼点心</link>，有助于驯化并使它保持满足。",
            ["STRINGS.CODEX.STORY_TRAITS.CRITTER_MANIPULATOR.SPECIES_ENTRIES.BUTTERFLY"] = "扫描时，这只小动物将翅膀紧紧裹在身上。查看数据以了解更多信息。",
            ["STRINGS.CODEX.STORY_TRAITS.CRITTER_MANIPULATOR.SPECIES_ENTRIES_EXPANDED.BUTTERFLY"] = "扫描时，这只小动物将翅膀紧紧裹在身上，并用触角推动身体在舱室中翻滚。\n\n这些感觉器官虽然看起来不起眼，但肌肉似乎比近似物种更加发达。尤其值得注意的是：离开这个封闭环境后，它拒绝再做出这种行为。",
            ["STRINGS.CREATURES.MODIFIERS.JAWBOFOOD.NAME"] = "吃过烤海鲜",
            ["STRINGS.CREATURES.MODIFIERS.JAWBOFOOD.TOOLTIP"] = "这只小动物最近吃过<style=\"KKeyword\">烤海鲜</style>。\n\n真美味！",
            ["STRINGS.CREATURES.STATS.PHOTOSYNTHESIS.NAME"] = "光照\n    • 最低：{LUX}",
            ["STRINGS.CREATURES.STATS.PHOTOSYNTHESIS.TOOLTIP"] = "这种植物至少需要 {LUX} 的<style=\"KKeyword\">光照</style>才能产生<style=\"KKeyword\">氧气</style>。",
            ["STRINGS.DUPLICANTS.TRAITS.GRANTED_SKILL_NO_MORALE_COST"] = "这个复制人初始已学会一项<style=\"KKeyword\">技能</style>，且不会因此增加<style=\"KKeyword\">士气需求</style>。",
            ["STRINGS.UI.PRODUCTINFO_INSUFFICIENTRESOURCES_TITLE"] = "可用{0}不足"
        };
        private const string StationEffect = "STRINGS.BUILDINGS.PREFABS.UNDERWATERBREATHINGSTATION.EFFECT";
        private const string OutdatedStationEffect = "通过相连的<link=\"GASPIPING\">气体管道</link>输入为水下复制人提供可呼吸的<link=\"OXYGEN\">氧气</link>。\n\n需要<link=\"POWER\">电力</link>。";
        private const string CorrectStationEffect = "通过相连的<link=\"GASPIPING\">气体管道</link>输入，为处于窒息环境的复制人提供可呼吸的<link=\"OXYGEN\">氧气</link>。";

        private static void Prefix(Dictionary<string, string> translated_strings, out int __state)
        {
            __state = 0;
            if (translated_strings == null || !ModStrings.PreferChinese()) return;
            foreach (var pair in Missing)
                if (!translated_strings.TryGetValue(pair.Key, out string value) || string.IsNullOrWhiteSpace(value))
                { translated_strings[pair.Key] = pair.Value; __state++; }
            // 仅识别已核实的旧官方译文；其它中文译法保持原值，仍交给原版校验。
            if (translated_strings.TryGetValue(StationEffect, out string station) && station == OutdatedStationEffect)
            { translated_strings[StationEffect] = CorrectStationEffect; __state++; }
        }
        private static void Postfix(int __state)
        {
            if (__state > 0) ModLog.Info($"[中文补全] 已补入或修正 {__state} 项中文，沿用原版翻译加载和校验流程。");
            CustomGameSettingsLocalizationFix.ApplyTranslations(force: true);
        }
    }
}
