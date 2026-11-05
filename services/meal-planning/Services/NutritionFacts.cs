using MealPlanningService.Entities;

namespace MealPlanningService.Services;

public readonly record struct NutritionFacts(decimal Kcal, decimal ProteinG, decimal FatG, decimal CarbsG)
{
    public static NutritionFacts ForFood(Food food, decimal grams) => new NutritionFacts(
        food.KcalPer100G,
        food.ProteinPer100G,
        food.FatPer100G,
        food.CarbsPer100G) * grams / 100m;

    // Expects the ingredients and their foods to be loaded.
    public static NutritionFacts ForRecipe(Recipe recipe) =>
        Sum(recipe.Ingredients.Select(i => ForFood(i.Food, i.AmountG)));

    public static NutritionFacts ForItem(MealItem item) => new NutritionFacts(
        item.KcalPerUnit,
        item.ProteinPerUnit,
        item.FatPerUnit,
        item.CarbsPerUnit) * item.Amount;

    public static NutritionFacts Sum(IEnumerable<NutritionFacts> items) =>
        items.Aggregate(new NutritionFacts(), (total, item) => total + item);

    public static NutritionFacts operator +(NutritionFacts a, NutritionFacts b) => new(
        a.Kcal + b.Kcal, a.ProteinG + b.ProteinG, a.FatG + b.FatG, a.CarbsG + b.CarbsG);

    public static NutritionFacts operator *(NutritionFacts a, decimal factor) => new(
        a.Kcal * factor, a.ProteinG * factor, a.FatG * factor, a.CarbsG * factor);

    public static NutritionFacts operator /(NutritionFacts a, decimal divisor) => new(
        a.Kcal / divisor, a.ProteinG / divisor, a.FatG / divisor, a.CarbsG / divisor);

    // Round only for output, so that sums are not built from already rounded values.
    public NutritionFacts Rounded(int decimals = 2) => new(
        Round(Kcal, decimals), Round(ProteinG, decimals), Round(FatG, decimals), Round(CarbsG, decimals));

    private static decimal Round(decimal value, int decimals) =>
        Math.Round(value, decimals, MidpointRounding.AwayFromZero);
}
