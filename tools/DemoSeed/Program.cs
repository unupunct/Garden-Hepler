// Demo homestead near Moieciu de Sus (Brasov County, Romania): about six months of records, for testing the app.
// Usage: DemoSeed <data folder> <content folder> [--force]
using GardenHelper.Core.Animals;
using GardenHelper.Core.Cameras;
using GardenHelper.Core.Data;
using GardenHelper.Core.Design;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Greenhouse;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Planning;
using GardenHelper.Core.Services;
using GardenHelper.Core.Site;

var dataDir = args[0]; var contentDir = args[1]; var force = args.Contains("--force");
var dbFile = Path.Combine(dataDir, "gardenhelper.db");
if (File.Exists(dbFile) && !force) { Console.WriteLine("Demo data already exists. Use --force to rebuild it."); return; }
if (force && Directory.Exists(dataDir)) foreach (var f in Directory.GetFiles(dataDir, "gardenhelper.db*")) File.Delete(f);
var paths = new AppPaths(dataDir);
await DbFactory.InitializeAsync(paths);
var repo = new Repository(new ContextFactory(paths));
var kb = KnowledgeBase.Load(new[] { contentDir });
var gh = new GreenhouseService(repo);
var rnd = new Random(2026);
var today = DateOnly.FromDateTime(DateTime.Today);
DateOnly Ago(int days) => today.AddDays(-days);
DateOnly Ahead(int days) => today.AddDays(days);
DateTime Utc(int daysAgo, int hour = 10) => DateTime.UtcNow.Date.AddDays(-daysAgo).AddHours(hour);

string P(string id) { if (kb.Get(id) is null) throw new Exception("Unknown plant id: " + id); return id; }

// ---------------------------------------------------------------- the place
var center = new GeoPoint(45.4795, 25.3185);          // meadows above Moieciu de Sus, about 800 m
var proj = new LocalProjection(center);
GeoPoint G(double x, double y) => proj.FromMetres(new Pt(x, y));           // metres east / north of the centre
List<GeoPoint> Box(double cx, double cy, double w, double h) => new[] { (-1, 1), (1, 1), (1, -1), (-1, -1) }.Select(s => G(cx + s.Item1 * w / 2, cy + s.Item2 * h / 2)).ToList();

var outlineM = new (double X, double Y)[] { (-36, 22), (-14, 25), (8, 27), (30, 24), (36, 14), (34, -2), (35, -14), (30, -23), (6, -26), (-14, -24), (-30, -22), (-37, -10), (-38, 4), (-36, 14) };
var outline = outlineM.Select(p => G(p.X, p.Y)).ToList();

var garden = await repo.SaveAsync(new Garden
{
    Name = "Moieciu Homestead", Country = "Romania", Region = "Brașov County", HardinessZone = "6a", Latitude = center.Lat, Longitude = center.Lon,
    LastSpringFrost = new DateOnly(today.Year, 5, 10), FirstAutumnFrost = new DateOnly(today.Year, 10, 5), IsActive = true,
    Notes = "South-facing slope at about 800 m, in the Bran-Moieciu area. Clay-loam soil, pH about 6.2, a spring-fed well and a 3,000 L rainwater tank. Late spring frosts: do not plant out tomatoes before mid-May. Strong wind from the north-west in winter, hence the hedge on that side. Bears and foxes pass through in autumn: the fence and the coop door need checking every evening.",
    BoundaryJson = GeoMath.ToJson(outline),
});

// ---------------------------------------------------------------- places (structures and spaces) and the plan
var features = new Dictionary<string, PropertyFeature>();
var beds = new Dictionary<string, GardenBed>();
var specs = new List<(string Key, string Name, string Kind, double Cx, double Cy, double W, double H, bool Bed)>
{
    ("house", "Farmhouse", "house", -18, 10, 10, 8, true), ("garage", "Garage", "garage", -7, 11, 6, 5, true), ("workshop", "Workshop", "workshop", -27, -6, 5, 4, true),
    ("shed", "Tool shed", "shed", 31, 17, 3, 3, true), ("barn", "Barn (goats and pigs)", "barn", -22, -17, 12, 6, true), ("coop", "Chicken coop", "coop", 15, -17, 3, 2, true),
    ("pen", "Duck and rabbit pen", "pen", 21, -17, 5, 3.5, true),
    ("greenhouse", "Greenhouse", "greenhouse", 2, -14, 4, 6, true), ("pond", "Pond", "pond", -10, -4, 4, 3, true), ("compost", "Compost area", "compost", 30, -5, 2.5, 2.5, true),
    ("veg", "Vegetable garden", "garden", 14, 4, 20, 14, false), ("orchard", "Orchard", "orchard", 23, 19, 20, 8, false),
};
foreach (var s in specs)
    features[s.Key] = await repo.SaveAsync(new PropertyFeature { GardenId = garden.Id, Name = s.Name, Kind = s.Kind, PointsJson = GeoMath.ToJson(Box(s.Cx, s.Cy, s.W, s.H)), CreatedUtc = Utc(190) });

// the same plan frame the designer builds (including the shift for the satellite picture), so beds line up with the places
var allKnown = features.Values.SelectMany(f => GeoMath.FromJson(f.PointsJson)).Concat(outline).ToList();
var frame = PlanFrame.Fit(allKnown, 500, out var planW, out var planH);
var grid = TileGrid.ForBounds(allKnown);
var tl = frame.ToPlan(grid.TopLeft);
frame.Shift(tl.X < 0 ? -tl.X : 0, tl.Y < 0 ? -tl.Y : 0);
if (File.Exists(Path.Combine(paths.SiteDir, $"backdrop-{garden.Id}.jpg"))) garden.BackdropFile = $"backdrop-{garden.Id}.jpg";   // picture loaded earlier from the app: reuse it
garden.BackdropTopLat = grid.TopLeft.Lat; garden.BackdropTopLon = grid.TopLeft.Lon; garden.BackdropCmPerPixel = grid.CmPerPixel;   // the app rebuilds the picture itself
garden = await repo.SaveAsync(garden);
Pt Plan(double x, double y) => frame.ToPlan(G(x, y));

async Task<GardenBed> Bed(string key, string kind, string name, double cx, double cy, double wCm, double hCm, double rot = 0, PropertyFeature? feature = null, string? notes = null, int shade = 0, bool locked = false)
{
    var c = Plan(cx, cy);
    var b = await repo.SaveAsync(new GardenBed { GardenId = garden.Id, Kind = kind, Name = name, X = Math.Round(c.X - wCm / 2), Y = Math.Round(c.Y - hCm / 2), Width = wCm, Height = hCm, Rotation = rot, FeatureId = feature?.Id, Notes = notes, ShadePercent = shade, Locked = locked });
    beds[key] = b; return b;
}
async Task Plant(string bedKey, string plantId, int qty, int plantedDaysAgo, PlantingStatus status, string? variety = null, string? notes = null) =>
    await repo.SaveAsync(new BedPlanting { BedId = beds[bedKey].Id, PlantId = P(plantId), Quantity = qty, Variety = variety, PlantedDate = plantedDaysAgo >= 0 ? Ago(plantedDaysAgo) : null, Status = status, Notes = notes });

foreach (var s in specs.Where(s => s.Bed))
    await Bed(s.Key, s.Kind, s.Name, s.Cx, s.Cy, s.W * 100, s.H * 100, 0, features[s.Key], locked: s.Key is "house" or "garage");

// vegetable garden: raised beds and ground beds (plan objects)
for (var i = 0; i < 4; i++) await Bed($"rb{i + 1}", "raised", $"Raised bed {i + 1}", 6 + i * 2.4, 7.5, 120, 300);
await Bed("herb", "raised", "Herb bed", 16.5, 7.5, 200, 200, notes: "Perennial herbs, south-facing, never watered unless very dry.");
string[] groundNames = { "Potato bed", "Three sisters bed", "Cabbage family bed", "Garlic and leek bed", "Pollinator strip", "Resting bed (cover crop)" };
for (var i = 0; i < groundNames.Length; i++) await Bed($"gb{i + 1}", "bed", groundNames[i], 6 + i * 2.4, 0.5, 120, 400);

