using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace OmniMod.Automation
{
    /// <summary>
    /// 工业机械自动运行——不需要人力操作。
    ///
    /// 复杂制造机 <see cref="ComplexFabricator"/> 覆盖了绝大多数工业机械：
    /// 碎石机、窑、金属精炼机、所有烹饪站（微生物研磨机/电烤炉/美食烹饪台/煤气灶/香料研磨机）、
    /// 药剂台、纺织机、玻璃锻造炉、分子锻造炉、陶瓷窑、氧石精炼机、超材料精炼机、蛋壳破碎机、榨汁机…
    ///
    /// 原理：ComplexFabricator 有个 <c>duplicantOperated</c> 开关。设为 false 后，游戏自带的
    /// <c>Sim200ms</c> 会在"有工作订单"时自行推进进度（<c>orderProgress += dt / recipe.time</c>）
    /// 并在完成时 <c>CompleteWorkingOrder()</c>——完全不需要小人。
    /// 仍然需要：通电、原料到位（原料由 OmniMod 的箱子 + 机械臂供应）。
    /// </summary>
    [HarmonyPatch(typeof(ComplexFabricator), "OnSpawn")]
    internal static class ComplexFabricator_OnSpawn_AutoRun_Patch
    {
        private static int _logged;

        private static void Postfix(ComplexFabricator __instance)
        {
            if (__instance != null)
            {
                __instance.duplicantOperated = false;
                if (_logged < 8)
                {
                    _logged++;
                    UnityEngine.Debug.Log($"[OmniMod] 机械自动运行：{__instance.name} 设为免人力操作");
                }
            }
        }
    }

    /// <summary>
    /// 自动研究：研究站 / 超级计算机 / 特殊研究设施——选好研究项目后不需要小人操作。
    ///
    /// 这类建筑不是 ComplexFabricator，而是一个 Workable，内部挂 ElementConverter：
    /// 小人工作时 <c>OnWorkTick</c> 把转化速率设为 2+效率，转化器消耗原料 → 出研究点。
    ///
    /// 香草的 <c>Sim200ms</c> 会在"通电 + 选了研究 + 有原料"时生成一个小人操作任务。
    /// 我们用 Prefix 抢在前面：满足条件时，直接把转化速率保持在基础值 2、让转化器自行运转，
    /// 并<b>跳过原方法</b>（不生成小人任务）；已有的任务也取消掉。
    /// 有小人正在操作、条件不满足、或玩家还没选研究项目时，放行原逻辑。
    ///
    /// 注：曾经自带"没选研究时自动挑下一项"的逻辑，但只会在一级科技打转、升不到二级，
    /// 不可靠，已移除——研究项目还是由玩家在科技树里手动选，这里只负责选完之后免人力自转。
    /// </summary>
    [HarmonyPatch(typeof(ResearchCenter), "Sim200ms")]
    internal static class ResearchCenter_Sim200ms_AutoResearch_Patch
    {
        private static readonly MethodInfo HasMaterialMethod =
            AccessTools.Method(typeof(ResearchCenter), "HasMaterial");

        private static int _logged;

        private static bool Prefix(ResearchCenter __instance)
        {
            try
            {
                if (__instance == null || __instance.worker != null)
                {
                    return true;   // 有小人在操作 → 走香草逻辑
                }
                if (Research.Instance == null)
                {
                    return true;
                }
                if (Research.Instance.GetActiveResearch() == null)
                {
                    return true;   // 玩家还没选研究项目 → 放行香草逻辑，不再自动挑
                }

                Operational op = __instance.GetComponent<Operational>();
                if (op == null || !op.IsOperational)
                {
                    return true;
                }
                if (HasMaterialMethod != null && !(bool)HasMaterialMethod.Invoke(__instance, null))
                {
                    return true;   // 没原料
                }

                ElementConverter conv = __instance.GetComponent<ElementConverter>();
                if (conv == null)
                {
                    return true;
                }

                // 取消香草可能已经建好的小人操作任务
                Traverse choreT = Traverse.Create(__instance).Field("chore");
                Chore chore = choreT.GetValue<Chore>();
                if (chore != null)
                {
                    chore.Cancel("OmniMod auto-research");
                    choreT.SetValue(null);
                }

                conv.SetWorkSpeedMultiplier(2f);
                op.SetActive(true);
                PlayWorkingAnim(__instance.GetComponent<KBatchedAnimController>());

                if (_logged < 5)
                {
                    _logged++;
                    UnityEngine.Debug.Log($"[OmniMod] 自动研究：{__instance.name} 自行运转，无需小人");
                }

                return false;   // 跳过原方法：不生成小人任务
            }
            catch
            {
                return true;
            }
        }

        internal static void PlayWorkingAnim(KBatchedAnimController kbac)
        {
            if (kbac == null) return;
            if (kbac.HasAnimation("working_loop"))
            {
                if (kbac.currentAnim != "working_loop") kbac.Play("working_loop", KAnim.PlayMode.Loop);
            }
            else if (kbac.HasAnimation("working"))
            {
                if (kbac.currentAnim != "working") kbac.Play("working", KAnim.PlayMode.Loop);
            }
        }

        internal static void StopWorkingAnim(KBatchedAnimController kbac, Operational op)
        {
            if (kbac == null) return;
            string anim = (op != null && op.IsOperational) ? "on" : "off";
            if (kbac.HasAnimation(anim))
            {
                kbac.Play(anim, KAnim.PlayMode.Once);
            }
        }
    }

    /// <summary>
    /// 自动材料研究：材料研究终端（NuclearResearchCenter）使用辐射粒子——选好研究项目后无需小人操作。
    ///
    /// 材料研究终端是 StateMachineComponent，并非 ResearchCenter 子类，也不带 ElementConverter。
    /// 它通过 HighEnergyParticleStorage 存储辐射粒子，在工作时消耗粒子推进点数。
    ///
    /// 这里挂载一个轻量组件 NuclearResearchCenterAutoRunner（实现 ISim200ms）：
    /// 当通电运转正常、有活动研究需要辐射粒子点数、且粒子储量足够时：
    /// 保持建筑为 Active 耗电状态，自动扣减辐射粒子并增加研究点，弹出研究气泡。
    /// </summary>
    public class NuclearResearchCenterAutoRunner : KMonoBehaviour, ISim200ms
    {
        private NuclearResearchCenter _nrc;
        private NuclearResearchCenterWorkable _workable;
        private Operational _operational;
        private HighEnergyParticleStorage _particleStorage;
        private KBatchedAnimController _kbac;
        private float _pointsProduced;
        private bool _wasActive;
        private static int _logged;

        protected override void OnSpawn()
        {
            base.OnSpawn();
            _nrc = GetComponent<NuclearResearchCenter>();
            _workable = GetComponent<NuclearResearchCenterWorkable>();
            _operational = GetComponent<Operational>();
            _particleStorage = GetComponent<HighEnergyParticleStorage>();
            _kbac = GetComponent<KBatchedAnimController>();
        }

        protected override void OnCleanUp()
        {
            // 拆除时 Operational 随 GameObject 一同清理，切勿在此手动调用 SetActive(false)
            base.OnCleanUp();
        }

        public void Sim200ms(float dt)
        {
            try
            {
                if (_nrc == null || _workable == null || _operational == null || _particleStorage == null)
                {
                    return;
                }

                if (_workable.worker != null)
                {
                    return; // 有小人在操作，放行香草逻辑
                }

                if (!_operational.IsOperational)
                {
                    StopRunning();
                    return;
                }

                Research research = Research.Instance;
                if (research == null)
                {
                    return;
                }

                TechInstance activeResearch = research.GetActiveResearch();
                if (activeResearch == null)
                {
                    StopRunning();
                    return;
                }

                string researchTypeID = _nrc.researchTypeID ?? "nuclear";
                if (!activeResearch.tech.costsByResearchTypeID.TryGetValue(researchTypeID, out float cost))
                {
                    StopRunning();
                    return;
                }

                float currentPoints = 0f;
                activeResearch.progressInventory.PointsByTypeID.TryGetValue(researchTypeID, out currentPoints);
                if (currentPoints >= cost)
                {
                    StopRunning();
                    return; // 当前科技该类型点数已集齐
                }

                if (_particleStorage.IsEmpty() || _particleStorage.Particles <= 0f)
                {
                    StopRunning();
                    return; // 缺少辐射粒子
                }

                // 取消香草可能遗留的小人任务
                var smi = _nrc.GetSMI<NuclearResearchCenter.StatesInstance>();
                if (smi != null)
                {
                    Traverse choreT = Traverse.Create(smi).Field("chore");
                    Chore chore = choreT.GetValue<Chore>();
                    if (chore != null)
                    {
                        chore.Cancel("OmniMod auto-research");
                        choreT.SetValue(null);
                    }
                }

                // 1 倍基准速率运转
                float speedMultiplier = 1f;
                float timePerPoint = _nrc.timePerPoint > 0f ? _nrc.timePerPoint : 100f;
                float progress = (dt * speedMultiplier) / timePerPoint;
                float particleNeeded = progress * _nrc.materialPerPoint;

                if (_particleStorage.Particles < particleNeeded)
                {
                    particleNeeded = _particleStorage.Particles;
                    progress = particleNeeded / _nrc.materialPerPoint;
                }

                if (particleNeeded <= 0f)
                {
                    StopRunning();
                    return;
                }

                _particleStorage.ConsumeAndGet(particleNeeded);
                _pointsProduced += progress;

                if (_pointsProduced >= 1f)
                {
                    int pointsToAdd = Mathf.FloorToInt(_pointsProduced);
                    _pointsProduced -= pointsToAdd;
                    research.AddResearchPoints(researchTypeID, pointsToAdd);

                    if (PopFXManager.Instance != null)
                    {
                        ResearchType rType = research.GetResearchType(researchTypeID);
                        string typeName = rType != null ? rType.name : researchTypeID;
                        PopFXManager.Instance.SpawnFX(PopFXManager.Instance.sprite_Research, typeName, transform);
                    }
                }

                if (!_wasActive)
                {
                    _operational.SetActive(true);
                    _wasActive = true;
                    ResearchCenter_Sim200ms_AutoResearch_Patch.PlayWorkingAnim(_kbac);
                    GetComponent<KSelectable>()?.AddStatusItem(Db.Get().BuildingStatusItems.ComplexFabricatorResearching, _nrc);
                }

                if (_logged < 5)
                {
                    _logged++;
                    Debug.Log($"[OmniMod] 自动研究：{_nrc.name} 自行运转材料研究，无需小人");
                }
            }
            catch
            {
                // 忽略异常，不干扰游戏
            }
        }

        private void StopRunning()
        {
            if (_wasActive)
            {
                if (_operational != null)
                {
                    _operational.SetActive(false);
                }
                _wasActive = false;
                ResearchCenter_Sim200ms_AutoResearch_Patch.StopWorkingAnim(_kbac, _operational);
                GetComponent<KSelectable>()?.RemoveStatusItem(Db.Get().BuildingStatusItems.ComplexFabricatorResearching, _nrc);
            }
        }
    }

    [HarmonyPatch(typeof(NuclearResearchCenter), "OnSpawn")]
    internal static class NuclearResearchCenter_OnSpawn_AutoRunner_Patch
    {
        private static void Postfix(NuclearResearchCenter __instance)
        {
            if (__instance != null)
            {
                __instance.gameObject.AddOrGet<NuclearResearchCenterAutoRunner>();
            }
        }
    }

    /// <summary>
    /// 免人力研究：阻止生成小人操作材料研究终端的搬运/研究 Chore。
    /// </summary>
    [HarmonyPatch(typeof(NuclearResearchCenter.StatesInstance), nameof(NuclearResearchCenter.StatesInstance.CreateChore))]
    internal static class NuclearResearchCenter_CreateChore_Patch
    {
        private static bool Prefix()
        {
            return false; // 直接阻止生成小人操作任务
        }
    }

    /// <summary>
    /// 修复拆除或退出 ready 状态时，因 chore 为空导致的原版 NullReferenceException 崩溃。
    /// </summary>
    [HarmonyPatch(typeof(NuclearResearchCenter.StatesInstance), nameof(NuclearResearchCenter.StatesInstance.DestroyChore))]
    internal static class NuclearResearchCenter_DestroyChore_Patch
    {
        private static bool Prefix(NuclearResearchCenter.StatesInstance __instance)
        {
            if (__instance == null)
            {
                return false;
            }
            Chore chore = Traverse.Create(__instance).Field("chore").GetValue<Chore>();
            if (chore == null)
            {
                return false; // chore 为空时不调用原版 chore.Cancel()，防止空指针异常
            }
            return true;
        }
    }

    // =========================================================================
    // 原油精炼机（Oil Refinery）自动运行——免人力操作
    // 原版中原油精炼机属于 StateMachineComponent 而非 ComplexFabricator，
    // 需要小人站上去操作才调用 operational.SetActive(true) 触发 ElementConverter。
    // 这里在 ready 状态下直接激活，并给小人任务追加永假前置条件，彻底免除小人跑腿。
    // =========================================================================

    [HarmonyPatch(typeof(OilRefinery.States), nameof(OilRefinery.States.InitializeStates))]
    internal static class OilRefinery_States_InitializeStates_AutoRun_Patch
    {
        private static void Postfix(OilRefinery.States __instance)
        {
            if (__instance == null) return;

            __instance.ready
                .Enter((OilRefinery.StatesInstance smi) =>
                {
                    if (smi == null || smi.master == null) return;
                    Operational op = smi.master.GetComponent<Operational>();
                    if (op != null)
                    {
                        op.SetActive(true);
                    }
                    ResearchCenter_Sim200ms_AutoResearch_Patch.PlayWorkingAnim(smi.GetComponent<KBatchedAnimController>());
                    smi.GetComponent<KSelectable>()?.AddStatusItem(Db.Get().BuildingStatusItems.Working);
                })
                .Exit((OilRefinery.StatesInstance smi) =>
                {
                    if (smi == null || smi.master == null) return;
                    Operational op = smi.master.GetComponent<Operational>();
                    if (op != null)
                    {
                        op.SetActive(false);
                    }
                    ResearchCenter_Sim200ms_AutoResearch_Patch.StopWorkingAnim(smi.GetComponent<KBatchedAnimController>(), op);
                    smi.GetComponent<KSelectable>()?.RemoveStatusItem(Db.Get().BuildingStatusItems.Working);
                });
        }
    }

    [HarmonyPatch(typeof(OilRefinery), "OnSpawn")]
    internal static class OilRefinery_OnSpawn_AutoRun_Patch
    {
        private static int _logged;

        private static void Postfix(OilRefinery __instance)
        {
            if (__instance == null) return;

            var smi = __instance.GetSMI<OilRefinery.StatesInstance>();
            if (smi != null && smi.GetCurrentState() == smi.sm.ready)
            {
                __instance.GetComponent<Operational>()?.SetActive(true);
                ResearchCenter_Sim200ms_AutoResearch_Patch.PlayWorkingAnim(__instance.GetComponent<KBatchedAnimController>());
                __instance.GetComponent<KSelectable>()?.AddStatusItem(Db.Get().BuildingStatusItems.Working);
            }

            var remote = __instance.GetComponent<ManuallySetRemoteWorkTargetComponent>();
            if (remote != null && remote.RemoteDockChore != null)
            {
                remote.RemoteDockChore.AddPrecondition(OilRefinery_SetChore_AutoRun_Patch.NeverAvailable);
            }

            if (_logged < 5)
            {
                _logged++;
                Debug.Log($"[OmniMod] 机械自动运行：{__instance.name} 设为免人力自动精炼原油");
            }
        }
    }

    [HarmonyPatch(typeof(ManuallySetRemoteWorkTargetComponent), nameof(ManuallySetRemoteWorkTargetComponent.SetChore))]
    internal static class OilRefinery_SetChore_AutoRun_Patch
    {
        internal static readonly Chore.Precondition NeverAvailable = new Chore.Precondition
        {
            id = "OmniMod_NeverAvailable",
            description = "OmniMod Auto Refinery",
            fn = (ref Chore.Precondition.Context context, object data) => false,
            canExecuteOnAnyThread = true
        };

        private static void Postfix(ManuallySetRemoteWorkTargetComponent __instance, Chore chore_)
        {
            if (__instance != null && __instance.GetComponent<OilRefinery>() != null && chore_ != null)
            {
                chore_.AddPrecondition(NeverAvailable);
            }
        }
    }

    // =========================================================================
    // 提炼器（Gleaner / MilkSeparator）自动排空：
    // 转化咸乳产生的固体产物（咸乳蜡 / 鱼子酱）积满时全自动排空到地面，无需小人跑腿手动清空。
    // =========================================================================
    [HarmonyPatch(typeof(MilkSeparator), nameof(MilkSeparator.InitializeStates))]
    internal static class MilkSeparator_AutoEmpty_Patch
    {
        private static void Postfix(MilkSeparator __instance)
        {
            if (__instance == null || __instance.operational == null || __instance.operational.full == null)
            {
                return;
            }

            // 当提炼器进入 full 状态（或从旧存档载入且处于 full 状态）时，
            // 立即调用 smi.DropMilkFat() 自动把咸乳蜡/鱼子酱吐到地上，并直接切入 emptyComplete 刷新仪表
            __instance.operational.full
                .Enter(smi =>
                {
                    smi.DropMilkFat();
                    smi.GoTo(__instance.operational.emptyComplete);
                });
        }
    }

    [HarmonyPatch(typeof(MilkSeparator), "CreateEmptyChore")]
    internal static class MilkSeparator_CreateEmptyChore_Patch
    {
        private static void Postfix(MilkSeparator.Instance smi, ref Chore __result)
        {
            if (smi != null)
            {
                smi.DropMilkFat();
                smi.GoTo(smi.sm.operational.emptyComplete);
            }
            if (__result != null)
            {
                // 绝不允许生成小人清空任务，杜绝小人跑腿
                __result.AddPrecondition(OilRefinery_SetChore_AutoRun_Patch.NeverAvailable);
            }
        }
    }

    // =========================================================================
    // 脱盐器（Desalinator）自动排空：
    // 盐分积满时全自动将所有积存盐掉落到地面，重置储盐量，无需小人跑腿手动清空。
    // =========================================================================
    [HarmonyPatch(typeof(Desalinator), "CheckCanConvert")]
    internal static class Desalinator_CheckCanConvert_AutoEmpty_Patch
    {
        private static readonly AccessTools.FieldRef<Desalinator, Storage> StorageField =
            AccessTools.FieldRefAccess<Desalinator, Storage>("storage");

        private static readonly AccessTools.FieldRef<Desalinator, Chore> RemoteChoreField =
            AccessTools.FieldRefAccess<Desalinator, Chore>("remoteChore");

        private static void Prefix(Desalinator __instance)
        {
            AutoEmptyIfNeeded(__instance);
        }

        internal static void AutoEmptyIfNeeded(Desalinator desalinator)
        {
            if (desalinator == null)
            {
                return;
            }

            Storage st = StorageField(desalinator);
            if (st == null)
            {
                return;
            }

            if (desalinator.SaltStorageLeft <= 0f || st.MassStored() >= desalinator.maxSalt)
            {
                if (st.items.Count > 0)
                {
                    st.DropAll(false, false, default, true);
                }
                desalinator.SaltStorageLeft = desalinator.maxSalt;
                Chore chore = RemoteChoreField(desalinator);
                if (chore != null)
                {
                    chore.Cancel("OmniMod Auto Empty");
                    RemoteChoreField(desalinator) = null;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Desalinator), "SaltStorageLeft", MethodType.Setter)]
    internal static class Desalinator_set_SaltStorageLeft_Patch
    {
        private static readonly AccessTools.FieldRef<Desalinator, Storage> StorageField =
            AccessTools.FieldRefAccess<Desalinator, Storage>("storage");

        private static readonly AccessTools.FieldRef<Desalinator, Chore> RemoteChoreField =
            AccessTools.FieldRefAccess<Desalinator, Chore>("remoteChore");

        private static void Prefix(Desalinator __instance, ref float value)
        {
            if (__instance == null)
            {
                return;
            }

            if (value <= 0f)
            {
                Storage st = StorageField(__instance);
                if (st != null && st.items.Count > 0)
                {
                    st.DropAll(false, false, default, true);
                }
                value = __instance.maxSalt;
                Chore chore = RemoteChoreField(__instance);
                if (chore != null)
                {
                    chore.Cancel("OmniMod Auto Empty");
                    RemoteChoreField(__instance) = null;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Desalinator), "OnSpawn")]
    internal static class Desalinator_OnSpawn_AutoEmpty_Patch
    {
        private static void Postfix(Desalinator __instance)
        {
            Desalinator_CheckCanConvert_AutoEmpty_Patch.AutoEmptyIfNeeded(__instance);
        }
    }

    // =========================================================================
    // 碎石机新增冰块粉碎配方：100kg 冰 -> 100kg 雪。
    // 配合 ComplexFabricator 自动运行，全自动无限供应糖心树等寒带作物所需的雪花肥料。
    // =========================================================================
    [HarmonyPatch(typeof(RockCrusherConfig), "ConfigureBuildingTemplate")]
    internal static class RockCrusherConfig_ConfigureBuildingTemplate_SnowRecipe_Patch
    {
        private static void Postfix()
        {
            ComplexRecipe.RecipeElement[] input = new[]
            {
                new ComplexRecipe.RecipeElement(SimHashes.Ice.CreateTag(), 100f)
            };
            ComplexRecipe.RecipeElement[] output = new[]
            {
                new ComplexRecipe.RecipeElement(SimHashes.Snow.CreateTag(), 100f, ComplexRecipe.RecipeElement.TemperatureOperation.AverageTemperature)
            };
            string recipeId = ComplexRecipeManager.MakeRecipeID("RockCrusher", input, output);
            if (ComplexRecipeManager.Get() != null && ComplexRecipeManager.Get().GetRecipe(recipeId) != null)
            {
                return;
            }

            new ComplexRecipe(recipeId, input, output)
            {
                time = 20f,
                description = "将致密冰块粉碎成疏松雪花，供寒带植物（如糖心树）施肥使用。",
                nameDisplay = ComplexRecipe.RecipeNameDisplay.IngredientToResult,
                fabricators = new System.Collections.Generic.List<Tag> { TagManager.Create("RockCrusher") },
                sortOrder = 9
            };
        }
    }
}

