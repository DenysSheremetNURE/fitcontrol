namespace MealPlanningService.Entities;

public class MealPlan
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public DateOnly PlanDate { get; set; }

    // Grows with every change of the plan. Events carry it so consumers can drop stale messages.
    public int Version { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<Meal> Meals { get; set; } = new();
}
