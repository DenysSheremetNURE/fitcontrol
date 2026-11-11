using MealPlanningService.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static MealPlanningService.Tests.TestHost;

namespace MealPlanningService.Tests;

public class RecipesControllerTests
{
    private readonly TestHost _host = new();

    private static RecipeRequest Request(string title = "Bowl", int servings = 2, params (int FoodId, decimal Grams)[] ingredients) => new(
        title,
        null,
        null,
        servings,
        (ingredients.Length > 0 ? ingredients : new[] { (BeetsId, 200m), (RiceId, 150m) })
            .Select(i => new RecipeIngredientRequest(i.Item1, i.Item2))
            .ToList());

    private async Task<Guid> CreateAsync(Guid owner) =>
        (await _host.Recipes(owner).Create(Request(), default)).Body().Id;

    [Fact]
    public async Task Create_TakesTheOwnerFromTheToken()
    {
        var id = await CreateAsync(Alice);

        await using var db = _host.Db();
        Assert.Equal(Alice, (await db.Recipes.SingleAsync(r => r.Id == id)).OwnerUserId);
    }

    [Fact]
    public async Task Create_ReturnsNutritionInTotalAndPerServing()
    {
        var recipe = (await _host.Recipes(Alice).Create(Request(servings: 2), default)).Body();

        Assert.Equal(633.5m, recipe.Nutrition.Kcal);
        Assert.Equal(316.75m, recipe.NutritionPerServing.Kcal);
        Assert.Equal(86m, recipe.Ingredients.Single(i => i.FoodId == BeetsId).Nutrition.Kcal);
    }

    [Fact]
    public async Task Create_TrimsTheTitle()
    {
        var recipe = (await _host.Recipes(Alice).Create(Request(title: "  Bowl  "), default)).Body();

        Assert.Equal("Bowl", recipe.Title);
    }

    [Fact]
    public async Task Create_RejectsUnknownFoodsAndSavesNothing()
    {
        var result = await _host.Recipes(Alice).Create(Request("Bowl", 1, (1, 10m)), default);

        Assert.Equal(400, result.Status());
        await using var db = _host.Db();
        Assert.False(await db.Recipes.AnyAsync());
    }

    [Fact]
    public async Task GetById_ShowsWhetherTheRecipeIsMine()
    {
        var id = await CreateAsync(Alice);

        Assert.True((await _host.Recipes(Alice).GetById(id, default)).Body().IsMine);
        Assert.False((await _host.Recipes(Bob).GetById(id, default)).Body().IsMine);
    }

    [Fact]
    public async Task GetById_UnknownRecipeIsNotFound()
    {
        Assert.Equal(404, (await _host.Recipes(Alice).GetById(Guid.NewGuid(), default)).Status());
    }

    [Fact]
    public async Task Update_ByAnotherUserIsForbiddenAndChangesNothing()
    {
        var id = await CreateAsync(Alice);

        var result = await _host.Recipes(Bob).Update(id, Request(title: "Stolen"), default);

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Equal("Bowl", (await _host.Recipes(Alice).GetById(id, default)).Body().Title);
    }

    [Fact]
    public async Task Delete_ByAnotherUserIsForbiddenAndKeepsTheRecipe()
    {
        var id = await CreateAsync(Alice);

        Assert.IsType<ForbidResult>(await _host.Recipes(Bob).Delete(id, default));
        Assert.Equal(200, (await _host.Recipes(Alice).GetById(id, default)).Status());
    }

    [Fact]
    public async Task Update_ReplacesIngredientsAndRecalculates()
    {
        var id = await CreateAsync(Alice);

        var updated = (await _host.Recipes(Alice).Update(id, Request("Rice", 4, (RiceId, 400m)), default)).Body();

        Assert.Equal(RiceId, Assert.Single(updated.Ingredients).FoodId);
        Assert.Equal(365m, updated.NutritionPerServing.Kcal);

        await using var db = _host.Db();
        Assert.Single(await db.Recipes.Where(r => r.Id == id).SelectMany(r => r.Ingredients).ToListAsync());
    }

    [Fact]
    public async Task Update_KeepsCreatedAtAndMovesUpdatedAt()
    {
        var created = (await _host.Recipes(Alice).Create(Request(), default)).Body();
        await Task.Delay(5);

        var updated = (await _host.Recipes(Alice).Update(created.Id, Request(title: "New"), default)).Body();

        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt > created.UpdatedAt);
    }

    [Fact]
    public async Task Delete_ByTheAuthorRemovesTheRecipe()
    {
        var id = await CreateAsync(Alice);

        Assert.Equal(204, (await _host.Recipes(Alice).Delete(id, default)).Status());
        Assert.Equal(404, (await _host.Recipes(Alice).GetById(id, default)).Status());
    }
}
