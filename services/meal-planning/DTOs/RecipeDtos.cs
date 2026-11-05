using System.ComponentModel.DataAnnotations;
using MealPlanningService.Services;

namespace MealPlanningService.DTOs;

public record RecipeIngredientRequest(
    [Range(1, int.MaxValue)] int FoodId,
    [Range(0.01, 100000)] decimal AmountG);

public record RecipeRequest(
    [Required, MaxLength(200)] string Title,
    [MaxLength(2000)] string? Description,
    [MaxLength(10000)] string? Instructions,
    [Range(1, 100)] int Servings,
    [Required, MinLength(1), MaxLength(100)] List<RecipeIngredientRequest> Ingredients);

public record RecipeIngredientResponse(
    int FoodId,
    string FoodName,
    decimal AmountG,
    NutritionFacts Nutrition);

public record RecipeResponse(
    Guid Id,
    string Title,
    string? Description,
    string? Instructions,
    int Servings,
    bool IsMine,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<RecipeIngredientResponse> Ingredients,
    NutritionFacts Nutrition,
    NutritionFacts NutritionPerServing);

public record RecipeSummaryResponse(
    Guid Id,
    string Title,
    string? Description,
    int Servings,
    bool IsMine,
    NutritionFacts NutritionPerServing);