// orchard trees, berries, hedge
var trees = new (string Key, string Plant, string Name, double X, double Y, double Crown)[]
{
    ("t_apple1", "apple", "Apple 'Ionathan'", 16, 19, 180), ("t_apple2", "apple", "Apple 'Golden Delicious'", 20, 19, 180), ("t_apple3", "apple", "Apple 'Reinette' (old Romanian variety)", 24, 19, 200),
    ("t_plum", "plum", "Plum 'Stanley'", 28, 19, 170), ("t_cherry", "sweet-cherry", "Sweet cherry", 16, 22, 200), ("t_pear", "pear", "Pear 'Williams'", 20, 22, 160),
    ("t_apricot", "apricot", "Apricot", 24, 22, 150), ("t_quince", "quince", "Quince", 28, 22, 130), ("t_walnut", "walnut", "Walnut (old)", 31, 8, 500), ("t_sour", "sour-cherry", "Sour cherry (near the house)", -9, 4, 160),
};
foreach (var t in trees) { await Bed(t.Key, "tree", t.Name, t.X, t.Y, t.Crown * 2, t.Crown * 2); }
await Bed("berries", "bed", "Berry bed", 28.5, -9, 120, 600, notes: "Mulched with straw each spring.");
await Bed("blueberry", "raised", "Blueberry bed (acid soil)", 25, 2, 120, 240, notes: "Filled with peat-free ericaceous compost and pine bark; pH checked at 4.8.");
await Bed("hedge_w", "hedge", "Native hedge (west)", -37, 2, 70, 2200, 0, notes: "Planted in spring 2024. Wind break and food for birds and bees.");
await Bed("hedge_n", "hedge", "Native hedge (north-east)", 8, 25, 70, 1500, 0);

// fences along the boundary (one gate on the north side, by the driveway), driveway, path, and the small things
var gateEdge = 1;      // outline point 1 -> 2 (north side)
for (var i = 0; i < outlineM.Length; i++)
{
    var (x1, y1) = outlineM[i]; var (x2, y2) = outlineM[(i + 1) % outlineM.Length];
    var len = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1)); var rot = Math.Atan2(-(y2 - y1), x2 - x1) * 180 / Math.PI;
    if (i == gateEdge) { await Bed($"fence{i}a", "fence", $"Fence {i + 1}a", (x1 + x2) / 2 - len * 0.32, (y1 + y2) / 2 - 0.5, len * 100 * 0.34, 12, rot); await Bed($"fence{i}b", "fence", $"Fence {i + 1}b", (x1 + x2) / 2 + len * 0.34, (y1 + y2) / 2 - 0.5, len * 100 * 0.30, 12, rot); }
    else await Bed($"fence{i}", "fence", $"Fence {i + 1}", (x1 + x2) / 2, (y1 + y2) / 2, len * 100, 12, rot, notes: i == 5 ? "Two posts rotted: replace before winter." : null);
}
await Bed("gate", "gate", "Main gate", -4.5, 25.9, 350, 12, -6, notes: "Hinges need oiling; latch is stiff in cold weather.");
await Bed("driveway", "driveway", "Driveway", -5, 19.5, 300, 1000);
await Bed("path1", "path", "Path to the garden", -1, 4, 80, 1400, 90);
await Bed("path2", "path", "Path to the coop", 8, -9, 80, 1000, 0);
await Bed("tank", "tank", "Rainwater tank (3,000 L)", -24.5, 1, 150, 150);
await Bed("well", "well", "Spring-fed well", -31, 9, 90, 90);
await Bed("solar", "solar", "Solar panels (pump and fence)", -3, 21.5, 220, 110);
await Bed("hive1", "hive", "Beehive 1", -33, -8, 60, 60); await Bed("hive2", "hive", "Beehive 2", -33, -10.5, 60, 60);
await Bed("bench1", "bench", "Bench by the house", -14, 3, 150, 50); await Bed("bench2", "bench", "Bench in the garden", 22, 12.5, 150, 50);
await Bed("bin1", "bin", "Trash bin", -3.8, 9.8, 60, 60); await Bed("bin2", "bin", "Recycling bin", -3.8, 11, 60, 60);

// ---------------------------------------------------------------- what is growing (autumn status)
await Plant("rb1", "tomato", 6, 165, PlantingStatus.Finished, "Roma and a Romanian 'Inimă de bou'", "Blight in September; vines cleared, 14 kg per plant in a good week of August.");
await Plant("rb1", "basil", 4, 160, PlantingStatus.Finished); await Plant("rb1", "marigold", 3, 160, PlantingStatus.Growing, null, "Still flowering after the first frost.");
await Plant("rb2", "pepper", 6, 160, PlantingStatus.Finished, "Kapia (Romanian roasting pepper)"); await Plant("rb2", "hot-pepper", 2, 160, PlantingStatus.Harvesting, null, "Drying the last ones on strings in the attic.");
await Plant("rb2", "parsley", 8, 150, PlantingStatus.Harvesting);
await Plant("rb3", "lettuce", 12, 40, PlantingStatus.Growing, null, "Autumn sowing under a fleece tunnel."); await Plant("rb3", "spinach", 10, 40, PlantingStatus.Growing); await Plant("rb3", "radish", 20, 45, PlantingStatus.Finished); await Plant("rb3", "chives", 3, 400, PlantingStatus.Growing);
await Plant("rb4", "carrot", 40, 135, PlantingStatus.Harvesting, "Nantes", "Store in damp sand in the cellar."); await Plant("rb4", "beetroot", 20, 130, PlantingStatus.Harvesting); await Plant("rb4", "onion", 30, 175, PlantingStatus.Finished, null, "Dried and braided; keeping well.");
await Plant("herb", "thyme", 3, 700, PlantingStatus.Growing); await Plant("herb", "sage", 2, 700, PlantingStatus.Growing); await Plant("herb", "rosemary", 1, 700, PlantingStatus.Growing, null, "Wrap in fleece before the hard frosts, it is borderline hardy here.");
await Plant("herb", "oregano", 2, 700, PlantingStatus.Growing); await Plant("herb", "lavender", 3, 700, PlantingStatus.Growing); await Plant("herb", "mint", 1, 700, PlantingStatus.Growing, null, "In a bottomless bucket to stop it spreading.");
await Plant("gb1", "potato", 40, 185, PlantingStatus.Finished, "Early rose and Roxana", "Harvested 62 kg. Some scab on the Roxana: rotate away from this bed.");
await Plant("gb2", "bean", 30, 165, PlantingStatus.Finished); await Plant("gb2", "sweetcorn", 12, 165, PlantingStatus.Finished); await Plant("gb2", "pumpkin", 4, 160, PlantingStatus.Harvesting, null, "Two giants still to bring in before the next frost.");
await Plant("gb3", "cabbage", 10, 140, PlantingStatus.Harvesting, null, "Caterpillars in August; netting helped. For sauerkraut."); await Plant("gb3", "kale", 8, 120, PlantingStatus.Growing); await Plant("gb3", "broccoli", 6, 125, PlantingStatus.Harvesting); await Plant("gb3", "kohlrabi", 6, 110, PlantingStatus.Finished); await Plant("gb3", "nasturtium", 6, 150, PlantingStatus.Finished, null, "Trap crop for aphids.");
await Plant("gb4", "garlic", 60, 14, PlantingStatus.Sown, "Local hardneck", "Planted two weeks ago, mulched with straw."); await Plant("gb4", "leek", 20, 150, PlantingStatus.Growing);
await Plant("gb5", "phacelia", 30, 170, PlantingStatus.Finished); await Plant("gb5", "borage", 6, 170, PlantingStatus.Finished); await Plant("gb5", "calendula", 8, 170, PlantingStatus.Growing); await Plant("gb5", "cornflower", 10, 170, PlantingStatus.Finished); await Plant("gb5", "yarrow", 3, 700, PlantingStatus.Growing);
await Plant("gb6", "winter-rye", 1, 25, PlantingStatus.Sown, null, "Green manure; dig in next April."); await Plant("gb6", "white-clover", 1, 25, PlantingStatus.Sown);
await Plant("greenhouse", "cucumber", 4, 170, PlantingStatus.Finished); await Plant("greenhouse", "eggplant", 4, 165, PlantingStatus.Finished, null, "First year of aubergines: 6 fruits per plant."); await Plant("greenhouse", "lettuce", 16, 20, PlantingStatus.Growing); await Plant("greenhouse", "tomato", 4, 175, PlantingStatus.Finished);
await Plant("berries", "blackcurrant", 5, 800, PlantingStatus.Growing); await Plant("berries", "raspberry", 12, 800, PlantingStatus.Growing, null, "Prune the old canes now."); await Plant("berries", "gooseberry", 3, 800, PlantingStatus.Growing); await Plant("berries", "aronia", 2, 500, PlantingStatus.Growing);
await Plant("blueberry", "blueberry", 6, 600, PlantingStatus.Growing); await Plant("hedge_w", "hawthorn", 12, 900, PlantingStatus.Growing); await Plant("hedge_w", "hazel", 6, 900, PlantingStatus.Growing); await Plant("hedge_w", "elder", 3, 900, PlantingStatus.Growing);
await Plant("hedge_w", "rose-hip", 6, 900, PlantingStatus.Growing); await Plant("hedge_w", "sea-buckthorn", 4, 900, PlantingStatus.Growing); await Plant("hedge_n", "cornelian-cherry", 6, 900, PlantingStatus.Growing); await Plant("hedge_n", "blackthorn", 10, 900, PlantingStatus.Growing);
foreach (var t in trees) await Plant(t.Key, t.Plant, 1, 3000, PlantingStatus.Growing);

