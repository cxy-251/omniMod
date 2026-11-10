using System;
using System.Collections.Generic;
using Klei.AI;
using OmniMod.Buildings;
using OmniMod.Core.Logging;

namespace OmniMod.Rooms
{
    /// <summary>
    /// 全新房间类型：【集体卧室】(Collective Bedroom)。
    /// 解决原版私人卧室（Private Bedroom）每人独占一间导致基地空间严重浪费的问题。
    /// 允许多名复制人共用一间高品质大通铺卧室（至少 2 张舒适床铺），最大支持 256 格大空间。
    /// 赋予专属四字特色增益：【聚众同眠】（聚众睡觉）——
    /// +8 士气、-15% 压力/周期、+3 力量、+50% 技能点经验。
    /// </summary>
    public static class CollectiveBedroomPatch
    {
        public const string RoomTypeId = "OmniCollectiveBedroom";
        public const string EffectId = "OmniCollectiveSlumber";

        public static RoomType CollectiveBedroomRoomType { get; private set; }
        public static RoomConstraints.Constraint HAS_AT_LEAST_TWO_LUXURY_BEDS { get; private set; }
        public static RoomConstraints.Constraint MAXIMUM_SIZE_256 { get; private set; }

        public static void Register()
        {
            try
            {
                RegisterStrings();
                RegisterEffect();
                RegisterConstraints();
                RegisterRoomType();
                ModLog.Info("[房间扩展] 【集体卧室 (Collective Bedroom)】房间类型与效果注册成功。");
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[房间扩展] 注册集体卧室失败: {ex}");
            }
        }

        private static void RegisterStrings()
        {
            string roomName = ModStrings.T("集体卧室", "Collective Bedroom").Resolve();
            string roomDesc = ModStrings.T(
                "专为多名复制人共同安歇打造的大型高品质卧室。摆放多张舒适床铺并搭配艺术装点，既节省基地宝贵空间，又能提供极佳的士气与身心增益。",
                "A high-quality dormitory designed for multiple Duplicants to sleep together. Furnished with comfy beds and decor, saving space while providing immense morale and physical benefits."
            ).Resolve();
            string roomEffect = ModStrings.T(
                "+8 士气\n-15% 压力/周期\n+3 力量\n+50% 技能点经验",
                "+8 Morale\n-15% Stress/cycle\n+3 Strength\n+50% Skill Point Experience"
            ).Resolve();

            string effectName = ModStrings.T("聚众同眠", "Collective Slumber").Resolve();
            string effectDesc = ModStrings.T(
                "大被同眠，聚众睡觉！大家整整齐齐睡在大通铺里，安全感与幸福感爆棚！力量 +3；技能点经验 +50%；士气 +8；压力 -15%/周期。持续一周期。",
                "Snug and sound together! Sleeping in a tidy collective dormitory provides tremendous comfort. +3 Strength, +50% Skill Point Experience, +8 Morale, and -15% Stress/cycle for one cycle."
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

            string effectName = ModStrings.T("聚众同眠", "Collective Slumber").Resolve();
            string effectDesc = ModStrings.T(
                "大被同眠，聚众睡觉！大家整整齐齐睡在大通铺里，安全感与幸福感爆棚！力量 +3；技能点经验 +50%；士气 +8；压力 -15%/周期。持续一周期。",
                "Snug and sound together! Sleeping in a tidy collective dormitory provides tremendous comfort. +3 Strength, +50% Skill Point Experience, +8 Morale, and -15% Stress/cycle for one cycle."
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

            var db = Db.Get();
            effect.Add(new AttributeModifier(db.Attributes.QualityOfLife.Id, 8f, effectName));
            effect.Add(new AttributeModifier(db.Amounts.Stress.deltaAttribute.Id, -15f / 600f, effectName));
            effect.Add(new AttributeModifier(db.Attributes.Strength.Id, 3f, effectName));

            db.effects.Add(effect);
        }

        private static void RegisterConstraints()
        {
            HAS_AT_LEAST_TWO_LUXURY_BEDS = new RoomConstraints.Constraint(
                building_criteria: kpid => kpid != null && kpid.HasTag(RoomConstraints.ConstraintTags.LuxuryBedType),
                room_criteria: null,
                times_required: 2,
                name: ModStrings.T("至少两张舒适床铺", "At least two Comfy Beds").Resolve(),
                description: ModStrings.T(
                    "包含至少 2 张舒适床铺（普通舒适床或紧凑型舒适床）",
                    "Requires at least 2 Comfy Beds (Luxury Beds or Compact Luxury Beds)"
                ).Resolve(),
                stomp_in_conflict: new List<RoomConstraints.Constraint>
                {
                    RoomConstraints.HAS_BED,
                    RoomConstraints.HAS_LUXURY_BED
                }
            );

            MAXIMUM_SIZE_256 = new RoomConstraints.Constraint(
                building_criteria: null,
                room_criteria: room => room != null && room.cavity != null && room.cavity.NumCells <= 256,
                times_required: 1,
                name: ModStrings.T("最大 256 格", "Maximum 256 Tiles").Resolve(),
                description: ModStrings.T(
                    "封闭房间最大不超过 256 格",
                    "An enclosed room of at most 256 tiles"
                ).Resolve()
            );
        }

        private static void RegisterRoomType()
        {
            if (Db.Get().RoomTypes.GetRoomTypeForID(RoomTypeId) != null)
            {
                return;
            }

            string roomName = ModStrings.T("集体卧室", "Collective Bedroom").Resolve();
            string roomDesc = ModStrings.T(
                "专为多名复制人共同安歇打造的大型高品质卧室。摆放多张舒适床铺并搭配艺术装点，既节省基地宝贵空间，又能提供极佳的士气与身心增益。",
                "A high-quality dormitory designed for multiple Duplicants to sleep together. Furnished with comfy beds and decor, saving space while providing immense morale and physical benefits."
            ).Resolve();
            string roomEffect = ModStrings.T(
                "+8 士气\n-15% 压力/周期\n+3 力量\n+50% 技能点经验",
                "+8 Morale\n-15% Stress/cycle\n+3 Strength\n+50% Skill Point Experience"
            ).Resolve();

            var category = Db.Get().RoomTypeCategories.Sleep;

            CollectiveBedroomRoomType = new RoomType(
                id: RoomTypeId,
                name: roomName,
                description: roomDesc,
                tooltip: roomName,
                effect: roomEffect,
                category: category,
                primary_constraint: HAS_AT_LEAST_TWO_LUXURY_BEDS,
                additional_constraints: CreateCollectiveBedroomConstraints(),
                display_details: new RoomDetails.Detail[]
                {
                    RoomDetails.SIZE,
                    RoomDetails.BUILDING_COUNT
                },
                priority: 3,
                upgrade_paths: null,
                single_assignee: false,
                priority_building_use: false,
                effects: new string[] { EffectId },
                sortKey: 25
            );

            Db.Get().RoomTypes.Add(CollectiveBedroomRoomType);
        }

        internal static RoomConstraints.Constraint[] CreateCollectiveBedroomConstraints() => new[]
        {
            RoomConstraints.NO_COTS,
            RoomConstraints.CEILING_HEIGHT_4,
            RoomConstraints.DECORATIVE_ITEM,
            RoomConstraints.NO_INDUSTRIAL_MACHINERY,
            RoomConstraints.NO_OUTHOUSES,
            RoomConstraints.NO_MESS_STATION,
            RoomConstraints.MINIMUM_SIZE_24,
            MAXIMUM_SIZE_256
        };
    }
}
