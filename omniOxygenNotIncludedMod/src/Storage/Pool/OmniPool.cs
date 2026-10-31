using System.Collections.Generic;
using System.Runtime.Serialization;
using KSerialization;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Pool
{
    /// <summary>
    /// 全星系共享的「杂物池」。
    ///
    /// 运行时按"元素 → 温度档（1°C 一档）→ (质量, Σ质量×温度)"聚合散装固体元素，不保留物品
    /// GameObject。不同温度的同种材料分档存放、互不混合：什么温度进去，就（在 1°C 以内）
    /// 什么温度出来。取出时每次只从一个温度档里拿。
    ///
    /// 持久化：本组件挂在 SaveGame 的 GameObject 上（SaveLoadRoot），存档时逐个序列化其
    /// KMonoBehaviour。序列化用平行 List&lt;基元&gt;（和香草 EntombedItemManager 一个套路），
    /// [OnSerializing] 时从运行时结构灌进 List，[OnDeserialized] 时反过来重建。
    /// 旧存档只有按元素汇总的三个列表（_sElementIds/_sMasses/_sTempMasses），读档时每种元素
    /// 迁移成一个温度档；存档时这三个汇总列表也照写，旧版本 mod 读新存档不会丢料。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class OmniPool : KMonoBehaviour, ISim1000ms
    {
        public static OmniPool Instance { get; private set; }

        // —— 旧格式（按元素汇总）：读旧档 + 继续写一份兼容 ——
        [Serialize] private List<int> _sElementIds = new List<int>();
        [Serialize] private List<float> _sMasses = new List<float>();
        [Serialize] private List<float> _sTempMasses = new List<float>();

        // —— 新格式（按元素 + 温度档）——
        [Serialize] private List<int> _sBucketElementIds = new List<int>();
        [Serialize] private List<int> _sBucketKeys = new List<int>();
        [Serialize] private List<float> _sBucketMasses = new List<float>();
        [Serialize] private List<float> _sBucketTempMasses = new List<float>();

        // —— 种子全局池序列化 ——
        [Serialize] private List<string> _sSeedTags = new List<string>();
        [Serialize] private List<int> _sSeedCounts = new List<int>();

        private sealed class Bucket
        {
            public float Mass;
            public float TempMass;
            public float Temperature => Mass > 0f ? TempMass / Mass : DefaultTemperatureK;
        }

        // —— 运行时结构 ——
        private readonly Dictionary<int, Dictionary<int, Bucket>> _buckets = new Dictionary<int, Dictionary<int, Bucket>>();
        private readonly Dictionary<int, float> _mass = new Dictionary<int, float>();   // 每种元素总质量（快查）
        private readonly Dictionary<Tag, int> _seeds = new Dictionary<Tag, int>();

        private const float DefaultTemperatureK = 293.15f;
        private const float LogIntervalSeconds = 60f;
        private float _logTimer;

        private static int BucketKey(float temperatureK) => Mathf.RoundToInt(temperatureK);

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Instance = this;
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            Instance = this;
            if (_mass.Count == 0 && (_sBucketElementIds.Count > 0 || _sElementIds.Count > 0))
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
            _sBucketElementIds.Clear();
            _sBucketKeys.Clear();
            _sBucketMasses.Clear();
            _sBucketTempMasses.Clear();
            foreach (KeyValuePair<int, Dictionary<int, Bucket>> el in _buckets)
            {
                float m = 0f, tm = 0f;
                foreach (KeyValuePair<int, Bucket> b in el.Value)
                {
                    if (b.Value.Mass <= 0f)
                    {
                        continue;
                    }
                    _sBucketElementIds.Add(el.Key);
                    _sBucketKeys.Add(b.Key);
                    _sBucketMasses.Add(b.Value.Mass);
                    _sBucketTempMasses.Add(b.Value.TempMass);
                    m += b.Value.Mass;
                    tm += b.Value.TempMass;
                }
                if (m > 0f)
                {
                    _sElementIds.Add(el.Key);
                    _sMasses.Add(m);
                    _sTempMasses.Add(tm);
                }
            }

            _sSeedTags.Clear();
            _sSeedCounts.Clear();
            foreach (KeyValuePair<Tag, int> kv in _seeds)
            {
                if (kv.Value > 0)
                {
                    _sSeedTags.Add(kv.Key.Name);
                    _sSeedCounts.Add(kv.Value);
                }
            }
        }

        [OnDeserialized]
        private void OnDeserialized()
        {
            RebuildFromSerialized();
        }

        private void RebuildFromSerialized()
        {
            _buckets.Clear();
            _mass.Clear();
            int nb = Mathf.Min(Mathf.Min(_sBucketElementIds.Count, _sBucketKeys.Count),
                               Mathf.Min(_sBucketMasses.Count, _sBucketTempMasses.Count));
            if (nb > 0)
            {
                for (int i = 0; i < nb; i++)
                {
                    AddRaw(_sBucketElementIds[i], _sBucketKeys[i], _sBucketMasses[i], _sBucketTempMasses[i]);
                }
            }
            else
            {
                // 旧档：每种元素一个平均温度 → 迁移成一个温度档
                int n = Mathf.Min(_sElementIds.Count, Mathf.Min(_sMasses.Count, _sTempMasses.Count));
                for (int i = 0; i < n; i++)
                {
                    float m = _sMasses[i];
                    if (m <= 0f)
                    {
                        continue;
                    }
                    AddRaw(_sElementIds[i], BucketKey(_sTempMasses[i] / m), m, _sTempMasses[i]);
                }
            }

            _seeds.Clear();
            int nSeeds = Mathf.Min(_sSeedTags.Count, _sSeedCounts.Count);
            for (int i = 0; i < nSeeds; i++)
            {
                if (_sSeedCounts[i] > 0)
                {
                    _seeds[new Tag(_sSeedTags[i])] = _sSeedCounts[i];
                }
            }

            ModLog.Info($"杂物池读档重建: {_mass.Count} 种元素, {_seeds.Count} 种种子");
        }

        public void AddSeed(Tag tag, int count = 1)
        {
            if (!tag.IsValid || count <= 0) return;
            _seeds[tag] = _seeds.TryGetValue(tag, out int c) ? c + count : count;
            OmniMod.Logistics.RemoteFetch.SolidTransferArm_FindFetchTarget_Patch.InvalidateFailedCache();
        }

        public bool RemoveSeed(Tag tag, int count = 1)
        {
            if (!tag.IsValid || count <= 0) return false;
            if (_seeds.TryGetValue(tag, out int c) && c >= count)
            {
                if (c == count)
                {
                    _seeds.Remove(tag);
                }
                else
                {
                    _seeds[tag] = c - count;
                }
                return true;
            }
            return false;
        }

        public int GetSeedCount(Tag tag)
        {
            if (!tag.IsValid) return 0;
            if (_seeds.TryGetValue(tag, out int c)) return c;

            // 若请求的是分类大标签（如 GameTags.Seed / GameTags.CropSeed），汇总所有种子总数
            if (tag == GameTags.Seed || tag == GameTags.CropSeed)
            {
                int total = 0;
                foreach (KeyValuePair<Tag, int> pair in _seeds)
                {
                    total += pair.Value;
                }
                return total;
            }

            int matching = 0;
            foreach (KeyValuePair<Tag, int> pair in _seeds)
            {
                if (pair.Value <= 0) continue;
                GameObject prefab = Assets.TryGetPrefab(pair.Key);
                KPrefabID id = prefab != null ? prefab.GetComponent<KPrefabID>() : null;
                if (id != null && id.HasTag(tag))
                {
                    matching += pair.Value;
                }
            }
            return matching;
        }

        public IEnumerable<KeyValuePair<Tag, int>> EnumerateSeeds()
        {
            return _seeds;
        }

        private void AddRaw(int element, int key, float mass, float tempMass)
        {
            if (mass <= 0f)
            {
                return;
            }
            if (!_buckets.TryGetValue(element, out Dictionary<int, Bucket> byTemp))
            {
                byTemp = new Dictionary<int, Bucket>();
                _buckets[element] = byTemp;
            }
            if (!byTemp.TryGetValue(key, out Bucket b))
            {
                b = new Bucket();
                byTemp[key] = b;
            }
            b.Mass += mass;
            b.TempMass += tempMass;
            _mass.TryGetValue(element, out float total);
            _mass[element] = total + mass;
            _massVersion++;
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
            int buckets = 0;
            foreach (KeyValuePair<int, float> kv in _mass)
            {
                total += kv.Value;
            }
            foreach (Dictionary<int, Bucket> byTemp in _buckets.Values)
            {
                buckets += byTemp.Count;
            }
            ModLog.Info($"杂物池: {_mass.Count} 种元素 / {buckets} 个温度档, 合计 {total:0.#} kg");
        }

        /// <summary>把一份固体元素并入杂物池（按温度分档，不和别的温度混合）。</summary>
        public void AddSolid(SimHashes element, float massKg, float temperatureK)
        {
            if (massKg <= 0f)
            {
                return;
            }
            AddRaw((int)element, BucketKey(temperatureK), massKg, massKg * temperatureK);
            OmniMod.Logistics.RemoteFetch.SolidTransferArm_FindFetchTarget_Patch.InvalidateFailedCache();
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

        private int _massVersion = 0;
        private int _categoryCacheVersion = -1;
        private readonly Dictionary<Tag, float> _categoryMassCache = new Dictionary<Tag, float>();
        private static readonly HashSet<Tag> _knownNonElementTags = new HashSet<Tag>();

        /// <summary>按 tag 查池中质量，支持类别 tag（如 "Metal"）——池里所有带该 tag 的元素质量相加。带按版本失效的 O(1) 字典缓存与非元素 Tag 快速跳过。</summary>
        public float GetMassMatchingTag(Tag tag)
        {
            float exact = GetMassForTag(tag);
            if (exact > 0f)
            {
                return exact;
            }

            // 离散物品（种子、蛋、装备、神器等）绝非 SimHashes 元素，快速跳过，避免无效遍历元素列表
            if (_knownNonElementTags.Contains(tag))
            {
                return 0f;
            }

            if (_categoryCacheVersion == _massVersion && _categoryMassCache.TryGetValue(tag, out float cached))
            {
                return cached;
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

            // 若整个游戏的元素库都不包含该 Tag，标记为已知非元素 Tag，后续直接 O(1) 返回 0
            if (sum == 0f && ElementLoader.elements != null)
            {
                bool isAnyElementTag = false;
                for (int i = 0; i < ElementLoader.elements.Count; i++)
                {
                    Element el = ElementLoader.elements[i];
                    if (el != null && (el.tag == tag || el.HasTag(tag)))
                    {
                        isAnyElementTag = true;
                        break;
                    }
                }
                if (!isAnyElementTag)
                {
                    _knownNonElementTags.Add(tag);
                }
            }

            if (_categoryCacheVersion != _massVersion)
            {
                _categoryMassCache.Clear();
                _categoryCacheVersion = _massVersion;
            }
            _categoryMassCache[tag] = sum;
            return sum;
        }

        /// <summary>某元素所有温度档的质量加权平均温度（只用于显示/统计）。</summary>
        public float GetTemperature(SimHashes element)
        {
            if (!_buckets.TryGetValue((int)element, out Dictionary<int, Bucket> byTemp))
            {
                return DefaultTemperatureK;
            }
            float m = 0f, tm = 0f;
            foreach (Bucket b in byTemp.Values)
            {
                m += b.Mass;
                tm += b.TempMass;
            }
            return m > 0f ? tm / m : DefaultTemperatureK;
        }

        /// <summary>
        /// 从杂物池取出至多 massKg 的某元素——只从质量最大的那一个温度档里拿，不跨档混合；
        /// 所以返回量可能少于请求量。返回实际取出量，out 该档温度。
        /// </summary>
        public float RemoveSolid(SimHashes element, float massKg, out float temperatureK)
        {
            temperatureK = DefaultTemperatureK;
            if (!_buckets.TryGetValue((int)element, out Dictionary<int, Bucket> byTemp))
            {
                return 0f;
            }
            int bestKey = 0;
            float bestMass = 0f;
            foreach (KeyValuePair<int, Bucket> kv in byTemp)
            {
                if (kv.Value.Mass > bestMass)
                {
                    bestMass = kv.Value.Mass;
                    bestKey = kv.Key;
                }
            }
            if (bestMass <= 0f)
            {
                return 0f;
            }
            return TakeFromBucket((int)element, byTemp, bestKey, massKg, out temperatureK);
        }

        /// <summary>只从和 temperatureK 同一个温度档（1°C）里取；该档没有就返回 0。</summary>
        public float RemoveSolidNear(SimHashes element, float temperatureK, float massKg, out float outTemperatureK)
        {
            outTemperatureK = temperatureK;
            if (!_buckets.TryGetValue((int)element, out Dictionary<int, Bucket> byTemp))
            {
                return 0f;
            }
            int key = BucketKey(temperatureK);
            if (!byTemp.ContainsKey(key))
            {
                return 0f;
            }
            return TakeFromBucket((int)element, byTemp, key, massKg, out outTemperatureK);
        }

        private float TakeFromBucket(int element, Dictionary<int, Bucket> byTemp, int key, float massKg, out float temperatureK)
        {
            Bucket b = byTemp[key];
            temperatureK = b.Temperature;
            float take = Mathf.Min(massKg, b.Mass);
            if (take <= 0f)
            {
                return 0f;
            }
            float remain = b.Mass - take;
            if (remain <= 1e-7f)
            {
                byTemp.Remove(key);
                take = b.Mass;
            }
            else
            {
                b.Mass = remain;
                b.TempMass = remain * temperatureK;
            }
            if (byTemp.Count == 0)
            {
                _buckets.Remove(element);
                _mass.Remove(element);
            }
            else
            {
                _mass.TryGetValue(element, out float total);
                _mass[element] = Mathf.Max(0f, total - take);
            }
            _massVersion++;
            return take;
        }

        /// <summary>遍历杂物池内容：(元素, 总质量kg, 默认温度K)。如需精确质量加权温度可调用 GetTemperature。</summary>
        public IEnumerable<PoolLine> EnumerateJunk()
        {
            foreach (KeyValuePair<int, float> kv in _mass)
            {
                if (kv.Value <= 0f)
                {
                    continue;
                }
                var element = (SimHashes)kv.Key;
                yield return new PoolLine(element, kv.Value, DefaultTemperatureK);
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
