using System;
using System.Collections.Generic;
using Klei.AI;
using OmniMod.Buildings;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Rooms
{
    /// <summary>标志展馆共用陈列要求；孵蛋室按孵化器独立识别。仅响应原版识别和到访事件。</summary>
    internal static class LandmarkRooms
    {
        internal const string HatcheryId = "OmniHatchery";
        internal const string NaturalHistoryId = "OmniNaturalHistoryHall";
        internal const string WorkshopId = "OmniWorkshop";
        internal const string FitnessId = "OmniFitness";
        internal const float VisitDuration = 600f;
        private const string HatcheryEffectId = HatcheryId + "Inspiration";
        private static readonly Dictionary<string, string> VisitEffects = new Dictionary<string, string>();
        private static readonly Tag[] GallerySigns =
        {
            ExhibitionSignConfig.ExhibitionSignTag, NaturalHistorySignConfig.SignTag,
            WorkshopSignConfig.SignTag, FitnessSignConfig.SignTag
        };
        private static readonly string[] GalleryEffectIds =
        {
            ExhibitionHallPatch.EffectId, NaturalHistoryId + "Inspiration",
            WorkshopId + "Inspiration", FitnessId + "Inspiration",
            CollectiveBedroomPatch.EffectId
        };

        // 由展览馆注册入口在公共约束准备完成后调用，避免多个 Db Postfix 的执行顺序依赖。
        internal static void Register()
        {
            VisitEffects.Clear();
            VisitEffects[ExhibitionHallPatch.RoomTypeId] = ExhibitionHallPatch.EffectId;
            VisitEffects[CollectiveBedroomPatch.RoomTypeId] = CollectiveBedroomPatch.EffectId;
            var db = Db.Get();
            var tuning = TuningData<RoomProber.Tuning>.Get();
            tuning.maxRoomSize = Math.Max(tuning.maxRoomSize, 256);
            RegisterGallery(NaturalHistoryId, NaturalHistorySignConfig.SignTag,
                ModStrings.T("自然馆", "Natural History Gallery").Resolve(),
                ModStrings.T("万物有灵", "Wonder of Life").Resolve(),
                ModStrings.T("从艺术与藏品中感受生命的多样，获得照料动物的灵感。", "Art and collections reveal the diversity of life and inspire animal husbandry.").Resolve(),
                db.Attributes.Ranching.Id, ModStrings.T("畜牧", "Husbandry").Resolve());
            RegisterGallery(WorkshopId, WorkshopSignConfig.SignTag,
                ModStrings.T("工艺馆", "Craft Gallery").Resolve(),
                ModStrings.T("匠心独运", "Crafting Inspiration").Resolve(),
                ModStrings.T("欣赏艺术与精巧藏品，激发机械操作的灵感。", "Art and finely crafted collections inspire mechanical aptitude.").Resolve(),
                db.Attributes.Machinery.Id, ModStrings.T("机械", "Machinery").Resolve());
            RegisterGallery(FitnessId, FitnessSignConfig.SignTag,
                ModStrings.T("活力馆", "Vitality Gallery").Resolve(),
                ModStrings.T("神采飞扬", "Radiant Vitality").Resolve(),
                ModStrings.T("从艺术与藏品中汲取活力，让行动更加轻快。", "Art and collections inspire energy and lighter steps.").Resolve(),
                db.Attributes.Athletics.Id, ModStrings.T("运动", "Athletics").Resolve());
            RegisterHatchery();
            ModLog.Info("[房间扩展] 自然馆、工艺馆、活力馆共用展览馆陈列要求；孵蛋室无需标牌，最大 256 格。");
        }

        internal static bool CanDesignateGallery(Room room, Tag ownSign)
        {
            bool hasSign = false;
            foreach (KPrefabID building in room.buildings)
                if (building != null && building.HasTag(ownSign)) { hasSign = true; break; }
            if (!hasSign) return false;
            foreach (KPrefabID building in room.buildings)
            {
                if (building == null) continue;
                foreach (Tag sign in GallerySigns)
                    if (sign != ownSign && building.HasTag(sign)) return false;
            }
            return true;
        }

        private static void RegisterGallery(string id, Tag signTag, string name, string effectName,
            string description, string attributeId, string attributeName)
        {
            string effectId = id + "Inspiration";
            string effectText = GalleryBenefits.Summary(attributeName);
            string durationText = ModStrings.T("到访后持续一周期，同馆不叠加，不同馆的经验加成相加。",
                "Lasts one cycle after visiting. Identical effects do not stack; experience bonuses from different galleries add together.").Resolve();
            RegisterEffectAndStrings(id, effectId, name, effectName, description, effectText, durationText, attributeId, gallery: true);
            if (Db.Get().RoomTypes.GetRoomTypeForID(id) != null) return;
            var sign = new RoomConstraints.Constraint(kpid => kpid.HasTag(signTag), room => CanDesignateGallery(room, signTag), 1,
                ModStrings.T(name + "标牌", name + " Sign").Resolve(),
                ModStrings.T("放置对应标牌；同一房间只用一种展馆标牌。", "Place the matching sign. Use only one kind of gallery sign per room.").Resolve(),
                new List<RoomConstraints.Constraint>
                {
                    RoomConstraints.REC_BUILDING, RoomConstraints.DECORATIVE_ITEM, RoomConstraints.PARK_BUILDING
                });
            var category = Db.Get().RoomTypeCategories.Recreation ?? Db.Get().RoomTypeCategories.Park;
            Db.Get().RoomTypes.Add(new RoomType(id, name, description, description, effectText, category, sign,
                ExhibitionHallPatch.CreateGalleryConstraints(), new[] { RoomDetails.SIZE, RoomDetails.BUILDING_COUNT },
                priority: 2, effects: new[] { effectId }, sortKey: 20 + VisitEffects.Count));
        }

        private static void RegisterHatchery()
        {
            OmniMod.Ranching.HatcheryIncubationBonus.RegisterEffect();
            string name = ModStrings.T("孵蛋室", "Hatchery").Resolve();
            string description = ModStrings.T("包含孵化器的封闭房间，最大 256 格，无需标牌或艺术品。",
                "An enclosed room containing an incubator, up to 256 tiles. No sign or artwork required.").Resolve();
            string effectText = ModStrings.T("+4 士气\n+3 畜牧\n孵化器 +200% 基础孵化速率",
                "+4 Morale\n+3 Husbandry\nIncubators: +200% base incubation rate").Resolve();
            RegisterEffectAndStrings(HatcheryId, HatcheryEffectId, name,
                ModStrings.T("育幼之乐", "Joy of Nurturing").Resolve(), description, effectText,
                ModStrings.T("复制人加成到访后持续一周期；室内工作中的孵化器给蛋提供额外孵化加成，与拥抱加成相加。",
                    "Duplicant bonuses last one cycle after visiting. Operational incubators in the room accelerate eggs, additive with Lullabied.").Resolve(),
                Db.Get().Attributes.Ranching.Id);
            if (Db.Get().RoomTypes.GetRoomTypeForID(HatcheryId) != null) return;
            var conflicts = new List<RoomConstraints.Constraint>();
            foreach (RoomType type in Db.Get().RoomTypes.resources)
                if (type.primary_constraint != null && !conflicts.Contains(type.primary_constraint))
                    conflicts.Add(type.primary_constraint);
            var incubator = new RoomConstraints.Constraint(kpid => kpid.GetComponent<EggIncubator>() != null,
                room => CanBecomeHatchery(room), 1,
                ModStrings.T("至少一台孵化器", "At least one Incubator").Resolve(),
                ModStrings.T("孵化器为空也可识别；优先保留已有的其它合格房间类型。",
                    "Empty incubators qualify. Existing valid room types take precedence.").Resolve(),
                stomp_in_conflict: conflicts);
            var maximum = new RoomConstraints.Constraint(null, room => room.cavity.NumCells <= 256, 1,
                ModStrings.T("最大 256 格", "Maximum 256 Tiles").Resolve(),
                ModStrings.T("封闭房间最多 256 格。", "An enclosed room of at most 256 tiles.").Resolve());
            Db.Get().RoomTypes.Add(new RoomType(HatcheryId, name, description, description, effectText,
                Db.Get().RoomTypeCategories.Agricultural, incubator, new[] { maximum },
                new[] { RoomDetails.SIZE, RoomDetails.BUILDING_COUNT }, effects: new[] { HatcheryEffectId }, sortKey: 25));
        }

        private static bool CanBecomeHatchery(Room room)
        {
            // 大部分房间没有孵化器，先按建筑排除，避免无关房间重复核对其它类型。
            bool hasIncubator = false;
            foreach (KPrefabID building in room.buildings)
            {
                if (building == null) continue;
                if (building.GetComponent<EggIncubator>() != null) hasIncubator = true;
                foreach (Tag sign in GallerySigns) if (building.HasTag(sign)) return false;
            }
            if (!hasIncubator || room.cavity.NumCells > 256) return false;
            foreach (RoomType type in Db.Get().RoomTypes.resources)
            {
                if (type.Id == HatcheryId || type.primary_constraint == null) continue;
                if (type.isSatisfactory(room) == RoomType.RoomIdentificationResult.all_satisfied) return false;
            }
            // 不覆盖含有照料站等强制房间要求的布局，即使其面积等条件暂时不合格。
            foreach (KPrefabID building in room.buildings)
            {
                RoomTracker tracker = building != null ? building.GetComponent<RoomTracker>() : null;
                if (tracker != null && tracker.requiredRoomType != HatcheryId &&
                    (tracker.requirement == RoomTracker.Requirement.Required || tracker.requirement == RoomTracker.Requirement.CustomRequired)) return false;
            }
            return true;
        }

        private static void RegisterEffectAndStrings(string id, string effectId, string name, string effectName,
            string description, string effectText, string details, string attributeId, bool gallery = false)
        {
            VisitEffects[id] = effectId;
            string effectDescription = description + "\n" + effectText + "\n" + details;
            var db = Db.Get();
            if (!db.effects.Exists(effectId))
            {
                var effect = new Effect(effectId, effectName, effectDescription, VisitDuration, true, true, false);
                if (gallery) GalleryBenefits.Apply(effect, attributeId, effectName);
                else
                {
                    effect.Add(new AttributeModifier(db.Attributes.QualityOfLife.Id, 4f, effectName));
                    effect.Add(new AttributeModifier(attributeId, 3f, effectName));
                }
                db.effects.Add(effect);
            }
            string key = id.ToUpperInvariant();
            Strings.Add($"STRINGS.ROOMS.TYPES.{key}.NAME", name);
            Strings.Add($"STRINGS.ROOMS.TYPES.{key}.DESCRIPTION", description);
            Strings.Add($"STRINGS.ROOMS.TYPES.{key}.TOOLTIP", description);
            Strings.Add($"STRINGS.ROOMS.TYPES.{key}.EFFECT", effectText);
            Strings.Add($"STRINGS.DUPLICANTS.MODIFIERS.{effectId.ToUpperInvariant()}.NAME", effectName);
            Strings.Add($"STRINGS.DUPLICANTS.MODIFIERS.{effectId.ToUpperInvariant()}.TOOLTIP", effectDescription);
        }

        internal static float GetSkillExperienceMultiplier(Effects effects)
        {
            int count = 0;
            foreach (string id in GalleryEffectIds) if (effects.HasEffect(id)) count++;
            return 1f + count * (ExhibitionHallPatch.SkillExperienceMultiplier - 1f);
        }

        internal static void ApplyVisitEffect(CavityInfo cavity, GameObject visitor)
        {
            if (visitor == null || visitor.GetComponent<MinionIdentity>() == null || cavity?.room?.roomType == null) return;
            if (VisitEffects.TryGetValue(cavity.room.roomType.Id, out string effectId))
                visitor.GetComponent<Effects>()?.Add(effectId, true);
        }

        internal static void RegisterVisitEffect(string roomId, string effectId)
        {
            if (!string.IsNullOrEmpty(roomId) && !string.IsNullOrEmpty(effectId))
            {
                VisitEffects[roomId] = effectId;
            }
        }
    }
}
