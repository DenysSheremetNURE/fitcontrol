using System.ComponentModel.DataAnnotations;
using MealPlanningService.Data;
using MealPlanningService.DTOs;
using MealPlanningService.Entities;
using MealPlanningService.Extensions;
using MealPlanningService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MealPlanningService.Controllers;

// Plans are private. Someone else's plan answers 404, so its existence is not revealed.
[ApiController]
[Route("api/meals/plans")]
public sealed class MealPlansController : ControllerBase
{
    private const decimal MaxServings = 100m;
    private const int SnapshotDecimals = 6;
    private const string DuplicateDateMessage = "A plan for this date already exists.";
    private const string DuplicateMealMessage = "This plan already has a meal of this type.";

    private readonly MealPlanningDbContext _dbContext;

    public MealPlansController(MealPlanningDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<MealPlanSummaryResponse>>> List(
        [FromQuery] DateOnly? date,
        [FromQuery, Range(1, 100)] int limit = 20,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var userId = User.GetUserId();

        var query = _dbContext.MealPlans.AsNoTracking().Where(p => p.OwnerUserId == userId);
        if (date is { } planDate)
        {
            query = query.Where(p => p.PlanDate == planDate);
        }

        var total = await query.CountAsync(cancellationToken);

        var plans = await query
            .Include(p => p.Meals)
            .ThenInclude(m => m.Items)
            .OrderByDescending(p => p.PlanDate)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var items = plans
            .Select(p => new MealPlanSummaryResponse(p.Id, p.PlanDate, p.Version, Planned(p).Rounded(), Consumed(p).Rounded()))
            .ToList();

        return Ok(new PagedResponse<MealPlanSummaryResponse>(items, total, limit, offset));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MealPlanResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var plan = await LoadPlanAsync(id, cancellationToken);

        return plan is null ? NotFound() : Ok(ToResponse(plan));
    }

    [HttpGet("{id:guid}/nutrition")]
    public async Task<ActionResult<MealPlanNutritionResponse>> GetNutrition(Guid id, CancellationToken cancellationToken)
    {
        var plan = await LoadPlanAsync(id, cancellationToken);
        if (plan is null)
        {
            return NotFound();
        }

        var meals = plan.Meals
            .OrderBy(m => m.MealType)
            .Select(m => new MealNutritionResponse(m.Id, m.MealType, Planned(m).Rounded(), Consumed(m).Rounded()))
            .ToList();

        return Ok(new MealPlanNutritionResponse(plan.Id, plan.PlanDate, meals, Planned(plan).Rounded(), Consumed(plan).Rounded()));
    }

    [HttpPost]
    public async Task<ActionResult<MealPlanResponse>> Create(
        [FromBody] MealPlanRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var planDate = request.PlanDate!.Value;

        if (await PlanExistsAsync(userId, planDate, cancellationToken))
        {
            return Conflict(new { message = DuplicateDateMessage });
        }

        var now = DateTimeOffset.UtcNow;
        var plan = new MealPlan
        {
            OwnerUserId = userId,
            PlanDate = planDate,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.MealPlans.Add(plan);

        var conflict = await SaveAsync(DuplicateDateMessage, cancellationToken);

        return conflict ?? CreatedAtAction(nameof(GetById), new { id = plan.Id }, ToResponse(plan));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<MealPlanResponse>> Update(
        Guid id,
        [FromBody] MealPlanRequest request,
        CancellationToken cancellationToken)
    {
        var plan = await LoadPlanAsync(id, cancellationToken);
        if (plan is null)
        {
            return NotFound();
        }

        if (plan.PlanDate == request.PlanDate!.Value)
        {
            return Ok(ToResponse(plan));
        }

        if (await PlanExistsAsync(plan.OwnerUserId, request.PlanDate.Value, cancellationToken))
        {
            return Conflict(new { message = DuplicateDateMessage });
        }

        plan.PlanDate = request.PlanDate.Value;
        Touch(plan);

        var conflict = await SaveAsync(DuplicateDateMessage, cancellationToken);

        return conflict ?? Ok(ToResponse(plan));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var plan = await LoadPlanAsync(id, cancellationToken);
        if (plan is null)
        {
            return NotFound();
        }

        _dbContext.MealPlans.Remove(plan);

        return await SaveAsync(string.Empty, cancellationToken) ?? NoContent();
    }

    [HttpPost("{id:guid}/meals")]
    public async Task<ActionResult<MealResponse>> AddMeal(
        Guid id,
        [FromBody] MealRequest request,
        CancellationToken cancellationToken)
    {
        var plan = await LoadPlanAsync(id, cancellationToken);
        if (plan is null)
        {
            return NotFound();
        }

        if (plan.Meals.Any(m => m.MealType == request.MealType))
        {
            return Conflict(new { message = DuplicateMealMessage });
        }

        var meal = new Meal { MealType = request.MealType!.Value };
        plan.Meals.Add(meal);
        Touch(plan);

        var conflict = await SaveAsync(DuplicateMealMessage, cancellationToken);

        return conflict ?? Created($"/api/meals/plans/{id}", ToResponse(meal));
    }

    [HttpDelete("{id:guid}/meals/{mealId:guid}")]
    public async Task<IActionResult> DeleteMeal(Guid id, Guid mealId, CancellationToken cancellationToken)
    {
        var plan = await LoadPlanAsync(id, cancellationToken);
        var meal = plan?.Meals.FirstOrDefault(m => m.Id == mealId);
        if (plan is null || meal is null)
        {
            return NotFound();
        }

        plan.Meals.Remove(meal);
        Touch(plan);

        return await SaveAsync(string.Empty, cancellationToken) ?? NoContent();
    }

    [HttpPost("{id:guid}/meals/{mealId:guid}/items")]
    public async Task<ActionResult<MealItemResponse>> AddItem(
        Guid id,
        Guid mealId,
        [FromBody] MealItemRequest request,
        CancellationToken cancellationToken)
    {
        var plan = await LoadPlanAsync(id, cancellationToken);
        var meal = plan?.Meals.FirstOrDefault(m => m.Id == mealId);
        if (plan is null || meal is null)
        {
            return NotFound();
        }

        var item = await BuildItemAsync(request, cancellationToken);
        if (item is null)
        {
            return ValidationProblem(ModelState);
        }

        meal.Items.Add(item);
        Touch(plan);

        var conflict = await SaveAsync(string.Empty, cancellationToken);

        return conflict ?? Created($"/api/meals/plans/{id}", ToResponse(item));
    }

    [HttpPut("{id:guid}/meals/{mealId:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<MealItemResponse>> UpdateItem(
        Guid id,
        Guid mealId,
        Guid itemId,
        [FromBody] MealItemAmountRequest request,
        CancellationToken cancellationToken)
    {
        var plan = await LoadPlanAsync(id, cancellationToken);
        var item = FindItem(plan, mealId, itemId);
        if (plan is null || item is null)
        {
            return NotFound();
        }

        if (!IsAmountAllowed(item.RecipeId is not null, request.Amount))
        {
            return ValidationProblem(ModelState);
        }

        item.Amount = request.Amount;
        Touch(plan);

        var conflict = await SaveAsync(string.Empty, cancellationToken);

        return conflict ?? Ok(ToResponse(item));
    }

    [HttpDelete("{id:guid}/meals/{mealId:guid}/items/{itemId:guid}")]
    public async Task<IActionResult> DeleteItem(Guid id, Guid mealId, Guid itemId, CancellationToken cancellationToken)
    {
        var plan = await LoadPlanAsync(id, cancellationToken);
        var item = FindItem(plan, mealId, itemId);
        if (plan is null || item is null)
        {
            return NotFound();
        }

        item.Meal.Items.Remove(item);
        Touch(plan);

        return await SaveAsync(string.Empty, cancellationToken) ?? NoContent();
    }

    [HttpPost("{id:guid}/meals/{mealId:guid}/items/{itemId:guid}/consume")]
    public Task<ActionResult<MealItemResponse>> Consume(Guid id, Guid mealId, Guid itemId, CancellationToken cancellationToken) =>
        SetConsumedAsync(id, mealId, itemId, consumed: true, cancellationToken);

    [HttpDelete("{id:guid}/meals/{mealId:guid}/items/{itemId:guid}/consume")]
    public Task<ActionResult<MealItemResponse>> Unconsume(Guid id, Guid mealId, Guid itemId, CancellationToken cancellationToken) =>
        SetConsumedAsync(id, mealId, itemId, consumed: false, cancellationToken);

    private async Task<ActionResult<MealItemResponse>> SetConsumedAsync(
        Guid id,
        Guid mealId,
        Guid itemId,
        bool consumed,
        CancellationToken cancellationToken)
    {
        var plan = await LoadPlanAsync(id, cancellationToken);
        var item = FindItem(plan, mealId, itemId);
        if (plan is null || item is null)
        {
            return NotFound();
        }

        // Repeating the same request changes nothing, which keeps the original time and version.
        if ((item.ConsumedAt is not null) == consumed)
        {
            return Ok(ToResponse(item));
        }

        item.ConsumedAt = consumed ? DateTimeOffset.UtcNow : null;
        Touch(plan);

        var conflict = await SaveAsync(string.Empty, cancellationToken);

        return conflict ?? Ok(ToResponse(item));
    }

    private async Task<MealPlan?> LoadPlanAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();

        return await _dbContext.MealPlans
            .Include(p => p.Meals)
            .ThenInclude(m => m.Items)
            .FirstOrDefaultAsync(p => p.Id == id && p.OwnerUserId == userId, cancellationToken);
    }

    private Task<bool> PlanExistsAsync(Guid userId, DateOnly planDate, CancellationToken cancellationToken) =>
        _dbContext.MealPlans.AnyAsync(p => p.OwnerUserId == userId && p.PlanDate == planDate, cancellationToken);

    private static MealItem? FindItem(MealPlan? plan, Guid mealId, Guid itemId) =>
        plan?.Meals.FirstOrDefault(m => m.Id == mealId)?.Items.FirstOrDefault(i => i.Id == itemId);

    private static void Touch(MealPlan plan)
    {
        plan.Version++;
        plan.UpdatedAt = DateTimeOffset.UtcNow;
    }

    // Returns null and records a model error when the request is invalid.
    private async Task<MealItem?> BuildItemAsync(MealItemRequest request, CancellationToken cancellationToken)
    {
        if ((request.RecipeId is null) == (request.FoodId is null))
        {
            ModelState.AddModelError(string.Empty, "Specify exactly one of recipeId and foodId.");
            return null;
        }

        if (!IsAmountAllowed(request.RecipeId is not null, request.Amount))
        {
            return null;
        }

        if (request.RecipeId is { } recipeId)
        {
            var recipe = await _dbContext.Recipes.AsNoTracking()
                .Include(r => r.Ingredients)
                .ThenInclude(i => i.Food)
                .FirstOrDefaultAsync(r => r.Id == recipeId, cancellationToken);

            if (recipe is null)
            {
                ModelState.AddModelError(nameof(MealItemRequest.RecipeId), $"Unknown recipe id: {recipeId}.");
                return null;
            }

            return NewItem(recipe.Title, request.Amount, NutritionFacts.ForRecipe(recipe) / recipe.Servings, recipeId, null);
        }

        var food = await _dbContext.Foods.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == request.FoodId, cancellationToken);

        if (food is null)
        {
            ModelState.AddModelError(nameof(MealItemRequest.FoodId), $"Unknown food id: {request.FoodId}.");
            return null;
        }

        return NewItem(food.Name, request.Amount, NutritionFacts.ForFood(food, 1m), null, food.Id);
    }

    private bool IsAmountAllowed(bool isRecipe, decimal amount)
    {
        if (isRecipe && amount > MaxServings)
        {
            ModelState.AddModelError(nameof(MealItemRequest.Amount), $"A recipe item can have at most {MaxServings} servings.");
            return false;
        }

        return true;
    }

    private static MealItem NewItem(string name, decimal amount, NutritionFacts perUnit, Guid? recipeId, int? foodId)
    {
        var snapshot = perUnit.Rounded(SnapshotDecimals);

        return new MealItem
        {
            RecipeId = recipeId,
            FoodId = foodId,
            Name = name,
            Amount = amount,
            KcalPerUnit = snapshot.Kcal,
            ProteinPerUnit = snapshot.ProteinG,
            FatPerUnit = snapshot.FatG,
            CarbsPerUnit = snapshot.CarbsG
        };
    }

    // Returns null on success, or the response to send when another request got there first.
    private async Task<ActionResult?> SaveAsync(string uniqueViolationMessage, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "The plan was changed by another request. Try again." });
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Conflict(new { message = uniqueViolationMessage });
        }
    }

    private static NutritionFacts Planned(Meal meal) => NutritionFacts.Sum(meal.Items.Select(NutritionFacts.ForItem));

    private static NutritionFacts Consumed(Meal meal) =>
        NutritionFacts.Sum(meal.Items.Where(i => i.ConsumedAt is not null).Select(NutritionFacts.ForItem));

    private static NutritionFacts Planned(MealPlan plan) => NutritionFacts.Sum(plan.Meals.Select(Planned));

    private static NutritionFacts Consumed(MealPlan plan) => NutritionFacts.Sum(plan.Meals.Select(Consumed));

    private static MealItemResponse ToResponse(MealItem item) => new(
        item.Id,
        item.RecipeId is not null ? "recipe" : "food",
        item.RecipeId?.ToString() ?? item.FoodId!.Value.ToString(),
        item.Name,
        item.Amount,
        item.RecipeId is not null ? "serving" : "g",
        item.ConsumedAt is not null,
        item.ConsumedAt,
        NutritionFacts.ForItem(item).Rounded());

    private static MealResponse ToResponse(Meal meal) => new(
        meal.Id,
        meal.MealType,
        meal.Items.Select(ToResponse).ToList(),
        Planned(meal).Rounded(),
        Consumed(meal).Rounded());

    private static MealPlanResponse ToResponse(MealPlan plan) => new(
        plan.Id,
        plan.PlanDate,
        plan.Version,
        plan.CreatedAt,
        plan.UpdatedAt,
        plan.Meals.OrderBy(m => m.MealType).Select(ToResponse).ToList(),
        Planned(plan).Rounded(),
        Consumed(plan).Rounded());
}
