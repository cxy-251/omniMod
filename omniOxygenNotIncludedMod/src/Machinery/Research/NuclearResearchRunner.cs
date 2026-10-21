using System;
using HarmonyLib;
using OmniMod.Core.Logging;
using OmniMod.Core.Safety;
using UnityEngine;

namespace OmniMod.Machinery.Research
{
    /// <summary>
    /// 自动材料研究：材料研究终端（NuclearResearchCenter）使用辐射粒子——选好研究项目后无需小人操作。
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
            base.OnCleanUp();
        }

        public void Sim200ms(float dt)
        {
            ExceptionShield.Execute("NuclearResearchCenterAutoRunner.Sim200ms", () =>
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

                global::Research research = global::Research.Instance;
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
                    BuildingAnimHelper.PlayWorkingAnim(_kbac);
                    GetComponent<KSelectable>()?.AddStatusItem(Db.Get().BuildingStatusItems.ComplexFabricatorResearching, _nrc);
                }

                ModLog.Throttle($"nuclear_res_{_nrc.PrefabID().Name}", $"自动研究：{_nrc.name} 自行运转材料研究，无需小人", 30f);
            });
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
                BuildingAnimHelper.StopWorkingAnim(_kbac, _operational);
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

    [HarmonyPatch(typeof(NuclearResearchCenter.StatesInstance), nameof(NuclearResearchCenter.StatesInstance.CreateChore))]
    internal static class NuclearResearchCenter_CreateChore_Patch
    {
        private static bool Prefix()
        {
            return false; // 阻止生成小人操作任务
        }
    }

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
}