// ---------------------------------------------------------------- trees in the plant inventory (linked to the plan)
async Task<InventoryPlant> Tree(string key, string name, string plantId, string type, int plantedYearsAgo, double height, double crown, string status, string? place, string notes)
{
    var inv = await repo.SaveAsync(new InventoryPlant { Name = name, SpeciesId = P(plantId), Location = PlantLocation.Outdoor, Place = place, Quantity = 1, Status = status, IsTree = true, TreeType = type, PlantedOn = Ago(plantedYearsAgo * 365), HeightCm = height, CrownRadiusCm = crown, BedId = beds[key].Id, Notes = notes, Source = "Local nursery", Acquired = Ago(plantedYearsAgo * 365), CreatedUtc = Utc(plantedYearsAgo * 365) });
    return inv;
}
async Task Log(InventoryPlant p, int daysAgo, string kind, string text) => await repo.SaveAsync(new InventoryLog { PlantId = p.Id, Date = Ago(daysAgo), Kind = kind, Text = text });
var apple1 = await Tree("t_apple1", "Apple 'Ionathan'", "apple", "fruit", 6, 380, 180, "healthy", "Orchard, row 1", "Heavy crop this year; thin the fruit next June.");
var apple2 = await Tree("t_apple2", "Apple 'Golden Delicious'", "apple", "fruit", 6, 360, 180, "attention", "Orchard, row 1", "Canker on a lower branch; cut out in dry weather and burn the wood.");
var apple3 = await Tree("t_apple3", "Apple 'Reinette'", "apple", "fruit", 30, 520, 200, "healthy", "Orchard, row 1", "Old variety that came with the land. Stores until March.");
var plum = await Tree("t_plum", "Plum 'Stanley'", "plum", "fruit", 5, 340, 170, "recovering", "Orchard, row 1", "A big branch split in the September storm; trimmed and painted.");
var cherry = await Tree("t_cherry", "Sweet cherry", "sweet-cherry", "fruit", 4, 300, 200, "healthy", "Orchard, row 2", "Netting needed next June, birds took half the crop.");
var pear = await Tree("t_pear", "Pear 'Williams'", "pear", "fruit", 4, 290, 160, "healthy", "Orchard, row 2", "");
var apricot = await Tree("t_apricot", "Apricot", "apricot", "fruit", 3, 220, 150, "attention", "Orchard, row 2", "Lost the blossom to the April frost. Consider fleece next spring.");
var quince = await Tree("t_quince", "Quince", "quince", "fruit", 3, 210, 130, "healthy", "Orchard, row 2", "First 12 fruits this year: made jelly.");
var walnut = await Tree("t_walnut", "Walnut (old)", "walnut", "nut", 40, 1100, 500, "healthy", "East side", "Shades the east end of the vegetable garden in the afternoon. Nuts collected in early October: 18 kg.");
var sour = await Tree("t_sour", "Sour cherry", "sour-cherry", "fruit", 8, 330, 160, "healthy", "By the house", "");
await Log(apple1, 160, "treatment", "Spring spray against scab (copper), calm morning, before blossom."); await Log(apple1, 90, "observation", "Fruit set good, about 8 per cluster. Thinned some.");
await Log(apple2, 35, "observation", "Found canker on the lower south branch. Photographed it."); await Log(apple2, 20, "pruning", "Cut out the cankered branch 20 cm below the wound; burned it.");
await Log(plum, 44, "observation", "Storm split a main limb. Cut clean and sealed."); await Log(plum, 10, "observation", "New growth fine, no gum. Keep watching.");
await Log(apricot, 190, "observation", "Blossom killed by frost on 17 April."); await Log(walnut, 25, "note", "Harvested nuts, dried them on racks for two weeks.");
await Log(cherry, 140, "note", "Birds took most of the fruit. Net it next year.");

// ---------------------------------------------------------------- indoor and greenhouse plant inventory (watering schedules)
async Task<InventoryPlant> House(string name, string plantId, PlantLocation where, string place, int qty, int every, int lastWateredDaysAgo, string status, string notes, int boughtDaysAgo = 900)
    => await repo.SaveAsync(new InventoryPlant { Name = name, SpeciesId = P(plantId), Location = where, Place = place, Quantity = qty, WateringIntervalDays = every, LastWatered = Ago(lastWateredDaysAgo), Status = status, Notes = notes, Acquired = Ago(boughtDaysAgo), CreatedUtc = Utc(boughtDaysAgo) });
var monstera = await House("Monstera", "monstera", PlantLocation.Indoor, "Living room, east window", 1, 7, 9, "attention", "Watering is 2 days overdue; leaf tips browning, maybe too dry near the stove.");
await House("Pothos", "pothos", PlantLocation.Indoor, "Kitchen shelf", 2, 8, 3, "healthy", "Cuttings rooting in a jar.");
await House("Snake plant", "snake-plant", PlantLocation.Indoor, "Hall", 1, 18, 11, "healthy", "Needs almost nothing.");
await House("Peace lily", "peace-lily", PlantLocation.Indoor, "Bedroom", 1, 6, 5, "healthy", "Flowered in September.");
await House("Aloe vera", "aloe-vera", PlantLocation.Indoor, "Kitchen window", 2, 21, 15, "healthy", "Used a leaf for a burn in August.");
await House("Spider plant", "spider-plant", PlantLocation.Indoor, "Bathroom", 1, 7, 1, "healthy", "");
var rubber = await House("Rubber plant", "rubber-plant", PlantLocation.Indoor, "Living room", 1, 10, 14, "attention", "Leaf drop since the heating came on; move it away from the radiator.");
var lettuceGh = await House("Autumn lettuce and spinach", "lettuce", PlantLocation.Greenhouse, "Greenhouse bench B", 30, 3, 2, "healthy", "Second sowing for winter salads; open the door on sunny days.", 20);
await House("Pepper cuttings (overwintering)", "pepper", PlantLocation.Greenhouse, "Greenhouse bench A", 6, 7, 6, "recovering", "Kapia mother plants cut back hard; keep frost-free.", 12);
await Log(monstera, 9, "watering", ""); await Log(monstera, 40, "feeding", "Liquid feed, half strength."); await Log(rubber, 60, "repotting", "Moved to a pot 4 cm wider.");
await Log(rubber, 14, "observation", "Two lower leaves dropped.");

// ---------------------------------------------------------------- seeds
async Task Seed(string plantId, string name, string? variety, string supplier, int qty, int remaining, int boughtDaysAgo, double? germ, string? storage, int? reviewIn, decimal cost, string? notes = null) =>
    await repo.SaveAsync(new SeedItem { PlantId = plantId, Name = name, Variety = variety, Supplier = supplier, Quantity = qty, QuantityRemaining = remaining, PurchaseDate = Ago(boughtDaysAgo), GerminationPercent = germ is null ? null : (decimal)germ, StorageLocation = storage, ReviewDate = reviewIn is null ? null : Ahead(reviewIn.Value), Cost = cost, Notes = notes });
await Seed("tomato", "Tomato", "Inimă de bou", "Seed swap, Zărnești", 40, 28, 260, 85, "Cellar box 1", 150, 0, "Saved from my own best fruit two years ago: retest germination in spring.");
await Seed("tomato", "Tomato", "Roma", "Garden centre, Brașov", 60, 30, 260, 92, "Cellar box 1", 150, 8.5m);
await Seed("pepper", "Pepper", "Kapia", "Local market", 30, 12, 260, 78, "Cellar box 1", 150, 6);
await Seed("carrot", "Carrot", "Nantes", "Garden centre, Brașov", 500, 300, 250, 80, "Cellar box 2", 200, 5);
await Seed("beetroot", "Beetroot", "Detroit", "Garden centre, Brașov", 200, 110, 250, 88, "Cellar box 2", 300, 4);
await Seed("lettuce", "Lettuce", "Winter Density", "Online shop", 800, 420, 90, 90, "Cellar box 2", 365, 7.5m);
await Seed("spinach", "Spinach", "Matador", "Garden centre, Brașov", 300, 180, 90, 85, "Cellar box 2", 300, 5);
await Seed("bean", "Bean", "Climbing, purple pods", "Neighbour (heirloom)", 150, 90, 250, 90, "Jar, kitchen", 300, 0, "Neighbour's grandmother's variety: keep growing and keep the seed.");
await Seed("pumpkin", "Pumpkin", "Hokkaido", "Seed swap, Zărnești", 20, 6, 250, 75, "Cellar box 3", 100, 0);
await Seed("garlic", "Garlic cloves", "Local hardneck", "Own harvest", 60, 0, 40, 95, null, null, 0, "All planted. Save the biggest bulbs next July.");
await Seed("phacelia", "Phacelia", null, "Online shop", 100, 70, 200, 85, "Cellar box 3", 20, 6, "Seed is 20 days past its review date: test a few on damp paper.");
await Seed("calendula", "Calendula", null, "Own harvest", 200, 150, 40, 90, "Jar, kitchen", 300, 0);
await Seed("winter-rye", "Winter rye (green manure)", null, "Agricultural co-op", 2000, 1200, 30, 90, "Shed", 365, 18);
await Seed("cucumber", "Cucumber", "Marketmore", "Garden centre, Brașov", 25, 4, 255, 82, "Cellar box 3", -10, 5, "Only 4 left and past review: buy fresh seed in the spring.");
await Seed("broccoli", "Broccoli", "Calabrese", "Online shop", 100, 70, 255, 88, "Cellar box 3", 200, 6);
await Seed("sunflower", "Sunflower", "Tall mixed", "Own harvest", 80, 60, 40, 90, "Jar, kitchen", 300, 0);

