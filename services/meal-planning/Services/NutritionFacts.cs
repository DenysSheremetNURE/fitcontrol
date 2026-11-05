using MealPlanningService.Entities;

namespace MealPlanningService.Services;

public readonly record struct NutritionFacts(decimal Kcal, decimal ProteinG, decimal FatG, decimal CarbsG)
{
    public static NutritionFacts ForFood(Food food, decimal grams) => new(
        food.KcalPer100G * grams / 100m,
        food.ProteinPer100G * grams / 100m,
        food.FatPer100G * grams / 100m,
        food.CarbsPer100G * grams / 100m);

    public static NutritionFacts Sum(IEnumerable<NutritionFacts> items) =>
        items.Aggregate(new NutritionFacts(), (total, item) => total + item);

    public static NutritionFacts operator +(NutritionFacts a, NutritionFacts b) => new(
        a.Kcal + b.Kcal, a.ProteinG + b.ProteinG, a.FatG + b.FatG, a.CarbsG + b.CarbsG);

    public static NutritionFacts operator /(NutritionFacts a, decimal divisor) => new(
        a.Kcal / divisor, a.ProteinG / divisor, a.FatG / divisor, a.CarbsG / divisor);

    // Round only for output, so that sums are not built from already rounded values.
    public NutritionFacts Rounded() => new(Round(Kcal), Round(ProteinG), Round(FatG), Round(CarbsG));

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
