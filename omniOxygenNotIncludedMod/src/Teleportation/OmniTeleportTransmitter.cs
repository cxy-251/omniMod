using System;
using System.Collections.Generic;
using KSerialization;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Teleportation
{
    /// <summary>
    /// 时空传送发射器组件：
    /// 将指定复制人安全跨星传送至全星系任意已建有接收器的星球或火箭内部。
    /// 内置目标星球/火箭切换按钮，支持一键呼叫就近复制人。
    /// </summary>
    public class OmniTeleportTransmitter : Workable, ISidescreenButtonControl
    {
        [Serialize]
        public int TargetWorldId = -1;

        private Assignable _assignable;
        private Chore _chore;
        private Guid _statusItemGuid = Guid.Empty;

        public bool IsOperational => GetComponent<Operational>()?.IsOperational ?? true;

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            _assignable = GetComponent<Assignable>();
            if (_assignable != null)
            {
                _assignable.OnAssign += OnAssignChanged;
            }
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            OmniTeleportRegistry.RegisterTransmitter(this);

            var kbac = GetComponent<KBatchedAnimController>();
            if (kbac != null)
            {
                kbac.Play("idle", KAnim.PlayMode.Loop);
            }

            SetWorkTime(2.5f);
            workLayer = Grid.SceneLayer.Building;
            overrideAnims = new KAnimFile[] { Assets.GetAnim("anim_interacts_warp_portal_sender_kanim") };
            workAnims = new HashedString[] { "sending_pre", "sending_loop" };
            workingPstComplete = new HashedString[] { "sending_pst" };
            workingPstFailed = new HashedString[] { "idle_loop" };
            synchronizeAnims = true;
            showProgressBar = true;

            Subscribe((int)GameHashes.RefreshUserMenu, OnRefreshUserMenu);
            UpdateStatusItem();
        }

        protected override void OnCleanUp()
        {
            CancelAssignment();
            OmniTeleportRegistry.UnregisterTransmitter(this);
            base.OnCleanUp();
        }

        private void OnAssignChanged(IAssignableIdentity newAssignee)
        {
            if (newAssignee == null)
            {
                CancelChore();
            }
            else
            {
                ActivateChore();
            }
            UpdateStatusItem();
        }

        private void ActivateChore()
        {
            CancelChore();

            _chore = new WorkChore<OmniTeleportTransmitter>(
                Db.Get().ChoreTypes.Migrate,
                this,
                null,
                run_until_complete: true,
                on_complete: OnChoreComplete,
                on_begin: OnChoreBegin,
                on_end: OnChoreEnd,
                allow_in_red_alert: true,
                schedule_block: null,
                ignore_schedule_block: true,
                only_when_operational: true,
                override_anims: Assets.GetAnim("anim_interacts_warp_portal_sender_kanim"),
                is_preemptable: false,
                allow_in_context_menu: true,
                allow_prioritization: false,
                priority_class: PriorityScreen.PriorityClass.high);

            SetWorkTime(2.5f);
        }

        private void CancelChore()
        {
            if (_chore != null)
            {
                _chore.Cancel("Assignment cancelled");
                _chore = null;
            }
        }

        public void CancelAssignment()
        {
            CancelChore();
            if (_assignable != null && _assignable.IsAssigned())
            {
                _assignable.Unassign();
            }
            var kbac = GetComponent<KBatchedAnimController>();
            if (kbac != null && !kbac.IsStopped())
            {
                kbac.Play("idle", KAnim.PlayMode.Loop);
            }
            UpdateStatusItem();
        }

        private void OnChoreBegin(Chore c)
        {
            var kbac = GetComponent<KBatchedAnimController>();
            if (kbac != null)
            {
                kbac.Play("sending_pre");
                kbac.Queue("sending_loop", KAnim.PlayMode.Loop);
            }
        }

        private void OnChoreEnd(Chore c)
        {
            var kbac = GetComponent<KBatchedAnimController>();
            if (kbac != null && !kbac.IsStopped())
            {
                kbac.Play("idle", KAnim.PlayMode.Loop);
            }
        }

        private void OnChoreComplete(Chore c)
        {
            ExecuteWarp();
        }

        public OmniTeleportReceiver GetTargetReceiver()
        {
            var receivers = OmniTeleportRegistry.GetAvailableReceivers();
            if (receivers.Count == 0) return null;

            // 如果当前设定的目标依然有效，直接使用
            if (TargetWorldId >= 0)
            {
                for (int i = 0; i < receivers.Count; i++)
                {
                    if (receivers[i].GetMyWorldId() == TargetWorldId)
                    {
                        return receivers[i];
                    }
                }
            }

            // 优先选择非本世界的第一个可用接收器
            int myWorld = this.GetMyWorldId();
            for (int i = 0; i < receivers.Count; i++)
            {
                if (receivers[i].GetMyWorldId() != myWorld)
                {
                    TargetWorldId = receivers[i].GetMyWorldId();
                    return receivers[i];
                }
            }

            // 兜底返回第一个可用接收器
            TargetWorldId = receivers[0].GetMyWorldId();
            return receivers[0];
        }

        public void CycleToNextTarget()
        {
            var receivers = OmniTeleportRegistry.GetAvailableReceivers();
            if (receivers.Count == 0)
            {
                TargetWorldId = -1;
                UpdateStatusItem();
                return;
            }

            int currentIndex = -1;
            for (int i = 0; i < receivers.Count; i++)
            {
                if (receivers[i].GetMyWorldId() == TargetWorldId)
                {
                    currentIndex = i;
                    break;
                }
            }

            int nextIndex = (currentIndex + 1) % receivers.Count;
            TargetWorldId = receivers[nextIndex].GetMyWorldId();

            KFMOD.PlayUISound(GlobalAssets.GetSound("HUD_Click"));
            UpdateStatusItem();
        }

        private void ExecuteWarp()
        {
            WorkerBase dupeWorker = base.worker;
            if (dupeWorker == null || dupeWorker.HasTag(GameTags.Dead) || dupeWorker.HasTag(GameTags.Dying))
            {
                CancelAssignment();
                return;
            }

            MinionIdentity minion = dupeWorker.GetComponent<MinionIdentity>();
            if (minion == null)
            {
                CancelAssignment();
                return;
            }

            OmniTeleportReceiver targetReceiver = GetTargetReceiver();
            if (targetReceiver == null)
            {
                PopFXManager.Instance?.SpawnFX(PopFXManager.Instance.sprite_Negative, "传送失败：全星系无可用接收器", transform);
                CancelAssignment();
                return;
            }

            int sourceWorldId = dupeWorker.GetMyWorldId();
            int destWorldId = targetReceiver.GetMyWorldId();

            try
            {
                dupeWorker.StopWork();
                targetReceiver.ReceiveDuplicant(dupeWorker);

                if (ClusterManager.Instance != null)
                {
                    ClusterManager.Instance.MigrateMinion(minion, destWorldId, sourceWorldId);
                }

                GameUtil.FocusCameraOnWorld(destWorldId, Grid.CellToPos(Grid.PosToCell(targetReceiver)));

                KFMOD.PlayUISound(GlobalAssets.GetSound("WarpPortal_Warp"));
                PopFXManager.Instance?.SpawnFX(PopFXManager.Instance.sprite_Plus, $"{minion.GetProperName()} 传送成功！", targetReceiver.transform);
                ModLog.Info($"[时空传送] 复制人 {minion.GetProperName()} 成功从世界 {sourceWorldId} 传送至世界 {destWorldId}");
            }
            catch (Exception ex)
            {
                ModLog.Error("传送执行异常", ex);
            }
            finally
            {
                CancelAssignment();
            }
        }

        private void OnRefreshUserMenu(object data)
        {
            // 按钮 1：呼叫就近复制人
            var callButton = new KIconButtonMenu.ButtonInfo(
                "action_character_select",
                "呼叫就近复制人传送",
                CallNearestDuplicant,
                Action.NumActions,
                null, null, null,
                "呼叫本星球距离最近的复制人前来此发射台进行时空传送");
            Game.Instance.userMenu.AddButton(gameObject, callButton, 1f);

            // 按钮 2：若当前有人被指定，显示取消传送按钮
            if (_assignable != null && _assignable.IsAssigned())
            {
                var cancelButton = new KIconButtonMenu.ButtonInfo(
                    "action_cancel",
                    "取消本次传送",
                    CancelAssignment,
                    Action.NumActions,
                    null, null, null,
                    "取消当前指定复制人的传送任务");
                Game.Instance.userMenu.AddButton(gameObject, cancelButton, 2f);
            }
        }

        private void CallNearestDuplicant()
        {
            int myWorld = this.GetMyWorldId();
            if (Components.LiveMinionIdentities == null || _assignable == null) return;

            List<MinionIdentity> minions = Components.LiveMinionIdentities.GetWorldItems(myWorld);
            if (minions == null || minions.Count == 0)
            {
                PopFXManager.Instance?.SpawnFX(PopFXManager.Instance.sprite_Negative, "本星球暂无可用复制人", transform);
                return;
            }

            MinionIdentity closest = null;
            int closestDist = int.MaxValue;
            int myCell = Grid.PosToCell(this);

            for (int i = 0; i < minions.Count; i++)
            {
                MinionIdentity m = minions[i];
                if (m == null || m.HasTag(GameTags.Dead) || m.HasTag(GameTags.Dying)) continue;

                int dupeCell = Grid.PosToCell(m);
                int dist = Grid.GetCellDistance(myCell, dupeCell);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = m;
                }
            }

            if (closest != null)
            {
                _assignable.Assign(closest);
                PopFXManager.Instance?.SpawnFX(PopFXManager.Instance.sprite_Plus, $"已呼叫: {closest.GetProperName()}", transform);
            }
        }

        private void UpdateStatusItem()
        {
            KSelectable selectable = GetComponent<KSelectable>();
            if (selectable == null) return;

            if (_statusItemGuid != Guid.Empty)
            {
                selectable.RemoveStatusItem(_statusItemGuid);
                _statusItemGuid = Guid.Empty;
            }

            OmniTeleportReceiver target = GetTargetReceiver();
            string title;
            string tooltip;

            if (target != null)
            {
                string targetName = OmniTeleportRegistry.GetWorldDisplayName(target.GetMyWorldId());
                title = $"传送目标: {targetName}";
                tooltip = $"复制人走上发射台后，将被量子跃迁至【{targetName}】的时空传送接收器。";
            }
            else
            {
                title = "传送网络：未发现接收器";
                tooltip = "全星系尚未检测到已建成的【时空传送接收器】。请先在目标星球或火箭内建造接收器。";
            }

            StatusItem statusItem = new StatusItem(
                "OmniTeleportTransmitterTarget",
                "BUILDING",
                "",
                StatusItem.IconType.Info,
                target != null ? NotificationType.Neutral : NotificationType.BadMinor,
                allow_multiples: false,
                OverlayModes.None.ID);

            statusItem.SetResolveStringCallback((str, data) => title);
            statusItem.resolveTooltipCallback = (str, data) => tooltip;

            _statusItemGuid = selectable.AddStatusItem(statusItem);
        }

        #region ISidescreenButtonControl

        public string SidescreenTitle => "时空传送目标";

        public int HorizontalGroupID() => -1;

        public string SidescreenButtonText
        {
            get
            {
                OmniTeleportReceiver target = GetTargetReceiver();
                if (target == null)
                {
                    return "目标: 无可用接收端 (请建造)";
                }
                return $"目标: {OmniTeleportRegistry.GetWorldDisplayName(target.GetMyWorldId())} (点击切换)";
            }
        }

        public string SidescreenButtonTooltip
        {
            get
            {
                var receivers = OmniTeleportRegistry.GetAvailableReceivers();
                if (receivers.Count == 0)
                {
                    return "全星系尚未建造或启用【时空传送接收器】。\n请在目标星球或火箭内部建造接收器后再进行传送。";
                }

                OmniTeleportReceiver current = GetTargetReceiver();
                int currentWorld = current != null ? current.GetMyWorldId() : -1;

                string tip = "点击切换传送目标星球/火箭。\n当前全星系可用接收端列表:\n";
                for (int i = 0; i < receivers.Count; i++)
                {
                    int wid = receivers[i].GetMyWorldId();
                    bool isCurrent = (wid == currentWorld);
                    tip += $"{(isCurrent ? " ▶ " : "   - ")}{OmniTeleportRegistry.GetWorldDisplayName(wid)}\n";
                }
                return tip;
            }
        }

        public bool SidescreenButtonInteractable()
        {
            return OmniTeleportRegistry.GetAvailableReceivers().Count > 0;
        }

        public void OnSidescreenButtonPressed()
        {
            CycleToNextTarget();
        }

        public int ButtonSideScreenSortOrder() => 20;

        public void SetButtonTextOverride(ButtonMenuTextOverride text) { }

        public bool SidescreenEnabled() => true;

        #endregion
    }
}