// ---------------------------------------------------------------- places: notes and history (what happened, what to do next)
async Task Note(string key, int daysAgo, string kind, string text) => await repo.SaveAsync(new FeatureLog { FeatureId = features[key].Id, Date = Ago(daysAgo), Kind = kind, Text = text });
await Note("veg", 175, "soil", "Soil test: pH 6.2, good organic matter. Added 5 cm of compost on all beds."); await Note("veg", 160, "plant", "Planted out tomatoes and peppers after the frost warning had passed.");
await Note("veg", 120, "observation", "Potato beetles on the Roxana: hand-picked every morning for two weeks."); await Note("veg", 75, "observation", "Slugs on the lettuce after the rain. Beer traps and a ring of ash."); await Note("veg", 40, "observation", "Late blight on tomatoes. Cleared the vines and burned them, not composted.");
await Note("veg", 14, "plant", "Planted 60 cloves of garlic in the garlic bed, 15 cm apart, mulched with straw."); await Note("veg", 8, "note", "NEXT: spread leaf mould on the resting bed, protect the rosemary, bring in the last pumpkins.");
await Note("orchard", 200, "plant", "Pruned all apple trees in late winter; removed crossing branches."); await Note("orchard", 44, "observation", "Storm damage on the plum. Check the stake ties on all young trees."); await Note("orchard", 12, "observation", "Windfall apples feeding the pigs and goats. Collect daily to avoid wasps.");
await Note("orchard", 5, "note", "NEXT: winter pruning from January, whitewash trunks in November, wrap the apricot.");
await Note("greenhouse", 150, "plant", "Planted tomatoes, cucumbers and aubergines. Aquaponic bench running with goldfish and carp: they overwinter better than warm-water fish."); await Note("greenhouse", 55, "observation", "Water temperature reached 26 °C in the heat wave: shaded the tank.");
await Note("greenhouse", 18, "observation", "Autumn lettuce growing well in the aquaponic raft beds."); await Note("greenhouse", 3, "note", "Humidity is high on cool mornings: open the roof vent earlier. NEXT: insulate the north wall with bubble wrap.");
await Note("coop", 70, "note", "Five new chicks joined the flock (integrated after two weeks in a separate pen)."); await Note("coop", 9, "observation", "Fox tracks around the coop. Lost one hen. Reinforced the run with buried wire."); await Note("coop", 2, "note", "NEXT: deep-litter for winter, check the pop-hole door every evening.");
await Note("pen", 50, "observation", "Rabbits bred: eight kits born, now weaned. Ducks laying well."); await Note("barn", 100, "note", "Goats and pigs in the barn at night. Cleaned out and limed the floor."); await Note("barn", 12, "maintenance", "Replaced the broken window pane; the roof needs checking before snow.");
await Note("pond", 130, "observation", "Frogs and dragonflies already. Cleared some duckweed."); await Note("compost", 21, "maintenance", "Turned both heaps; the first is ready for the beds in spring."); await Note("shed", 30, "maintenance", "Sharpened and oiled the tools.");
await Note("workshop", 60, "note", "Built a new hive box and two cold frames."); await Note("house", 15, "maintenance", "Cleaned the chimney; bought 4 m³ of firewood to dry.");

// ---------------------------------------------------------------- animals
async Task<AnimalGroup> Group(string name, string species, string breed, int count, string? housing, string notes, params (string Feed, double Grams, decimal Price)[] rations)
{
    var g = await repo.SaveAsync(new AnimalGroup { GardenId = garden.Id, Name = name, Species = species, Breed = breed, Count = count, FeatureId = housing is null ? null : features[housing].Id, Notes = notes });
    foreach (var r in rations) await repo.SaveAsync(new FeedRation { GroupId = g.Id, Name = r.Feed, GramsPerAnimalPerDay = r.Grams, PricePerKg = r.Price });
    return g;
}
async Task Ev(AnimalGroup g, int daysAgo, string kind, string text, double? qty = null, decimal? cost = null, int? withdrawDays = null) =>
    await repo.SaveAsync(new AnimalLog { GroupId = g.Id, Date = Ago(daysAgo), Kind = kind, Text = text, Quantity = qty, Cost = cost, WithdrawalUntil = withdrawDays is null ? null : Ago(daysAgo).AddDays(withdrawDays.Value) });
var hens = await Group("Laying hens", "chicken", "Lohmann Brown and Rhode Island Red", 34, "coop", "34 hens after the five chicks and one fox loss. Moult is starting: expect fewer eggs.", ("Layer pellets", 115, 2.4m), ("Whole corn (evening)", 25, 1.5m), ("Oyster shell", 6, 3.2m));
var ducks = await Group("Pekin ducks", "duck", "Pekin", 6, "pen", "Free-range by day; need the pond water changed weekly.", ("Wheat", 100, 1.6m), ("Duck pellets", 60, 2.5m));
var rabbits = await Group("New Zealand White rabbits", "rabbit", "New Zealand White", 12, "pen", "4 adults + 8 weaned kits. Sell or keep the kits by Christmas.", ("Rabbit pellets", 90, 2.8m), ("Hay", 150, 0.6m));
var goats = await Group("Carpathian goats", "goat", "Carpathian goat", 4, "barn", "Two in milk, two dry. Hoof trimming due.", ("Hay", 1500, 0.6m), ("Grain mix", 300, 1.9m));
var pigs = await Group("Mangalița pigs", "pig", "Mangalița", 2, "barn", "Raised for the winter slaughter in December. Windfall apples and kitchen scraps.", ("Grain mix", 2000, 1.9m), ("Kitchen scraps", 1500, 0m));
for (var d = 60; d >= 0; d--) { var eggs = Math.Clamp((int)Math.Round(29 - Math.Max(0, 38 - d) * 0.0 - (60 - d) * 0.13 + rnd.Next(-2, 3)), 12, 30); if (d < 10) eggs -= 3; await Ev(hens, d, "production", d == 0 ? "Morning collection" : "", eggs); }
for (var d = 45; d >= 0; d -= 1) await Ev(ducks, d, "production", "", 3 + rnd.Next(0, 3));
for (var d = 40; d >= 0; d -= 1) await Ev(goats, d, "production", "Milk, morning and evening", Math.Round(3.0 + rnd.NextDouble() * 1.0, 1));
await Ev(hens, 70, "count", "Five chicks joined the flock", 5); await Ev(hens, 9, "count", "Lost one hen to a fox", -1); await Ev(hens, 12, "treatment", "Vitamins and electrolytes in the water (after the heat)", null, 18m, 0);
await Ev(hens, 40, "expense", "Layer pellets, 3 × 25 kg", null, 175m); await Ev(hens, 6, "expense", "Layer pellets, 2 × 25 kg", null, 118m); await Ev(hens, 4, "note", "Moult starting on three hens: feathers on the run floor, egg count down.");
await Ev(goats, 4, "treatment", "Dewormer (ivermectin, vet's dose) for all four", null, 45m, 14); await Ev(goats, 3, "note", "Hooves need trimming; ask the vet at the next visit."); await Ev(goats, 28, "expense", "Hay bales (40)", null, 320m);
await Ev(goats, 60, "note", "Kidding season over. Both kids sold to a neighbour.");
await Ev(rabbits, 50, "count", "Eight kits born", 8); await Ev(rabbits, 20, "treatment", "Coccidiosis treatment in the water (5 days)", null, 32m, 7); await Ev(rabbits, 35, "expense", "Rabbit pellets, 25 kg", null, 70m);
await Ev(rabbits, 2, "note", "Two kits sneezing: separated and watching. Call the vet if it continues.");
await Ev(pigs, 30, "treatment", "Erysipelas vaccination (vet visit)", null, 60m, 0); await Ev(pigs, 100, "count", "Two weaners bought", 2); await Ev(pigs, 100, "expense", "Two weaners", null, 700m);
await Ev(pigs, 14, "expense", "Grain mix, 100 kg", null, 190m); await Ev(pigs, 5, "note", "Weighing about 95 kg each. Slaughter booked for 12 December.");
await Ev(ducks, 80, "expense", "Pekin ducklings (6)", null, 90m); await Ev(ducks, 7, "note", "Cleaned and refilled the paddling pool.");

