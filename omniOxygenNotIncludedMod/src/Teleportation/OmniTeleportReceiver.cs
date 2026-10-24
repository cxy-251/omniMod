using System;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Teleportation
{
    /// <summary>
    /// 时空传送接收器组件：
    /// 接收全星系任意时空发射器传送而来的复制人，重构并释放。
    /// </summary>
    public class OmniTeleportReceiver : Workable
    {
        private Chore _arrivalChore;
        private Guid _statusItemGuid = Guid.Empty;

        public bool IsOperational => GetComponent<Operational>()?.IsOperational ?? true;

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            OmniTeleportRegistry.RegisterReceiver(this);

            var kbac = GetComponent<KBatchedAnimController>();
            if (kbac != null)
            {
                kbac.Play("idle", KAnim.PlayMode.Loop);
            }

            UpdateStatusItem();
        }

        protected override void OnCleanUp()
        {
            if (_arrivalChore != null)
            {
                _arrivalChore.Cancel("Receiver destroyed");
                _arrivalChore = null;
            }

            OmniTeleportRegistry.UnregisterReceiver(this);
            base.OnCleanUp();
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

            string title = "时空传送网络：就绪";
            string tooltip = "已连接至跨星球量子锚点，随时等待接收来自任意发射器的复制人。";

            StatusItem statusItem = new StatusItem(
                "OmniTeleportReceiverReady",
                "BUILDING",
                "",
                StatusItem.IconType.Info,
                NotificationType.Neutral,
                allow_multiples: false,
                OverlayModes.None.ID);

            statusItem.SetResolveStringCallback((str, data) => title);
            statusItem.resolveTooltipCallback = (str, data) => tooltip;

            _statusItemGuid = selectable.AddStatusItem(statusItem);
        }

        public void ReceiveDuplicant(WorkerBase dupe)
        {
            if (dupe == null || dupe.gameObject == null) return;

            try
            {
                int cell = Grid.PosToCell(this);
                Vector3 destPos = Grid.CellToPos(cell, CellAlignment.Bottom, Grid.SceneLayer.Move);
                dupe.transform.SetPosition(destPos);

                // 取消此前的到达动画任务（若有）
                if (_arrivalChore != null)
                {
                    _arrivalChore.Cancel("New arrival");
                    _arrivalChore = null;
                }

                // 播放建筑打印重组动画
                KBatchedAnimController kbac = GetComponent<KBatchedAnimController>();
                if (kbac != null)
                {
                    kbac.Play("printing_pre");
                    kbac.Queue("printing_loop");
                    kbac.Queue("printing_pst");
                    kbac.Queue("idle", KAnim.PlayMode.Loop);
                }

                // 播放复制人落地微调动画
                KAnimFile anim = Assets.GetAnim("anim_interacts_warp_portal_receiver_kanim");
                if (anim != null)
                {
                    _arrivalChore = new WorkChore<Workable>(
                        Db.Get().ChoreTypes.Migrate,
                        this,
                        dupe.GetComponent<ChoreProvider>(),
                        run_until_complete: true,
                        on_complete: OnArrivalChoreComplete,
                        on_begin: null,
                        on_end: null,
                        allow_in_red_alert: true,
                        schedule_block: null,
                        ignore_schedule_block: true,
                        only_when_operational: false,
                        override_anims: anim,
                        is_preemptable: false,
                        allow_in_context_menu: true,
                        allow_prioritization: false,
                        priority_class: PriorityScreen.PriorityClass.compulsory);

                    SetWorkTime(1.5f);
                    workLayer = Grid.SceneLayer.Building;
                    workAnims = new HashedString[] { "printing_pre", "printing_loop" };
                    workingPstComplete = new HashedString[] { "printing_pst" };
                    workingPstFailed = new HashedString[] { "printing_pst" };
                    synchronizeAnims = true;
                }

                KFMOD.PlayUISound(GlobalAssets.GetSound("WarpPortal_Receiver"));
            }
            catch (Exception ex)
            {
                ModLog.Error("接收复制人异常", ex);
            }
        }

        private void OnArrivalChoreComplete(Chore c)
        {
            if (_arrivalChore != null)
            {
                _arrivalChore.Cleanup();
                _arrivalChore = null;
            }

            var kbac = GetComponent<KBatchedAnimController>();
            if (kbac != null && !kbac.IsStopped())
            {
                kbac.Play("idle", KAnim.PlayMode.Loop);
            }
        }
    }
}
