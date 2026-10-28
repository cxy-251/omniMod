using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using KSerialization;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Conduits
{
    /// <summary>
    /// 全局管道流体守护者（ConduitGuardian）：
    /// 解决 ONI 管道在读档、重载、崩溃恢复或复杂网络切换时流体莫名消失/排空的顽疾。
    /// 1. 存盘拦截：在每次游戏存档时（手动存盘、周期自动存盘、崩溃恢复存盘），自动对全星系所有管道内流体（水/气体）进行像素级快照；
    /// 2. 读档守护校验：游戏重载或重启进入世界后，校验所有管道格。一旦发现原先有流体的管道格变为空管（被原版吞包、重构丢包或异常排空），立即自动守护补回流体；
    /// 3. 相变防爆锁死：协同 AntiBurst 补丁，确保管道流体温度恒定处于安全相态区间，永不相变爆管，永不因凝固/沸腾导致流体外泄被吞。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class ConduitGuardian : KMonoBehaviour, ISaveLoadable, ISim1000ms
    {
        public static ConduitGuardian Instance { get; private set; }

        [Serialize] private List<int> _liquidCells = new List<int>();
        [Serialize] private List<int> _liquidElements = new List<int>();
        [Serialize] private List<float> _liquidMasses = new List<float>();
        [Serialize] private List<float> _liquidTemps = new List<float>();
        [Serialize] private List<byte> _liquidDiseaseIdxs = new List<byte>();
        [Serialize] private List<int> _liquidDiseaseCounts = new List<int>();

        [Serialize] private List<int> _gasCells = new List<int>();
        [Serialize] private List<int> _gasElements = new List<int>();
        [Serialize] private List<float> _gasMasses = new List<float>();
        [Serialize] private List<float> _gasTemps = new List<float>();
        [Serialize] private List<byte> _gasDiseaseIdxs = new List<byte>();
        [Serialize] private List<int> _gasDiseaseCounts = new List<int>();

        private int _initialVerifyTicks = 0;
        private const int MaxInitialVerifyTicks = 5;
        private float _periodicSnapshotTimer = 0f;

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Instance = this;
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            Instance = this;

            if (Game.Instance != null)
            {
                Game.Instance.OnSave += OnGameSave;

                if (Game.Instance.liquidConduitFlow != null)
                {
                    Game.Instance.liquidConduitFlow.onConduitsRebuilt += OnConduitsRebuilt;
                }
                if (Game.Instance.gasConduitFlow != null)
                {
                    Game.Instance.gasConduitFlow.onConduitsRebuilt += OnConduitsRebuilt;
                }
            }

            // 读档后第 0 帧立即执行初次管道流体核验与修复
            RestoreSnapshots();
        }

        protected override void OnCleanUp()
        {
            if (Game.Instance != null)
            {
                Game.Instance.OnSave -= OnGameSave;

                if (Game.Instance.liquidConduitFlow != null)
                {
                    Game.Instance.liquidConduitFlow.onConduitsRebuilt -= OnConduitsRebuilt;
                }
                if (Game.Instance.gasConduitFlow != null)
                {
                    Game.Instance.gasConduitFlow.onConduitsRebuilt -= OnConduitsRebuilt;
                }
            }

            if (Instance == this)
            {
                Instance = null;
            }

            base.OnCleanUp();
        }

        public void Sim1000ms(float dt)
        {
            // 读档前 5 秒内持续守护，防止部分建筑/电网/管道网在跨帧异步重建时吞掉流体包
            if (_initialVerifyTicks < MaxInitialVerifyTicks)
            {
                _initialVerifyTicks++;
                RestoreSnapshots();
            }

            // 每隔 8 秒对运行中管道进行一次静默内存快照，确保突发崩溃或自动存盘时快照始终最新
            _periodicSnapshotTimer += dt;
            if (_periodicSnapshotTimer >= 8f)
            {
                _periodicSnapshotTimer = 0f;
                CaptureSnapshots();
            }
        }

        private void OnConduitsRebuilt()
        {
            // 管道网重构（如玩家接驳管道或拆改管网）完成时触发一次流体核验
            if (_initialVerifyTicks <= MaxInitialVerifyTicks)
            {
                RestoreSnapshots();
            }
        }

        [OnSerializing]
        private void OnSerializing()
        {
            CaptureSnapshots();
        }

        private void OnGameSave(Game.GameSaveData data)
        {
            CaptureSnapshots();
        }

        /// <summary>
        /// 全量抓取星系所有液体/气体管道内的流体快照。
        /// </summary>
        public void CaptureSnapshots()
        {
            try
            {
                if (Game.Instance == null) return;

                CaptureFlow(
                    Game.Instance.liquidConduitFlow,
                    _liquidCells,
                    _liquidElements,
                    _liquidMasses,
                    _liquidTemps,
                    _liquidDiseaseIdxs,
                    _liquidDiseaseCounts);

                CaptureFlow(
                    Game.Instance.gasConduitFlow,
                    _gasCells,
                    _gasElements,
                    _gasMasses,
                    _gasTemps,
                    _gasDiseaseIdxs,
                    _gasDiseaseCounts);
            }
            catch (Exception ex)
            {
                ModLog.Error($"[ConduitGuardian] 管道流体快照抓取异常: {ex}");
            }
        }

        private void CaptureFlow(
            ConduitFlow flow,
            List<int> cells,
            List<int> elements,
            List<float> masses,
            List<float> temps,
            List<byte> diseaseIdxs,
            List<int> diseaseCounts)
        {
            if (flow == null || flow.soaInfo == null) return;

            cells.Clear();
            elements.Clear();
            masses.Clear();
            temps.Clear();
            diseaseIdxs.Clear();
            diseaseCounts.Clear();

            int num = flow.soaInfo.NumEntries;
            for (int i = 0; i < num; i++)
            {
                ConduitFlow.Conduit conduit = flow.soaInfo.GetConduit(i);
                ConduitFlow.ConduitContents contents = conduit.GetContents(flow);
                if (contents.mass > 0.001f && contents.element != SimHashes.Void)
                {
                    int cell = flow.soaInfo.GetCell(conduit.idx);
                    if (Grid.IsValidCell(cell))
                    {
                        cells.Add(cell);
                        elements.Add((int)contents.element);
                        masses.Add(contents.mass);
                        temps.Add(contents.temperature);
                        diseaseIdxs.Add(contents.diseaseIdx);
                        diseaseCounts.Add(contents.diseaseCount);
                    }
                }
            }
        }

        /// <summary>
        /// 校验并守护恢复所有异常排空的管道流体包。
        /// </summary>
        public void RestoreSnapshots()
        {
            try
            {
                if (Game.Instance == null) return;

                RestoreFlow(
                    Game.Instance.liquidConduitFlow,
                    _liquidCells,
                    _liquidElements,
                    _liquidMasses,
                    _liquidTemps,
                    _liquidDiseaseIdxs,
                    _liquidDiseaseCounts,
                    isLiquid: true);

                RestoreFlow(
                    Game.Instance.gasConduitFlow,
                    _gasCells,
                    _gasElements,
                    _gasMasses,
                    _gasTemps,
                    _gasDiseaseIdxs,
                    _gasDiseaseCounts,
                    isLiquid: false);
            }
            catch (Exception ex)
            {
                ModLog.Error($"[ConduitGuardian] 管道流体守护恢复校验异常: {ex}");
            }
        }

        private void RestoreFlow(
            ConduitFlow flow,
            List<int> cells,
            List<int> elements,
            List<float> masses,
            List<float> temps,
            List<byte> diseaseIdxs,
            List<int> diseaseCounts,
            bool isLiquid)
        {
            if (flow == null || cells == null || cells.Count == 0) return;

            int restoredCount = 0;
            float restoredMass = 0f;

            for (int i = 0; i < cells.Count; i++)
            {
                int cell = cells[i];
                if (!Grid.IsValidCell(cell)) continue;

                // 只有当前格子存在对应类型的管道实体时才处理
                if (!flow.HasConduit(cell)) continue;

                ConduitFlow.ConduitContents current = flow.GetContents(cell);

                // 若当前管道格为空，但快照记录原本存有流体：执行安全补回
                if (current.mass <= 0.001f || current.element == SimHashes.Void)
                {
                    SimHashes elemHash = (SimHashes)elements[i];
                    float mass = masses[i];
                    float temp = temps[i];
                    byte disease = diseaseIdxs[i];
                    int diseaseCount = diseaseCounts[i];

                    Element element = ElementLoader.FindElementByHash(elemHash);
                    if (element != null && mass > 0.001f)
                    {
                        // 智能安全温度约束：避免在读档时流体因处于相变边缘而立即爆管
                        if (isLiquid && element.IsLiquid)
                        {
                            temp = Mathf.Clamp(temp, element.lowTemp + 2.0f, element.highTemp - 2.0f);
                        }
                        else if (!isLiquid && element.IsGas)
                        {
                            temp = Mathf.Clamp(temp, element.lowTemp + 2.0f, 9999f);
                        }

                        ConduitFlow.ConduitContents packet = new ConduitFlow.ConduitContents(
                            elemHash, mass, temp, disease, diseaseCount
                        );

                        flow.SetContents(cell, packet);
                        restoredCount++;
                        restoredMass += mass;
                    }
                }
            }

            if (restoredCount > 0)
            {
                string typeStr = isLiquid ? "液体" : "气体";
                ModLog.Warn($"[ConduitGuardian] 管道守护盾已生效：检测到读档后管道{typeStr}异常排空，已自动补回 {restoredCount} 格管道流体（共计 {restoredMass:0.0} kg），流体无损！");
            }
        }
    }
}