// ---------------------------------------------------------------- cameras
async Task Cam(string name, string brand, string url, double x, double y, double heading, double fov, double height, string? feature, string notes, string? user = "admin")
{
    var g = G(x, y); var c = Plan(x, y);
    await repo.SaveAsync(new Camera { GardenId = garden.Id, Name = name, Kind = "snapshot", Brand = brand, Url = url, Username = user, Lat = g.Lat, Lon = g.Lon, PlanX = Math.Round(c.X), PlanY = Math.Round(c.Y), HeadingDeg = heading, FovDeg = fov, HeightCm = height, FeatureId = feature is null ? null : features[feature].Id, Enabled = true, Notes = notes });
}
await Cam("Gate and driveway", "hikvision", "http://192.168.1.64/ISAPI/Streaming/channels/101/picture", -4, 13.5, 0, 100, 300, "garage", "Hikvision bullet camera on the garage corner. Placeholder address: set the real IP and password to test.");
await Cam("Coop and duck pen", "dahua", "http://192.168.1.108/cgi-bin/snapshot.cgi?channel=1", 13.3, -15.2, 80, 110, 220, "coop", "Dahua/Amcrest camera over the coop door; used to watch for the fox. Placeholder address.");
await Cam("Greenhouse inside", "esp32", "http://192.168.1.90/capture", -0.4, -12, 90, 70, 200, "greenhouse", "ESP32-CAM on a 5 V supply, time-lapse of the aquaponic beds. Placeholder address.", null);
await Cam("Garden overview (old phone)", "ipwebcam", "http://192.168.1.77:8080/shot.jpg", 24.5, 12.5, 250, 80, 260, "veg", "Old Android phone running IP Webcam, on the shed corner. Placeholder address.", null);

// ---------------------------------------------------------------- greenhouse: aquaponics, layout, sensor history
var setup = await gh.SetupAsync(features["greenhouse"].Id);
setup.System = "aquaponic"; GreenhouseCatalog.ApplyDefaults(setup); setup.WidthCm = 400; setup.LengthCm = 600; setup.AutoControl = true; setup.TempMax = 27; setup.TempMin = 6; setup.HumidityMax = 85;
await repo.SaveAsync(setup);
async Task Item(string kind, string name, double x, double y, double w, double h, string? notes = null) => await repo.SaveAsync(new GreenhouseItem { FeatureId = features["greenhouse"].Id, Kind = kind, Name = name, X = x, Y = y, Width = w, Height = h, Notes = notes });
await Item("tank", "Fish tank (1,000 L)", 20, 20, 150, 150, "Goldfish and carp; feed once a day, less in cold weather.");
await Item("growbed", "Lettuce raft bed", 200, 20, 160, 100, "Lettuce and rocket on floating rafts.");
await Item("growbed", "Herb media bed", 200, 140, 160, 100, "Basil and mint in clay pebbles; basil finished for the year.");
await Item("channel", "Return channel", 20, 200, 340, 22);
await Item("bench", "Seedling bench A", 20, 270, 160, 60, "Overwintering pepper cuttings.");
await Item("bench", "Seedling bench B", 200, 270, 160, 60, "Autumn salads in trays.");
await Item("pump", "Water pump", 100, 175, 30, 30, "Check the filter monthly."); await Item("sensor", "Air sensor (DHT22)", 190, 560, 30, 30); await Item("sensor", "Water sensor (temperature, pH)", 60, 140, 30, 30);
await Item("vent", "Roof vent", 180, 3, 90, 12, "Auto-opens above 27 °C or in high humidity.");
for (var h = 36; h >= 1; h--)
{
    var t = DateTime.UtcNow.AddHours(-h); var dayHour = t.ToLocalTime().Hour; var warm = Math.Sin((dayHour - 7) / 24.0 * 2 * Math.PI);
    var air = Math.Round(11 + 7 * Math.Max(warm, -0.4) + rnd.NextDouble(), 1); var hum = Math.Round(Math.Clamp(78 - 12 * warm + rnd.Next(-3, 4) + (h < 8 ? 9 : 0), 55, 96), 0);
    await gh.IngestAsync(features["greenhouse"].Id, new Dictionary<string, double> { ["air_temp"] = air, ["humidity"] = hum, ["water_temp"] = Math.Round(19 + 1.5 * warm + rnd.NextDouble() * 0.4, 1), ["ph"] = Math.Round(6.9 + rnd.NextDouble() * 0.2, 2), ["water_level"] = 82 - h * 0.05 }, t);
}
await gh.IngestAsync(features["greenhouse"].Id, new Dictionary<string, double> { ["air_temp"] = 13.4, ["humidity"] = 91, ["water_temp"] = 19.8, ["ph"] = 6.55, ["water_level"] = 80, ["ec"] = 1.3 }, DateTime.UtcNow.AddMinutes(-3));

// ---------------------------------------------------------------- tasks (done, overdue, now, coming up)
async Task Task_(string title, string activity, int dueOffset, bool done = false, string? plant = null, string? notes = null) =>
    await repo.SaveAsync(new GardenTask { Title = title, Activity = activity, DueDate = dueOffset >= 0 ? Ahead(dueOffset) : Ago(-dueOffset), Done = done, CompletedUtc = done ? Utc(Math.Max(0, -dueOffset)) : null, PlantId = plant, Notes = notes });
await Task_("Sow tomatoes and peppers indoors", "SowIndoors", -215, true, "tomato"); await Task_("Plant out tomatoes after the last frost", "Transplant", -170, true, "tomato");
await Task_("Pinch out tomato side shoots weekly", "Prune", -140, true, "tomato"); await Task_("Harvest first potatoes", "Harvest", -120, true, "potato"); await Task_("Net the cabbages against butterflies", "Inspect", -135, true, "cabbage");
await Task_("Harvest and dry the onions", "Harvest", -105, true, "onion"); await Task_("Pick walnuts and dry them", "Harvest", -25, true, "walnut"); await Task_("Plant garlic cloves", "SowDirect", -14, true, "garlic", "Done on 15 October, mulched with straw.");
await Task_("Clean out and limewash the goat shed", "Other", -12, true); await Task_("Put up the fleece tunnel over the lettuce", "Other", -35, true);
await Task_("Replace the rotten fence posts on the east side", "Other", -9, false, null, "Two posts near the walnut. The pigs lean on it; fix before winter.");
await Task_("Water the Monstera and move the rubber plant from the radiator", "Water", -2, false);
await Task_("Trim the goats' hooves", "Other", -4, false, null, "Ask the vet to show how at the next visit.");
await Task_("Bring in the last pumpkins before a hard frost", "Harvest", 0, false, "pumpkin");
await Task_("Check the coop pop-hole door and the buried wire after the fox", "Inspect", 0, false);
await Task_("Wrap the rosemary and the apricot in fleece", "Other", 2, false, "rosemary");
await Task_("Collect leaves for leaf mould and mulch", "Other", 3, false); await Task_("Whitewash the fruit tree trunks", "Prune", 7, false, "apple");
await Task_("Cut out old raspberry canes", "Prune", 10, false, "raspberry"); await Task_("Test spare seed for germination (phacelia, cucumber)", "Inspect", 12, false);
await Task_("Slaughter the pigs (booked), prepare the cellar and the smokehouse", "Other", 44, false, null, "Booked with the local butcher for 12 December.");
await Task_("Order seeds and seed potatoes for spring", "Other", 60, false); await Task_("Winter pruning of the apple and pear trees", "Prune", 75, false, "apple");
await Task_("Sow tomatoes and peppers indoors", "SowIndoors", 135, false, "tomato");

