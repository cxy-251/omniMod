using System;
using HarmonyLib;
using OmniMod.Core.Logging;
using OmniMod.Core.Safety;
using UnityEngine;

namespace OmniMod.Machinery.Research
{
    /// <summary>
    /// 隔绝式望远镜 / 小型望远镜（ClusterTelescope）无人全自动扫描星图与识别流星雨
    /// </summary>
    public class ClusterTelescopeAutoRunner : KMonoBehaviour, ISim200ms
    {
        private ClusterTelescope.Instance _smi;
        private ClusterTelescope.ClusterTelescopeWorkable _workable;
        private ClusterTelescope.ClusterTelescopeIdentifyMeteorWorkable _meteorWorkable;
        private Operational _operational;
        private Storage _storage;
        private KBatchedAnimController _kbac;
        private GameObject _telescopeTargetMarker;
        private AxialI _currentTarget;
        private bool _wasActive;
        private bool _wasMeteor;

        protected override void OnSpawn()
        {
            base.OnSpawn();
            _smi = this.GetSMI<ClusterTelescope.Instance>();
            _workable = GetComponent<ClusterTelescope.ClusterTelescopeWorkable>();
            _meteorWorkable = GetComponent<ClusterTelescope.ClusterTelescopeIdentifyMeteorWorkable>();
            _operational = GetComponent<Operational>();
            _storage = GetComponent<Storage>();
            _kbac = GetComponent<KBatchedAnimController>();
        }

        protected override void OnCleanUp()
        {
            DestroyTargetMarker();
            base.OnCleanUp();
        }

