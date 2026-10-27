using System;
using System.Collections;
using KSerialization;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 折叠空间生态巨舱控制器。
    /// 
    /// 功能：
    /// 1. 建筑物放置在基地任何地板或水底，自动调用底层引擎生成一个 32x32（内部 28x28）的专属异次元口袋世界。
    /// 2. 自动配对舱门两端的双向导航传送（NavTeleporter），复制人与小动物可自由出入。
    /// 3. 提供侧边栏【进入舱内】快捷按钮，一键将镜头切换到舱内进行规划与建造。
    /// 4. 完全防水防淹（Floodable = false），可安全沉浸在深水区、酸液、石油底部。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class OmniPocketHabitat : KMonoBehaviour, ISaveLoadable, ISidescreenButtonControl
    {
        [Serialize]
        public int targetWorldId = -1;

#pragma warning disable CS0649
        [MyCmpReq]
        private NavTeleporter navTeleporter;
#pragma warning restore CS0649

        private ClustercraftInteriorDoor pairedInteriorDoor;

        public string SidescreenTitle => ModStrings.T("折叠空间生态巨舱", "Omni Pocket Hyper-Habitat").Resolve();
        public string SidescreenButtonText => ModStrings.T("进入舱内 (View Interior)", "View Interior").Resolve();
        public string SidescreenButtonTooltip => ModStrings.T("将镜头视角立即切换至该生态舱内部空间", "Pan camera directly into the habitat interior world").Resolve();

        public bool SidescreenEnabled() => targetWorldId >= 0;
        public bool SidescreenButtonInteractable() => targetWorldId >= 0;
        public int ButtonSideScreenSortOrder() => 20;
        public int HorizontalGroupID() => -1;
        public void SetButtonTextOverride(ButtonMenuTextOverride textOverride) { }

        public void OnSidescreenButtonPressed()
        {
            if (targetWorldId >= 0)
            {
                ClusterManager.Instance.SetActiveWorld(targetWorldId);
                ManagementMenu.Instance.CloseAll();

                // 将镜头居中到舱内门位置
                if (pairedInteriorDoor != null)
                {
                    CameraController.Instance.SetTargetPos(pairedInteriorDoor.transform.position, 8f, true);
                }
            }
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();

            if (targetWorldId < 0)
            {
                // 新建建筑：申请并构建 32x32 极限异次元口袋世界
                CreatePocketWorld();
            }
            else
            {
                // 读档还原：直接与已有世界的门重新配对
                StartCoroutine(DelayedPairDoor());
            }
        }

        private void CreatePocketWorld()
        {
            try
            {
                WorldContainer world = ClusterManager.Instance.CreateRocketInteriorWorld(
                    this.gameObject,
                    OmniPocketTemplate.TemplateName,
                    () =>
                    {
                        PairWithInteriorDoor();
                    }
                );

                if (world != null)
                {
                    targetWorldId = world.id;
                    world.SetParentIdx(ClusterUtil.GetMyWorldId(this));
                    PairWithInteriorDoor();
                    ModLog.Info($"[折叠生态舱] 成功创建 32x32 极限生态舱世界，World ID: {targetWorldId}");
                }
                else
                {
                    ModLog.Warn("[折叠生态舱] 创建内部世界失败：可能底层星图物理网格已被填满。");
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[折叠生态舱] 创建内部世界异常: {ex}");
            }
        }

        private IEnumerator DelayedPairDoor()
        {
            yield return null;
            PairWithInteriorDoor();
        }

        public void PairWithInteriorDoor()
        {
            if (targetWorldId < 0) return;

            foreach (ClustercraftInteriorDoor door in Components.ClusterCraftInteriorDoors.Items)
            {
                if (door != null && ClusterUtil.GetMyWorldId(door) == targetWorldId)
                {
                    pairedInteriorDoor = door;
                    break;
                }
            }

            if (pairedInteriorDoor != null && navTeleporter != null)
            {
                NavTeleporter interiorNav = pairedInteriorDoor.GetComponent<NavTeleporter>();
                if (interiorNav != null)
                {
                    navTeleporter.SetTarget(interiorNav);
                    navTeleporter.EnableTwoWayTarget(true);

                    interiorNav.SetTarget(navTeleporter);
                    interiorNav.EnableTwoWayTarget(true);
                }

                WorldContainer targetWorld = ClusterManager.Instance.GetWorld(targetWorldId);
                if (targetWorld != null)
                {
                    targetWorld.SetParentIdx(ClusterUtil.GetMyWorldId(this));
                }

                ModLog.Info($"[折叠生态舱] 外部舱门与内部门双向导航贯通成功 (World {targetWorldId})！");
            }
        }

        protected override void OnCleanUp()
        {
            if (targetWorldId >= 0 && ClusterManager.Instance != null)
            {
                try
                {
                    // 建筑拆除时，安全销毁对应的异次元微型世界
                    ClusterManager.Instance.DestoryRocketInteriorWorld(targetWorldId, null);
                    ModLog.Info($"[折叠生态舱] 建筑已拆除，安全销毁内部世界 (World {targetWorldId})。");
                }
                catch (Exception ex)
                {
                    ModLog.Warn($"[折叠生态舱] 销毁内部世界异常: {ex.Message}");
                }
            }

            base.OnCleanUp();
        }
    }
}
