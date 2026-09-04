using HarmonyLib;

namespace OmniMod.Pool
{
    /// <summary>
    /// 把 <see cref="OmniPool"/> 组件挂到 SaveGame 的 GameObject 上。
    ///
    /// SaveGame 是每个殖民地存档里都有、且会被序列化的核心对象（香草自己也在这里
    /// AddComponent 了 EntombedItemManager、WorldGenSpawner）。它带 SaveLoadRoot，
    /// 存档时会遍历其上所有 KMonoBehaviour 逐个序列化——我们的 OmniPool 带 [Serialize]
    /// 字段，于是杂物池内容自动随存档保存。
    ///
    /// 补丁在 OnPrefabInit 之后运行；读档时 SaveLoadRoot 先实例化预制体（触发 OnPrefabInit
    /// → 本补丁挂上 OmniPool），再反序列化各组件数据，所以顺序是对的。
    /// </summary>
    [HarmonyPatch(typeof(SaveGame), "OnPrefabInit")]
    internal static class SaveGame_OnPrefabInit_Patch
    {
        private static void Postfix(SaveGame __instance)
        {
            __instance.gameObject.AddOrGet<OmniPool>();
        }
    }
}