// ---------------------------------------------------------------- journal: a season in notes
async Task Entry(int daysAgo, string title, string text, string tags, string? plant = null) => await repo.SaveAsync(new JournalEntry { Date = Ago(daysAgo), Title = title, Text = text, Tags = tags, PlantId = plant });
await Entry(176, "Planting day", "Cold nights are finally over. Planted out 6 tomatoes, 6 peppers and 4 aubergines in the raised beds and the greenhouse. Watered with the rain-barrel water to avoid shock.", "planting,spring", "tomato");
await Entry(162, "Bees swarmed", "One hive swarmed in the afternoon. Caught the swarm in a box on the hedge and put it in the new hive. Hive 2 is now strong.", "bees");
await Entry(150, "June frost scare", "Forecast said 1 °C. Covered the beans and squash with fleece and sheets. No damage, but the apricot had already lost its blossom in April.", "weather,frost");
await Entry(128, "First harvest", "Radishes, lettuce, spinach and the first early potatoes. The hens love the lettuce thinnings.", "harvest", "lettuce");
await Entry(112, "Potato beetles", "Hand-picked beetles every morning for two weeks, dropped them in soapy water. Less each day.", "pests,potato", "potato");
await Entry(95, "Heat wave", "Five days over 32 °C. Watered the garden early, shaded the greenhouse and the fish tank. Hens panting; added shade and extra water.", "weather,animals");
await Entry(78, "Tomato glut", "Canned 24 jars of tomato sauce and dried 3 trays. Gave a crate to the neighbours. Kapia peppers roasted and frozen.", "harvest,preserving", "tomato");
await Entry(60, "Late blight", "Blight hit the tomatoes after humid weather. Removed all infected leaves and burned them. Next year: more spacing and no overhead watering.", "pests,tomato", "tomato");
await Entry(44, "Storm", "A strong storm broke a limb on the plum tree and tore the shade cloth on the greenhouse. Fixed the cloth the next day.", "weather,trees");
await Entry(30, "Apple harvest", "Ionathan and Golden Delicious are in: about 180 kg. Pressed juice with the neighbour's press and stored the Reinettes in the cellar.", "harvest,fruit", "apple");
await Entry(22, "Walnuts", "Old walnut gave 18 kg this year. Dried on racks in the attic; the squirrels got some.", "harvest", "walnut");
await Entry(14, "Garlic planted", "Sixty cloves of our own hardneck, 15 cm apart, mulched with straw. The cover crop (rye and clover) is sown on the resting bed.", "planting,autumn", "garlic");
await Entry(9, "Fox visit", "Lost one hen. Tracks along the north fence. Buried wire around the run and added a second latch on the pop-hole door.", "animals,predators");
await Entry(4, "First frost", "Hard frost 5 October already took the basil and the cucumbers. Brought the peppers in; the Hokkaido pumpkins are still outside.", "weather");
await Entry(1, "Plan for November", "Whitewash trees, wrap the apricot, mulch beds, insulate the greenhouse north wall, fix the east fence. Fewer eggs: hens are moulting. Check the sneezing rabbit kits again.", "plan");

// ---------------------------------------------------------------- money
async Task Money(int daysAgo, string cat, decimal amount, string text, bool income = false) => await repo.SaveAsync(new BudgetEntry { GardenId = garden.Id, Date = Ago(daysAgo), Category = cat, Amount = amount, IsIncome = income, Text = text });
await Money(205, "seeds", 85, "Seeds and seed potatoes"); await Money(200, "plants", 480, "Apricot, quince and 6 blueberries"); await Money(195, "tools", 340, "Wheelbarrow, loppers and a good hoe");
await Money(190, "infrastructure", 650, "Fence posts and wire"); await Money(180, "utilities", 120, "Electricity (greenhouse pump and lights), 3 months"); await Money(150, "other", 60, "Rainwater tank cleaning");
await Money(135, "infrastructure", 280, "Greenhouse shade cloth and fittings"); await Money(120, "tools", 95, "Spare fence energiser battery"); await Money(110, "vet", 90, "Vet visit: goats and rabbits");
await Money(95, "plants", 120, "Lettuce and herb plugs for the aquaponics bench"); await Money(80, "utilities", 110, "Electricity, 3 months"); await Money(60, "seeds", 55, "Autumn seeds: lettuce, spinach, rye");
await Money(45, "infrastructure", 190, "Barn window and hinges"); await Money(35, "other", 40, "Jars and lids for preserving"); await Money(20, "utilities", 98, "Firewood, 4 m³");
for (var w = 0; w < 26; w++) await Money(7 * w + 3, "sales", 40 + rnd.Next(0, 25), "Eggs sold to neighbours and at the weekend market", true);
await Money(70, "sales", 230, "Honey, 12 jars", true); await Money(32, "sales", 160, "Apple juice, 40 bottles", true); await Money(18, "sales", 120, "Surplus tomatoes, peppers and pumpkins at the market", true);

// patio, pergola and the small things that make a place lived in
await Bed("patio", "patio", "Patio", -12.5, 6.2, 300, 200, notes: "Gravel-set stone slabs; the family eats here from May to September.");
await Bed("pergola", "pergola", "Pergola with grape", -12.5, 6.2, 300, 200, notes: "Table grape and a hop on the posts. Needs a coat of linseed oil every second spring.");
await Bed("firepit", "firepit", "Fire pit", -12.5, 2.2, 120, 120); await Bed("barrel", "barrel", "Rain barrel (roof run-off)", -22.4, 6.4, 60, 60); await Bed("woodpile", "woodpile", "Firewood stack (drying)", -26, 10, 80, 300, notes: "4 m³ stacked in September: dry by next winter.");
await Bed("mailbox", "mailbox", "Mailbox", -5.5, 20.5, 30, 25); await Bed("hotel", "insecthotel", "Insect hotel", 28.5, -4, 60, 40, notes: "Built from pallets, bamboo and pine cones. Leaf-cutter bees moved in this summer.");
await Bed("lawn", "lawn", "Lawn (mown, for the children)", -10, -8, 500, 400); await Bed("flowers", "flowerbed", "Flower bed by the gate", -2, 18, 250, 100); await Bed("coldframe", "coldframe", "Cold frame", 9, 21, 120, 80, notes: "Winter lettuce and early radish. Open on sunny days.");

// ---------------------------------------------------------------- around the house: foundation beds, planted over the years
await Bed("house_s", "bed", "South border by the house", -18, 5.0, 800, 80, notes: "Sunny, dry and sheltered by the wall: the herbs and lavender love it. Bees all summer.");
await Bed("house_w", "bed", "Grape wall (west)", -23.9, 10, 70, 600, notes: "Grapes trained on wires up the west wall; pruned every February.");
await Bed("house_n", "bed", "Shade border (north of the house)", -18, 14.9, 800, 90, notes: "Damp and shady: rhubarb, chives and mint do well here, nothing that needs sun.");
await Bed("house_e", "bed", "Pollinator corner (between house and garage)", -11.5, 9.5, 70, 300, notes: "Sown with flowers for bees and hoverflies. Self-seeds a lot: thin each spring.");
await Plant("house_s", "lavender", 8, 1100, PlantingStatus.Growing, null, "Cut back after flowering in August. Replace the two oldest plants next spring.");
await Plant("house_s", "rosemary", 2, 1100, PlantingStatus.Growing, null, "Survived two winters against the wall, wrapped in fleece."); await Plant("house_s", "thyme", 6, 1100, PlantingStatus.Growing); await Plant("house_s", "sage", 2, 1100, PlantingStatus.Growing);
await Plant("house_w", "grape", 3, 1460, PlantingStatus.Growing, "Local table grape", "Good crop this year, 25 kg. Mildew late in August: improve the airflow with leaf removal.");
await Plant("house_n", "rhubarb", 2, 1400, PlantingStatus.Growing, null, "Divide next spring."); await Plant("house_n", "chives", 6, 1100, PlantingStatus.Growing); await Plant("house_n", "mint", 2, 900, PlantingStatus.Growing, null, "Sunk in buckets.");
await Plant("house_n", "guelder-rose", 2, 1000, PlantingStatus.Growing, null, "Berries for the birds in winter.");
await Plant("house_e", "echinacea", 4, 800, PlantingStatus.Growing); await Plant("house_e", "borage", 6, 400, PlantingStatus.Finished); await Plant("house_e", "calendula", 8, 400, PlantingStatus.Growing); await Plant("house_e", "anise-hyssop", 3, 700, PlantingStatus.Growing);
await Note("house", 1000, "note", "Bought the property. Roof good, chimney needs work, windows to replace one by one."); await Note("house", 720, "maintenance", "Replaced the kitchen and sunroom windows with double glazing.");
await Note("house", 400, "maintenance", "Repainted the south wall; the lavender border was cut back to protect it."); await Note("house", 300, "maintenance", "New wood stove installed in the living room; chimney lined.");
await Note("house", 3, "note", "NEXT: bleed the radiators, bring the lemon geranium cuttings indoors before frost, check the cellar for mice.");

// ---------------------------------------------------------------- house plans: ground floor and cellar
const double HX = 50, HY = 50;     // top-left corner of the house outline inside the plan (cm)
var hid = features["house"].Id;
async Task Wall(int floor, double x1, double y1, double x2, double y2) => await repo.SaveAsync(InteriorCatalog.WallBetween(hid, floor, HX + x1, HY + y1, HX + x2, HY + y2));
async Task Room(string name, double x1, double y1, double x2, double y2, int floor = 0) =>
    await repo.SaveAsync(new InteriorItem { FeatureId = hid, Floor = floor, Kind = "room", Name = name, X = HX + x1 + 8, Y = HY + y1 + 8, Width = x2 - x1 - 16, Height = y2 - y1 - 16 });
