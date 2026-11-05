namespace MealPlanningService.DTOs;

public record FoodResponse(
    int Id,
    string Name,
    string Category,
    decimal KcalPer100G,
    decimal ProteinPer100G,
    decimal FatPer100G,
    decimal CarbsPer100G);

public record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Limit,
    int Offset);
