using System.Security.Claims;
using MealPlanningService.Controllers;
using MealPlanningService.Data;
using MealPlanningService.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MealPlanningService.Tests;

// Runs controllers against an in-memory database. Every call gets a fresh context, as a real request would.
// Things the in-memory provider does not do, such as unique indexes and ILIKE, need a real PostgreSQL to check.
public sealed class TestHost
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public const int BeetsId = 169145;
    public const int RiceId = 169756;

    private static readonly IServiceProvider Services = new ServiceCollection()
        .AddLogging()
        .AddControllers()
        .Services
        .BuildServiceProvider();

    private readonly DbContextOptions<MealPlanningDbContext> _options =
        new DbContextOptionsBuilder<MealPlanningDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    public TestHost()
    {
        using var db = Db();
        db.Foods.AddRange(
            new Food
            {
                Id = BeetsId,
                Name = "Beets, raw",
                Category = "Vegetables",
                KcalPer100G = 43m,
                ProteinPer100G = 1.61m,
                FatPer100G = 0.17m,
                CarbsPer100G = 9.56m
            },
            new Food
            {
                Id = RiceId,
                Name = "Rice, white, raw",
                Category = "Grains",
                KcalPer100G = 365m,
                ProteinPer100G = 7.13m,
                FatPer100G = 0.66m,
                CarbsPer100G = 79.95m
            });
        db.SaveChanges();
    }

    public MealPlanningDbContext Db() => new(_options);

    public MealPlansController Plans(Guid userId) => WithUser(new MealPlansController(Db()), userId);

    public RecipesController Recipes(Guid userId) => WithUser(new RecipesController(Db()), userId);

    private static T WithUser<T>(T controller, Guid userId) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                RequestServices = Services,
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) },
                    "Test"))
            }
        };
        return controller;
    }
}
