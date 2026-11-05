using System.Globalization;
using System.Reflection;
using MealPlanningService.Entities;
using Microsoft.EntityFrameworkCore;

namespace MealPlanningService.Data;

public static class FoodSeeder
{
    private const string ResourceName = "foods.tsv";

    public static async Task SeedAsync(MealPlanningDbContext db, ILogger logger)
    {
        if (await db.Foods.AnyAsync())
        {
            return;
        }

        var foods = ReadFoods();
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.Foods.AddRange(foods);
        await db.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} foods.", foods.Count);
    }

    private static List<Food> ReadFoods()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);

        reader.ReadLine(); // header

        var foods = new List<Food>();
        while (reader.ReadLine() is { Length: > 0 } line)
        {
            var cells = line.Split('\t');
            foods.Add(new Food
            {
                Id = int.Parse(cells[0], CultureInfo.InvariantCulture),
                Name = cells[1],
                Category = cells[2],
                KcalPer100G = decimal.Parse(cells[3], CultureInfo.InvariantCulture),
                ProteinPer100G = decimal.Parse(cells[4], CultureInfo.InvariantCulture),
                FatPer100G = decimal.Parse(cells[5], CultureInfo.InvariantCulture),
                CarbsPer100G = decimal.Parse(cells[6], CultureInfo.InvariantCulture)
            });
        }

        return foods;
    }
}
