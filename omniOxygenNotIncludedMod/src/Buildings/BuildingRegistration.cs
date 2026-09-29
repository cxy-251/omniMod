using HarmonyLib;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 建筑注册补丁。
    ///
    /// IBuildingConfig 本身会被 Mod 加载器自动发现并注册，但两件事仍需我们手动做：
    ///   1. 往字符串表写入建筑的名字/描述/效果文字；
    ///   2. 把建筑加进建造菜单（否则玩家看不到、点不出来）。
    ///
    /// GeneratedBuildings.LoadGeneratedBuildings(List&lt;Type&gt;) 是游戏遍历所有建筑配置
    /// 并逐个 RegisterBuilding 的地方。我们用 Harmony 在它执行"之前"(Prefix) 插入上述两步。
    /// </summary>
    [HarmonyPatch(typeof(GeneratedBuildings), nameof(GeneratedBuildings.LoadGeneratedBuildings))]
    internal static class GeneratedBuildings_LoadGeneratedBuildings_Patch
    {
        private static bool _done;

        private static void Prefix()
        {
            if (_done)
            {
                return;   // 防止某些 mod 管理器重复调用导致菜单里出现重复入口
            }
            _done = true;

            RegisterJunkBox();
            RegisterFoodBox();
            RegisterGasPurifiers();
            RegisterBackgroundLight();
        }

        private static void RegisterBackgroundLight()
        {
            ModStrings.AddBuilding(
                BackgroundLightConfig.ID,
                name: ModStrings.T("背景灯", "Background Light"),
                desc: ModStrings.T(
                    "退到建筑背景层、可随处建造的一盏灯。放在作物之间或作物后方给毛刺花等补光，" +
                    "不占前景空间、不挡视线。圆形光照，半径 6 格。",
                    "A lamp that sits on the background layer and can be built anywhere. Place it among or behind " +
                    "crops to light Bristle Blossoms and the like without taking up foreground space. " +
                    "Circular light, radius 6."),
                effect: ModStrings.T("为四周提供光照。", "Provides light to the surrounding area."));

            ModUtil.AddBuildingToPlanScreen(
                new HashedString("Furniture"),
                BackgroundLightConfig.ID,
                "uncategorized",
                relativeBuildingId: "CeilingLight",
                ordering: ModUtil.BuildingOrdering.After);
        }

        private static void RegisterGasPurifiers()
        {
            var list = new (string id, string zhGas, string enGas, string zhOut, string enOut, string relative)[]
            {
                (Co2PurifierConfig.ID,        "二氧化碳", "Carbon Dioxide", "煤炭",   "Coal",         "AirFilter"),
                (ChlorinePurifierConfig.ID,   "氯气",     "Chlorine",       "漂白石", "Bleach Stone", Co2PurifierConfig.ID),
                (OxygenPurifierConfig.ID,     "氧气",     "Oxygen",         "氧石",   "Oxylite",      ChlorinePurifierConfig.ID),
                (NaturalGasPurifierConfig.ID, "天然气",   "Natural Gas",    "肥料",   "Fertilizer",   OxygenPurifierConfig.ID),
                (HydrogenPurifierConfig.ID,   "氢气",     "Hydrogen",       "水",     "Water",        NaturalGasPurifierConfig.ID),
                (PollutedOxygenPurifierConfig.ID, "污氧", "Polluted Oxygen","菌泥",   "Slime",        HydrogenPurifierConfig.ID),
            };

            foreach (var p in list)
            {
                ModStrings.AddBuilding(
                    p.id,
                    name: ModStrings.T(p.zhGas + "净化器", p.enGas + " Purifier"),
                    desc: ModStrings.T(
                        $"从四周抽取{p.zhGas}，消耗滤材（沙子/浮土等）转化为{p.zhOut}。固体副产物累积到 100kg 时掉落。需要供电和持续供应滤材。",
                        $"Draws {p.enGas.ToLowerInvariant()} from the surrounding area and consumes filter material " +
                        $"(sand, regolith, etc.) to convert it into {p.enOut.ToLowerInvariant()}. " +
                        $"Solid byproduct is dropped once 100 kg accumulates. Requires power and a steady filter material supply."),
                    effect: ModStrings.T(
                        $"消耗滤材，吸收{p.zhGas}，产出{p.zhOut}。",
                        $"Consumes filter material, absorbs {p.enGas.ToLowerInvariant()}, produces {p.enOut.ToLowerInvariant()}."));

                ModUtil.AddBuildingToPlanScreen(
                    new HashedString("Oxygen"),
                    p.id,
                    "uncategorized",
                    relativeBuildingId: p.relative,
                    ordering: ModUtil.BuildingOrdering.After);
            }
        }

        private static void RegisterJunkBox()
        {
            ModStrings.AddBuilding(
                JunkBoxConfig.Id,
                name: ModStrings.T("杂物箱", "Junk Box"),
                desc: ModStrings.T(
                    "容量近乎无限的杂物聚合箱。箱内物品防腐、防挥发，并与外界温度隔离。" +
                    "建成后会自动把本星球散落在地面的非食用杂物吸进来（极端温度物品除外）。",
                    "A junk aggregation bin with near-infinite capacity. Contents are preserved, sealed against " +
                    "off-gassing, and thermally insulated. Once built, it automatically pulls in loose non-edible " +
                    "debris from across its planet (extreme-temperature items excluded)."),
                effect: ModStrings.T(
                    "自动收集并存放本星球上几乎任意数量的固体杂物，且不会腐坏或挥发。",
                    "Automatically collects and stores an almost unlimited amount of solid debris from this planet, " +
                    "without spoilage or off-gassing."));

            // 建造菜单：基础(Base) / 储物(storage)，排在"智能储物柜"之后
            ModUtil.AddBuildingToPlanScreen(
                new HashedString("Base"),
                JunkBoxConfig.Id,
                "storage",
                relativeBuildingId: "StorageLockerSmart",
                ordering: ModUtil.BuildingOrdering.After);
        }

        private static void RegisterFoodBox()
        {
            ModStrings.AddBuilding(
                FoodBoxConfig.Id,
                name: ModStrings.T("食物箱", "Food Box"),
                desc: ModStrings.T(
                    "容量近乎无限的食物聚合箱。箱内食物绝对防腐、永不变质。" +
                    "建成后会自动把本星球散落在地面的可食用物吸进来。",
                    "A food aggregation bin with near-infinite capacity. Food inside never spoils. " +
                    "Once built, it automatically pulls in loose edibles from across its planet."),
                effect: ModStrings.T(
                    "自动收集并存放本星球上几乎任意数量的食物，且绝对防腐。",
                    "Automatically collects and stores an almost unlimited amount of food from this planet, " +
                    "with absolute preservation."));

            // 建造菜单：食物(Food) / 储物(storage)，排在"迷你冰箱"之后
            ModUtil.AddBuildingToPlanScreen(
                new HashedString("Food"),
                FoodBoxConfig.Id,
                "storage",
                relativeBuildingId: "MiniFridge",
                ordering: ModUtil.BuildingOrdering.After);
        }

        // 注：阶段二仍不做科技门槛，两个箱子一开局就能造。
        // 后续加研究解锁时，在 Db.Initialize 的 Postfix 里：
        //   Db.Get().Techs.Get("SomeTechId").unlockedItemIDs.Add(JunkBoxConfig.Id);
    }
}
