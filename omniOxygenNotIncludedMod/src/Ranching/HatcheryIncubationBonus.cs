using HarmonyLib;
using Klei.AI;
using OmniMod.Rooms;
using UnityEngine;

namespace OmniMod.Ranching
{
    /// <summary>仅由孵化器维护短期蛋效果；离室、断电、取出即移除，不扫描散落蛋或共享池。</summary>
    [SkipSaveFileSerialization]
    public sealed class HatcheryIncubationBonus : KMonoBehaviour, ISim1000ms
    {
        private const string EffectId = "OmniHatcheryIncubation";
        private const float Duration = 2.5f;
        private EggIncubator incubator;
        private Operational operational;
        private int eggId = -1;

        internal static void RegisterEffect()
        {
            var db = Db.Get();
            if (db.effects.Exists(EffectId)) return;
            string name = ModStrings.T("暖巢育生", "Nurturing Nest").Resolve();
            var effect = new Effect(EffectId, name, ModStrings.T(
                "孵蛋室内工作中的孵化器：额外增加基础孵化速率的 200%，与拥抱加成相加。",
                "An operational incubator in a Hatchery adds 200% of the base incubation rate, additive with Lullabied.").Resolve(),
                Duration, true, false, false);
            effect.Add(new AttributeModifier(db.Amounts.Incubation.deltaAttribute.Id, 2f, name, is_multiplier: true));
            db.effects.Add(effect);
        }
        protected override void OnSpawn()
        {
            base.OnSpawn();
            incubator = GetComponent<EggIncubator>();
            operational = GetComponent<Operational>();
            Subscribe((int)GameHashes.OnStorageChange, Refresh);
            Subscribe((int)GameHashes.OperationalChanged, Refresh);
            Refresh(null);
        }
        public void Sim1000ms(float dt) => Refresh(null);
        private void Refresh(object data)
        {
            if (App.isLoading || Game.Instance?.roomProber == null || incubator == null) return;
            GameObject egg = incubator.Occupant;
            int cell = Grid.PosToCell(this);
            bool active = operational != null && operational.IsOperational && Grid.IsValidCell(cell) &&
                Game.Instance.roomProber.GetCavityForCell(cell)?.room?.roomType?.Id == LandmarkRooms.HatcheryId &&
                egg != null && egg.HasTag(GameTags.Egg) && egg.GetDef<IncubationMonitor.Def>() != null;
            int current = active ? egg.GetComponent<KPrefabID>().InstanceID : -1;
            if (current != eggId) RemoveBonus();
            if (!active) return;
            Effects effects = egg.GetComponent<Effects>();
            if (effects == null) return;
            eggId = current;
            EffectInstance instance = effects.Get(EffectId);
            if (instance == null) effects.Add(EffectId, should_save: false);
            else instance.timeRemaining = Duration;
        }
        private void RemoveBonus()
        {
            if (eggId >= 0) KPrefabIDTracker.Get().GetInstance(eggId)?.GetComponent<Effects>()?.Remove(EffectId);
            eggId = -1;
        }
        protected override void OnCleanUp() { RemoveBonus(); base.OnCleanUp(); }
    }

    [HarmonyPatch(typeof(EggIncubatorConfig), nameof(EggIncubatorConfig.DoPostConfigureComplete))]
    internal static class HatcheryIncubation_Configure
    {
        private static void Postfix(GameObject go) => go.AddOrGet<HatcheryIncubationBonus>();
    }
}
