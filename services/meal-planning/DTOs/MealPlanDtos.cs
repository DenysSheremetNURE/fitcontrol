using System.ComponentModel.DataAnnotations;
using MealPlanningService.Entities;
using MealPlanningService.Services;

namespace MealPlanningService.DTOs;

// Nullable with [Required], so that a missing value is rejected instead of becoming the default.
public record MealPlanRequest([Required] DateOnly? PlanDate);

public record MealRequest([Required] MealType? MealType);

public record MealItemRequest(
    Guid? RecipeId,
    [Range(1, int.MaxValue)] int? FoodId,
    [Range(0.01, 100000)] decimal Amount);

public record MealItemAmountRequest([Range(0.01, 100000)] decimal Amount);

public record MealItemResponse(
    Guid Id,
    string SourceType,
    string SourceId,
    string Name,
    decimal Amount,
    string Unit,
    bool Consumed,
    DateTimeOffset? ConsumedAt,
    NutritionFacts Nutrition);

public record MealResponse(
    Guid Id,
    MealType MealType,
    IReadOnlyList<MealItemResponse> Items,
    NutritionFacts Planned,
    NutritionFacts Consumed);

public record MealPlanResponse(
    Guid Id,
    DateOnly PlanDate,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<MealResponse> Meals,
    NutritionFacts Planned,
    NutritionFacts Consumed);

public record MealPlanSummaryResponse(
    Guid Id,
    DateOnly PlanDate,
    int Version,
    NutritionFacts Planned,
    NutritionFacts Consumed);

public record MealNutritionResponse(
    Guid MealId,
    MealType MealType,
    NutritionFacts Planned,
    NutritionFacts Consumed);

public record MealPlanNutritionResponse(
    Guid PlanId,
    DateOnly PlanDate,
    IReadOnlyList<MealNutritionResponse> Meals,
    NutritionFacts Planned,
    NutritionFacts Consumed);
