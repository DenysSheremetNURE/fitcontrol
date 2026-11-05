namespace MealPlanningService.Entities;

public enum MealType
{
    Breakfast,
    Lunch,
    Dinner,
    Snack
}

public class Meal
{
    public Guid Id { get; set; }
    public Guid MealPlanId { get; set; }
    public MealType MealType { get; set; }

    public MealPlan MealPlan { get; set; } = null!;
    public List<MealItem> Items { get; set; } = new();
}
