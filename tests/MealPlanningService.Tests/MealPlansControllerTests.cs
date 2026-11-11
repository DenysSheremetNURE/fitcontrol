using MealPlanningService.DTOs;
using MealPlanningService.Entities;
using Microsoft.EntityFrameworkCore;
using static MealPlanningService.Tests.TestHost;

namespace MealPlanningService.Tests;

public class MealPlansControllerTests
{
    private static readonly DateOnly Day = new(2026, 11, 5);

    private readonly TestHost _host = new();

    private async Task<Guid> CreatePlanAsync(Guid owner) =>
        (await _host.Plans(owner).Create(new MealPlanRequest(Day), default)).Body().Id;

    private async Task<(Guid PlanId, Guid MealId)> CreateMealAsync(Guid owner)
    {
        var planId = await CreatePlanAsync(owner);
        var meal = (await _host.Plans(owner).AddMeal(planId, new MealRequest(MealType.Lunch), default)).Body();
        return (planId, meal.Id);
    }

    private async Task<(Guid PlanId, Guid MealId, Guid ItemId)> CreateFoodItemAsync(Guid owner, decimal grams = 150m)
    {
        var (planId, mealId) = await CreateMealAsync(owner);
        var item = (await _host.Plans(owner).AddItem(planId, mealId, new MealItemRequest(null, BeetsId, grams), default)).Body();
        return (planId, mealId, item.Id);
    }

    // 200 g of beets and 150 g of rice in 3 servings: 211.1666... kcal per serving.
    private async Task<Guid> CreateRecipeAsync(Guid owner)
    {
        var request = new RecipeRequest(
            "Bowl",
            null,
            null,
            3,
            new List<RecipeIngredientRequest> { new(BeetsId, 200m), new(RiceId, 150m) });

        return (await _host.Recipes(owner).Create(request, default)).Body().Id;
    }

    private async Task<MealPlanResponse> GetPlanAsync(Guid planId, Guid owner = default) =>
        (await _host.Plans(owner == default ? Alice : owner).GetById(planId, default)).Body();

    // --- Creating plans

    [Fact]
    public async Task Create_TakesTheOwnerFromTheTokenAndStartsAtVersionOne()
    {
        var plan = (await _host.Plans(Alice).Create(new MealPlanRequest(Day), default)).Body();

        Assert.Equal(1, plan.Version);
        await using var db = _host.Db();
        Assert.Equal(Alice, (await db.MealPlans.SingleAsync()).OwnerUserId);
    }

    [Fact]
    public async Task Create_SameDateTwiceIsAConflict()
    {
        await CreatePlanAsync(Alice);

        Assert.Equal(409, (await _host.Plans(Alice).Create(new MealPlanRequest(Day), default)).Status());
    }

    [Fact]
    public async Task Create_SameDateIsFineForAnotherUser()
    {
        await CreatePlanAsync(Alice);

        Assert.Equal(201, (await _host.Plans(Bob).Create(new MealPlanRequest(Day), default)).Status());
    }

    // --- Access