        public void Sim200ms(float dt)
        {
            ExceptionShield.Execute("ClusterTelescopeAutoRunner.Sim200ms", () =>
            {
                if (_smi == null)
                {
                    _smi = this.GetSMI<ClusterTelescope.Instance>();
                    if (_smi == null) return;
                }

                if (_operational == null || !_operational.IsOperational)
                {
                    StopRunning();
                    return;
                }

                if (!_smi.HasSkyVisibility() || _smi.PercentClear <= 0f)
                {
                    StopRunning();
                    return;
                }

                if ((_workable != null && _workable.worker != null) ||
                    (_meteorWorkable != null && _meteorWorkable.worker != null))
                {
                    return;
                }

                bool workOnMeteor = _smi.ShouldBeWorkingOnMeteorIdentification();
                bool workOnTile = !workOnMeteor && _smi.ShouldBeWorkingOnRevealingTile();

                if (!workOnMeteor && !workOnTile)
                {
                    StopRunning();
                    return;
                }

                TryConsumeOxygen(dt);

                float skyMultiplier = Mathf.Clamp01(_smi.PercentClear);
                float speedMultiplier = 2.0f;

                if (workOnMeteor)
                {
                    ClusterMapMeteorShower.Instance meteorTarget = _smi.GetMeteorTarget();
                    if (meteorTarget != null)
                    {
                        float points = (dt / 20f) * skyMultiplier * speedMultiplier;
                        meteorTarget.ProgressIdentifiction(points);
                        StartRunning(isMeteor: true);
                        UpdateMarker(meteorTarget.ClusterGridPosition(), meteorTarget);
                    }
                    else
                    {
                        StopRunning();
                    }
                }
                else if (workOnTile)
                {
                    AxialI analyzeTarget = _smi.GetAnalyzeTarget();
                    float num = TUNING.ROCKETRY.CLUSTER_FOW.POINTS_TO_REVEAL / TUNING.ROCKETRY.CLUSTER_FOW.DEFAULT_CYCLES_PER_REVEAL / 600f;
                    float points = dt * num * skyMultiplier * speedMultiplier;

                    var fowManager = SaveGame.Instance.GetSMI<ClusterFogOfWarManager.Instance>();
                    if (fowManager != null && fowManager.EarnRevealPointsForLocation(analyzeTarget, points))
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            Util.KInstantiate(
                                Assets.GetPrefab(DatabankHelper.ID),
                                transform.position + Vector3.up * 2f + Vector3.right,
                                Quaternion.identity, null, null, initialize_id: true, 23
                            ).SetActive(true);
                        }
                        PopFXManager.Instance?.SpawnFX(
                            global::Def.GetUISprite(DatabankHelper.ID).first,
                            DatabankHelper.NAME_PLURAL,
                            transform, Vector3.up * 2f, 1.5f, track_target: true
                        );
                    }

                    StartRunning(isMeteor: false);
                    UpdateMarker(analyzeTarget, null);
                }

                ModLog.Throttle($"telescope_{name}", $"望远镜自动运行：{name} 正在全自动扫描星图，无需小人", 30f);
            });
        }

        private void TryConsumeOxygen(float dt)
        {
            if (_storage != null && _storage.items != null && _storage.items.Count > 0)
            {
                float amount = 0.1f * dt;
                _storage.ConsumeAndGetDisease(GameTags.Breathable, amount, out _, out _, out _, out _);
            }
        }

        private void StartRunning(bool isMeteor)
        {
            if (!_wasActive || _wasMeteor != isMeteor)
            {
                _wasActive = true;
                _wasMeteor = isMeteor;
                _operational.SetActive(true);
                BuildingAnimHelper.PlayWorkingAnim(_kbac);

                KSelectable selectable = GetComponent<KSelectable>();
                if (selectable != null)
                {
                    if (isMeteor)
                    {
                        selectable.RemoveStatusItem(Db.Get().BuildingStatusItems.TelescopeWorking, _workable);
                        selectable.AddStatusItem(Db.Get().BuildingStatusItems.ClusterTelescopeMeteorWorking, _meteorWorkable);
                    }
                    else
                    {
                        selectable.RemoveStatusItem(Db.Get().BuildingStatusItems.ClusterTelescopeMeteorWorking, _meteorWorkable);
                        selectable.AddStatusItem(Db.Get().BuildingStatusItems.TelescopeWorking, _workable);
                    }
                }
            }
        }

        private void StopRunning()
        {
            if (_wasActive)
            {
                _wasActive = false;
                if (_operational != null)
                {
                    _operational.SetActive(false);
                }
                BuildingAnimHelper.StopWorkingAnim(_kbac, _operational);

                KSelectable selectable = GetComponent<KSelectable>();
                if (selectable != null)
                {
                    selectable.RemoveStatusItem(Db.Get().BuildingStatusItems.TelescopeWorking, _workable);
                    selectable.RemoveStatusItem(Db.Get().BuildingStatusItems.ClusterTelescopeMeteorWorking, _meteorWorkable);
                }
                DestroyTargetMarker();
            }
        }

        private void UpdateMarker(AxialI target, ClusterMapMeteorShower.Instance meteorTarget)
        {
            if (target != _currentTarget)
            {
                _currentTarget = target;
                if (_telescopeTargetMarker != null)
                {
                    TelescopeTarget comp = _telescopeTargetMarker.GetComponent<TelescopeTarget>();
                    if (comp != null)
                    {
                        comp.Init(target);
                        comp.SetTargetMeteorShower(meteorTarget);
                    }
                }
            }

            if (_telescopeTargetMarker == null && ClusterGrid.Instance != null &&
                !ClusterGrid.Instance.GetEntityOfLayerAtCell(target, EntityLayer.Telescope))
            {
                _telescopeTargetMarker = GameUtil.KInstantiate(Assets.GetPrefab("TelescopeTarget"), Grid.SceneLayer.Background);
                if (_telescopeTargetMarker != null)
                {
                    _telescopeTargetMarker.SetActive(true);
                    TelescopeTarget comp = _telescopeTargetMarker.GetComponent<TelescopeTarget>();
                    if (comp != null)
                    {
                        comp.Init(target);
                        comp.SetTargetMeteorShower(meteorTarget);
                    }
                }
            }
        }

        private void DestroyTargetMarker()
        {
            if (_telescopeTargetMarker != null)
            {
                Util.KDestroyGameObject(_telescopeTargetMarker);
                _telescopeTargetMarker = null;
            }
        }
    }

    [HarmonyPatch(typeof(ClusterTelescope.ClusterTelescopeWorkable), "OnSpawn")]
    internal static class ClusterTelescopeWorkable_OnSpawn_AutoRunner_Patch
    {
        private static void Postfix(ClusterTelescope.ClusterTelescopeWorkable __instance)
        {
            if (__instance != null)
            {
                __instance.gameObject.AddOrGet<ClusterTelescopeAutoRunner>();
            }
        }
    }

    [HarmonyPatch(typeof(ClusterTelescope.Instance), nameof(ClusterTelescope.Instance.CreateRevealTileChore))]
    internal static class ClusterTelescope_CreateRevealTileChore_Patch
    {
        private static void Postfix(ref Chore __result)
        {
            __result?.AddPrecondition(MachineryPreconditions.NeverAvailable);
        }
    }

    [HarmonyPatch(typeof(ClusterTelescope.Instance), nameof(ClusterTelescope.Instance.CreateIdentifyMeteorChore))]
    internal static class ClusterTelescope_CreateIdentifyMeteorChore_Patch
    {
        private static void Postfix(ref Chore __result)
        {
            __result?.AddPrecondition(MachineryPreconditions.NeverAvailable);
        }
    }

    /// <summary>
    /// 本体望远镜（Telescope）无人全自动星空分析
    /// </summary>
    [HarmonyPatch(typeof(Telescope), "CreateChore")]
    internal static class Telescope_CreateChore_Patch
    {
        private static void Postfix(ref Chore __result)
        {
            __result?.AddPrecondition(MachineryPreconditions.NeverAvailable);
        }
    }

    [HarmonyPatch(typeof(Telescope), "Sim200ms")]
    internal static class Telescope_Sim200ms_AutoRun_Patch
    {
        private static void Postfix(Telescope __instance, float dt)
        {
            if (__instance == null || __instance.worker != null) return;

            ExceptionShield.Execute("Telescope.Sim200ms", () =>
            {
                Operational op = __instance.GetComponent<Operational>();
                if (op == null || !op.IsOperational) return;

                if (SpacecraftManager.instance != null && SpacecraftManager.instance.HasAnalysisTarget())
                {
                    int destId = SpacecraftManager.instance.GetStarmapAnalysisDestinationID();
                    SpaceDestination dest = SpacecraftManager.instance.GetDestination(destId);
                    if (dest != null && SpacecraftManager.instance.GetDestinationAnalysisState(dest) != SpacecraftManager.DestinationAnalysisState.Complete)
                    {
                        float num = 1f / (float)dest.OneBasedDistance;
                        float num2 = TUNING.ROCKETRY.DESTINATION_ANALYSIS.DISCOVERED;
                        float defaultCycles = TUNING.ROCKETRY.DESTINATION_ANALYSIS.DEFAULT_CYCLES_PER_DISCOVERY;
                        float num3 = num2 / defaultCycles / 600f;
                        float points = dt * num * num3 * 2.0f;
                        SpacecraftManager.instance.EarnDestinationAnalysisPoints(destId, points);

                        op.SetActive(true);
                        BuildingAnimHelper.PlayWorkingAnim(__instance.GetComponent<KBatchedAnimController>());
                        return;
                    }
                }

                if (op.IsActive && __instance.worker == null)
                {
                    op.SetActive(false);
                    BuildingAnimHelper.StopWorkingAnim(__instance.GetComponent<KBatchedAnimController>(), op);
                }
            });
        }
    }
}
