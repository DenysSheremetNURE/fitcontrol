using System.ComponentModel.DataAnnotations;
using MealPlanningService.Data;
using MealPlanningService.DTOs;
using MealPlanningService.Entities;
using MealPlanningService.Extensions;
using MealPlanningService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MealPlanningService.Controllers;

// Recipes form a shared catalog: every signed-in user can read them, only the author can change them.
[ApiController]
[Route("api/meals/recipes")]
public sealed class RecipesController : ControllerBase
{
    private readonly MealPlanningDbContext _dbContext;

    public RecipesController(MealPlanningDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<RecipeSummaryResponse>>> Search(
        [FromQuery, MaxLength(100)] string? search,
        [FromQuery] bool mine = false,
        [FromQuery, Range(0, 100000)] decimal? maxKcalPerServing = null,
        [FromQuery, Range(1, 100)] int limit = 20,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var userId = User.GetUserId();

        var query = _dbContext.Recipes.AsNoTracking();
        foreach (var word in LikeSearch.Words(search))
        {
            var pattern = $"%{word}%";
            query = query.Where(r => EF.Functions.ILike(r.Title, pattern, LikeSearch.Escape));
        }

        if (mine)
        {
            query = query.Where(r => r.OwnerUserId == userId);
        }

        if (maxKcalPerServing is { } maxKcal)
        {
            query = query.Where(r =>
                r.Ingredients.Sum(i => i.Food.KcalPer100G * i.AmountG / 100m) / r.Servings <= maxKcal);
        }

        var total = await query.CountAsync(cancellationToken);

        var recipes = await query
            .Include(r => r.Ingredients)
            .ThenInclude(i => i.Food)
            .OrderBy(r => r.Title)
            .ThenBy(r => r.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var items = recipes
            .Select(r => new RecipeSummaryResponse(
                r.Id,
                r.Title,
                r.Description,
                r.Servings,
                r.OwnerUserId == userId,
                (TotalNutrition(r) / r.Servings).Rounded()))
            .ToList();

        return Ok(new PagedResponse<RecipeSummaryResponse>(items, total, limit, offset));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RecipeResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var recipe = await _dbContext.Recipes.AsNoTracking()
            .Include(r => r.Ingredients)
            .ThenInclude(i => i.Food)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        return recipe is null ? NotFound() : Ok(ToResponse(recipe));
    }

    [HttpPost]
    public async Task<ActionResult<RecipeResponse>> Create(
        [FromBody] RecipeRequest request,
        CancellationToken cancellationToken)
    {
        var foods = await LoadFoodsAsync(request, cancellationToken);
        if (foods is null)
        {
            return ValidationProblem(ModelState);
        }

        var now = DateTimeOffset.UtcNow;
        var recipe = new Recipe
        {
            OwnerUserId = User.GetUserId(),
            CreatedAt = now
        };
        Apply(recipe, request, foods, now);

        _dbContext.Recipes.Add(recipe);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = recipe.Id }, ToResponse(recipe));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RecipeResponse>> Update(
        Guid id,
        [FromBody] RecipeRequest request,
        CancellationToken cancellationToken)
    {
        var recipe = await _dbContext.Recipes
            .Include(r => r.Ingredients)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (recipe is null)
        {
            return NotFound();
        }

        if (recipe.OwnerUserId != User.GetUserId())
        {
            return Forbid();
        }

        var foods = await LoadFoodsAsync(request, cancellationToken);
        if (foods is null)
        {
            return ValidationProblem(ModelState);
        }

        Apply(recipe, request, foods, DateTimeOffset.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(recipe));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var recipe = await _dbContext.Recipes.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (recipe is null)
        {
            return NotFound();
        }

        if (recipe.OwnerUserId != User.GetUserId())
        {
            return Forbid();
        }

        _dbContext.Recipes.Remove(recipe);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    // Returns null and records a model error when the request refers to a food that does not exist.
    private async Task<Dictionary<int, Food>?> LoadFoodsAsync(
        RecipeRequest request,
        CancellationToken cancellationToken)
    {
        var foodIds = request.Ingredients.Select(i => i.FoodId).Distinct().ToList();

        var foods = await _dbContext.Foods
            .Where(f => foodIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, cancellationToken);

        var unknown = foodIds.Where(id => !foods.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
        {
            ModelState.AddModelError(nameof(RecipeRequest.Ingredients), $"Unknown food ids: {string.Join(", ", unknown)}.");
            return null;
        }

        return foods;
    }

    private static void Apply(Recipe recipe, RecipeRequest request, Dictionary<int, Food> foods, DateTimeOffset now)
    {
        recipe.Title = request.Title.Trim();
        recipe.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        recipe.Instructions = string.IsNullOrWhiteSpace(request.Instructions) ? null : request.Instructions.Trim();
        recipe.Servings = request.Servings;
        recipe.UpdatedAt = now;

        recipe.Ingredients.Clear();
        recipe.Ingredients.AddRange(request.Ingredients.Select(i => new RecipeIngredient
        {
            Food = foods[i.FoodId],
            AmountG = i.AmountG
        }));
    }

    private static NutritionFacts TotalNutrition(Recipe recipe) =>
        NutritionFacts.Sum(recipe.Ingredients.Select(i => NutritionFacts.ForFood(i.Food, i.AmountG)));

    private RecipeResponse ToResponse(Recipe recipe)
    {
        var total = TotalNutrition(recipe);

        return new RecipeResponse(
            recipe.Id,
            recipe.Title,
            recipe.Description,
            recipe.Instructions,
            recipe.Servings,
            recipe.OwnerUserId == User.GetUserId(),
            recipe.CreatedAt,
            recipe.UpdatedAt,
            recipe.Ingredients
                .Select(i => new RecipeIngredientResponse(
                    i.FoodId,
                    i.Food.Name,
                    i.AmountG,
                    NutritionFacts.ForFood(i.Food, i.AmountG).Rounded()))
                .ToList(),
            total.Rounded(),
            (total / recipe.Servings).Rounded());
    }
}
