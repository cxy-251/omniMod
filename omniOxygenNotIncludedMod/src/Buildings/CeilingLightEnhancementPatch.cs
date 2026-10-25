using HarmonyLib;
using TUNING;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 将原版非背景灯（挂顶灯 CeilingLight、落地灯 FloorLamp）升级为与全能背景灯一致的强力全向圆形光照：
    /// 1. 光形改为 360° 全向圆形（LightShape.Circle），彻底消除原版窄扇形死角；
    /// 2. 默认提供 12,000 Lux 糖心树强光（完美满足《冰封星球》糖心树 ≥ 10,000 Lux 的生长与产蜜照度需求）；
    /// 3. 挂载 BackgroundLightController，支持侧边栏一键切换【12,000 Lux 糖心树强光】、【40,000 Lux 超日照耀斑】、【1,800 Lux 柔和补光】；
    /// 4. 挂载 CopyBuildingSettings，支持批量拖选复制灯光模式；
    /// 5. 挂顶灯建造规则改为 Anywhere，不再强制要求顶部必须有实心天花板，在室内、旷野农场、树间均可作为前景建筑自由建造。
    /// </summary>
    [HarmonyPatch(typeof(CeilingLightConfig), nameof(CeilingLightConfig.CreateBuildingDef))]
    internal static class CeilingLightConfig_CreateBuildingDef_Patch
    {
        private static void Postfix(BuildingDef __result)
        {
            if (__result != null)
            {
                // 允许挂顶灯随处建造，不再强制依赖顶部必须有实心砖天花板
                __result.BuildLocationRule = BuildLocationRule.Anywhere;
            }
        }
    }

    [HarmonyPatch(typeof(CeilingLightConfig), nameof(CeilingLightConfig.DoPostConfigurePreview))]
    internal static class CeilingLightConfig_DoPostConfigurePreview_Patch
    {
        private static void Postfix(BuildingDef def, GameObject go)
        {
            if (go == null) return;
            LightShapePreview preview = go.GetComponent<LightShapePreview>();
            if (preview != null)
            {
                preview.shape = LightShape.Circle;
                preview.radius = 8f;
                preview.lux = 12000;
                preview.offset = CellOffset.none;
            }
        }
    }

    [HarmonyPatch(typeof(CeilingLightConfig), nameof(CeilingLightConfig.DoPostConfigureComplete))]
    internal static class CeilingLightConfig_DoPostConfigureComplete_Patch
    {
        private static void Postfix(GameObject go)
        {
            if (go == null) return;

            Light2D light = go.GetComponent<Light2D>();
            if (light != null)
            {
                light.shape = LightShape.Circle;
                light.Direction = Vector2.zero;
                light.Offset = Vector2.zero;
                light.Range = 8f;
                light.Lux = 12000;
            }

            go.AddOrGet<CopyBuildingSettings>().copyGroupTag = GameTags.LightSource;
            go.AddOrGet<BackgroundLightController>();
        }
    }

    [HarmonyPatch(typeof(FloorLampConfig), nameof(FloorLampConfig.DoPostConfigurePreview))]
    internal static class FloorLampConfig_DoPostConfigurePreview_Patch
    {
        private static void Postfix(BuildingDef def, GameObject go)
        {
            if (go == null) return;
            LightShapePreview preview = go.GetComponent<LightShapePreview>();
            if (preview != null)
            {
                preview.shape = LightShape.Circle;
                preview.radius = 8f;
                preview.lux = 12000;
            }
        }
    }

    [HarmonyPatch(typeof(FloorLampConfig), nameof(FloorLampConfig.DoPostConfigureComplete))]
    internal static class FloorLampConfig_DoPostConfigureComplete_Patch
    {
        private static void Postfix(GameObject go)
        {
            if (go == null) return;

            Light2D light = go.GetComponent<Light2D>();
            if (light != null)
            {
                light.shape = LightShape.Circle;
                light.Range = 8f;
                light.Lux = 12000;
            }

            go.AddOrGet<CopyBuildingSettings>().copyGroupTag = GameTags.LightSource;
            go.AddOrGet<BackgroundLightController>();
        }
    }
}