    [Fact]
    public async Task AnotherUserGetsNotFoundFromEveryOperation()
    {
        var (planId, mealId, itemId) = await CreateFoodItemAsync(Alice);
        var before = await GetPlanAsync(planId);

        Assert.Equal(404, (await _host.Plans(Bob).GetById(planId, default)).Status());
        Assert.Equal(404, (await _host.Plans(Bob).GetNutrition(planId, default)).Status());
        Assert.Equal(404, (await _host.Plans(Bob).Update(planId, new MealPlanRequest(Day.AddDays(1)), default)).Status());
        Assert.Equal(404, (await _host.Plans(Bob).Delete(planId, default)).Status());
        Assert.Equal(404, (await _host.Plans(Bob).AddMeal(planId, new MealRequest(MealType.Dinner), default)).Status());
        Assert.Equal(404, (await _host.Plans(Bob).DeleteMeal(planId, mealId, default)).Status());
        Assert.Equal(404, (await _host.Plans(Bob).AddItem(planId, mealId, new MealItemRequest(null, BeetsId, 1m), default)).Status());
        Assert.Equal(404, (await _host.Plans(Bob).UpdateItem(planId, mealId, itemId, new MealItemAmountRequest(1m), default)).Status());
        Assert.Equal(404, (await _host.Plans(Bob).DeleteItem(planId, mealId, itemId, default)).Status());
        Assert.Equal(404, (await _host.Plans(Bob).Consume(planId, mealId, itemId, default)).Status());
        Assert.Equal(404, (await _host.Plans(Bob).Unconsume(planId, mealId, itemId, default)).Status());

        var after = await GetPlanAsync(planId);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.Planned, after.Planned);
    }

    [Fact]
    public async Task List_ShowsOnlyMyPlans()
    {
        var mine = await CreatePlanAsync(Alice);
        await CreatePlanAsync(Bob);

        var list = (await _host.Plans(Alice).List(null, 20, 0, default)).Body();

        Assert.Equal(1, list.Total);
        Assert.Equal(mine, Assert.Single(list.Items).Id);
    }

    [Fact]
    public async Task List_FiltersByDateAndPutsNewestFirst()
    {
        await _host.Plans(Alice).Create(new MealPlanRequest(Day), default);
        await _host.Plans(Alice).Create(new MealPlanRequest(Day.AddDays(1)), default);

        var all = (await _host.Plans(Alice).List(null, 20, 0, default)).Body();
        var oneDay = (await _host.Plans(Alice).List(Day, 20, 0, default)).Body();

        Assert.Equal(new[] { Day.AddDays(1), Day }, all.Items.Select(p => p.PlanDate));
        Assert.Equal(Day, Assert.Single(oneDay.Items).PlanDate);
    }

    [Fact]
    public async Task AddItem_ToAMealOfAnotherPlanIsNotFound()
    {
        var (_, mealId) = await CreateMealAsync(Alice);
        var otherPlan = (await _host.Plans(Alice).Create(new MealPlanRequest(Day.AddDays(1)), default)).Body().Id;

        var result = await _host.Plans(Alice).AddItem(otherPlan, mealId, new MealItemRequest(null, BeetsId, 1m), default);

        Assert.Equal(404, result.Status());
    }

    [Fact]
    public async Task ItemOperations_ThroughAnotherMealOfTheSamePlanAreNotFound()
    {
        var (planId, _, itemId) = await CreateFoodItemAsync(Alice);
        var otherMeal = (await _host.Plans(Alice).AddMeal(planId, new MealRequest(MealType.Dinner), default)).Body().Id;

        Assert.Equal(404, (await _host.Plans(Alice).UpdateItem(planId, otherMeal, itemId, new MealItemAmountRequest(1m), default)).Status());
        Assert.Equal(404, (await _host.Plans(Alice).Consume(planId, otherMeal, itemId, default)).Status());
        Assert.Equal(404, (await _host.Plans(Alice).Unconsume(planId, otherMeal, itemId, default)).Status());
        Assert.Equal(404, (await _host.Plans(Alice).DeleteItem(planId, otherMeal, itemId, default)).Status());

        var item = (await GetPlanAsync(planId)).Meals.SelectMany(m => m.Items).Single();
        Assert.Equal(150m, item.Amount);
        Assert.False(item.Consumed);
    }

    // --- Meals

    [Fact]
    public async Task AddMeal_SameTypeTwiceIsAConflict()
    {
        var (planId, _) = await CreateMealAsync(Alice);

        Assert.Equal(409, (await _host.Plans(Alice).AddMeal(planId, new MealRequest(MealType.Lunch), default)).Status());
    }

    [Fact]
    public async Task GetById_OrdersMealsByTimeOfDay()
    {
        var planId = await CreatePlanAsync(Alice);
        foreach (var type in new[] { MealType.Snack, MealType.Dinner, MealType.Breakfast, MealType.Lunch })
        {
            await _host.Plans(Alice).AddMeal(planId, new MealRequest(type), default);
        }

        var meals = (await GetPlanAsync(planId)).Meals.Select(m => m.MealType);

        Assert.Equal(new[] { MealType.Breakfast, MealType.Lunch, MealType.Dinner, MealType.Snack }, meals);
    }

    // --- Items and copied values

    [Fact]
    public async Task AddItem_FoodIsMeasuredInGrams()
    {
        var (planId, mealId) = await CreateMealAsync(Alice);

        var item = (await _host.Plans(Alice).AddItem(planId, mealId, new MealItemRequest(null, BeetsId, 150m), default)).Body();

        Assert.Equal("food", item.SourceType);
        Assert.Equal(BeetsId.ToString(), item.SourceId);
        Assert.Equal("Beets, raw", item.Name);
        Assert.Equal("g", item.Unit);
        Assert.Equal(64.5m, item.Nutrition.Kcal);
        Assert.Equal(2.42m, item.Nutrition.ProteinG);
    }

    [Fact]
    public async Task AddItem_RecipeIsMeasuredInServings()
    {
        var recipeId = await CreateRecipeAsync(Alice);
        var (planId, mealId) = await CreateMealAsync(Alice);

        var item = (await _host.Plans(Alice).AddItem(planId, mealId, new MealItemRequest(recipeId, null, 1.5m), default)).Body();

        Assert.Equal("recipe", item.SourceType);
        Assert.Equal(recipeId.ToString(), item.SourceId);
        Assert.Equal("Bowl", item.Name);
        Assert.Equal("serving", item.Unit);
        Assert.Equal(316.75m, item.Nutrition.Kcal);
    }

    [Fact]
    public async Task AddItem_CanUseARecipeOfAnotherUser()
    {
        var recipeId = await CreateRecipeAsync(Bob);
        var (planId, mealId) = await CreateMealAsync(Alice);

        var result = await _host.Plans(Alice).AddItem(planId, mealId, new MealItemRequest(recipeId, null, 1m), default);

        Assert.Equal(201, result.Status());
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task AddItem_RequiresExactlyOneSource(bool withRecipe, bool withFood)
    {
        var recipeId = await CreateRecipeAsync(Alice);
        var (planId, mealId) = await CreateMealAsync(Alice);
        var request = new MealItemRequest(withRecipe ? recipeId : null, withFood ? BeetsId : null, 1m);

        Assert.Equal(400, (await _host.Plans(Alice).AddItem(planId, mealId, request, default)).Status());
    }

    [Fact]
    public async Task AddItem_RejectsUnknownSources()
    {
        var (planId, mealId) = await CreateMealAsync(Alice);

        Assert.Equal(400, (await _host.Plans(Alice).AddItem(planId, mealId, new MealItemRequest(null, 1, 1m), default)).Status());
        Assert.Equal(400, (await _host.Plans(Alice).AddItem(planId, mealId, new MealItemRequest(Guid.NewGuid(), null, 1m), default)).Status());
    }

    [Fact]
    public async Task ServingLimit_AppliesToRecipesButNotToFoods()
    {
        var recipeId = await CreateRecipeAsync(Alice);
        var (planId, mealId) = await CreateMealAsync(Alice);

        Assert.Equal(201, (await _host.Plans(Alice).AddItem(planId, mealId, new MealItemRequest(recipeId, null, 100m), default)).Status());
        Assert.Equal(400, (await _host.Plans(Alice).AddItem(planId, mealId, new MealItemRequest(recipeId, null, 100.01m), default)).Status());
        Assert.Equal(201, (await _host.Plans(Alice).AddItem(planId, mealId, new MealItemRequest(null, BeetsId, 101m), default)).Status());
    }

    [Fact]
    public async Task EditingARecipe_DoesNotChangeItemsAlreadyInAPlan()
    {
        var recipeId = await CreateRecipeAsync(Alice);
        var (planId, mealId) = await CreateMealAsync(Alice);
        await _host.Plans(Alice).AddItem(planId, mealId, new MealItemRequest(recipeId, null, 1.5m), default);
        var before = await GetPlanAsync(planId);

        var edit = new RecipeRequest("Renamed", null, null, 1, new List<RecipeIngredientRequest> { new(RiceId, 500m) });
        await _host.Recipes(Alice).Update(recipeId, edit, default);

        var after = await GetPlanAsync(planId);
        Assert.Equal("Bowl", after.Meals.Single().Items.Single().Name);
        Assert.Equal(316.75m, after.Planned.Kcal);
        Assert.Equal(before.Version, after.Version);
    }

    [Fact]
    public async Task DeletingARecipe_KeepsItemsAndConsumedCaloriesInPlans()
    {
        var recipeId = await CreateRecipeAsync(Bob);
        var (planId, mealId) = await CreateMealAsync(Alice);
        var item = (await _host.Plans(Alice).AddItem(planId, mealId, new MealItemRequest(recipeId, null, 1.5m), default)).Body();
        await _host.Plans(Alice).Consume(planId, mealId, item.Id, default);

        await _host.Recipes(Bob).Delete(recipeId, default);

        var plan = await GetPlanAsync(planId);
        var kept = plan.Meals.Single().Items.Single();
        Assert.Equal("Bowl", kept.Name);
        Assert.Equal(recipeId.ToString(), kept.SourceId);
        Assert.Equal(316.75m, plan.Consumed.Kcal);

        var rescaled = (await _host.Plans(Alice).UpdateItem(planId, mealId, item.Id, new MealItemAmountRequest(3m), default)).Body();
        Assert.Equal(633.5m, rescaled.Nutrition.Kcal);
    }

    [Fact]
    public async Task UpdateItem_ScalesFromTheCopiedValues()
    {
        var (planId, mealId, itemId) = await CreateFoodItemAsync(Alice);

        var item = (await _host.Plans(Alice).UpdateItem(planId, mealId, itemId, new MealItemAmountRequest(100m), default)).Body();

        Assert.Equal(100m, item.Amount);
        Assert.Equal(43m, item.Nutrition.Kcal);
    }

    [Fact]
    public async Task UpdateItem_RecipeAboveTheServingLimitIsRejected()
    {
        var recipeId = await CreateRecipeAsync(Alice);
        var (planId, mealId) = await CreateMealAsync(Alice);
        var item = (await _host.Plans(Alice).AddItem(planId, mealId, new MealItemRequest(recipeId, null, 1m), default)).Body();

        var result = await _host.Plans(Alice).UpdateItem(planId, mealId, item.Id, new MealItemAmountRequest(101m), default);

        Assert.Equal(400, result.Status());
        Assert.Equal(1m, (await GetPlanAsync(planId)).Meals.Single().Items.Single().Amount);
    }

    // --- Consumed items

    [Fact]
    public async Task Totals_SeparatePlannedFromConsumed()
    {
        var (planId, mealId, foodItem) = await CreateFoodItemAsync(Alice);
        await _host.Plans(Alice).AddItem(planId, mealId, new MealItemRequest(null, RiceId, 100m), default);

        var untouched = await GetPlanAsync(planId);
        Assert.Equal(429.5m, untouched.Planned.Kcal);
        Assert.Equal(0m, untouched.Consumed.Kcal);

        await _host.Plans(Alice).Consume(planId, mealId, foodItem, default);

        var plan = await GetPlanAsync(planId);
        Assert.Equal(429.5m, plan.Planned.Kcal);
        Assert.Equal(64.5m, plan.Consumed.Kcal);
    }

    [Fact]
    public async Task Consume_SecondCallKeepsTheTimeAndVersion()
    {
        var (planId, mealId, itemId) = await CreateFoodItemAsync(Alice);

        var first = (await _host.Plans(Alice).Consume(planId, mealId, itemId, default)).Body();
        var version = (await GetPlanAsync(planId)).Version;
        await Task.Delay(5);
        var second = (await _host.Plans(Alice).Consume(planId, mealId, itemId, default)).Body();

        Assert.True(first.Consumed);
        Assert.NotNull(first.ConsumedAt);
        Assert.Equal(first.ConsumedAt, second.ConsumedAt);
        Assert.Equal(version, (await GetPlanAsync(planId)).Version);
    }

    [Fact]
    public async Task Unconsume_ClearsTheMarkAndTheConsumedTotal()
    {
        var (planId, mealId, itemId) = await CreateFoodItemAsync(Alice);
        await _host.Plans(Alice).Consume(planId, mealId, itemId, default);

        var item = (await _host.Plans(Alice).Unconsume(planId, mealId, itemId, default)).Body();

        Assert.False(item.Consumed);
        Assert.Null(item.ConsumedAt);
        Assert.Equal(0m, (await GetPlanAsync(planId)).Consumed.Kcal);
    }

    [Fact]
    public async Task Unconsume_OfANotConsumedItemKeepsTheVersion()
    {
        var (planId, mealId, itemId) = await CreateFoodItemAsync(Alice);
        var version = (await GetPlanAsync(planId)).Version;

        Assert.Equal(200, (await _host.Plans(Alice).Unconsume(planId, mealId, itemId, default)).Status());
        Assert.Equal(version, (await GetPlanAsync(planId)).Version);
    }

    [Fact]
    public async Task UpdateItem_OfAConsumedItemKeepsTheMarkAndMovesTheTotal()
    {
        var (planId, mealId, itemId) = await CreateFoodItemAsync(Alice);
        await _host.Plans(Alice).Consume(planId, mealId, itemId, default);

        var item = (await _host.Plans(Alice).UpdateItem(planId, mealId, itemId, new MealItemAmountRequest(100m), default)).Body();

        Assert.True(item.Consumed);
        Assert.Equal(43m, (await GetPlanAsync(planId)).Consumed.Kcal);
    }

    // --- Version

    [Fact]
    public async Task Version_GrowsByOneWithEveryChange()
    {
        var planId = await CreatePlanAsync(Alice);

        async Task AssertVersion(int expected) => Assert.Equal(expected, (await GetPlanAsync(planId)).Version);

        await AssertVersion(1);
        var meal = (await _host.Plans(Alice).AddMeal(planId, new MealRequest(MealType.Lunch), default)).Body();
        await AssertVersion(2);
        var item = (await _host.Plans(Alice).AddItem(planId, meal.Id, new MealItemRequest(null, BeetsId, 100m), default)).Body();
        await AssertVersion(3);
        await _host.Plans(Alice).UpdateItem(planId, meal.Id, item.Id, new MealItemAmountRequest(50m), default);
        await AssertVersion(4);
        await _host.Plans(Alice).Consume(planId, meal.Id, item.Id, default);
        await AssertVersion(5);
        await _host.Plans(Alice).Unconsume(planId, meal.Id, item.Id, default);
        await AssertVersion(6);
        await _host.Plans(Alice).Update(planId, new MealPlanRequest(Day.AddDays(1)), default);
        await AssertVersion(7);
        await _host.Plans(Alice).DeleteItem(planId, meal.Id, item.Id, default);
        await AssertVersion(8);
        await _host.Plans(Alice).DeleteMeal(planId, meal.Id, default);
        await AssertVersion(9);
    }

    [Fact]
    public async Task Update_ToTheSameDateChangesNothing()
    {
        var planId = await CreatePlanAsync(Alice);

        var plan = (await _host.Plans(Alice).Update(planId, new MealPlanRequest(Day), default)).Body();

        Assert.Equal(1, plan.Version);
    }

    [Fact]
    public async Task Update_ToATakenDateIsAConflictAndKeepsTheDate()
    {
        var planId = await CreatePlanAsync(Alice);
        await _host.Plans(Alice).Create(new MealPlanRequest(Day.AddDays(1)), default);

        Assert.Equal(409, (await _host.Plans(Alice).Update(planId, new MealPlanRequest(Day.AddDays(1)), default)).Status());
        Assert.Equal(Day, (await GetPlanAsync(planId)).PlanDate);
    }

    [Fact]
    public async Task Version_RejectsSavingAPlanLoadedBeforeAnotherChange()
    {
        var (planId, mealId, itemId) = await CreateFoodItemAsync(Alice);

        // Another request changes the plan after this context has already loaded it.
        await using var staleDb = _host.Db();
        var stale = await staleDb.MealPlans.SingleAsync(p => p.Id == planId);
        await _host.Plans(Alice).UpdateItem(planId, mealId, itemId, new MealItemAmountRequest(100m), default);

        stale.Version++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleDb.SaveChangesAsync());
    }

    // --- Deleting

    [Fact]
    public async Task DeleteItem_UpdatesTotalsAndSecondCallIsNotFound()
    {
        var (planId, mealId, itemId) = await CreateFoodItemAsync(Alice);

        Assert.Equal(204, (await _host.Plans(Alice).DeleteItem(planId, mealId, itemId, default)).Status());
        Assert.Equal(404, (await _host.Plans(Alice).DeleteItem(planId, mealId, itemId, default)).Status());
        Assert.Equal(0m, (await GetPlanAsync(planId)).Planned.Kcal);
    }

    [Fact]
    public async Task DeleteMeal_RemovesItsItems()
    {
        var (planId, mealId, _) = await CreateFoodItemAsync(Alice);

        Assert.Equal(204, (await _host.Plans(Alice).DeleteMeal(planId, mealId, default)).Status());

        await using var db = _host.Db();
        Assert.False(await db.Set<MealItem>().AnyAsync());
    }

    [Fact]
    public async Task Delete_RemovesThePlanWithEverythingInIt()
    {
        var (planId, _, _) = await CreateFoodItemAsync(Alice);

        Assert.Equal(204, (await _host.Plans(Alice).Delete(planId, default)).Status());
        Assert.Equal(404, (await _host.Plans(Alice).GetById(planId, default)).Status());

        await using var db = _host.Db();
        Assert.False(await db.Set<Meal>().AnyAsync());
        Assert.False(await db.Set<MealItem>().AnyAsync());
    }

    // --- Nutrition endpoint

    [Fact]
    public async Task GetNutrition_GivesTotalsForTheDayAndEachMeal()
    {
        var (planId, mealId, itemId) = await CreateFoodItemAsync(Alice);
        await _host.Plans(Alice).AddMeal(planId, new MealRequest(MealType.Breakfast), default);
        await _host.Plans(Alice).Consume(planId, mealId, itemId, default);

        var nutrition = (await _host.Plans(Alice).GetNutrition(planId, default)).Body();

        Assert.Equal(64.5m, nutrition.Planned.Kcal);
        Assert.Equal(64.5m, nutrition.Consumed.Kcal);
        Assert.Equal(new[] { MealType.Breakfast, MealType.Lunch }, nutrition.Meals.Select(m => m.MealType));
        Assert.Equal(0m, nutrition.Meals[0].Planned.Kcal);
        Assert.Equal(64.5m, nutrition.Meals[1].Consumed.Kcal);
    }
}
