using System;
using System.Collections.Generic;
using HarmonyLib;
using Klei.AI;
using OmniMod.Buildings;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Rooms
{
    /// <summary>
    /// 全新娱乐休闲专属房间扩展与原版娱乐建筑数值强化：
    /// 1. 【风月微醺吧】(Chic Lounge)：以全能微醺特调吧为核心的高格调酒吧，弥漫爵士与酒香；
    /// 2. 【极速电竞馆】(Cyber Cafe)：多台街机联机连坐的赛博网吧，热血组队开黑；
    /// 3. 【夜夜笙歌厅】(Karaoke Lounge)：以点唱音响为核心的沉浸式K歌大包房，放声高歌释放压力；
    /// 4. 优化原版游戏机与音响：大幅降低运行功耗（由 1200W/960W 骤降至 120W），成倍强化游戏与跳舞的士气/属性/减压增益！
    /// </summary>
    public static class EntertainmentRooms
    {
        // 房间 ID
        public const string LoungeBarRoomTypeId = "OmniLoungeBar";
        public const string CyberCafeRoomTypeId = "OmniCyberCafe";
        public const string KaraokeLoungeRoomTypeId = "OmniKaraokeLounge";

        // 到访效果 ID
        public const string LoungeBarEffectId = "OmniLoungeBarVibe";
        public const string CyberCafeEffectId = "OmniCyberCafeThrill";
        public const string KaraokeLoungeEffectId = "OmniKaraokeFever";

        public const float EffectDuration = 600f; // 1 周期

        public static void Register()
        {
            try
            {
                RegisterStrings();
                RegisterEffects();
                UpgradeVanillaRecEffects();
                RegisterRooms();
                ModLog.Info("[房间扩展] 【风月微醺吧】、【极速电竞馆】、【夜夜笙歌厅】及娱乐数值强化注册成功。");
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[房间扩展] 注册休闲娱乐房间扩展失败: {ex}");
            }
        }

        private static void RegisterStrings()
        {
            // 1. 风月微醺吧
            string barName = ModStrings.T("风月微醺吧", "Chic Lounge").Resolve();
            string barDesc = ModStrings.T(
                "弥漫着摇摆爵士与特调酒香的优雅酒吧。摆放【全能微醺特调吧】，酒香四溢，情意绵绵。为小酌放松的复制人提供极佳的士气与身心愉悦。",
                "An elegant lounge bathed in smooth jazz and artisan cocktails. Features the Omni Liquor Dispenser, granting patrons immense relaxation and morale."
            ).Resolve();
            string barEffectSummary = ModStrings.T(
                "+6 士气\n-20% 压力/周期\n+3 免疫力\n+2 运动能力",
                "+6 Morale\n-20% Stress/cycle\n+3 Immunity\n+2 Athletics"
            ).Resolve();

            string barEffectName = ModStrings.T("风月微醺", "Twilight Vibes").Resolve();
            string barEffectDesc = ModStrings.T(
                "刚刚在风月微醺吧小酌片刻，情调优雅，身心舒畅！士气 +6；压力 -20%/周期；免疫力 +3；运动 +2。持续一周期。",
                "Chilling in the chic lounge sipping handcrafted spirits: +6 Morale, -20% Stress/cycle, +3 Immunity, +2 Athletics for 1 cycle."
            ).Resolve();

            AddRoomStrings(LoungeBarRoomTypeId, barName, barDesc, barEffectSummary);
            AddEffectStrings(LoungeBarEffectId, barEffectName, barEffectDesc);

            // 2. 极速电竞馆（网吧）
            string cafeName = ModStrings.T("极速电竞馆", "Cyber Cafe").Resolve();
            string cafeDesc = ModStrings.T(
                "专为复制人开黑组队打造的赛博电竞馆。至少摆放两台街机游戏机，按键如飞，连击不断。为到访开黑的复制人提供澎湃的竞技激情与灵感。",
                "A cyber esports cafe built for multiplayer squad gaming. Requires at least two arcade machines. Grants boundless passion, reflexes, and inspiration."
            ).Resolve();
            string cafeEffectSummary = ModStrings.T(
                "+8 士气\n-25% 压力/周期\n+3 学习能力\n+3 机械操作",
                "+8 Morale\n-25% Stress/cycle\n+3 Science\n+3 Machinery"
            ).Resolve();

            string cafeEffectName = ModStrings.T("开黑狂欢", "Gamer's High").Resolve();
            string cafeEffectDesc = ModStrings.T(
                "在电竞馆通宵连坐开黑大获全胜！士气 +8；学习能力 +3；机械操作 +3；压力 -25%/周期。持续一周期。",
                "Squad gaming all night and clutching the final match: +8 Morale, +3 Science, +3 Machinery, -25% Stress/cycle for 1 cycle."
            ).Resolve();

            AddRoomStrings(CyberCafeRoomTypeId, cafeName, cafeDesc, cafeEffectSummary);
            AddEffectStrings(CyberCafeEffectId, cafeEffectName, cafeEffectDesc);

            // 3. 夜夜笙歌厅（K歌房）
            string ktvName = ModStrings.T("夜夜笙歌厅", "Karaoke Lounge").Resolve();
            string ktvDesc = ModStrings.T(
                "光影摇曳、动感音浪奔涌的沉浸式K歌大包房。摆放点唱音响与炫彩装点，复制人在此尽情放声高歌、劲歌热舞，彻底释放所有疲劳与烦闷。",
                "An immersive karaoke lounge booming with vibrant beats and stage lights. Features a jukebox for duet singing and dance battles, blowing all stress away."
            ).Resolve();
            string ktvEffectSummary = ModStrings.T(
                "+6 士气\n-25% 压力/周期\n+3 运动能力\n+2 力量",
                "+6 Morale\n-25% Stress/cycle\n+3 Athletics\n+2 Strength"
            ).Resolve();

            string ktvEffectName = ModStrings.T("麦霸附体", "Soul Vocalist").Resolve();
            string ktvEffectDesc = ModStrings.T(
                "在夜夜笙歌厅手握麦克风放声高歌，嗨翻全场！士气 +6；运动 +3；力量 +2；压力 -25%/周期。持续一周期。",
                "Rocking the microphone and hitting all high notes in the karaoke room: +6 Morale, +3 Athletics, +2 Strength, -25% Stress/cycle for 1 cycle."
            ).Resolve();

            AddRoomStrings(KaraokeLoungeRoomTypeId, ktvName, ktvDesc, ktvEffectSummary);
            AddEffectStrings(KaraokeLoungeEffectId, ktvEffectName, ktvEffectDesc);
        }

        private static void AddRoomStrings(string id, string name, string desc, string effect)
        {
            string upper = id.ToUpperInvariant();
            Strings.Add($"STRINGS.ROOMS.TYPES.{upper}.NAME", name);
            Strings.Add($"STRINGS.ROOMS.TYPES.{upper}.DESCRIPTION", desc);
            Strings.Add($"STRINGS.ROOMS.TYPES.{upper}.TOOLTIP", name);
            Strings.Add($"STRINGS.ROOMS.TYPES.{upper}.EFFECT", effect);
        }

        private static void AddEffectStrings(string id, string name, string desc)
        {
            string upper = id.ToUpperInvariant();
            Strings.Add($"STRINGS.DUPLICANTS.MODIFIERS.{upper}.NAME", name);
            Strings.Add($"STRINGS.DUPLICANTS.MODIFIERS.{upper}.TOOLTIP", desc);
        }

        private static void RegisterEffects()
        {
            var db = Db.Get();

            // 1. 风月微醺效果
            if (!db.effects.Exists(LoungeBarEffectId))
            {
                var barEffect = new Effect(LoungeBarEffectId, ModStrings.T("风月微醺", "Twilight Vibes").Resolve(),
                    "", EffectDuration, show_in_ui: true, trigger_floating_text: true, is_bad: false);
                barEffect.Add(new AttributeModifier(db.Attributes.QualityOfLife.Id, 6f, barEffect.Name));
                barEffect.Add(new AttributeModifier(db.Amounts.Stress.deltaAttribute.Id, -20f / 600f, barEffect.Name));
                barEffect.Add(new AttributeModifier(db.Attributes.Immunity.Id, 3f, barEffect.Name));
                barEffect.Add(new AttributeModifier(db.Attributes.Athletics.Id, 2f, barEffect.Name));
                db.effects.Add(barEffect);
            }

            // 2. 开黑狂欢效果
            if (!db.effects.Exists(CyberCafeEffectId))
            {
                var cafeEffect = new Effect(CyberCafeEffectId, ModStrings.T("开黑狂欢", "Gamer's High").Resolve(),
                    "", EffectDuration, show_in_ui: true, trigger_floating_text: true, is_bad: false);
                cafeEffect.Add(new AttributeModifier(db.Attributes.QualityOfLife.Id, 8f, cafeEffect.Name));
                cafeEffect.Add(new AttributeModifier(db.Amounts.Stress.deltaAttribute.Id, -25f / 600f, cafeEffect.Name));
                cafeEffect.Add(new AttributeModifier(db.Attributes.Learning.Id, 3f, cafeEffect.Name));
                cafeEffect.Add(new AttributeModifier(db.Attributes.Machinery.Id, 3f, cafeEffect.Name));
                db.effects.Add(cafeEffect);
            }

            // 3. 麦霸附体效果
            if (!db.effects.Exists(KaraokeLoungeEffectId))
            {
                var ktvEffect = new Effect(KaraokeLoungeEffectId, ModStrings.T("麦霸附体", "Soul Vocalist").Resolve(),
                    "", EffectDuration, show_in_ui: true, trigger_floating_text: true, is_bad: false);
                ktvEffect.Add(new AttributeModifier(db.Attributes.QualityOfLife.Id, 6f, ktvEffect.Name));
                ktvEffect.Add(new AttributeModifier(db.Amounts.Stress.deltaAttribute.Id, -25f / 600f, ktvEffect.Name));
                ktvEffect.Add(new AttributeModifier(db.Attributes.Athletics.Id, 3f, ktvEffect.Name));
                ktvEffect.Add(new AttributeModifier(db.Attributes.Strength.Id, 2f, ktvEffect.Name));
                db.effects.Add(ktvEffect);
            }
        }

        /// <summary>
        /// 原版街机与音响使用效果成倍强化
        /// </summary>
        private static void UpgradeVanillaRecEffects()
        {
            var db = Db.Get();

            // 街机玩耍效果强化：士气 +8，学习 +3，运动 +2，压力 -20%/周期
            if (db.effects.Exists("PlayedArcade"))
            {
                var playedArcade = db.effects.Get("PlayedArcade");
                playedArcade.SelfModifiers.Clear();
                playedArcade.Add(new AttributeModifier(db.Attributes.QualityOfLife.Id, 8f, playedArcade.Name));
                playedArcade.Add(new AttributeModifier(db.Attributes.Learning.Id, 3f, playedArcade.Name));
                playedArcade.Add(new AttributeModifier(db.Attributes.Athletics.Id, 2f, playedArcade.Name));
                playedArcade.Add(new AttributeModifier(db.Amounts.Stress.deltaAttribute.Id, -20f / 600f, playedArcade.Name));

                Strings.Add("STRINGS.DUPLICANTS.MODIFIERS.PLAYEDARCADE.TOOLTIP",
                    ModStrings.T(
                        "刚刚在街机上大杀四方！士气 +8；学习能力 +3；运动 +2；压力 -20%/周期。",
                        "Just beat the final boss on the arcade machine: +8 Morale, +3 Science, +2 Athletics, -20% Stress/cycle."
                    ).Resolve()
                );
            }

            // 跳舞动感效果强化：士气 +6，运动 +3，力量 +2，压力 -25%/周期
            if (db.effects.Exists("Danced"))
            {
                var danced = db.effects.Get("Danced");
                danced.SelfModifiers.Clear();
                danced.Add(new AttributeModifier(db.Attributes.QualityOfLife.Id, 6f, danced.Name));
                danced.Add(new AttributeModifier(db.Attributes.Athletics.Id, 3f, danced.Name));
                danced.Add(new AttributeModifier(db.Attributes.Strength.Id, 2f, danced.Name));
                danced.Add(new AttributeModifier(db.Amounts.Stress.deltaAttribute.Id, -25f / 600f, danced.Name));

                Strings.Add("STRINGS.DUPLICANTS.MODIFIERS.DANCED.TOOLTIP",
                    ModStrings.T(
                        "伴着炫动节拍尽情摇摆！士气 +6；运动能力 +3；力量 +2；压力 -25%/周期。",
                        "Grooving to electric beats: +6 Morale, +3 Athletics, +2 Strength, -25% Stress/cycle."
                    ).Resolve()
                );
            }
        }

        private static void RegisterRooms()
        {
            var db = Db.Get();
            var category = db.RoomTypeCategories.Recreation;

            // 1. 【风月微醺吧】(Chic Lounge)
            if (db.RoomTypes.GetRoomTypeForID(LoungeBarRoomTypeId) == null)
            {
                var liquorDispenserConstraint = new RoomConstraints.Constraint(
                    building_criteria: kpid => kpid != null && kpid.GetComponent<LiquorDispenser>() != null,
                    room_criteria: null,
                    times_required: 1,
                    name: ModStrings.T("至少一台全能微醺特调吧", "At least 1 Liquor Dispenser").Resolve(),
                    description: ModStrings.T(
                        "必须包含至少一台特调酒饮机，为复制人调配浪漫微醺酒品",
                        "Requires at least one Liquor Dispenser for serving artisan drinks"
                    ).Resolve(),
                    stomp_in_conflict: new List<RoomConstraints.Constraint>
                    {
                        RoomConstraints.REC_BUILDING,
                        RoomConstraints.DECORATIVE_ITEM
                    }
                );

                var barRoom = new RoomType(
                    id: LoungeBarRoomTypeId,
                    name: ModStrings.T("风月微醺吧", "Chic Lounge").Resolve(),
                    description: Strings.Get($"STRINGS.ROOMS.TYPES.{LoungeBarRoomTypeId.ToUpperInvariant()}.DESCRIPTION"),
                    tooltip: ModStrings.T("风月微醺吧", "Chic Lounge").Resolve(),
                    effect: Strings.Get($"STRINGS.ROOMS.TYPES.{LoungeBarRoomTypeId.ToUpperInvariant()}.EFFECT"),
                    category: category,
                    primary_constraint: liquorDispenserConstraint,
                    additional_constraints: new[]
                    {
                        RoomConstraints.DECORATIVE_ITEM,
                        RoomConstraints.NO_INDUSTRIAL_MACHINERY,
                        RoomConstraints.NO_COTS,
                        RoomConstraints.NO_LUXURY_BEDS,
                        RoomConstraints.NO_OUTHOUSES,
                        RoomConstraints.NO_MESS_STATION,
                        RoomConstraints.MINIMUM_SIZE_12,
                        RoomConstraints.MAXIMUM_SIZE_120
                    },
                    display_details: new RoomDetails.Detail[]
                    {
                        RoomDetails.SIZE,
                        RoomDetails.BUILDING_COUNT
                    },
                    priority: 2,
                    upgrade_paths: null,
                    single_assignee: false,
                    priority_building_use: false,
                    effects: new[] { LoungeBarEffectId },
                    sortKey: 18
                );

                db.RoomTypes.Add(barRoom);
                LandmarkRooms.RegisterVisitEffect(LoungeBarRoomTypeId, LoungeBarEffectId);
            }

            // 2. 【极速电竞馆】(Cyber Cafe) —— 至少两台游戏机！
            if (db.RoomTypes.GetRoomTypeForID(CyberCafeRoomTypeId) == null)
            {
                var multipleArcadesConstraint = new RoomConstraints.Constraint(
                    building_criteria: kpid => kpid != null && kpid.GetComponent<ArcadeMachine>() != null,
                    room_criteria: null,
                    times_required: 2,
                    name: ModStrings.T("至少两台街机游戏机", "At least 2 Arcade Machines").Resolve(),
                    description: ModStrings.T(
                        "必须包含至少两台街机，满足多名复制人联机连坐、组队开黑的热血竞技需求",
                        "Requires at least 2 Arcade Machines for multiplayer squad gaming"
                    ).Resolve(),
                    stomp_in_conflict: new List<RoomConstraints.Constraint>
                    {
                        RoomConstraints.REC_BUILDING,
                        RoomConstraints.DECORATIVE_ITEM
                    }
                );

                var cafeRoom = new RoomType(
                    id: CyberCafeRoomTypeId,
                    name: ModStrings.T("极速电竞馆", "Cyber Cafe").Resolve(),
                    description: Strings.Get($"STRINGS.ROOMS.TYPES.{CyberCafeRoomTypeId.ToUpperInvariant()}.DESCRIPTION"),
                    tooltip: ModStrings.T("极速电竞馆", "Cyber Cafe").Resolve(),
                    effect: Strings.Get($"STRINGS.ROOMS.TYPES.{CyberCafeRoomTypeId.ToUpperInvariant()}.EFFECT"),
                    category: category,
                    primary_constraint: multipleArcadesConstraint,
                    additional_constraints: new[]
                    {
                        RoomConstraints.NO_INDUSTRIAL_MACHINERY,
                        RoomConstraints.NO_COTS,
                        RoomConstraints.NO_LUXURY_BEDS,
                        RoomConstraints.NO_OUTHOUSES,
                        RoomConstraints.NO_MESS_STATION,
                        RoomConstraints.MINIMUM_SIZE_24,
                        RoomConstraints.MAXIMUM_SIZE_120
                    },
                    display_details: new RoomDetails.Detail[]
                    {
                        RoomDetails.SIZE,
                        RoomDetails.BUILDING_COUNT
                    },
                    priority: 2,
                    upgrade_paths: null,
                    single_assignee: false,
                    priority_building_use: false,
                    effects: new[] { CyberCafeEffectId },
                    sortKey: 19
                );

                db.RoomTypes.Add(cafeRoom);
                LandmarkRooms.RegisterVisitEffect(CyberCafeRoomTypeId, CyberCafeEffectId);
            }

            // 3. 【夜夜笙歌厅】(Karaoke Lounge) —— 至少一台点唱音响与氛围装点
            if (db.RoomTypes.GetRoomTypeForID(KaraokeLoungeRoomTypeId) == null)
            {
                var phonoboxConstraint = new RoomConstraints.Constraint(
                    building_criteria: kpid => kpid != null && kpid.GetComponent<Phonobox>() != null,
                    room_criteria: null,
                    times_required: 1,
                    name: ModStrings.T("至少一台点唱音响", "At least 1 Jukebox").Resolve(),
                    description: ModStrings.T(
                        "必须包含至少一台点唱音响，让复制人放声高歌、劲歌热舞",
                        "Requires at least one Jukebox for singing and dancing"
                    ).Resolve(),
                    stomp_in_conflict: new List<RoomConstraints.Constraint>
                    {
                        RoomConstraints.REC_BUILDING,
                        RoomConstraints.DECORATIVE_ITEM
                    }
                );

                var ktvRoom = new RoomType(
                    id: KaraokeLoungeRoomTypeId,
                    name: ModStrings.T("夜夜笙歌厅", "Karaoke Lounge").Resolve(),
                    description: Strings.Get($"STRINGS.ROOMS.TYPES.{KaraokeLoungeRoomTypeId.ToUpperInvariant()}.DESCRIPTION"),
                    tooltip: ModStrings.T("夜夜笙歌厅", "Karaoke Lounge").Resolve(),
                    effect: Strings.Get($"STRINGS.ROOMS.TYPES.{KaraokeLoungeRoomTypeId.ToUpperInvariant()}.EFFECT"),
                    category: category,
                    primary_constraint: phonoboxConstraint,
                    additional_constraints: new[]
                    {
                        RoomConstraints.DECORATIVE_ITEM,
                        RoomConstraints.NO_INDUSTRIAL_MACHINERY,
                        RoomConstraints.NO_COTS,
                        RoomConstraints.NO_LUXURY_BEDS,
                        RoomConstraints.NO_OUTHOUSES,
                        RoomConstraints.NO_MESS_STATION,
                        RoomConstraints.MINIMUM_SIZE_12,
                        RoomConstraints.MAXIMUM_SIZE_120
                    },
                    display_details: new RoomDetails.Detail[]
                    {
                        RoomDetails.SIZE,
                        RoomDetails.BUILDING_COUNT
                    },
                    priority: 2,
                    upgrade_paths: null,
                    single_assignee: false,
                    priority_building_use: false,
                    effects: new[] { KaraokeLoungeEffectId },
                    sortKey: 20
                );

                db.RoomTypes.Add(ktvRoom);
                LandmarkRooms.RegisterVisitEffect(KaraokeLoungeRoomTypeId, KaraokeLoungeEffectId);
            }
        }
    }

    /// <summary>
    /// 原版街机游戏机数值调优：耗电由 1200W 降至 120W，大幅降低产热
    /// </summary>
    [HarmonyPatch(typeof(ArcadeMachineConfig), "CreateBuildingDef")]
    public static class ArcadeMachineConfig_CreateBuildingDef_Patch
    {
        public static void Postfix(ref BuildingDef __result)
        {
            if (__result != null)
            {
                __result.EnergyConsumptionWhenActive = 120f;
                __result.SelfHeatKilowattsWhenActive = 0.5f;
            }
        }
    }

    /// <summary>
    /// 原版点唱音响数值调优：耗电由 960W 降至 120W，大幅降低产热
    /// </summary>
    [HarmonyPatch(typeof(PhonoboxConfig), "CreateBuildingDef")]
    public static class PhonoboxConfig_CreateBuildingDef_Patch
    {
        public static void Postfix(ref BuildingDef __result)
        {
            if (__result != null)
            {
                __result.EnergyConsumptionWhenActive = 120f;
                __result.SelfHeatKilowattsWhenActive = 0.5f;
            }
        }
    }
}
