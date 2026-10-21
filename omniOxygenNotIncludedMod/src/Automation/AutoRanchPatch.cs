using System;
using System.Collections.Generic;
using HarmonyLib;
using Klei.AI;
using UnityEngine;

namespace OmniMod.Automation
{
    /// <summary>
    /// 全自动小动物照料站、挤奶站、剪毛站（包含水下变种）：
    /// 小动物自动前往照料点，全自动梳毛、挤奶、剪毛，完全不需要复制人跑腿参与。
    /// 
    /// 涵盖建筑：
    /// - 照料站 (RanchStationConfig)
    /// - 挤奶站 (MilkingStationConfig)
    /// - 剪毛站 (ShearingStationConfig)
    /// - 水下照料站 (UnderwaterRanchStationConfig)
    /// - 水下挤奶站 (UnderwaterMilkingStationConfig)
    /// - 水下剪毛站 (UnderwaterShearingStationConfig)
    /// </summary>
    internal class OmniVirtualRancher : WorkerBase
    {
        public override Attributes GetAttributes()
        {
            var attrs = new Attributes(gameObject);
            var ranchingAttr = Db.Get().Attributes.Ranching;
            var instance = attrs.Add(ranchingAttr);

            // 基础提供 15 点照料技能加成；若殖民地有更高技能的养殖小人，则直接继承最高技能
            float bestSkill = 15f;
            if (Components.LiveMinionIdentities != null)
            {
                for (int i = 0; i < Components.LiveMinionIdentities.Count; i++)
                {
                    var minion = Components.LiveMinionIdentities[i];
                    if (minion == null) continue;
                    var attr = minion.GetAttributes()?.Get(ranchingAttr.Id);
                    if (attr != null && attr.GetTotalValue() > bestSkill)
                    {
                        bestSkill = attr.GetTotalValue();
                    }
                }
            }

            instance.Add(new AttributeModifier(ranchingAttr.Id, bestSkill, "OmniMod Automated Rancher"));
            return attrs;
        }

        public override AttributeConverterInstance GetAttributeConverter(string id) => null;
        public override KBatchedAnimController GetAnimController() => GetComponent<KBatchedAnimController>();
        public override State GetState() => State.Idle;
        public override StartWorkInfo GetStartWorkInfo() => null;
        public override Workable GetWorkable() => null;
        public override bool InstantlyFinish() => true;
        public override bool IsFetchDrone() => false;
        public override Guid OfferStatusItem(StatusItem item, object data = null) => Guid.Empty;
        public override void RevokeStatusItem(Guid id) { }
        public override void SetWorkCompleteData(object data) { }
        public override void StartWork(StartWorkInfo start_work_info) { }
        public override void StopWork() { }
        public override bool UsesMultiTool() => false;
        public override WorkResult Work(float dt) => WorkResult.Success;
    }

    internal static class AutoRanchTracker
    {
        private static readonly HashSet<int> _calledCritterIds = new HashSet<int>();

        internal static bool HasBeenCalled(int id) => _calledCritterIds.Contains(id);

        internal static void MarkCalled(int id) => _calledCritterIds.Add(id);

        internal static void Clear(int id) => _calledCritterIds.Remove(id);
    }

    internal static class AutoRanchHelper
    {
        private static readonly AccessTools.FieldRef<RanchStation.Instance, WorkerBase> RancherField =
            AccessTools.FieldRefAccess<RanchStation.Instance, WorkerBase>("rancher");

        internal static bool IsStationOperational(RanchStation.Instance smi)
        {
            if (smi == null || !smi.IsRunning()) return false;
            Operational op = smi.GetComponent<Operational>();
            if (op != null && !op.IsOperational) return false;
            if (smi.sm != null && smi.sm.Operational != null)
            {
                return smi.IsInsideState(smi.sm.Operational);
            }
            return true;
        }

