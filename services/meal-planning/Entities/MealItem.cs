namespace MealPlanningService.Entities;

// Name and nutrition are copied when the item is added, so later changes to a recipe, or its deletion,
// do not rewrite plans and calories that were already consumed. RecipeId and FoodId only record the origin.
public class MealItem
{
    public Guid Id { get; set; }
    public Guid MealId { get; set; }

    // Exactly one of these is set. A recipe is measured in servings, a food in grams.
    public Guid? RecipeId { get; set; }
    public int? FoodId { get; set; }

    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    // Per one serving of a recipe or per one gram of a food.
    public decimal KcalPerUnit { get; set; }
    public decimal ProteinPerUnit { get; set; }
    public decimal FatPerUnit { get; set; }
    public decimal CarbsPerUnit { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    public Meal Meal { get; set; } = null!;
}
