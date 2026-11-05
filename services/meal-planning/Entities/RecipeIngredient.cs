namespace MealPlanningService.Entities;

public class RecipeIngredient
{
    public Guid Id { get; set; }
    public Guid RecipeId { get; set; }
    public int FoodId { get; set; }
    public decimal AmountG { get; set; }

    public Recipe Recipe { get; set; } = null!;
    public Food Food { get; set; } = null!;
}