async Task Opening(string kind, string name, double cx, double cy, double w, double rot, int floor = 0) =>
    await repo.SaveAsync(new InteriorItem { FeatureId = hid, Floor = floor, Kind = kind, Name = name, Width = w, Height = 15, X = HX + cx - w / 2, Y = HY + cy - 7.5, Rotation = rot });
async Task Thing(string kind, string name, double x, double y, double w, double h, double rot = 0, int floor = 0, string? notes = null, int? plant = null) =>
    await repo.SaveAsync(new InteriorItem { FeatureId = hid, Floor = floor, Kind = kind, Name = name, X = HX + x, Y = HY + y, Width = w, Height = h, Rotation = rot, Notes = notes, InventoryPlantId = plant });

await Room("Living room", 0, 0, 450, 430); await Room("Kitchen", 0, 430, 450, 800); await Room("Hall", 450, 0, 600, 800); await Room("Bedroom", 600, 0, 1000, 400);
await Room("Bathroom", 600, 400, 800, 600); await Room("Pantry", 600, 600, 800, 800); await Room("Sunroom", 800, 400, 1000, 800);
foreach (var (x1, y1, x2, y2) in new[] { (0.0, 0.0, 1000.0, 0.0), (1000.0, 0.0, 1000.0, 800.0), (1000.0, 800.0, 0.0, 800.0), (0.0, 800.0, 0.0, 0.0),
    (450.0, 0.0, 450.0, 800.0), (600.0, 0.0, 600.0, 800.0), (0.0, 430.0, 450.0, 430.0), (600.0, 400.0, 1000.0, 400.0), (800.0, 400.0, 800.0, 800.0), (600.0, 600.0, 800.0, 600.0) })
    await Wall(0, x1, y1, x2, y2);
await Opening("door", "Front door", 525, 800, 100, 0); await Opening("door", "Back door (to the garden)", 525, 0, 100, 0);
await Opening("window", "Living room window", 225, 0, 150, 0); await Opening("window", "Living room window (west)", 0, 215, 150, 90); await Opening("window", "Kitchen window", 225, 800, 130, 0);
await Opening("window", "Kitchen window (west)", 0, 615, 110, 90); await Opening("window", "Bedroom window", 800, 0, 150, 0); await Opening("window", "Bedroom window (east)", 1000, 200, 140, 90);
await Opening("window", "Bathroom window", 700, 400, 60, 0); await Opening("window", "Sunroom windows", 900, 800, 160, 0); await Opening("window", "Sunroom windows (east)", 1000, 600, 200, 90);
await Opening("door", "Living room door", 450, 215, 90, 90); await Opening("door", "Kitchen door", 450, 615, 90, 90); await Opening("door", "Bedroom door", 600, 200, 90, 90);
await Opening("door", "Bathroom door", 600, 500, 80, 90); await Opening("door", "Pantry door", 600, 700, 80, 90); await Opening("door", "Sunroom door", 800, 700, 80, 90);
await Thing("stairs", "Cellar stairs", 475, 500, 100, 200, notes: "Down to the cellar: preserves, potatoes, apples and the wine barrel.");
await Thing("woodstove", "Wood stove", 360, 20, 70, 70, notes: "Main heating in winter. Chimney swept in October."); await Thing("sofa", "Sofa", 80, 280, 200, 90); await Thing("shelf", "Bookshelf", 70, 15, 200, 30);
await Thing("radiator", "Radiator", 8, 160, 100, 12, 90); await Thing("table", "Kitchen table", 130, 510, 160, 90); await Thing("counter", "Kitchen counter", 20, 735, 240, 60); await Thing("sink", "Sink", 270, 745, 80, 50);
await Thing("stove", "Stove", 360, 735, 60, 60); await Thing("fridge", "Fridge", 375, 650, 65, 65); await Thing("bed", "Bed", 690, 60, 160, 200); await Thing("wardrobe", "Wardrobe", 840, 330, 120, 60);
await Thing("radiator", "Radiator", 984, 130, 100, 12, 90); await Thing("sink", "Bathroom sink", 640, 415, 80, 50); await Thing("shelf", "Pantry shelf", 610, 610, 180, 30, notes: "Jars: tomato sauce, pickles, jam, compote.");
await Thing("shelf", "Pantry shelf (drying)", 610, 755, 180, 30, notes: "Herbs and hot peppers drying on strings above."); await Thing("planter", "Sunroom planter (north)", 815, 415, 170, 40); await Thing("planter", "Sunroom planter (south)", 815, 745, 170, 40);
// basement
foreach (var (x1, y1, x2, y2) in new[] { (0.0, 400.0, 600.0, 400.0), (600.0, 400.0, 600.0, 800.0), (600.0, 800.0, 0.0, 800.0), (0.0, 800.0, 0.0, 400.0) }) await Wall(-1, x1, y1, x2, y2);
await Room("Cellar", 0, 400, 600, 800, -1); await Opening("window", "Cellar window", 200, 800, 60, 0, -1); await Thing("stairs", "Stairs up", 475, 500, 100, 200, floor: -1);
await Thing("shelf", "Preserves shelves", 20, 420, 300, 30, floor: -1, notes: "Tomato sauce, pickles, jam. Count the jars every November."); await Thing("shelf", "Apple racks", 20, 760, 300, 30, floor: -1, notes: "Reinette keeps until March. Check weekly for rot.");
await Thing("planter", "Potato crates", 340, 420, 120, 60, floor: -1, notes: "Cool and dark. Check for sprouts in February."); await Thing("planter", "Carrots in damp sand", 340, 740, 120, 40, floor: -1);
await Thing("table", "Work table", 200, 560, 160, 90, floor: -1); await Thing("shelf", "Wine barrel stand", 40, 600, 120, 60, floor: -1, notes: "Fruit wine from the plums and the grapes.");

// indoor plants on the plan (some are in the sunroom; the parsley is still to be placed)
await House("Basil (windowsill, winter)", "basil", PlantLocation.Indoor, "Sunroom", 4, 3, 2, "healthy", "Pinch the flower buds; best on the sunniest sill.", 40);
await House("Rosemary (pot, overwintering)", "rosemary", PlantLocation.Indoor, "Sunroom", 1, 9, 4, "healthy", "Brought in before the first frost. Do not overwater.", 25);
await House("Chives (pot)", "chives", PlantLocation.Indoor, "Sunroom", 1, 5, 6, "attention", "Watering is overdue; yellow tips.", 60);
await House("Mint (pot)", "mint", PlantLocation.Indoor, "Pantry", 1, 4, 2, "healthy", "For tea in winter.", 70);
await House("Parsley (pot)", "parsley", PlantLocation.Indoor, "Kitchen", 2, 4, 3, "healthy", "Not yet on the house plan.", 15);
var indoor = await repo.ListAsync<InventoryPlant>(q => q.Where(p => !p.IsTree && p.Location == PlantLocation.Indoor));
int? Inv(string name) => indoor.FirstOrDefault(p => p.Name.StartsWith(name))?.Id;
await Thing("plant", "", 40, 190, 50, 50, plant: Inv("Monstera")); await Thing("plant", "", 190, 20, 50, 50, plant: Inv("Rubber plant")); await Thing("plant", "", 185, 740, 50, 50, plant: Inv("Pothos"));
await Thing("plant", "", 80, 740, 50, 50, plant: Inv("Aloe")); await Thing("plant", "", 490, 30, 50, 50, plant: Inv("Snake plant")); await Thing("plant", "", 920, 130, 50, 50, plant: Inv("Peace lily"));
await Thing("plant", "", 730, 420, 50, 50, plant: Inv("Spider plant")); await Thing("plant", "", 830, 420, 50, 50, plant: Inv("Basil")); await Thing("plant", "", 900, 420, 50, 50, plant: Inv("Rosemary"));
await Thing("plant", "", 830, 750, 50, 50, plant: Inv("Chives")); await Thing("plant", "", 650, 760, 50, 50, plant: Inv("Mint"));

