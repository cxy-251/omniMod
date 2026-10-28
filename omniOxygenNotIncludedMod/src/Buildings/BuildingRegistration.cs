using HarmonyLib;
using OmniMod.Logistics.RemoteFetch;
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
            RegisterFluidProcessors();
            RegisterAutoIceMaker();
            RegisterLiquidChiller();
            RegisterTeleporters();
            RegisterNuclearReprocessor();
            RegisterPocketHabitat();
            RegisterExhibitionSign();
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
            Strings.Add("STRINGS.UI.UISIDESCREENS.OMNIMOD_LIQUIDCHILLER.TARGET_TEMP_TOOLTIP", "调节管道流体冷却后的目标出液温度（-200°C ~ +100°C）");

            ModStrings.AddBuilding(
                LiquidChillerConfig.ID,
                name: ModStrings.T("全能恒温流体冷却机", "Auto Liquid Chiller"),
                desc: ModStrings.T(
                    "采用先进的分子共振与深冷压缩热吸收技术，直接将管道通入的任意流体一次性恒温冷却至目标温度。通流即冷却，一步直达，无多余废热外泄，内置智能防爆管保护，彻底替代繁复且笨重的传统液温调节器架构。",
                    "Uses cryogenic absorption technology to instantly cool any liquid in the pipe to a user-defined target temperature in a single pass. Eliminates the need for bulky Thermo Aquatuner setups and dangerous pipe ruptures."),
                effect: ModStrings.T(
                    "最高支持 10kg/s 满管液体连续恒温处理，侧边栏自由设定目标出液温度（-200°C ~ +100°C）。内置液体冰点智能防爆管保护，极低能耗且支持复制设置。",
                    "Cools up to 10 kg/s of any piped liquid directly to a target temperature (-200°C to +100°C). Features automatic freeze-burst protection, minimal heat output, and Copy Settings."));

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
                    "采用高维空间折叠技术建造的独立基地太空舱。外部仅占 3x3 地块，防水防淹，可放置于基地地面或水底。内部提供 28x27 格纯粹净空（外框为标准钢制火箭壁板，不塞中子物质、不预充气体），并支持通过原版【电源插座】将舱内与舱外电网直接连通。",
                    "A standalone pocket habitat module occupying a 3x3 footprint externally, fully waterproof and submergeable. Internally provides a clean 28x27 vacuum space framed by standard steel rocket walls, with direct cross-dimensional power grid linkage via standard Rocket Interior Power Plugs."),
                effect: ModStrings.T(
                    "建造后开辟 28x27 净空太空舱空间，复制人可自由进出，侧边栏支持舱内外一键切换视角。外部自带电源接口，与舱内【电源插座】共享同一电网。",
                    "Creates a 28x27 clean rocket interior upon construction. Duplicants can enter and exit freely, with one-click interior/exterior camera switching. Features an external power port that bridges directly with interior Power Plugs."));

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
                effect: ModStrings.T(
                    "将满足艺术品陈列条件的房间标识为【展览馆】（到访复制人获得 +6 士气、-15% 压力/周期、+2 学习能力）。未放置此标牌的房间不会被识别为展览馆。",
                    "Designates a qualified room as an Exhibition Hall (+6 Morale, -15% Stress/cycle, +2 Science to visitors). Rooms without this sign will never conflict with Exhibition Hall."));

            ModUtil.AddBuildingToPlanScreen(
                new HashedString("Furniture"),
                ExhibitionSignConfig.ID,
                "decor",
                relativeBuildingId: "ParkSign",
                ordering: ModUtil.BuildingOrdering.After);
        }

        // 注：阶段二仍不做科技门槛，两个箱子一开局就能造。
        // 后续加研究解锁时，在 Db.Initialize 的 Postfix 里：
        //   Db.Get().Techs.Get("SomeTechId").unlockedItemIDs.Add(JunkBoxConfig.Id);
    }
}
