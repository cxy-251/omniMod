using System;
using System.Collections.Generic;
using HarmonyLib;
using Klei.AI;
using KSerialization;
using STRINGS;
using TUNING;
using UnityEngine;

namespace OmniMod.Buildings
{
    [AddComponentMenu("KMonoBehaviour/Workable/LiquorDispenserWorkable")]
    public class LiquorDispenserWorkable : Workable, IWorkerPrioritizable
    {
        private Operational operational;

        public int basePriority = RELAXATION.PRIORITY.TIER5;

        private LiquorDispenserWorkable()
        {
            SetReportType(ReportManager.ReportType.PersonalTime);
        }

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            operational = GetComponent<Operational>();
            overrideAnims = new KAnimFile[1] { Assets.GetAnim("anim_interacts_sodamaker_kanim") };
            showProgressBar = true;
            resetProgressOnStop = true;
            synchronizeAnims = false;
            SetWorkTime(8f);
        }

        protected override void OnStartWork(WorkerBase worker)
        {
            base.OnStartWork(worker);
            operational?.SetActive(true);
        }

        protected override void OnCompleteWork(WorkerBase worker)
        {
            Storage storage = GetComponent<Storage>();
            if (storage != null)
            {
                storage.ConsumeIgnoringDisease(SimHashes.Ethanol.CreateTag(), LiquorDispenser.ETHANOL_MASS_PER_USE);
                storage.ConsumeIgnoringDisease(SimHashes.Sucrose.CreateTag(), LiquorDispenser.SUCROSE_MASS_PER_USE);
            }

            Effects effects = worker.GetComponent<Effects>();
            if (effects != null)
            {
                effects.Add(LiquorDispenser_Effect_Patch.EffectId, should_save: true);
                effects.Add("RecentlyRecDrink", should_save: true);

                // 烈酒暖身：驱散一切严寒、受冻与失温不良状态
                effects.Remove("ColdAir");
                effects.Remove("Hypothermia");
                effects.Remove("ChillySurroundings");
                effects.Remove("Thirsty");
            }

            operational?.SetActive(false);
        }

        protected override void OnStopWork(WorkerBase worker)
        {
            base.OnStopWork(worker);
            operational?.SetActive(false);
        }