// ---------------------------------------------------------------- years of history: older seasons, lessons learned, watering
async Task Water(int daysAgo, string plant, string bed) => await repo.SaveAsync(new JournalEntry { Date = Ago(daysAgo), Title = $"Watered {plant} ({bed})", Text = "", Tags = "watering", PlantId = P(plant) });
await Water(2, "lettuce", "Raised bed 3"); await Water(2, "spinach", "Raised bed 3"); await Water(3, "kale", "Cabbage family bed"); await Water(3, "cabbage", "Cabbage family bed"); await Water(3, "broccoli", "Cabbage family bed");
await Water(6, "carrot", "Raised bed 4"); await Water(9, "leek", "Garlic and leek bed"); await Water(5, "beetroot", "Raised bed 4"); await Water(1, "lettuce", "Greenhouse"); await Water(15, "garlic", "Garlic and leek bed");
await Entry(1090, "We bought the land", "First walk with the old owner. South slope, a spring-fed well, an old walnut and one apple tree (Reinette) that is older than the house. Soil looks good. Plan: house repairs first, garden second.", "history");
await Entry(1040, "First fence posts", "Marked the boundary and set the first 40 posts along the road. Bears were seen on the ridge: electric fence on the list.", "history,fence");
await Entry(980, "Dug the first beds", "Hand-dug 4 beds near the house, added manure from the neighbour. Planted potatoes, beans and pumpkins: a modest first year but the family ate well.", "history,beds");
await Entry(720, "Planted the orchard", "Planted 2 apples, plum and pear, 5 m apart, staked, tubed against hares. Mulch with straw in a 1 m circle.", "history,trees");
await Entry(700, "Greenhouse from old windows", "Built the 4 x 6 m greenhouse using old windows from the village. Concrete footing, polycarbonate roof, vent at the ridge.", "history,greenhouse");
await Entry(540, "Hedge planted", "300 bare-root plants: hawthorn, hazel, elder, rose hips and sea-buckthorn along the west side. Watered all summer.", "history,hedge");
await Entry(420, "First honey", "Two hives gave 35 kg. Learned: do not take too much; leave 15 kg for the winter.", "history,bees");
await Entry(380, "Pond dug", "Dug and lined the pond for frogs and dragonflies. Planted marsh marigold and water mint. Slugs are fewer since the frogs came.", "history,pond");
await Entry(365, "Lessons from last year", "Tomato blight in a wet August: grow in the greenhouse or under the roof overhang, space wider, remove lower leaves. Rotate the potatoes: scab on the old bed. Keep records of everything.", "history,lessons");
await Entry(330, "Frost on the apricot (again)", "Second year in a row the blossom was lost on 17 April. Fleece next time, or plant a later variety.", "history,frost");
await Entry(290, "Garlic and the first snow", "Planted garlic in October; snow came on 11 November and the bulbs overwintered perfectly under straw.", "history");
foreach (var (b, plant, qty, ago, st) in new (string, string, int, int, PlantingStatus)[]
{
    ("rb1", "pepper", 6, 530, PlantingStatus.Finished), ("rb2", "tomato", 6, 530, PlantingStatus.Finished), ("rb3", "bean", 20, 530, PlantingStatus.Finished), ("rb4", "cabbage", 8, 520, PlantingStatus.Finished),
    ("gb1", "bean", 30, 530, PlantingStatus.Finished), ("gb2", "potato", 40, 530, PlantingStatus.Finished), ("gb3", "potato", 30, 520, PlantingStatus.Finished), ("gb4", "sweetcorn", 12, 530, PlantingStatus.Finished),
})
    await Plant(b, plant, qty, ago, st, null, "Last year's crop here (for crop rotation).");
await Money(1000, "infrastructure", 2200, "Roof repair and the chimney lining"); await Money(900, "tools", 420, "First tools: spade, rake, secateurs"); await Money(720, "plants", 600, "Orchard trees and stakes");
await Money(700, "infrastructure", 1400, "Greenhouse: polycarbonate, concrete, hinges"); await Money(540, "plants", 380, "Hedge plants, 300 bare-root"); await Money(420, "sales", 520, "First honey and wax", true);
await Money(380, "infrastructure", 350, "Pond liner and pump"); await Money(330, "sales", 1100, "Eggs, honey and vegetables over the year", true);

// outdoor soil sensors: the vegetable garden is dry (red dot), the orchard is fine (green dot)
foreach (var k in new[] { "rb1", "rb2", "rb3", "rb4", "herb", "gb1", "gb2", "gb3", "gb4", "gb5", "gb6" }) { beds[k].SensorFeatureId = features["veg"].Id; await repo.SaveAsync(beds[k]); }
foreach (var t in trees) { beds[t.Key].SensorFeatureId = features["orchard"].Id; await repo.SaveAsync(beds[t.Key]); }
await gh.IngestAsync(features["veg"].Id, new Dictionary<string, double> { ["soil_moisture"] = 21, ["air_temp"] = 11.2 }, DateTime.UtcNow.AddMinutes(-12), default, applyRules: false);
await gh.IngestAsync(features["orchard"].Id, new Dictionary<string, double> { ["soil_moisture"] = 58, ["air_temp"] = 10.8 }, DateTime.UtcNow.AddMinutes(-9), default, applyRules: false);

// a few pictures for the photo timeline (the satellite picture stands in for real photos)
var photoSource = Path.Combine(paths.SiteDir, "backdrop-1.jpg");
if (File.Exists(photoSource))
{
    var store = new SeedPhotoStore(paths);
    foreach (var (ago, kind, owner, plant, caption) in new (int, string, int?, string?, string)[]
    {
        (120, "bed", beds["rb1"].Id, "tomato", "Tomatoes planted out, first flowers"), (85, "bed", beds["rb1"].Id, "tomato", "Blight on the lower leaves: removed them"),
        (60, "animal", null, null, "Hens enjoying the lettuce thinnings"), (45, "plant", null, "pumpkin", "The two giant pumpkins"), (30, "place", features["greenhouse"].Id, null, "Aquaponic bench, lettuce rafts"),
        (14, "bed", beds["gb4"].Id, "garlic", "Garlic planted and mulched"), (3, "general", null, null, "First frost on the beds this morning"),
    })
    {
        var name = await store.AddFileAsync(photoSource); if (name is null) continue;
        var ownerId = kind == "animal" ? hens.Id : owner;
        await repo.SaveAsync(new Photo { Date = Ago(ago), File = name, Caption = caption, OwnerKind = kind, OwnerId = ownerId, PlantId = plant });
    }
}

// harvest amounts for the season review
foreach (var (ago, plant, bed, qty, unit) in new (int, string, string, double, string)[]
{
    (128, "lettuce", "rb3", 1.2, "kg"), (120, "radish", "rb3", 0.8, "kg"), (112, "potato", "gb1", 18, "kg"), (100, "potato", "gb1", 22, "kg"), (96, "potato", "gb1", 22, "kg"),
    (90, "tomato", "rb1", 6, "kg"), (84, "tomato", "rb1", 14, "kg"), (77, "tomato", "rb1", 19, "kg"), (70, "tomato", "greenhouse", 11, "kg"), (63, "tomato", "greenhouse", 16, "kg"),
    (80, "pepper", "rb2", 7, "kg"), (66, "pepper", "rb2", 9, "kg"), (74, "cucumber", "greenhouse", 40, "pcs"), (60, "cucumber", "greenhouse", 28, "pcs"),
    (55, "bean", "gb2", 4.5, "kg"), (50, "pumpkin", "gb2", 3, "pcs"), (42, "onion", "rb4", 9, "kg"), (38, "carrot", "rb4", 8, "kg"), (30, "apple", "t_apple1", 45, "kg"),
    (30, "apple", "t_apple3", 70, "kg"), (26, "walnut", "t_walnut", 18, "kg"), (20, "cabbage", "gb3", 12, "kg"), (6, "kale", "gb3", 2.4, "kg"),
})
    await repo.SaveAsync(new Harvest { Date = Ago(ago), PlantId = P(plant), BedId = beds[bed].Id, Quantity = qty, Unit = unit });

// animal care: vaccination due soon, young expected, feed running low, weights
await repo.SaveAsync(new AnimalLog { GroupId = hens.Id, Date = Ago(80), Kind = "vaccination", Text = "Newcastle disease booster", DueDate = Ahead(3) });
await repo.SaveAsync(new AnimalLog { GroupId = goats.Id, Date = Ago(75), Kind = "worming", Text = "Rotated wormer, faecal count was moderate", DueDate = Ahead(15) });
await repo.SaveAsync(new AnimalLog { GroupId = rabbits.Id, Date = Ago(26), Kind = "breeding", Text = "Doe Lili mated with Max", DueDate = Ahead(5), Quantity = 1 });
foreach (var (g, ration, kg) in new[] { (goats, "Hay", 28.0), (hens, "Layer pellets", 160.0), (pigs, "Grain mix", 900.0) })
    if ((await repo.ListAsync<FeedRation>(q => q.Where(r => r.GroupId == g.Id && r.Name == ration))).FirstOrDefault() is { } fr) { fr.StockKg = kg; fr.StockDate = Ago(1); await repo.SaveAsync(fr); }
foreach (var (ago, kg) in new[] { (60, 41.0), (30, 58.0), (7, 71.0) }) await repo.SaveAsync(new AnimalLog { GroupId = pigs.Id, Date = Ago(ago), Kind = "weight", Quantity = kg, Text = "Weighed with the tape" });

// ---------------------------------------------------------------- settings: the demo knows it is demo data
var settings = new SettingsService(new ContextFactory(paths));
await settings.SetAsync("language", "en");
Console.WriteLine($"Demo homestead created in {dataDir}");
Console.WriteLine($"  places: {features.Count}, plan objects: {beds.Count}, outline pins: {outline.Count}");
