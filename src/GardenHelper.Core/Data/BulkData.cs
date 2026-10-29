using GardenHelper.Core.Animals;
using GardenHelper.Core.Domain;

namespace GardenHelper.Core.Data;

/// <summary>Years of made-up records for one garden: used by the stress test and by the demo seed's --big option to see how the app copes with a property that has been kept for a long time.</summary>
public static class BulkData
{
    public static void Add(GardenDbContext db, int gardenId, IReadOnlyList<string> plantIds, DateOnly today, string photoFile = "x.jpg", int scale = 1, int seed = 1)
    {
        var rnd = new Random(seed);
        var beds = 600 * scale;
        for (var b = 0; b < beds; b++)
        {
            var bed = new GardenBed { GardenId = gardenId, Name = "Bulk bed " + b, Kind = "raised", X = (b % 40) * 120, Y = (b / 40) * 120 };
            for (var p = 0; p < 6; p++) bed.Plantings.Add(new BedPlanting { PlantId = plantIds[rnd.Next(plantIds.Count)], Quantity = 5, PlantedDate = today.AddDays(-rnd.Next(1500)), Status = PlantingStatus.Growing });
            db.Beds.Add(bed);
        }
        for (var i = 0; i < 6000 * scale; i++) db.Journal.Add(new JournalEntry { Date = today.AddDays(-rnd.Next(1500)), Title = "Entry " + i, Text = "x", Tags = i % 3 == 0 ? "harvest" : "watering", PlantId = plantIds[rnd.Next(plantIds.Count)] });
        db.SaveChanges();
        var bedIds = db.Beds.Where(b => b.Name.StartsWith("Bulk bed")).Select(b => b.Id).ToList();
        for (var i = 0; i < 4000 * scale; i++) db.Harvests.Add(new Harvest { Date = today.AddDays(-rnd.Next(1500)), PlantId = plantIds[rnd.Next(plantIds.Count)], BedId = bedIds[rnd.Next(bedIds.Count)], Quantity = rnd.Next(1, 30), Unit = "kg" });
        for (var i = 0; i < 3000 * scale; i++) db.Photos.Add(new Photo { Date = today.AddDays(-rnd.Next(1500)), File = photoFile, Caption = "Photo " + i });
        for (var g = 0; g < 20; g++)
        {
            var grp = new AnimalGroup { GardenId = gardenId, Name = "Bulk group " + g, Species = "chicken", Count = 30 };
            grp.Rations.Add(new FeedRation { Name = "Feed", GramsPerAnimalPerDay = 100, PricePerKg = 2 });
            for (var i = 0; i < 700 * scale; i++) grp.Logs.Add(new AnimalLog { Date = today.AddDays(-rnd.Next(1500)), Kind = "production", Quantity = rnd.Next(10, 30) });
            db.AnimalGroups.Add(grp);
        }
        for (var i = 0; i < 3000 * scale; i++) db.Tasks.Add(new GardenTask { Title = "Task " + i, DueDate = today.AddDays(-rnd.Next(1500)), Done = i % 5 != 0 });
        db.SaveChanges();
    }
}
