using System.Collections.Generic;
using System.Runtime.Serialization;
using KSerialization;
using UnityEngine;

namespace OmniMod.Pool
{
    /// <summary>
    /// 全星系共享的「杂物池」。
    ///
    /// 运行时用两个 Dictionary（元素 → 总质量 / Σ(质量×温度)）聚合散装固体元素，
    /// 不保留物品 GameObject。平均温度 = tempMass / mass（同种杂物进池自动平衡热量）。
    ///
    /// 持久化：本组件挂在 SaveGame 的 GameObject 上（SaveLoadRoot），存档时逐个序列化其
    /// KMonoBehaviour。为稳妥，序列化用的是三个平行 List&lt;基元&gt;（和香草 EntombedItemManager
    /// 一个套路），[OnSerializing] 时从运行时 Dictionary 灌进 List，[OnDeserialized] 时反过来
    /// 重建 Dictionary。之前直接 [Serialize] Dictionary&lt;int,float&gt; 疑似读档后按键查不到。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class OmniPool : KMonoBehaviour, ISim1000ms
    {
        public static OmniPool Instance { get; private set; }

        // —— 序列化用的平行列表（元素id / 质量 / 质量×温度累加）——
        [Serialize] private List<int> _sElementIds = new List<int>();
        [Serialize] private List<float> _sMasses = new List<float>();
        [Serialize] private List<float> _sTempMasses = new List<float>();

        // —— 运行时结构 ——
        private readonly Dictionary<int, float> _mass = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _tempMass = new Dictionary<int, float>();

        private const float DefaultTemperatureK = 293.15f;
        private const float LogIntervalSeconds = 15f;
        private float _logTimer;

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Instance = this;
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            Instance = this;
            if (_mass.Count == 0 && _sElementIds.Count > 0)
            {
                RebuildFromSerialized();   // [OnDeserialized] 没跑到时的兜底
            }
        }

        protected override void OnCleanUp()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            base.OnCleanUp();
        }

        [OnSerializing]
        private void OnSerializing()
        {
            _sElementIds.Clear();
            _sMasses.Clear();
            _sTempMasses.Clear();
            foreach (KeyValuePair<int, float> kv in _mass)
            {
                if (kv.Value <= 0f)
                {
                    continue;
                }
                _tempMass.TryGetValue(kv.Key, out float tm);
                _sElementIds.Add(kv.Key);
                _sMasses.Add(kv.Value);
                _sTempMasses.Add(tm);
            }
        }

        [OnDeserialized]
        private void OnDeserialized()
        {
            RebuildFromSerialized();
        }

        private void RebuildFromSerialized()
        {
            _mass.Clear();
            _tempMass.Clear();
            int n = Mathf.Min(_sElementIds.Count, Mathf.Min(_sMasses.Count, _sTempMasses.Count));
            for (int i = 0; i < n; i++)
            {
                int id = _sElementIds[i];
                _mass[id] = _sMasses[i];
                _tempMass[id] = _sTempMasses[i];
            }
            Debug.Log($"[OmniMod] 杂物池读档重建: {_mass.Count} 种元素");
        }

        public void Sim1000ms(float dt)
        {
            _logTimer += dt;
            if (_logTimer < LogIntervalSeconds)
            {
                return;
            }
            _logTimer = 0f;

            float total = 0f;
            foreach (KeyValuePair<int, float> kv in _mass)
            {
                total += kv.Value;
            }
            Debug.Log($"[OmniMod] 杂物池: {_mass.Count} 种元素, 合计 {total:0.#} kg");
        }

        /// <summary>把一份固体元素并入杂物池。</summary>
        public void AddSolid(SimHashes element, float massKg, float temperatureK)
        {
            if (massKg <= 0f)
            {
                return;
            }

            int key = (int)element;
            _mass.TryGetValue(key, out float m);
            _tempMass.TryGetValue(key, out float tm);
            _mass[key] = m + massKg;
            _tempMass[key] = tm + massKg * temperatureK;
        }

        public float GetMass(SimHashes element)
        {
            _mass.TryGetValue((int)element, out float m);
            return m;
        }

        /// <summary>按资源 tag 查池中质量（tag 恰好是某固体元素时的快路径）。</summary>
        public float GetMassForTag(Tag tag)
        {
            Element e = ElementLoader.GetElement(tag);
            if (e == null || !e.IsSolid)
            {
                return 0f;
            }
            return GetMass(e.id);
        }

        /// <summary>按 tag 查池中质量，支持类别 tag（如 "Metal"）——池里所有带该 tag 的元素质量相加。</summary>
        public float GetMassMatchingTag(Tag tag)
        {
            float exact = GetMassForTag(tag);
            if (exact > 0f)
            {
                return exact;
            }

            float sum = 0f;
            foreach (KeyValuePair<int, float> kv in _mass)
            {
                Element e = ElementLoader.FindElementByHash((SimHashes)kv.Key);
                if (e != null && (e.tag == tag || e.HasTag(tag)))
                {
                    sum += kv.Value;
                }
            }
            return sum;
        }

        public float GetTemperature(SimHashes element)
        {
            int key = (int)element;
            _mass.TryGetValue(key, out float m);
            if (m <= 0f)
            {
                return DefaultTemperatureK;
            }
            _tempMass.TryGetValue(key, out float tm);
            return tm / m;
        }

        /// <summary>从杂物池取出至多 massKg 的某元素；返回实际取出量，out 平均温度。</summary>
        public float RemoveSolid(SimHashes element, float massKg, out float temperatureK)
        {
            int key = (int)element;
            temperatureK = GetTemperature(element);

            _mass.TryGetValue(key, out float have);
            if (have <= 0f)
            {
                return 0f;
            }

            float take = Mathf.Min(massKg, have);
            float remain = have - take;
            if (remain <= 1e-7f)
            {
                _mass.Remove(key);
                _tempMass.Remove(key);
            }
            else
            {
                _mass[key] = remain;
                _tempMass[key] = remain * temperatureK;
            }
            return take;
        }

        /// <summary>遍历杂物池内容：(元素, 质量kg, 平均温度K)。</summary>
        public IEnumerable<PoolLine> EnumerateJunk()
        {
            foreach (KeyValuePair<int, float> kv in _mass)
            {
                if (kv.Value <= 0f)
                {
                    continue;
                }
                var element = (SimHashes)kv.Key;
                yield return new PoolLine(element, kv.Value, GetTemperature(element));
            }
        }

        public int JunkElementCount => _mass.Count;

        public readonly struct PoolLine
        {
            public readonly SimHashes Element;
            public readonly float MassKg;
            public readonly float TemperatureK;

            public PoolLine(SimHashes element, float massKg, float temperatureK)
            {
                Element = element;
                MassKg = massKg;
                TemperatureK = temperatureK;
            }
        }
    }
}
