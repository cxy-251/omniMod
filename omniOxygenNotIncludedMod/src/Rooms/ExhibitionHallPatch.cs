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
    /// 全新房间类型：【展览馆】(Exhibition Hall)。
    ///
    /// 核心设计：
    /// 采用与原版【自然保护区 / 公园】完全一致的标牌标识机制：
    /// 必须放置专属建筑【展览馆标牌】(OmniExhibitionSign) 作为主条件（primary_constraint），
    /// 从而彻底杜绝摆放了画作、雕塑与藏品展台的豪华私人卧室等房间发生房间识别冲突。
    ///
    /// 房间要求：
    /// 1. 主条件：包含至少 1 个【展览馆标牌】
    /// 2. 附加条件：
    ///    - 至少 1 件画作（帆布画等）
    ///    - 至少 1 座雕塑（大理石雕、金属雕、木雕、冰雕、化石雕塑等）
    ///    - 至少 1 件展出藏品（陈列了文物/物品的展台）
    ///    - 空间尺寸：32 ~ 120 格
    ///    - 无工业机械、无床铺、无厕所、无餐桌
    ///
    /// 房间效果：
    /// 复制人进入展览馆即可获得持续 1 周期的【博览群雅】增益：
    /// +6 士气、-15% 压力/周期、+3 科学属性、+50% 技能点经验。
    /// </summary>
    public static class ExhibitionHallPatch
    {
        public const string RoomTypeId = "ExhibitionHall";
        public const string EffectId = "ExhibitionHallInspiration";
        public const float SkillExperienceMultiplier = GalleryBenefits.ExperienceMultiplier;

        public static RoomType ExhibitionHallRoomType { get; private set; }
        public static RoomConstraints.Constraint HAS_EXHIBITION_SIGN { get; private set; }
        public static RoomConstraints.Constraint HAS_PAINTING { get; private set; }
        public static RoomConstraints.Constraint HAS_SCULPTURE { get; private set; }
        public static RoomConstraints.Constraint HAS_DISPLAYED_COLLECTION { get; private set; }

        [HarmonyPatch(typeof(Db), nameof(Db.Initialize))]
        public static class Db_Initialize_Patch
        {
            public static void Postfix()
            {
                try
                {
                    RegisterStrings();
                    RegisterEffect();
                    RegisterConstraints();
                    RegisterRoomType();
                    LandmarkRooms.Register();
                    ModLog.Info("[房间扩展] 【展览馆 (Exhibition Hall)】房间类型与标牌约束注册成功。");
                }
                catch (Exception ex)
                {
                    ModLog.Warn($"[房间扩展] 注册展览馆及扩展房间失败: {ex}");
                }
            }
        }

        private static void RegisterStrings()
        {
            string roomName = ModStrings.T("展览馆", "Exhibition Hall").Resolve();
            string roomDesc = ModStrings.T(
                "立有展览馆标牌，并陈列了名画、雕塑与稀世藏品的文化殿堂。可为到访的复制人提供极高的士气与灵感。",
                "A designated cultural hall marked by an Exhibition Sign, showcasing paintings, sculptures, and displayed artifacts. Grants visiting duplicants high morale and inspiration."
            ).Resolve();
            string roomEffect = GalleryBenefits.Summary(ModStrings.T("学习能力", "Science").Resolve());

            string effectName = ModStrings.T("博览群雅", "Exhibition Inspiration").Resolve();
            string effectDesc = ModStrings.T(
                "刚刚参观过展览馆，精神深受启发！学习能力 +3；技能点经验 +50%（包括随时间及亲自工作获得的经验）。持续一周期，同馆不叠加，不同馆的经验加成相加。",
                "Inspired by a recent Exhibition Hall visit: +3 Science and +50% skill point experience from both time alive and personal work. Lasts one cycle; identical effects do not stack; bonuses from different galleries add together."
            ).Resolve();

            string upperRoomId = RoomTypeId.ToUpperInvariant();
            Strings.Add($"STRINGS.ROOMS.TYPES.{upperRoomId}.NAME", roomName);
            Strings.Add($"STRINGS.ROOMS.TYPES.{upperRoomId}.DESCRIPTION", roomDesc);
            Strings.Add($"STRINGS.ROOMS.TYPES.{upperRoomId}.TOOLTIP", roomName);
            Strings.Add($"STRINGS.ROOMS.TYPES.{upperRoomId}.EFFECT", roomEffect);

            string upperEffectId = EffectId.ToUpperInvariant();
            Strings.Add($"STRINGS.DUPLICANTS.MODIFIERS.{upperEffectId}.NAME", effectName);
            Strings.Add($"STRINGS.DUPLICANTS.MODIFIERS.{upperEffectId}.TOOLTIP", effectDesc);
        }

        private static void RegisterEffect()
        {
            if (Db.Get().effects.Exists(EffectId))
            {
                return;
            }

            string effectName = ModStrings.T("博览群雅", "Exhibition Inspiration").Resolve();
            string effectDesc = ModStrings.T(
                "刚刚参观过展览馆，精神深受启发！学习能力 +3；技能点经验 +50%（包括随时间及亲自工作获得的经验）。持续一周期，同馆不叠加，不同馆的经验加成相加。",
                "Inspired by a recent Exhibition Hall visit: +3 Science and +50% skill point experience from both time alive and personal work. Lasts one cycle; identical effects do not stack; bonuses from different galleries add together."
            ).Resolve();

            var effect = new Effect(
                id: EffectId,
                name: effectName,
                description: effectDesc,
                duration: 600f,
                show_in_ui: true,
                trigger_floating_text: true,
                is_bad: false
            );

            GalleryBenefits.Apply(effect, Db.Get().Attributes.Learning.Id, effectName);

            Db.Get().effects.Add(effect);
        }

        private static void RegisterConstraints()
        {
            HAS_EXHIBITION_SIGN = new RoomConstraints.Constraint(
                building_criteria: kpid => kpid != null && kpid.HasTag(ExhibitionSignConfig.ExhibitionSignTag),
                room_criteria: room => LandmarkRooms.CanDesignateGallery(room, ExhibitionSignConfig.ExhibitionSignTag),
                times_required: 1,
                name: ModStrings.T("展览馆标牌", "Exhibition Sign").Resolve(),
                description: ModStrings.T(
                    "必须建造至少一座【展览馆标牌】以将该房间标识为展览馆",
                    "Requires at least one Exhibition Sign to designate the room as an Exhibition Hall"
                ).Resolve(),
                stomp_in_conflict: new List<RoomConstraints.Constraint>
                {
                    RoomConstraints.REC_BUILDING,
                    RoomConstraints.DECORATIVE_ITEM,
                    RoomConstraints.PARK_BUILDING
                }
            );

            HAS_PAINTING = new RoomConstraints.Constraint(
                building_criteria: kpid => kpid != null && kpid.GetComponent<Painting>() != null,
                room_criteria: null,
                times_required: 1,
                name: ModStrings.T("至少一件画作", "At least one Painting").Resolve(),
                description: ModStrings.T(
                    "包含至少一件画作（帆布画、风景画、肖像画等）",
                    "Requires at least one painting (Canvas, etc.)"
                ).Resolve()
            );

            HAS_SCULPTURE = new RoomConstraints.Constraint(
                building_criteria: kpid => kpid != null && kpid.GetComponent<Sculpture>() != null,
                room_criteria: null,
                times_required: 1,
                name: ModStrings.T("至少一座雕塑", "At least one Sculpture").Resolve(),
                description: ModStrings.T(
                    "包含至少一座雕塑（大理石雕塑、金属雕塑、木雕、冰雕等）",
                    "Requires at least one sculpture (Marble, Metal, Wood, etc.)"
                ).Resolve()
            );

            HAS_DISPLAYED_COLLECTION = new RoomConstraints.Constraint(
                building_criteria: kpid =>
                {
                    if (kpid == null) return false;
                    var receptacle = kpid.GetComponent<SingleEntityReceptacle>();
                    return receptacle != null && receptacle.Occupant != null;
                },
                room_criteria: null,
                times_required: 1,
                name: ModStrings.T("至少一件展出藏品", "At least one Displayed Collection").Resolve(),
                description: ModStrings.T(
                    "包含至少一个陈列了物品的展台（展出文物、矿石或珍稀藏品）",
                    "Requires at least one pedestal displaying an artifact or item"
                ).Resolve()
            );
        }

        private static void RegisterRoomType()
        {
            string roomName = ModStrings.T("展览馆", "Exhibition Hall").Resolve();
            string roomDesc = ModStrings.T(
                "立有展览馆标牌，并陈列了名画、雕塑与稀世藏品的文化殿堂。可为到访的复制人提供极高的士气与灵感。",
                "A designated cultural hall marked by an Exhibition Sign, showcasing paintings, sculptures, and displayed artifacts. Grants visiting duplicants high morale and inspiration."
            ).Resolve();
            string roomEffect = GalleryBenefits.Summary(ModStrings.T("学习能力", "Science").Resolve());

            var category = Db.Get().RoomTypeCategories.Recreation ?? Db.Get().RoomTypeCategories.Park;

            ExhibitionHallRoomType = new RoomType(
                id: RoomTypeId,
                name: roomName,
                description: roomDesc,
                tooltip: roomName,
                effect: roomEffect,
                category: category,
                primary_constraint: HAS_EXHIBITION_SIGN,
                additional_constraints: CreateGalleryConstraints(),
                display_details: new RoomDetails.Detail[]
                {
                    RoomDetails.SIZE,
                    RoomDetails.BUILDING_COUNT
                },
                priority: 2,
                upgrade_paths: null,
                single_assignee: false,
                priority_building_use: false,
                effects: new string[] { EffectId },
                sortKey: 16
            );

            Db.Get().RoomTypes.Add(ExhibitionHallRoomType);
        }

        // 所有标志展馆共用同一组约束，差异只有标牌、名称和到访效果。
        internal static RoomConstraints.Constraint[] CreateGalleryConstraints() => new[]
        {
            HAS_PAINTING, HAS_SCULPTURE, HAS_DISPLAYED_COLLECTION,
            RoomConstraints.NO_INDUSTRIAL_MACHINERY,
            RoomConstraints.NO_COTS, RoomConstraints.NO_LUXURY_BEDS,
            RoomConstraints.NO_OUTHOUSES, RoomConstraints.NO_MESS_STATION,
            RoomConstraints.MINIMUM_SIZE_32, RoomConstraints.MAXIMUM_SIZE_120
        };
    }

    /// <summary>
    /// 当复制人进入展览馆房间时，立即触发【博览群雅】士气与灵感增益。
    /// </summary>
    [HarmonyPatch(typeof(CavityInfo), nameof(CavityInfo.OnEnter))]
    public static class CavityInfo_OnEnter_ExhibitionHall_Patch
    {
        public static void Postfix(CavityInfo __instance, object data)
        {
            try
            {
                if (data is GameObject go) LandmarkRooms.ApplyVisitEffect(__instance, go);
            }
            catch (Exception ex)
            {
                ModLog.Throttle("landmark_room_visit", $"[房间扩展] 到访效果未完成：{ex.Message}", 10f);
            }
        }
    }
}
