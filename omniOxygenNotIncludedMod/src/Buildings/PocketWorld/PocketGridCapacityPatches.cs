using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OmniMod.Core.Logging;
using ProcGen;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>旧存档高度兼容：核对游戏的原始数组长度，准备全部替换缓冲后才提交元数据。</summary>
    [HarmonyPatch(typeof(KSerialization.Deserializer), "Deserialize", new[] { typeof(object) })]
    public static class SaveFileRoot_Deserialize_HeightExpander_Patch
    {
        // SaveFileRoot 在游戏中为 internal 类型，缓存字段信息，避免每次反序列化都构造 Traverse。
        private static readonly Type RootType = AccessTools.TypeByName("Klei.SaveFileRoot");
        private static readonly FieldInfo WidthField = RootType != null ? AccessTools.Field(RootType, "WidthInCells") : null;
        private static readonly FieldInfo HeightField = RootType != null ? AccessTools.Field(RootType, "HeightInCells") : null;
        private static readonly FieldInfo StreamedField = RootType != null ? AccessTools.Field(RootType, "streamed") : null;

        public static void Postfix(object obj)
        {
            if (obj == null || obj.GetType() != RootType || WidthField == null || HeightField == null || StreamedField == null) return;
            try
            {
                int width = (int)WidthField.GetValue(obj);
                int height = (int)HeightField.GetValue(obj);
                if (height <= 0 || height >= OmniPocketSpaceManager.TargetGridHeight || width <= 0) return;
                int oldCount = checked(width * height);
                int newCount = checked(width * OmniPocketSpaceManager.TargetGridHeight);
                var source = (Dictionary<string, byte[]>)StreamedField.GetValue(obj);
                if (source == null || !source.TryGetValue("GridVisible", out byte[] visible) || visible == null ||
                    visible.Length != oldCount || !source.TryGetValue("GridDamage", out byte[] damage) ||
                    damage == null || damage.Length != checked(oldCount * sizeof(float)))
                {
                    ModLog.Warn("[生态舱] 存档网格缓冲与尺寸不匹配，保留原高度。");
                    return;
                }
                bool hasSpawnable = source.TryGetValue("GridSpawnable", out byte[] spawnable);
                if (hasSpawnable && (spawnable == null || spawnable.Length != oldCount))
                {
                    ModLog.Warn("[生态舱] 存档生成标记缓冲长度异常，保留原高度。");
                    return;
                }
                var prepared = new Dictionary<string, byte[]>(source)
                {
                    ["GridVisible"] = Extend(visible, newCount),
                    ["GridDamage"] = Extend(damage, checked(newCount * sizeof(float)))
                };
                if (hasSpawnable) prepared["GridSpawnable"] = Extend(spawnable, newCount);
                // SaveLoader 将这三项直接用作 Grid 数组。Sim、Camera 等其它数据保持原引用。
                // 分配及校验全部完成后再修改根对象，避免异常留下只改了一半的尺寸和缓冲。
                StreamedField.SetValue(obj, prepared);
                try { HeightField.SetValue(obj, OmniPocketSpaceManager.TargetGridHeight); }
                catch
                {
                    StreamedField.SetValue(obj, source);
                    throw;
                }
                ModLog.Info($"[生态舱] 已准备旧存档网格高度扩展：{height} → {OmniPocketSpaceManager.TargetGridHeight}，宽度保持 {width}。");
            }
            catch (Exception ex) { ModLog.Warn("[生态舱] 网格高度扩展准备失败，保留原数据：" + ex.Message); }
        }

        private static byte[] Extend(byte[] source, int size)
        {
            var result = new byte[size];
            Array.Copy(source, result, source.Length);
            return result;
        }
    }

    /// <summary>
    /// 新游戏开局星系高度预先扩充补丁：确保未来开新档时同样拥有 384 高度充足空间。
    /// </summary>
    [HarmonyPatch(typeof(BestFit), nameof(BestFit.BestFitWorlds))]
    public static class BestFit_BestFitWorlds_Patch
    {
        public static void Postfix(ref Vector2I __result)
        {
            if (__result.y < OmniPocketSpaceManager.TargetGridHeight)
            {
                __result.y = OmniPocketSpaceManager.TargetGridHeight;
            }
        }
    }

    /// <summary>
    /// 全局虚拟管网（电网/气网/液网）安全屏障：
    /// 拦截所有 AddToVirtualNetworks(null, ...) 调用，防止原版 Dictionary.ContainsKey(null) 抛出 ArgumentNullException 破坏游戏！
    /// </summary>
    [HarmonyPatch]
    public static class UtilityNetworkManager_AddToVirtualNetworks_SafePatch
    {
        private static MethodBase TargetMethod()
        {
            try
            {
                Type mgrType = typeof(Game).Assembly.GetType("UtilityNetworkManager`2")
                    ?.MakeGenericType(typeof(ElectricalUtilityNetwork), typeof(Wire));
                return mgrType?.GetMethod("AddToVirtualNetworks", BindingFlags.Public | BindingFlags.Instance);
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[CrashFix] 反射获取 AddToVirtualNetworks 方法异常: {ex.Message}");
                return null;
            }
        }

        public static bool Prefix(object key)
        {
            if (key == null)
            {
                ModLog.Warn("[CrashFix] 拦截到 null key 注册虚拟电网请求，成功阻断 ArgumentNullException 崩溃。");
                return false;
            }
            return true;
        }
    }
}