        internal static void EnsureVirtualRancher(RanchStation.Instance smi)
        {
            if (smi == null || smi.gameObject == null) return;

            OmniVirtualRancher worker = smi.gameObject.AddOrGet<OmniVirtualRancher>();
            WorkerBase current = RancherField(smi);
            if (current == null || current != worker)
            {
                RancherField(smi) = worker;
            }
        }
    }

    /// <summary>
    /// 1. 禁用复制人工作任务：让香草生成正常 Chore 避免 ToggleChore/SetupChore 抛 NRE，
    /// 但追加永远返回 false 的前置条件，并隐藏悬浮提示，使小人永远不会接取该任务。
    /// </summary>
    [HarmonyPatch(typeof(RanchStation.Instance), "CreateChore")]
    internal static class RanchStation_CreateChore_Patch
    {
        private static int _logged;

        private static void Postfix(RanchStation.Instance __instance, ref Chore __result)
        {
            if (__result != null)
            {
                __result.AddPrecondition(new Chore.Precondition
                {
                    id = "OmniMod_PreventDuplicantRanching",
                    description = "OmniMod Automated Ranching",
                    fn = (ref Chore.Precondition.Context context, object data) => false
                });
                __result.showAvailabilityInHoverText = false;
            }
            AutoRanchHelper.EnsureVirtualRancher(__instance);

            if (_logged < 5)
            {
                _logged++;
                Debug.Log($"[OmniMod] 照料站免人工操作生效：{__instance.gameObject.name} 任务已屏蔽复制人接取");
            }
        }
    }

    [HarmonyPatch(typeof(RanchStation.Instance), "OnRancherChanged")]
    internal static class RanchStation_OnRancherChanged_Patch
    {
        private static void Postfix(RanchStation.Instance __instance)
        {
            AutoRanchHelper.EnsureVirtualRancher(__instance);
        }
    }

