using System.ComponentModel.DataAnnotations;
using MealPlanningService.Data;
using MealPlanningService.DTOs;
using MealPlanningService.Entities;
using MealPlanningService.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MealPlanningService.Controllers;

[ApiController]
[Route("api/meals/foods")]
public sealed class FoodsController : ControllerBase
{
    private readonly MealPlanningDbContext _dbContext;

    public FoodsController(MealPlanningDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<FoodResponse>>> Search(
        [FromQuery, MaxLength(100)] string? search,
        [FromQuery, Range(1, 100)] int limit = 20,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        // Names look like "Chicken, broilers or fryers, breast", so every word is matched on its own.
        var words = LikeSearch.Words(search);

        var query = _dbContext.Foods.AsNoTracking();
        foreach (var word in words)
        {
            var pattern = $"%{word}%";
            query = query.Where(f => EF.Functions.ILike(f.Name, pattern, LikeSearch.Escape));
        }

        var total = await query.CountAsync(cancellationToken);

        // Names starting with the first word come first, and shorter names are usually the plainer foods.
        var prefix = words.Count > 0 ? $"{words[0]}%" : "%";
        var items = await query
            .OrderByDescending(f => EF.Functions.ILike(f.Name, prefix, LikeSearch.Escape))
            .ThenBy(f => f.Name.Length)
            .ThenBy(f => f.Name)
            .Skip(offset)
            .Take(limit)
            .Select(f => ToResponse(f))
            .ToListAsync(cancellationToken);

        return Ok(new PagedResponse<FoodResponse>(items, total, limit, offset));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<FoodResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        var food = await _dbContext.Foods.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        return food is null ? NotFound() : Ok(ToResponse(food));
    }

    private static FoodResponse ToResponse(Food f) => new(
        f.Id, f.Name, f.Category, f.KcalPer100G, f.ProteinPer100G, f.FatPer100G, f.CarbsPer100G);
}
