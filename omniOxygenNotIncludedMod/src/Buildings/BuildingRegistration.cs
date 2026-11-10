using HarmonyLib;
using OmniMod.Logistics.RemoteFetch;
using OmniMod.Rooms;
using OmniMod.Teleportation;

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

            SolidTransferArmTagsHelper.EnsureInitialized();

            RegisterJunkBox();
            RegisterFoodBox();
            RegisterGasPurifiers();
            RegisterBackgroundLight();
            RegisterLightEnhancements();
            RegisterRadboltGenerator();
            RegisterRadboltIrradiator();
            RegisterFluidProcessors();
            RegisterAutoIceMaker();
            RegisterLiquidChiller();
            RegisterTeleporters();
            RegisterNuclearReprocessor();
            RegisterPocketHabitat();
            RegisterExhibitionSign();
            RegisterLandmarkSigns();
            RegisterEggCrackerSafety();
            RegisterCompactLuxuryBed();
            RegisterLiquorDispenser();
        }

        private static void RegisterEggCrackerSafety()
        {
            Strings.Add("STRINGS.UI.UISIDESCREENS.OMNIMOD_EGGCRACKER.RESERVE_THRESHOLD_TITLE", "物种蛋保留阈值 (防绝种)");
            Strings.Add("STRINGS.UI.UISIDESCREENS.OMNIMOD_EGGCRACKER.RESERVE_THRESHOLD_TOOLTIP", "设定该物种蛋在全殖民地的保留数量。低于或等于此数值时打蛋机将自动停工，杜绝灭绝。");
        }

        private static void RegisterFluidProcessors()
        {
            // 生物发酵反应罐
            ModStrings.AddBuilding(
                BioFermenterConfig.ID,
                name: ModStrings.T("生物发酵反应罐", "Bio-Fermenter"),
                desc: ModStrings.T(
                    "利用工业生物发酵工艺，将高通量纯水与蔗糖（或汗甜玉米）高效发酵转化为清洁的液体乙醇，副产二氧化碳废气。专为消纳基地巨量过剩水资源而设计。",
                    "Uses industrial biological fermentation to convert high-throughput water and sucrose (or sweatcorn) into clean liquid ethanol, producing carbon dioxide gas. Designed to consume massive water surpluses."),
                effect: ModStrings.T(
                    "高通量工业生物发酵转化：默认【蔗糖催化发酵】消耗纯水 2kg/s 与蔗糖 0.5kg/s，稳定排出 2kg/s 液体乙醇与 0.5kg/s 二氧化碳废气。可在侧边栏随时一键切换为【纯水直接发酵】（无需固体料）、【汗甜玉米】或【木材水解】模式。",
                    "High-throughput biological fermentation: Defaults to [Sucrose Catalysis] consuming 2 kg/s Water and 0.5 kg/s Sucrose to yield 2 kg/s Ethanol and 0.5 kg/s CO2. Can be toggled on the sidescreen to [Water Only] (no solids required), [Sweatcorn], or [Wood Hydrolysis]."));

            ModUtil.AddBuildingToPlanScreen(
                new HashedString("Refining"),
                BioFermenterConfig.ID,
                "refining",
                relativeBuildingId: "OilRefinery",
                ordering: ModUtil.BuildingOrdering.After);

            // 水热合成油反应釜
            ModStrings.AddBuilding(
                PetroleumSynthesizerConfig.ID,
                name: ModStrings.T("水热合成油反应釜", "Petroleum Synthesizer"),
                desc: ModStrings.T(
                    "利用高温高压水热裂解与费托合成工艺，将纯水与碳源（煤炭或精炼碳）高压重整为高纯度精炼石油，极大拓展了石油制备渠道。",
                    "Uses high-temperature and high-pressure hydrothermal reforming and Fischer-Tropsch synthesis to reform water and carbon (coal or refined carbon) into high-purity petroleum."),
                effect: ModStrings.T(
                    "每秒消耗 2kg 纯水，搭配 0.5kg 煤炭（或 0.25kg 精炼碳），直接合成 2kg 精炼石油并通过管道排出。",
                    "Consumes 2 kg/s Water and 0.5 kg/s Coal (or 0.25 kg/s Refined Carbon) to produce 2 kg/s refined Petroleum via pipe."));

            ModUtil.AddBuildingToPlanScreen(
                new HashedString("Refining"),
                PetroleumSynthesizerConfig.ID,
                "refining",
                relativeBuildingId: BioFermenterConfig.ID,
                ordering: ModUtil.BuildingOrdering.After);

            // 农作物研磨机
            ModStrings.AddBuilding(
                CropMillConfig.ID,
                name: ModStrings.T("农作物研磨机", "Crop Mill"),
                desc: ModStrings.T(
                    "专门用于研磨、粉碎和精炼各类农作物的农业加工机械。破坏作物的纤维与细胞结构，高效提取结晶蔗糖等关键农业中间产物。",
                    "An agricultural processing machine specially designed to mill, crush, and refine various crops. Breaks down plant fibers and cellular structures to efficiently extract crystallized sucrose and other key agricultural byproducts."),
                effect: ModStrings.T(
                    "根据选定配方，将农作物（如汗甜玉米）研磨提取为结晶蔗糖等固体产物。全面支持自动化免人工运行。",
                    "Mills and crushes agricultural crops (such as Sweatcorn) into solid crystallized sucrose and other byproducts based on selected recipes. Fully supports automated unattended operation."));

            ModUtil.AddBuildingToPlanScreen(
                new HashedString("Refining"),
                CropMillConfig.ID,
                "refining",
                relativeBuildingId: PetroleumSynthesizerConfig.ID,
                ordering: ModUtil.BuildingOrdering.After);
        }

        private static void RegisterAutoIceMaker()
        {
            Strings.Add("STRINGS.UI.UISIDESCREENS.OMNIMOD_ICEMAKER.TARGET_TEMP_TITLE", "出料温度");
            Strings.Add("STRINGS.UI.UISIDESCREENS.OMNIMOD_ICEMAKER.TARGET_TEMP_TOOLTIP", "设定急速冷冻出料的绝对恒温（-100°C ~ -1°C，落地不融化）");

            ModStrings.AddBuilding(
                AutoIceMakerConfig.ID,
                name: ModStrings.T("全能冷冻造雪机", "Auto Cryo Snow & Ice Maker"),
                desc: ModStrings.T(
                    "采用高效压缩制冷与分子结晶技术，直接将管道通入的纯水全自动急速冷冻为低温疏松雪花或坚硬冰块。无需小人值守，不向环境外泄废热，支持自定义出料形态与结晶温度，为深冷农业（如糖心树）与极端工业提供全自动冰雪供给。",
                    "Uses cryogenic crystallization technology to instantly freeze piped pure water into snow or solid ice at a customizable sub-zero temperature. Fully automated with zero duplicant labor and negligible heat emissions."),
                effect: ModStrings.T(
                    "每秒消耗 2kg 纯水，直接在机器脚下生成自选温度（-100°C ~ -1°C）的疏松雪花或致密冰块。支持侧边栏自由切换形态、调节温度与复制设置。",
                    "Consumes 2 kg/s piped water to produce snow or ice at a chosen temperature (-100°C to -1°C) directly on the floor. Supports mode switching, temperature adjustment, and Copy Settings."));

            ModUtil.AddBuildingToPlanScreen(
                new HashedString("Refining"),
                AutoIceMakerConfig.ID,
                "refining",
                relativeBuildingId: CropMillConfig.ID,
                ordering: ModUtil.BuildingOrdering.After);
        }

        private static void RegisterLiquidChiller()
        {
            Strings.Add("STRINGS.UI.UISIDESCREENS.OMNIMOD_LIQUIDCHILLER.TARGET_TEMP_TITLE", "目标出液温度");
            Strings.Add("STRINGS.UI.UISIDESCREENS.OMNIMOD_LIQUIDCHILLER.TARGET_TEMP_TOOLTIP", "调节管道流体冷却后的目标出液温度（-200°C ~ +400°C）");
            Strings.Add("STRINGS.UI.UISIDESCREENS.OMNIMOD_LIQUIDCHILLER_REFILL.TITLE", "循环管道智能校准");
            Strings.Add("STRINGS.UI.UISIDESCREENS.OMNIMOD_LIQUIDCHILLER_FLUSH.TITLE", "循环管道除杂排空");

            ModStrings.AddBuilding(
                LiquidChillerConfig.ID,
                name: ModStrings.T("全能恒温流体冷却机", "Auto Liquid Chiller"),
                desc: ModStrings.T(
                    "采用先进的分子共振与深冷压缩热吸收技术，直接将管道通入的任意流体一次性恒温冷却至目标温度。通流即冷却，一步直达，无多余废热外泄，内置智能防爆管保护，彻底替代繁复且笨重的传统液温调节器架构。",
                    "Uses cryogenic absorption technology to instantly cool any liquid in the pipe to a user-defined target temperature in a single pass. Eliminates the need for bulky Thermo Aquatuner setups and dangerous pipe ruptures."),
                effect: ModStrings.T(
                    "最高支持 10kg/s 满管液体连续恒温处理，侧边栏自由设定目标出液温度（-200°C ~ +400°C）。内置液体冰点智能防爆管保护与一键充满超级冷却剂功能，极低能耗且支持复制设置。",
                    "Cools up to 10 kg/s of any piped liquid directly to a target temperature (-200°C to +400°C). Features automatic freeze-burst protection, one-click super coolant conduit fill, minimal heat output, and Copy Settings."));

            ModUtil.AddBuildingToPlanScreen(
                new HashedString("Utilities"),
                LiquidChillerConfig.ID,
                "temperature",
                relativeBuildingId: "LiquidConditioner",
                ordering: ModUtil.BuildingOrdering.After);
        }

        private static void RegisterRadboltGenerator()
        {
            if (!DlcManager.IsExpansion1Active()) return;

            Strings.Add("STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.PANEL_TITLE", "辐射发电机配置");
            Strings.Add("STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.CAPACITY_TITLE", "粒子存储上限");
            Strings.Add("STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.CAPACITY_TOOLTIP", "发电机实际允许储存的最大辐射粒子总量（0 ~ 4000 粒子，默认 4000 粒子）。");
            Strings.Add("STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.PASSTHROUGH_TITLE", "穿透放行比例");
            Strings.Add("STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.PASSTHROUGH_TOOLTIP", "每次射入粒子光球时，按此比例将部分粒子穿透放行给后方的发电机（0% ~ 100%，默认 50% 两台平分）。若本机已满则自动全额放行。");
            Strings.Add("STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.CONSUMPTION_RATE_TITLE", "粒子消耗速率");
            Strings.Add("STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.CONSUMPTION_RATE_TOOLTIP", "发电机满载发电时的辐射粒子消耗速率（5 ~ 100 粒子/秒，大档位步进 5）。满载持续发电 20,000 W。");

            ModStrings.AddBuilding(
                RadboltGeneratorConfig.ID,
                name: ModStrings.T("辐射粒子发电机", "Radbolt Generator"),
                desc: ModStrings.T(
                    "通过磁约束场捕获高能辐射粒子并引发受控微湮灭，将其转化为巨额电能。" +
                    "可无缝接入自动化电网，高效消纳辐射粒子产能。内置 4000 粒子海量储仓，侧边栏全面支持存储上限可调、穿透放行比例可调、消耗速率大档位可调与穿透模式切换，支持同轴串联与真实光球无损穿透传导。",
                    "Captures high-energy radiation particles in a magnetic containment field, converting them into massive electrical power through controlled annihilation. " +
                    "Features a 4000-particle buffer, customizable storage capacity, pass-through ratio, and consumption rate, with inline beam transmission to feed downstream generators."),
                effect: ModStrings.T(
                    "吸收高能辐射粒子并转化为清洁电能（满载 20kW / 20,000W）。侧边栏全面支持存储上限可调（0 ~ 4000 粒子，默认 4000）、穿透放行比例可调（0% ~ 100%，默认 50% 两台平分）、消耗速率大档位可调（5 ~ 100 粒子/秒，默认 10）以及常用模式一键切换与复制设置（C 键）。两台串联平分光球平稳跑满 40kW 电路。",
                    "Converts radbolts into clean power (up to 20 kW). Features full side screen controls for storage capacity (0 - 4000 particles, default 4000), pass-through ratio (0% - 100%, default 50% for 2-way split), consumption rate in large steps (5 - 100 radbolts/s, default 10), and quick preset toggling with Copy Settings (C key)."));

            ModUtil.AddBuildingToPlanScreen(
                new HashedString("Power"),
                RadboltGeneratorConfig.ID,
                "generators",
                relativeBuildingId: "HydrogenGenerator",
                ordering: ModUtil.BuildingOrdering.After);
        }

        private static void RegisterRadboltIrradiator()
        {
            if (!DlcManager.IsExpansion1Active()) return;
            ModStrings.AddBuilding(RadboltIrradiatorConfig.ID,
                ModStrings.T("横向辐照器", "Horizontal Radbolt Irradiator"),
                ModStrings.T("占地 1×1 的普通建筑，无需电力。运送一定量浓缩铀（10 kg）作为反应核心后即可永久工作，提供均匀横向窄带辐射，适合已经建成的长条农场。",
                    "A normal 1×1 building that needs no electricity. Once fueled with a one-time delivery of Enriched Uranium (10 kg), it runs permanently without running consumption, providing uniform horizontal radiation for long farms."),
                ModStrings.T("运送 10 kg 浓缩铀后成为永久反应核心，无需持续补充燃料。默认向右延伸 30 格、高 5 格，高度和上下偏移可调，到世界边界缩短。工作时矩形区域每格辐射均为设定值，无距离、遮挡及四角衰减，重叠取较高值。支持自动化及复制设置；辐射同样影响复制人。",
                    "Delivering 10 kg of Enriched Uranium activates a permanent core that never depletes. Defaults to 30 tiles rightward and 5 tiles high, with adjustable height and offset, clipped at world borders. Every tile uses the target radiation without falloff or shielding; overlaps use the higher target. Supports automation and Copy Settings; also affects duplicants."));
            string key = RadboltIrradiator.SliderKey;
            Strings.Add(key + "TITLE", ModStrings.T("横向辐照设置", "Horizontal Irradiation").Resolve());
            Strings.Add(key + "REACH_TITLE", ModStrings.T("横向延伸距离", "Horizontal Reach").Resolve());
            Strings.Add(key + "REACH_TOOLTIP", ModStrings.T("单向延伸距离；双向时平均分配。到当前世界边界自动缩短。", "One-sided reach, split evenly in both directions; clipped to world borders.").Resolve());
            Strings.Add(key + "HEIGHT_TITLE", ModStrings.T("辐照高度", "Irradiation Height").Resolve());
            Strings.Add(key + "HEIGHT_TOOLTIP", ModStrings.T("辐照带总高度，3–15 格，步进 2。", "Total band height, 3–15 tiles in steps of 2.").Resolve());
            Strings.Add(key + "STRENGTH_TITLE", ModStrings.T("每格目标辐射", "Target Radiation Per Tile").Resolve());
            Strings.Add(key + "STRENGTH_TOOLTIP", ModStrings.T("工作时区域每格采用此实际辐射值，重叠取较高值。", "Every covered tile uses this actual radiation value while running; overlaps use the higher target.").Resolve());
            Strings.Add("STRINGS.BUILDING.STATUSITEMS.OMNIRADBOLTIRRADIATOR.NAME", ModStrings.T("横向辐照", "Horizontal Irradiation").Resolve());
            Strings.Add("STRINGS.BUILDING.STATUSITEMS.OMNIRADBOLTIRRADIATOR.TOOLTIP", ModStrings.T("装载浓缩铀提供永久横向辐照。", "Fueled with Enriched Uranium to provide permanent horizontal radiation.").Resolve());
            TUNING.BUILDINGS.PLANSUBCATEGORYSORTING[RadboltIrradiatorConfig.ID] = "transmissions";
            ModUtil.AddBuildingToPlanScreen(new HashedString("HEP"), RadboltIrradiatorConfig.ID,
                "transmissions", relativeBuildingId: "HighEnergyParticleRedirector", ordering: ModUtil.BuildingOrdering.After);
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

        private static void RegisterLightEnhancements()
        {
            STRINGS.BUILDINGS.PREFABS.CEILINGLIGHT.EFFECT = new LocString(ModStrings.T(
                "【全能模组增强】为四周提供全向圆形光照。默认 12,000 Lux 糖心树强光，可在侧边栏自由切换强光/耀斑/柔和三种模式，支持随处建造。",
                "[OmniMod] Provides 360° circular illumination. Defaults to 12,000 Lux sunlight (ideal for SpaceTree); can switch modes on sidescreen. Can be built anywhere."
            ).Resolve());

            STRINGS.BUILDINGS.PREFABS.FLOORLAMP.EFFECT = new LocString(ModStrings.T(
                "【全能模组增强】为四周提供全向圆形光照。默认 12,000 Lux 糖心树强光，可在侧边栏自由切换强光/耀斑/柔和三种模式。",
                "[OmniMod] Provides 360° circular illumination. Defaults to 12,000 Lux sunlight (ideal for SpaceTree); can switch modes on sidescreen."
            ).Resolve());
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
                    "建成后会自动把本星球散落在地面的可食用物吸进来，并作为全星系共享食物的本地入口。",
                    "A food aggregation bin with near-infinite capacity. Food inside never spoils. " +
                    "Once built, it automatically pulls in loose edibles from across its planet and provides local access to shared food across worlds."),
                effect: ModStrings.T(
                    "自动收集食物并存入全局共享池，统一防腐保存。箱子作为存取门户，筛选清单显示共享池总量，储存内容仅显示待领取实物。复制人按禁食设置，经可步行到达的入口领取食物；未取走的出库食物归还共享池。",
                    "Automatically deposits food into one preserved global pool. Boxes are access portals: filter rows show pool totals and storage contents show pending checkouts. Duplicants fetch permitted meals on foot through a reachable portal; unclaimed meals return to the pool."));

            // 建造菜单：食物(Food) / 储物(storage)，排在"迷你冰箱"之后
            ModUtil.AddBuildingToPlanScreen(
                new HashedString("Food"),
                FoodBoxConfig.Id,
                "storage",
                relativeBuildingId: "MiniFridge",
                ordering: ModUtil.BuildingOrdering.After);
        }

        private static void RegisterTeleporters()
        {
            // 时空传送发射器
            ModStrings.AddBuilding(
                OmniTeleportTransmitterConfig.ID,
                name: ModStrings.T("时空传送发射器", "Omni Teleport Transmitter"),
                desc: ModStrings.T(
                    "采用定向引力共振与量子跃迁技术，可将指定复制人安全跨星传送至全星系任意已建有接收器的星球或火箭内部。支持侧边栏一键切换目标星球/火箭，每星/每火箭限建一台。",
                    "Uses quantum entanglement and gravitational resonance to safely teleport duplicants across worlds to any planet or rocket equipped with a receiver. Supports destination cycling on the side screen. Limited to one per planet/rocket."),
                effect: ModStrings.T(
                    "指定复制人后，复制人将步入发射台并传送至选定的目标接收端。侧边栏一键切换目标星球/火箭，支持一键呼叫就近复制人。每个星球或火箭内部限建一台。",
                    "Transfers an assigned duplicant to the selected destination receiver. Allows cycling targets on the side screen and calling the nearest duplicant. Limited to one per planet or rocket."));

            ModUtil.AddBuildingToPlanScreen(
                new HashedString("Base"),
                OmniTeleportTransmitterConfig.ID,
                "storage",
                relativeBuildingId: JunkBoxConfig.Id,
                ordering: ModUtil.BuildingOrdering.After);

            // 时空传送接收器
            ModStrings.AddBuilding(
                OmniTeleportReceiverConfig.ID,
                name: ModStrings.T("时空传送接收器", "Omni Teleport Receiver"),
                desc: ModStrings.T(
                    "用于接收从全星系任意时空传送发射器传送而来的复制人，建立稳定的量子重组通道。每个星球或火箭内部限造一台。",
                    "Receives duplicants warped from any teleport transmitter across the galaxy, providing a stable quantum arrival platform. Limited to one per planet/rocket."),
                effect: ModStrings.T(
                    "与全星系时空传送网络建立锚定，安全接收并释放传送抵达的复制人。每个星球或火箭内部限建一台。",
                    "Anchors to the galactic teleportation network to safely materialize arriving duplicants. Limited to one per planet or rocket."));

            ModUtil.AddBuildingToPlanScreen(
                new HashedString("Base"),
                OmniTeleportReceiverConfig.ID,
                "storage",
                relativeBuildingId: OmniTeleportTransmitterConfig.ID,
                ordering: ModUtil.BuildingOrdering.After);
        }

        private static void RegisterNuclearReprocessor()
        {
            Strings.Add("STRINGS.UI.UISIDESCREENS.OMNIMOD_REPROCESSOR.TITLE", "浓缩铀出料配置");
            Strings.Add("STRINGS.UI.UISIDESCREENS.OMNIMOD_REPROCESSOR_REFILL.TITLE", "进料管道智能校准");
            Strings.Add("STRINGS.UI.UISIDESCREENS.OMNIMOD_REPROCESSOR_FLUSH.TITLE", "进料管道除杂排空");

            ModStrings.AddBuilding(
                NuclearReprocessorConfig.ID,
                name: ModStrings.T("核废料再处理机", "Nuclear Waste Reprocessor"),
                desc: ModStrings.T(
                    "利用先进的乏燃料化学分离与放射性同位素重富集工艺，将反应堆排放的高放射性核废料（液态或固态核废料）高效提纯还原为高纯度浓缩铀。实现核能燃料的完全闭式循环。",
                    "Uses advanced spent fuel chemical separation and radioisotope re-enrichment to extract and reduce high-level nuclear waste back into pure Enriched Uranium, achieving a closed nuclear fuel cycle."),
                effect: ModStrings.T(
                    "每秒消耗 5kg 液态核废料（或 5kg 固态核废料块），高效提纯出 0.5kg 浓缩铀。支持满 20kg 自动落料入库（供聚合箱全图瞬时调配）或留存机内供机械手直取。",
                    "Consumes 5 kg/s Liquid Nuclear Waste (or 5 kg/s Solid Nuclear Waste) to produce 0.5 kg/s high-purity Enriched Uranium. Supports automatic 20kg floor-drop for global aggregate storage or internal storage retention."));

            string relativeId = DlcManager.IsExpansion1Active() ? "UraniumCentrifuge" : "OilRefinery";

            ModUtil.AddBuildingToPlanScreen(
                new HashedString("Refining"),
                NuclearReprocessorConfig.ID,
                "refining",
                relativeBuildingId: relativeId,
                ordering: ModUtil.BuildingOrdering.After);
        }

        private static void RegisterPocketHabitat()
        {
            ModStrings.AddBuilding(
                OmniPocketHabitatConfig.ID,
                name: ModStrings.T("折叠空间生态巨舱", "Omni Pocket Hyper-Habitat"),
                desc: ModStrings.T(
                    "采用高维空间折叠技术建造的独立基地太空舱。外部仅占 1x3 格，防水防淹，可放置于基地地面或水底。内部提供 30x30 格净空，不作为独立小行星列出；点击舱门侧边栏进入，通过舱内门返回。支持原版气液插座和电源插座连通内外管网。",
                    "A waterproof pocket habitat with a compact 1x3 exterior and a clean 30x30 interior. Hidden from the asteroid list. Enter through the exterior door's side panel and return using the interior door. Standard interior conduit ports and power plugs link to the exterior networks."),
                effect: ModStrings.T(
                    "外部宽 1 格、高 3 格。底格：电源、气体输入、液体输入；顶格：气体输出、液体输出。各类线管分层独立连接，舱内空间仍为 30x30 格。复制人可自由进出，侧边栏支持舱内外一键切换视角。",
                    "Exterior: 1 tile wide and 3 tiles tall. Bottom tile: power, gas input and liquid input. Top tile: gas output and liquid output. Each utility uses its own layer. The interior remains 30x30, with free duplicant access and one-click camera switching."));

            ModUtil.AddBuildingToPlanScreen(
                new HashedString("Base"),
                OmniPocketHabitatConfig.ID,
                "doors",
                relativeBuildingId: "Door",
                ordering: ModUtil.BuildingOrdering.After);
        }

        private static void RegisterExhibitionSign()
        {
            ModStrings.AddBuilding(
                ExhibitionSignConfig.ID,
                name: ModStrings.T("展览馆标牌", "Exhibition Sign"),
                desc: ModStrings.T(
                    "一座典雅的展馆导览铭牌。如同【公园标牌】用于标识公园与自然保护区一样，在房间内立起此标牌并陈列画作、雕塑与展台藏品，即可将该房间正式认定为【展览馆】，彻底避免与摆放了艺术品的私人卧室发生冲突。",
                    "An elegant gallery plaque. Just like a Park Sign designates a Park or Nature Reserve, placing this sign in a room alongside a painting, sculpture, and displayed artifact designates the room as an Exhibition Hall without conflicting with decorated Private Bedrooms."),
                effect: GalleryBenefits.BuildingEffect(ModStrings.T("博览群雅", "Exhibition Inspiration"), ModStrings.T("学习能力", "Science")));

            ModUtil.AddBuildingToPlanScreen(
                new HashedString("Furniture"),
                ExhibitionSignConfig.ID,
                "decor",
                relativeBuildingId: "ParkSign",
                ordering: ModUtil.BuildingOrdering.After);
        }

        private static void RegisterLandmarkSigns()
        {
            RegisterLandmarkSign(NaturalHistorySignConfig.ID, ModStrings.T("自然馆标牌", "Natural History Gallery Sign"),
                GalleryBenefits.BuildingEffect(ModStrings.T("万物有灵", "Wonder of Life"), ModStrings.T("畜牧", "Husbandry")),
                ExhibitionSignConfig.ID);
            RegisterLandmarkSign(WorkshopSignConfig.ID, ModStrings.T("工艺馆标牌", "Craft Gallery Sign"),
                GalleryBenefits.BuildingEffect(ModStrings.T("匠心独运", "Crafting Inspiration"), ModStrings.T("机械", "Machinery")), NaturalHistorySignConfig.ID);
            RegisterLandmarkSign(FitnessSignConfig.ID, ModStrings.T("活力馆标牌", "Vitality Gallery Sign"),
                GalleryBenefits.BuildingEffect(ModStrings.T("神采飞扬", "Radiant Vitality"), ModStrings.T("运动", "Athletics")), WorkshopSignConfig.ID);
        }

        private static void RegisterLandmarkSign(string id, ModStrings.Bilingual name, ModStrings.Bilingual effect, string previous)
        {
            var requirements = ModStrings.T(
                "房间要求与展览馆一致：32～120 格，至少一件画作、一座雕塑、一个放有藏品的展台；无工业机械、床铺、厕所和餐桌。同房间只放一种展馆标牌。不同展馆的经验加成相加，同馆重复到访只刷新持续时间。",
                "Uses the Exhibition Hall requirements: 32–120 tiles, a painting, a sculpture and an occupied pedestal; no industrial machinery, beds, toilets or mess tables. Use one kind of gallery sign per room. Experience bonuses from different galleries add together; revisits only refresh duration.");
            ModStrings.AddBuilding(id, name, requirements, effect);
            ModUtil.AddBuildingToPlanScreen(new HashedString("Furniture"), id, "decor",
                relativeBuildingId: previous, ordering: ModUtil.BuildingOrdering.After);
        }

        private static void RegisterCompactLuxuryBed()
        {
            ModStrings.AddBuilding(
                CompactLuxuryBedConfig.ID,
                name: ModStrings.T("紧凑型舒适床铺", "Compact Luxury Bed"),
                desc: ModStrings.T(
                    "采用高密度缓压材料精巧打造的紧凑型舒适床铺。仅占 2 格横向空间，却能带来与 4 格豪华床完全相同的优质睡眠体验。",
                    "A compact comfy bed crafted with high-density pressure-relief materials. Takes up only 2 horizontal tiles while providing the exact same deluxe rest experience as a 4-tile Luxury Bed."
                ),
                effect: ModStrings.T(
                    "提供与豪华床完全相同的顶级睡眠恢复（豪华床体力恢复、+40% 舒适度、健康恢复），并带有舒适床铺标签，可完美满足集体卧室与私人卧室的要求。",
                    "Provides the exact same deluxe sleep recovery as a Luxury Bed (Luxury Stamina recovery, +40% comfort, Health recovery). Carries the Luxury Bed tag to satisfy Collective Bedroom and Private Bedroom requirements."
                )
            );

            ModUtil.AddBuildingToPlanScreen(
                new HashedString("Furniture"),
                CompactLuxuryBedConfig.ID,
                "bed",
                relativeBuildingId: "LuxuryBed",
                ordering: ModUtil.BuildingOrdering.After
            );
        }

        private static void RegisterLiquorDispenser()
        {
            ModStrings.AddBuilding(
                LiquorDispenserConfig.ID,
                name: ModStrings.T("全能微醺特调吧", "Omni Lounge Bar"),
                desc: ModStrings.T(
                    "使用管道直连乙醇与高纯结晶蔗糖精心调配特制微醺鸡尾酒的娱乐设施。浓烈酒香不仅能让复制人体会到久违的放松与狂喜，还能彻底隔绝严寒，气血充盈，绝无任何病菌滋生风险。",
                    "A deluxe recreation bar that crafts sweet cocktails from piped ethanol and crystalline sucrose. The rich aroma gives duplicants unmatched relaxation and joy, provides complete cold immunity, and carries zero germ risk."
                ),
                effect: ModStrings.T(
                    "消耗管道供应的乙醇与固体蔗糖。复制人饮用后获得骚气爆棚的【醉意撩人】强效增益：士气 +6，体能与力量各 +3，免疫力 +4，保温御寒（免除寒冷惩罚与受冻状态），大幅舒缓压力。\n自带娱乐设施标签，可直接替代原版饮水机满足【高级餐厅】与【娱乐室】的房间要求。",
                    "Consumes piped ethanol and sucrose. Duplicants who drink gain the flamboyant [Tipsy & Flirtatious] buff: Morale +6, Athletics & Strength +3, Immunity +4, Thermal Barrier (immune to hypothermia and cold), and massive stress relief.\nCarries the Recreation Building tag to satisfy Great Mess Hall and Recreation Room requirements."
                )
            );

            ModUtil.AddBuildingToPlanScreen(
                new HashedString("Furniture"),
                LiquorDispenserConfig.ID,
                "recreation",
                relativeBuildingId: "WaterCooler",
                ordering: ModUtil.BuildingOrdering.After
            );
        }

        // 注：阶段二仍不做科技门槛，两个箱子一开局就能造。
        // 后续加研究解锁时，在 Db.Initialize 的 Postfix 里：
        //   Db.Get().Techs.Get("SomeTechId").unlockedItemIDs.Add(JunkBoxConfig.Id);
    }
}