        public bool GetWorkerPriority(WorkerBase worker, out int priority)
        {
            priority = basePriority;
            Effects effects = worker.GetComponent<Effects>();
            if (effects == null) return false;

            if (effects.HasEffect("RecentlyRecDrink"))
            {
                priority = 0;
                return false;
            }
            if (effects.HasEffect(LiquorDispenser_Effect_Patch.EffectId))
            {
                priority = RELAXATION.PRIORITY.RECENTLY_USED;
            }
            return true;
        }
    }

    /// <summary>
    /// 全能微醺特调吧逻辑主控：
    /// - 接入管道乙醇 + 配送固体蔗糖；
    /// - 供复制人在休息时间饮用，触发专属酒类【醉意撩人】增益效果；
    /// - 自身具备娱乐建筑标签（RecBuilding），直接覆盖并替代原版饮水机满足高级餐厅与娱乐室要求；
    /// - 纯乙醇蔗糖配方，绝无病菌污染。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class LiquorDispenser : StateMachineComponent<LiquorDispenser.StatesInstance>, IGameObjectEffectDescriptor
    {
        public class States : GameStateMachine<States, StatesInstance, LiquorDispenser>
        {
            public class ReadyStates : State
            {
                public State idle;
                public State working;
                public State post;
            }

            public State unoperational;
            public State operational;
            public ReadyStates ready;

            public override void InitializeStates(out BaseState default_state)
            {
                default_state = unoperational;

                unoperational
                    .PlayAnim("off")
                    .TagTransition(GameTags.Operational, operational);

                operational
                    .PlayAnim("off")
                    .TagTransition(GameTags.Operational, unoperational, on_remove: true)
                    .Transition(ready, IsReady)
                    .EventTransition(GameHashes.OnStorageChange, ready, IsReady);

                ready
                    .TagTransition(GameTags.Operational, unoperational, on_remove: true)
                    .DefaultState(ready.idle)
                    .ToggleChore(CreateChore, operational);

                ready.idle
                    .PlayAnim("on", KAnim.PlayMode.Loop)
                    .WorkableStartTransition((StatesInstance smi) => smi.master.GetComponent<LiquorDispenserWorkable>(), ready.working)
                    .Transition(operational, Not(IsReady))
                    .EventTransition(GameHashes.OnStorageChange, operational, Not(IsReady));

                ready.working
                    .PlayAnim("working_pre")
                    .QueueAnim("working_loop", loop: true)
                    .WorkableStopTransition((StatesInstance smi) => smi.master.GetComponent<LiquorDispenserWorkable>(), ready.post);

                ready.post
                    .PlayAnim("working_pst")
                    .OnAnimQueueComplete(ready);
            }

            private Chore CreateChore(StatesInstance smi)
            {
                Workable component = smi.master.GetComponent<LiquorDispenserWorkable>();
                WorkChore<LiquorDispenserWorkable> workChore = new WorkChore<LiquorDispenserWorkable>(
                    Db.Get().ChoreTypes.Relax,
                    component,
                    null,
                    run_until_complete: true,
                    null,
                    null,
                    null,
                    allow_in_red_alert: false,
                    Db.Get().ScheduleBlockTypes.Recreation,
                    ignore_schedule_block: false,
                    only_when_operational: true,
                    null,
                    is_preemptable: false,
                    allow_in_context_menu: true,
                    allow_prioritization: false,
                    PriorityScreen.PriorityClass.high);
                workChore.AddPrecondition(ChorePreconditions.instance.CanDoWorkerPrioritizable, component);
                return workChore;
            }

            private bool IsReady(StatesInstance smi) => smi.master.HasIngredients();
        }

        public class StatesInstance : GameStateMachine<States, StatesInstance, LiquorDispenser, object>.GameInstance
        {
            public StatesInstance(LiquorDispenser smi) : base(smi) { }
        }

        public const float ETHANOL_MASS_PER_USE = 1.0f;
        public const float SUCROSE_MASS_PER_USE = 0.2f;

        private Storage storage;

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            storage = GetComponent<Storage>();
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            if (storage == null) storage = GetComponent<Storage>();
            LiquorDispenser_Effect_Patch.RegisterEffect();
            smi.StartSM();
        }

        public bool HasIngredients()
        {
            if (storage == null) return false;
            float ethanol = storage.GetMassAvailable(SimHashes.Ethanol.CreateTag());
            float sucrose = storage.GetMassAvailable(SimHashes.Sucrose.CreateTag());
            return ethanol >= ETHANOL_MASS_PER_USE && sucrose >= SUCROSE_MASS_PER_USE;
        }

        public List<Descriptor> GetDescriptors(GameObject go)
        {
            List<Descriptor> list = new List<Descriptor>();

            Descriptor rec = default;
            rec.SetupDescriptor(UI.BUILDINGEFFECTS.RECREATION, UI.BUILDINGEFFECTS.TOOLTIPS.RECREATION);
            list.Add(rec);

            Effect.AddModifierDescriptions(gameObject, list, LiquorDispenser_Effect_Patch.EffectId, increase_indent: true);

            string ethanolName = ElementLoader.FindElementByHash(SimHashes.Ethanol).name;
            Descriptor ethanolDesc = default;
            ethanolDesc.SetupDescriptor(
                $"每次消耗：{ethanolName} {ETHANOL_MASS_PER_USE:0.0} kg",
                $"每次调酒消耗 {ETHANOL_MASS_PER_USE:0.0} kg 乙醇。",
                Descriptor.DescriptorType.Requirement);
            list.Add(ethanolDesc);

            string sucroseName = ElementLoader.FindElementByHash(SimHashes.Sucrose).name;
            Descriptor sucroseDesc = default;
            sucroseDesc.SetupDescriptor(
                $"每次消耗：{sucroseName} {SUCROSE_MASS_PER_USE:0.0} kg",
                $"每次调酒消耗 {SUCROSE_MASS_PER_USE:0.0} kg 蔗糖。",
                Descriptor.DescriptorType.Requirement);
            list.Add(sucroseDesc);

            return list;
        }
    }

    /// <summary>
    /// 骚气酒类效果【醉意撩人】：士气 +6，体能 +3，力量 +3，免疫 +4，防寒抗冻（导热屏障 +0.05，免疫受冻与失温），大幅减压。
    /// </summary>
    [HarmonyPatch(typeof(Db), "Initialize")]
    internal static class LiquorDispenser_Effect_Patch
    {
        public const string EffectId = "OmniMod_TipsyChic";

        private static void Postfix()
        {
            RegisterEffect();
        }

        internal static void RegisterEffect()
        {
            if (Db.Get() == null || Db.Get().effects == null) return;
            if (Db.Get().effects.Exists(EffectId)) return;

            string name = ModStrings.T("醉意撩人", "Tipsy & Flirtatious").Resolve();
            string desc = ModStrings.T(
                "甘冽乙醇融入甜美蔗糖，酒劲上涌，面色潮红，气血沸腾，骚气蓬勃！身躯如烈火般温暖抗寒，步履如风，精神极度亢奋！\n士气 +6，体能/力量 +3，免疫力 +4，保温御寒（免除寒冷惩罚），极大舒缓压力。",
                "Sweet sucrose blended with rich ethanol—the buzz rises, cheeks flush, footsteps grow light, and pure charisma overflows!\nMorale +6, Athletics & Strength +3, Immunity +4, Thermal Barrier (immune to hypothermia and cold), and massive stress relief."
            ).Resolve();

            var effect = new Effect(
                id: EffectId,
                name: name,
                description: desc,
                duration: 600f,
                show_in_ui: true,
                trigger_floating_text: true,
                is_bad: false
            );

            effect.Add(new AttributeModifier(Db.Get().Attributes.QualityOfLife.Id, 6f, name));
            effect.Add(new AttributeModifier(Db.Get().Attributes.Athletics.Id, 3f, name));
            effect.Add(new AttributeModifier(Db.Get().Attributes.Strength.Id, 3f, name));
            effect.Add(new AttributeModifier(Db.Get().Attributes.Immunity.Id, 4f, name));
            effect.Add(new AttributeModifier(Db.Get().Attributes.ThermalConductivityBarrier.Id, 0.05f, name));
            effect.Add(new AttributeModifier(Db.Get().Amounts.Stress.deltaAttribute.Id, -20f / 600f, name));
            effect.Add(new AttributeModifier(Db.Get().Amounts.Stamina.deltaAttribute.Id, 5f / 600f, name));

            Db.Get().effects.Add(effect);
        }
    }
}
