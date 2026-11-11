using System.Globalization;
using System.Security.Claims;
using MealPlanningService.Data;
using MealPlanningService.Extensions;

namespace MealPlanningService.Tests;

public class LikeSearchTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Words_EmptyInputGivesNoWords(string? search)
    {
        Assert.Empty(LikeSearch.Words(search));
    }

    [Fact]
    public void Words_SplitsOnAnyWhitespace()
    {
        Assert.Equal(new[] { "chicken", "breast" }, LikeSearch.Words("  chicken \t breast\n"));
    }

    [Theory]
    [InlineData("100%", @"100\%")]
    [InlineData("a_b", @"a\_b")]
    [InlineData(@"a\b", @"a\\b")]
    [InlineData(@"\%", @"\\\%")]
    public void Words_EscapesLikeWildcards(string search, string expected)
    {
        Assert.Equal(expected, Assert.Single(LikeSearch.Words(search)));
    }
}

public class ClaimsPrincipalExtensionsTests
{
    private static ClaimsPrincipal UserWith(params Claim[] claims) => new(new ClaimsIdentity(claims));

    [Fact]
    public void GetUserId_ReadsTheNameIdentifierClaim()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id, UserWith(new Claim(ClaimTypes.NameIdentifier, id.ToString())).GetUserId());
    }

    [Fact]
    public void GetUserId_ThrowsWhenTheClaimIsMissing()
    {
        Assert.Throws<InvalidOperationException>(() => UserWith().GetUserId());
    }

    [Fact]
    public void GetUserId_ThrowsWhenTheClaimIsNotAGuid()
    {
        var user = UserWith(new Claim(ClaimTypes.NameIdentifier, "not-a-guid"));

        Assert.Throws<InvalidOperationException>(() => user.GetUserId());
    }
}

public class FoodSeederTests
{
    [Fact]
    public void ReadFoods_LoadsTheWholeEmbeddedFile()
    {
        Assert.Equal(7793, FoodSeeder.ReadFoods().Count);
    }

    [Fact]
    public void ReadFoods_ParsesEveryColumn()
    {
        var beets = FoodSeeder.ReadFoods().Single(f => f.Name == "Beets, raw");

        Assert.Equal(169145, beets.Id);
        Assert.Equal("Vegetables and Vegetable Products", beets.Category);
        Assert.Equal(43m, beets.KcalPer100G);
        Assert.Equal(1.61m, beets.ProteinPer100G);
        Assert.Equal(0.17m, beets.FatPer100G);
        Assert.Equal(9.56m, beets.CarbsPer100G);
    }

    [Fact]
    public void ReadFoods_DoesNotDependOnTheMachineCulture()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("uk-UA"); // uses a comma as the decimal separator
        try
        {
            Assert.Equal(1.61m, FoodSeeder.ReadFoods().Single(f => f.Name == "Beets, raw").ProteinPer100G);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ReadFoods_ValuesFitTheDatabaseSchema()
    {
        var foods = FoodSeeder.ReadFoods();

        Assert.Equal(foods.Count, foods.Select(f => f.Id).Distinct().Count());
        Assert.All(foods, f =>
        {
            Assert.InRange(f.Name.Length, 1, 200);
            Assert.InRange(f.Category.Length, 1, 100);
            Assert.InRange(f.KcalPer100G, 0m, 9999.99m);
            Assert.InRange(f.ProteinPer100G, 0m, 100m);
            Assert.InRange(f.FatPer100G, 0m, 100m);
            Assert.InRange(f.CarbsPer100G, 0m, 100m);
        });
    }
}
