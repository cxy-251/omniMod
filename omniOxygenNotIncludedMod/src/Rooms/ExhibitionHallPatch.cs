using System;
using System.Collections.Generic;
using HarmonyLib;
using Klei.AI;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Rooms
{
    /// <summary>
    /// 全新房间类型：【展览馆】(Exhibition Hall)。
    /// 
    /// 核心设计：
    /// 将原版分散且仅用于堆装饰度的“画作”、“雕塑”、“陈列藏品（展台）”统一整合为一个顶级文化殿堂房间。
    /// 
    /// 房间要求：
    /// 1. 至少包含 1 件画作（帆布画、杰作壁画等）
    /// 2. 至少包含 1 座雕塑（大理石雕塑、金属雕塑、木雕、化石雕塑等）
    /// 3. 至少包含 1 件展出藏品（陈列了文物、矿石、标本的展台）
    /// 4. 空间尺寸：32 ~ 120 格
    /// 5. 限制：无工业机械、无床铺、无马桶、无餐桌
    /// 
    /// 房间效果：
    /// 小人穿行或进入展览馆，立即获得【博览群雅】(Exhibition Inspiration) 专属增益：
    /// - +6 士气（与顶级自然保护区持平）
    /// - -15% 压力 / 周期
    /// - +2 科学 / 学习能力（启发思维）
    /// 持续整整 1 周期（600 秒）。
    /// </summary>
    public static class ExhibitionHallPatch
    {
        public const string RoomTypeId = "ExhibitionHall";
        public const string EffectId = "ExhibitionHallInspiration";

        public static RoomType ExhibitionHallRoomType { get; private set; }
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
                    ModLog.Info("[房间扩展] 【展览馆 (Exhibition Hall)】房间类型与专属士气效果注册成功！");
                }
                catch (Exception ex)
                {
                    ModLog.Warn($"[房间扩展] 注册【展览馆】失败: {ex}");
                }
            }
        }

        private static void RegisterStrings()
        {
            string roomName = ModStrings.T("展览馆", "Exhibition Hall").Resolve();
            string roomDesc = ModStrings.T(
                "陈列了名画、雕塑与稀世藏品的文化殿堂。可为身处其中的复制人提供极高的士气与灵感。",
                "A grand cultural hall showcasing completed paintings, majestic sculptures, and rare displayed artifacts. Inspires duplicants with high morale and creative focus."
            ).Resolve();
            string roomTooltip = ModStrings.T("展览馆", "Exhibition Hall").Resolve();
            string roomEffect = ModStrings.T(
                "+6 士气\n-15% 压力/周期\n+2 学习能力",
                "+6 Morale\n-15% Stress/cycle\n+2 Science"
            ).Resolve();

            string effectName = ModStrings.T("博览群雅", "Exhibition Inspiration").Resolve();
            string effectDesc = ModStrings.T(
                "这名复制人刚刚参观了陈列着画作、雕塑与稀世藏品的展览馆，精神深受启发！",
                "This Duplicant recently walked through an Exhibition Hall filled with fine art and rare artifacts. They feel cultured, stress-free, and inspired!"
            ).Resolve();

            Strings.Add($"STRINGS.ROOMS.TYPES.{RoomTypeId.ToUpperInvariant()}.NAME", roomName);
            Strings.Add($"STRINGS.ROOMS.TYPES.{RoomTypeId.ToUpperInvariant()}.DESCRIPTION", roomDesc);
            Strings.Add($"STRINGS.ROOMS.TYPES.{RoomTypeId.ToUpperInvariant()}.TOOLTIP", roomTooltip);
            Strings.Add($"STRINGS.ROOMS.TYPES.{RoomTypeId.ToUpperInvariant()}.EFFECT", roomEffect);

            Strings.Add($"STRINGS.DUPLICANTS.MODIFIERS.{EffectId.ToUpperInvariant()}.NAME", effectName);
            Strings.Add($"STRINGS.DUPLICANTS.MODIFIERS.{EffectId.ToUpperInvariant()}.TOOLTIP", effectDesc);
        }

        private static void RegisterEffect()
        {
            if (Db.Get().effects.Exists(EffectId))
            {
                return;
            }

            string effectName = ModStrings.T("博览群雅", "Exhibition Inspiration").Resolve();
            string effectDesc = ModStrings.T(
                "这名复制人刚刚参观了陈列着画作、雕塑与稀世藏品的展览馆，精神深受启发！",
                "This Duplicant recently walked through an Exhibition Hall filled with fine art and rare artifacts. They feel cultured, stress-free, and inspired!"
            ).Resolve();

            var effect = new Effect(
                id: EffectId,
                name: effectName,
                description: effectDesc,
                duration: 600f, // 1 周期
                show_in_ui: true,
                trigger_floating_text: true,
                is_bad: false
            );

            // +6 士气
            effect.Add(new AttributeModifier(Db.Get().Attributes.QualityOfLife.Id, 6f, effectName));
            // -15% 压力 / 周期
            effect.Add(new AttributeModifier(Db.Get().Amounts.Stress.deltaAttribute.Id, -0.025f, effectName));
            // +2 学习/科学能力
            effect.Add(new AttributeModifier(Db.Get().Attributes.Learning.Id, 2f, effectName));

            Db.Get().effects.Add(effect);
        }

        private static void RegisterConstraints()
        {
            string pName = ModStrings.T("至少一件画作", "At least one Painting").Resolve();
            string pDesc = ModStrings.T("包含至少一件画作（帆布画、杰作壁画等）", "Requires at least one painting (Canvas, etc.)").Resolve();
            HAS_PAINTING = new RoomConstraints.Constraint(
                building_criteria: kpid => kpid != null && kpid.GetComponent<Painting>() != null,
                room_criteria: null,
                times_required: 1,
                name: pName,
                description: pDesc
            );

            string sName = ModStrings.T("至少一座雕塑", "At least one Sculpture").Resolve();
            string sDesc = ModStrings.T("包含至少一座雕塑（大理石雕塑、金属雕塑、木雕等）", "Requires at least one sculpture (Marble, Metal, Wood, etc.)").Resolve();
            HAS_SCULPTURE = new RoomConstraints.Constraint(
                building_criteria: kpid => kpid != null && kpid.GetComponent<Sculpture>() != null,
                room_criteria: null,
                times_required: 1,
                name: sName,
                description: sDesc
            );

            string cName = ModStrings.T("至少一件展出藏品", "At least one Displayed Collection").Resolve();
            string cDesc = ModStrings.T("包含至少一个陈列了物品的展台（展出文物、矿石、标本等）", "Requires at least one pedestal displaying an artifact or item").Resolve();
            HAS_DISPLAYED_COLLECTION = new RoomConstraints.Constraint(
                building_criteria: kpid =>
                {
                    if (kpid == null) return false;
                    var receptacle = kpid.GetComponent<SingleEntityReceptacle>();
                    return receptacle != null && receptacle.Occupant != null;
                },
                room_criteria: null,
                times_required: 1,
                name: cName,
                description: cDesc
            );
        }

        private static void RegisterRoomType()
        {
            string roomName = ModStrings.T("展览馆", "Exhibition Hall").Resolve();
            string roomDesc = ModStrings.T(
                "陈列了名画、雕塑与稀世藏品的文化殿堂。可为身处其中的复制人提供极高的士气与灵感。",
                "A grand cultural hall showcasing completed paintings, majestic sculptures, and rare displayed artifacts. Inspires duplicants with high morale and creative focus."
            ).Resolve();
            string roomTooltip = ModStrings.T("展览馆", "Exhibition Hall").Resolve();
            string roomEffect = ModStrings.T(
                "+6 士气\n-15% 压力/周期\n+2 学习能力",
                "+6 Morale\n-15% Stress/cycle\n+2 Science"
            ).Resolve();

            var category = Db.Get().RoomTypeCategories.Recreation ?? Db.Get().RoomTypeCategories.Park;

            ExhibitionHallRoomType = new RoomType(
                id: RoomTypeId,
                name: roomName,
                description: roomDesc,
                tooltip: roomTooltip,
                effect: roomEffect,
                category: category,
                primary_constraint: HAS_DISPLAYED_COLLECTION,
                additional_constraints: new RoomConstraints.Constraint[]
                {
                    HAS_PAINTING,
                    HAS_SCULPTURE,
                    RoomConstraints.NO_INDUSTRIAL_MACHINERY,
                    RoomConstraints.NO_COTS,
                    RoomConstraints.NO_LUXURY_BEDS,
                    RoomConstraints.NO_OUTHOUSES,
                    RoomConstraints.NO_MESS_STATION,
                    RoomConstraints.MINIMUM_SIZE_32,
                    RoomConstraints.MAXIMUM_SIZE_120
                },
                display_details: new RoomDetails.Detail[]
                {
                    RoomDetails.SIZE,
                    RoomDetails.BUILDING_COUNT
                },
                priority: 2, // 高于普通娱乐室 (0)，确保满足条件时准确识别为展览馆
                upgrade_paths: null,
                single_assignee: false,
                priority_building_use: false,
                effects: new string[] { EffectId },
                sortKey: 16
            );

            Db.Get().RoomTypes.Add(ExhibitionHallRoomType);
        }
    }

    /// <summary>
    /// 当复制人进入任意房间空间时触发：若为展览馆，立即享受【博览群雅】高额士气增益。
    /// （对齐原版自然保护区与公园的触发机制）
    /// </summary>
    [HarmonyPatch(typeof(CavityInfo), nameof(CavityInfo.OnEnter))]
    public static class CavityInfo_OnEnter_ExhibitionHall_Patch
    {
        public static void Postfix(CavityInfo __instance, object entity)
        {
            try
            {
                if (entity is GameObject go && go != null && go.GetComponent<MinionIdentity>() != null)
                {
                    Room room = Game.Instance?.roomProber?.GetRoomOfGameObject(go);
                    if (room != null && room.roomType != null && room.roomType.Id == ExhibitionHallPatch.RoomTypeId)
                    {
                        var effects = go.GetComponent<Effects>();
                        if (effects != null)
                        {
                            effects.Add(ExhibitionHallPatch.EffectId, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[展览馆] 触发入室士气效果异常: {ex.Message}");
            }
        }
    }
}