    /// <summary>
    /// 2. 状态机就绪时保持 HasRancher 为 true，使小动物排队和校验逻辑正常运转。
    /// </summary>
    [HarmonyPatch(typeof(RanchStation.Instance), "get_HasRancher")]
    internal static class RanchStation_HasRancher_Patch
    {
        private static void Postfix(RanchStation.Instance __instance, ref bool __result)
        {
            if (AutoRanchHelper.IsStationOperational(__instance))
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// 3. 站台就绪状态判定：只要站台通电运转且当前台面上没有动物，即视作随时就绪，
    /// 确保小动物时刻判定 ShouldGoGetRanched 为 true，不会中途掉出排队。
    /// </summary>
    [HarmonyPatch(typeof(RanchStation.Instance), "get_IsRancherReady")]
    internal static class RanchStation_IsRancherReady_Patch
    {
        private static void Postfix(RanchStation.Instance __instance, ref bool __result)
        {
            if (AutoRanchHelper.IsStationOperational(__instance))
            {
                __result = (__instance.ActiveRanchable == null);
            }
        }
    }

    /// <summary>
    /// 4. 状态机启动时，初始化挂载虚拟照料 Worker。
    /// </summary>
    [HarmonyPatch(typeof(RanchStation.Instance), "StartSM")]
    internal static class RanchStation_StartSM_Patch
    {
        private static void Postfix(RanchStation.Instance __instance)
        {
            AutoRanchHelper.EnsureVirtualRancher(__instance);
        }
    }

    /// <summary>
    /// 5. 空间判定兜底：若房间探针暂时未识别到 Cavity，直接取建筑所在 Cell 的 Cavity，避免寻路中断。
    /// </summary>
    [HarmonyPatch(typeof(RanchStation.Instance), "GetStationCavity")]
    internal static class RanchStation_GetStationCavity_Patch
    {
        private static void Postfix(RanchStation.Instance __instance, ref CavityInfo __result)
        {
            if (__result == null && __instance != null)
            {
                __result = Game.Instance.roomProber.GetCavityForCell(__instance.GetRanchNavTarget());
            }
        }
    }

    /// <summary>
    /// 6. 只要站台处于正常通电工作状态，绝不断开并解散排队小动物。
    /// </summary>
    [HarmonyPatch(typeof(RanchStation.Instance), "TriggerRanchStationNoLongerAvailable")]
    internal static class RanchStation_TriggerRanchStationNoLongerAvailable_Patch
    {
        private static bool Prefix(RanchStation.Instance __instance)
        {
            if (AutoRanchHelper.IsStationOperational(__instance))
            {
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// 7. 寻路并召唤小动物：
    /// - 放行原版 FindRanchable，使其每 200ms 持续刷新并筛选厩舍内需要照料/产奶/剪毛的动物；
    /// - 精确单次通知：对加入 targetRanchables 的每只动物仅广播一次呼唤信号，避免重复打断移动。
    /// </summary>
    [HarmonyPatch(typeof(RanchStation.Instance), "FindRanchable")]
    internal static class RanchStation_FindRanchable_Patch
    {
        private static readonly AccessTools.FieldRef<RanchStation.Instance, List<RanchableMonitor.Instance>> TargetRanchablesField =
            AccessTools.FieldRefAccess<RanchStation.Instance, List<RanchableMonitor.Instance>>("targetRanchables");

        private static void Postfix(RanchStation.Instance __instance)
        {
            if (!AutoRanchHelper.IsStationOperational(__instance))
            {
                return;
            }

            AutoRanchHelper.EnsureVirtualRancher(__instance);

            // 看门狗：如果站台上已有到位的动物（如旧存档停滞或刚走到位），立即触发完成并驱离，防止死锁
            if (__instance.ActiveRanchable != null)
            {
                __instance.RanchCreature();
                return;
            }

            List<RanchableMonitor.Instance> list = TargetRanchablesField(__instance);
            if (list == null || list.Count == 0)
            {
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                var critter = list[i];
                if (critter == null || critter.IsNullOrStopped() || critter.gameObject == null)
                {
                    continue;
                }

                int id = critter.gameObject.GetInstanceID();
                if (!AutoRanchTracker.HasBeenCalled(id))
                {
                    AutoRanchTracker.MarkCalled(id);
                    __instance.sm.RancherIsReady.Set(true, __instance);
                    Game.BrainScheduler.PrioritizeBrain(critter.GetComponent<CreatureBrain>());
                    critter.Trigger((int)GameHashes.RancherReadyAtRanchStation);
                }
            }
        }
    }

    /// <summary>
    /// 8. 动物完成照料后，清理呼唤状态记录并强制小动物离场。
    /// </summary>
    [HarmonyPatch(typeof(RanchStation.Instance), "RanchCreature")]
    internal static class RanchStation_RanchCreature_Patch
    {
        private static readonly AccessTools.FieldRef<RanchedStates, RanchedStates.RanchStates> RanchStatesRef =
            AccessTools.FieldRefAccess<RanchedStates, RanchedStates.RanchStates>("ranch");

        private static RanchedStates.Instance _lastActiveRanched;

        private static void Prefix(RanchStation.Instance __instance)
        {
            _lastActiveRanched = __instance?.ActiveRanchable;
            if (_lastActiveRanched != null && _lastActiveRanched.gameObject != null)
            {
                AutoRanchTracker.Clear(_lastActiveRanched.gameObject.GetInstanceID());
            }
        }

        private static void Postfix(RanchStation.Instance __instance)
        {
            if (_lastActiveRanched != null && !_lastActiveRanched.IsNullOrStopped())
            {
                try
                {
                    if (_lastActiveRanched.sm != null)
                    {
                        var ranch = RanchStatesRef(_lastActiveRanched.sm);
                        if (ranch?.Runaway != null)
                        {
                            _lastActiveRanched.GoTo(ranch.Runaway);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[OmniMod] 照料离场跳转异常: {ex.Message}");
                }
            }
            _lastActiveRanched = null;
        }
    }

    /// <summary>
    /// 9. 动物离开队列时，清理呼唤状态记录。
    /// </summary>
    [HarmonyPatch(typeof(RanchStation.Instance), "Abandon")]
    internal static class RanchStation_Abandon_Patch
    {
        private static void Postfix(RanchableMonitor.Instance critter)
        {
            if (critter != null && critter.gameObject != null)
            {
                AutoRanchTracker.Clear(critter.gameObject.GetInstanceID());
            }
        }
    }

    /// <summary>
    /// 10. 小动物到达照料站：立即触发完成照料/挤奶/剪毛。
    /// </summary>
    [HarmonyPatch(typeof(RanchStation.Instance), "MessageCreatureArrived")]
    internal static class RanchStation_MessageCreatureArrived_Patch
    {
        private static void Postfix(RanchStation.Instance __instance, RanchedStates.Instance critter)
        {
            if (!AutoRanchHelper.IsStationOperational(__instance))
            {
                return;
            }

            AutoRanchHelper.EnsureVirtualRancher(__instance);

            RanchedStates.Instance targetCritter = __instance.ActiveRanchable ?? critter;
            if (targetCritter == null)
            {
                return;
            }

            // 到位后立即执行照料与产出，无需延迟，避免竞态卡顿
            __instance.RanchCreature();
        }
    }

    /// <summary>
    /// 11. 动物状态机补丁：
    /// 原版 Wavegoodbye 依赖小人工作完成播放收尾动画，由 OnAnimQueueComplete 触发 Runaway 离场。
    /// 全自动免小人照料时，小动物可能处于循环动画中，OnAnimQueueComplete 不会自发触发，导致小动物卡在台面上不动。
    /// 此补丁在进入 Wavegoodbye 时直接过渡到 Runaway，让小动物照料完毕后立刻离开站台。
    /// </summary>
    [HarmonyPatch(typeof(RanchedStates), "InitializeStates")]
    internal static class RanchedStates_InitializeStates_Patch
    {
        private static readonly AccessTools.FieldRef<RanchedStates, RanchedStates.RanchStates> RanchStatesRef =
            AccessTools.FieldRefAccess<RanchedStates, RanchedStates.RanchStates>("ranch");

        private static void Postfix(RanchedStates __instance)
        {
            var ranch = RanchStatesRef(__instance);
            if (ranch?.Wavegoodbye != null && ranch?.Runaway != null)
            {
                ranch.Wavegoodbye.Enter(smi =>
                {
                    if (smi != null && !smi.IsNullOrStopped())
                    {
                        var rs = RanchStatesRef(smi.sm);
                        if (rs?.Runaway != null)
                        {
                            smi.GoTo(rs.Runaway);
                        }
                    }
                });
            }
        }
    }

    // =========================================================================
    // 全自动小动物生命周期与隔空转运系统 (Zero-Dupe Critter Lifecycle System)
    // 涵盖：
    // 1. 袋装小动物全局注册表 (BaggedCreatureRegistry)
    // 2. 动物放生点全局注册表与自动解袋 (CreatureDeliveryRegistry & CreatureDeliveryPoint)
    // 3. 孵化仓自动弹出并打包幼体 (EggIncubator)
    // 4. 动物移出点自动呼唤并打包成体 (FixedCapturePoint)
    // 5. 手动框选捕获自动秒打包 (Capturable)
    // =========================================================================

    public static class BaggedCreatureRegistry
    {
        private static readonly object _lock = new object();
        private static readonly HashSet<Baggable> _bagged = new HashSet<Baggable>();
        private static readonly Dictionary<Baggable, int> _bagToCell = new Dictionary<Baggable, int>();
        private static readonly HashSet<int> _baggedCells = new HashSet<int>();

        public static void Register(Baggable bag)
        {
            if (bag == null || bag.gameObject == null) return;
            lock (_lock)
            {
                _bagged.Add(bag);
                int cell = Grid.PosToCell(bag.transform.GetPosition());
                if (Grid.IsValidCell(cell))
                {
                    _bagToCell[bag] = cell;
                    _baggedCells.Add(cell);
                }
            }

            bag.Subscribe((int)GameHashes.ObjectDestroyed, (data) =>
            {
                Unregister(bag);
            });
        }

        public static void Unregister(Baggable bag)
        {
            if (bag == null) return;
            lock (_lock)
            {
                _bagged.Remove(bag);
                if (_bagToCell.TryGetValue(bag, out int oldCell))
                {
                    _bagToCell.Remove(bag);
                    if (!_bagToCell.ContainsValue(oldCell))
                    {
                        _baggedCells.Remove(oldCell);
                    }
                }
            }
        }

        public static bool HasBaggedCreatureAtCell(int cell)
        {
            lock (_lock)
            {
                return _baggedCells.Contains(cell);
            }
        }

        public static List<Baggable> GetBaggedCreatures()
        {
            lock (_lock)
            {
                return new List<Baggable>(_bagged);
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _bagged.Clear();
                _bagToCell.Clear();
                _baggedCells.Clear();
            }
        }
    }

    public static class CreatureDeliveryRegistry
    {
        private static readonly HashSet<int> _deliveryCells = new HashSet<int>();

        public static void Register(CreatureDeliveryPoint point)
        {
            if (point == null) return;
            int baseCell = Grid.PosToCell(point.transform.GetPosition());
            if (Grid.IsValidCell(baseCell))
            {
                _deliveryCells.Add(baseCell);
            }
            if (point.deliveryOffsets != null)
            {
                for (int i = 0; i < point.deliveryOffsets.Length; i++)
                {
                    int offsetCell = Grid.OffsetCell(baseCell, point.deliveryOffsets[i]);
                    if (Grid.IsValidCell(offsetCell))
                    {
                        _deliveryCells.Add(offsetCell);
                    }
                }
            }
        }

        public static void Unregister(CreatureDeliveryPoint point)
        {
            if (point == null) return;
            int baseCell = Grid.PosToCell(point.transform.GetPosition());
            _deliveryCells.Remove(baseCell);
            if (point.deliveryOffsets != null)
            {
                for (int i = 0; i < point.deliveryOffsets.Length; i++)
                {
                    int offsetCell = Grid.OffsetCell(baseCell, point.deliveryOffsets[i]);
                    _deliveryCells.Remove(offsetCell);
                }
            }
        }

        public static bool IsDeliveryCell(int cell)
        {
            return _deliveryCells.Contains(cell);
        }

        public static void Clear()
        {
            _deliveryCells.Clear();
        }
    }

    [HarmonyPatch(typeof(Baggable), nameof(Baggable.SetWrangled))]
    internal static class Baggable_SetWrangled_Patch
    {
        private static void Postfix(Baggable __instance)
        {
            if (__instance != null)
            {
                __instance.gameObject.AddTag(GameTags.Creatures.Deliverable);
                BaggedCreatureRegistry.Register(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Baggable), nameof(Baggable.Free))]
    internal static class Baggable_Free_Patch
    {
        private static void Postfix(Baggable __instance)
        {
            if (__instance != null)
            {
                BaggedCreatureRegistry.Unregister(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(CreatureDeliveryPoint), "OnSpawn")]
    internal static class CreatureDeliveryPoint_OnSpawn_Patch
    {
        private static void Postfix(CreatureDeliveryPoint __instance)
        {
            if (__instance == null) return;
            CreatureDeliveryRegistry.Register(__instance);

            __instance.Subscribe((int)GameHashes.OnStorageChange, (data) =>
            {
                CreatureDeliveryPointHelper.ReleaseAllInStorage(__instance);
            });
            CreatureDeliveryPointHelper.ReleaseAllInStorage(__instance);
        }
    }

    [HarmonyPatch(typeof(CreatureDeliveryPoint), "OnCleanUp")]
    internal static class CreatureDeliveryPoint_OnCleanUp_Patch
    {
        private static void Prefix(CreatureDeliveryPoint __instance)
        {
            if (__instance != null)
            {
                CreatureDeliveryRegistry.Unregister(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(CreatureDeliveryPoint.States), nameof(CreatureDeliveryPoint.States.DropAllCreatures))]
    internal static class CreatureDeliveryPoint_DropAllCreatures_Patch
    {
        private static void Postfix(CreatureDeliveryPoint.SMInstance smi)
        {
            if (smi?.master != null)
            {
                CreatureDeliveryPointHelper.ReleaseAllInStorage(smi.master);
            }
        }
    }

    internal static class CreatureDeliveryPointHelper
    {
        private static bool _isReleasing = false;

        internal static void ReleaseAllInStorage(CreatureDeliveryPoint deliveryPoint)
        {
            if (_isReleasing || deliveryPoint == null) return;
            Storage storage = deliveryPoint.GetComponent<Storage>();
            if (storage == null || storage.IsEmpty()) return;

            try
            {
                _isReleasing = true;
                List<GameObject> items = new List<GameObject>(storage.items);
                int cell = Grid.OffsetCell(Grid.PosToCell(deliveryPoint.transform.GetPosition()), deliveryPoint.spawnOffset);
                Vector3 spawnPos = Grid.CellToPosCBC(cell, Grid.SceneLayer.Creatures);

                for (int i = 0; i < items.Count; i++)
                {
                    GameObject go = items[i];
                    if (go == null) continue;

                    storage.Drop(go, true);
                    go.transform.SetPosition(spawnPos);

                    Baggable baggable = go.GetComponent<Baggable>();
                    if (baggable != null)
                    {
                        BaggedCreatureRegistry.Unregister(baggable);
                        baggable.Free();
                    }

                    KBatchedAnimController anim = go.GetComponent<KBatchedAnimController>();
                    if (anim != null)
                    {
                        anim.SetSceneLayer(Grid.SceneLayer.Creatures);
                    }
                }

                if (deliveryPoint.critterCapacity != null)
                {
                    deliveryPoint.critterCapacity.RefreshCreatureCount();
                }
            }
            finally
            {
                _isReleasing = false;
            }
        }
    }

    [HarmonyPatch(typeof(EggIncubator), nameof(EggIncubator.StoreBaby))]
    internal static class EggIncubator_StoreBaby_Patch
    {
        private static void Postfix(EggIncubator __instance, GameObject baby)
        {
            if (__instance == null || baby == null) return;

            __instance.OrderRemoveOccupant();

            Baggable baggable = baby.GetComponent<Baggable>();
            if (baggable != null)
            {
                baby.AddTag(GameTags.Creatures.Deliverable);
                baggable.SetWrangled();
            }

            __instance.Trigger((int)GameHashes.OccupantChanged, null);
        }
    }

    [HarmonyPatch(typeof(EggIncubator), "OnSpawn")]
    internal static class EggIncubator_OnSpawn_Watchdog_Patch
    {
        private static void Postfix(EggIncubator __instance)
        {
            if (__instance == null) return;
            GameObject occupant = __instance.Occupant;
            if (occupant != null && !occupant.HasTag(GameTags.Egg))
            {
                __instance.OrderRemoveOccupant();

                Baggable baggable = occupant.GetComponent<Baggable>();
                if (baggable != null)
                {
                    occupant.AddTag(GameTags.Creatures.Deliverable);
                    baggable.SetWrangled();
                }

                __instance.Trigger((int)GameHashes.OccupantChanged, null);
            }
        }
    }

    [HarmonyPatch(typeof(FixedCapturePoint.Instance), nameof(FixedCapturePoint.Instance.FindFixedCapturable))]
    internal static class FixedCapturePoint_FindFixedCapturable_Patch
    {
        private static readonly AccessTools.FieldRef<FixedCapturePoint.Instance, int> CaptureCellRef =
            AccessTools.FieldRefAccess<FixedCapturePoint.Instance, int>("captureCell");

        private static void Postfix(FixedCapturePoint.Instance __instance)
        {
            if (__instance == null || !__instance.IsOperational) return;

            var target = __instance.targetCapturable;
            if (target != null && !target.IsNullOrStopped() && target.gameObject != null)
            {
                __instance.SetRancherIsAvailableForCapturing();

                int creatureCell = Grid.PosToCell(target.transform.GetPosition());
                int captureCell = CaptureCellRef(__instance);

                if (Grid.IsValidCell(creatureCell) && Grid.IsValidCell(captureCell))
                {
                    int dist = Grid.GetCellDistance(creatureCell, captureCell);
                    if (dist <= 2)
                    {
                        AutoWrangleTargetCreature(__instance);
                    }
                }
            }
        }

        internal static void AutoWrangleTargetCreature(FixedCapturePoint.Instance smi)
        {
            if (smi == null || smi.targetCapturable == null || smi.targetCapturable.IsNullOrStopped()) return;
            GameObject go = smi.targetCapturable.gameObject;
            if (go == null) return;

            Capturable capturable = go.GetComponent<Capturable>();
            if (capturable != null)
            {
                capturable.MarkForCapture(false);
            }
            Baggable baggable = go.GetComponent<Baggable>();
            if (baggable != null)
            {
                go.AddTag(GameTags.Creatures.Deliverable);
                baggable.SetWrangled();
            }
            smi.ResetCapturePoint();
        }
    }

    [HarmonyPatch(typeof(FixedCapturePoint.Instance), nameof(FixedCapturePoint.Instance.StartSM))]
    internal static class FixedCapturePoint_StartSM_Patch
    {
        private static void Postfix(FixedCapturePoint.Instance __instance)
        {
            if (__instance == null || __instance.gameObject == null) return;
            __instance.Subscribe((int)GameHashes.CreatureArrivedAtCapturePoint, (data) =>
            {
                FixedCapturePoint_FindFixedCapturable_Patch.AutoWrangleTargetCreature(__instance);
            });
        }
    }

    [HarmonyPatch(typeof(FixedCapturePoint.Instance), nameof(FixedCapturePoint.Instance.CreateChore))]
    internal static class FixedCapturePoint_CreateChore_Patch
    {
        private static void Postfix(ref Chore __result)
        {
            if (__result != null)
            {
                __result.AddPrecondition(new Chore.Precondition
                {
                    id = "OmniMod_PreventDuplicantCapture",
                    description = "OmniMod Automated Capture",
                    fn = (ref Chore.Precondition.Context context, object data) => false
                });
                __result.showAvailabilityInHoverText = false;
            }
        }
    }

    [HarmonyPatch(typeof(Capturable), nameof(Capturable.MarkForCapture), typeof(bool), typeof(PrioritySetting), typeof(bool))]
    internal static class Capturable_MarkForCapture_Patch
    {
        private static void Postfix(Capturable __instance, bool mark)
        {
            if (mark && __instance != null && __instance.IsMarkedForCapture)
            {
                Baggable baggable = __instance.GetComponent<Baggable>();
                if (baggable != null && !baggable.wrangled)
                {
                    __instance.MarkForCapture(false);
                    __instance.gameObject.AddTag(GameTags.Creatures.Deliverable);
                    baggable.SetWrangled();
                }
            }
        }
    }

    [HarmonyPatch(typeof(Game), "OnDestroy")]
    internal static class AutoRanch_Game_OnDestroy_Patch
    {
        private static void Postfix()
        {
            BaggedCreatureRegistry.Clear();
            CreatureDeliveryRegistry.Clear();
        }
    }
}
