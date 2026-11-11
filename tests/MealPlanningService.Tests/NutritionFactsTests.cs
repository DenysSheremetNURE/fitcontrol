using MealPlanningService.Entities;
using MealPlanningService.Services;

namespace MealPlanningService.Tests;

public class NutritionFactsTests
{
    private static readonly Food Beets = new()
    {
        KcalPer100G = 43m,
        ProteinPer100G = 1.61m,
        FatPer100G = 0.17m,
        CarbsPer100G = 9.56m
    };

    private static readonly Food Rice = new()
    {
        KcalPer100G = 365m,
        ProteinPer100G = 7.13m,
        FatPer100G = 0.66m,
        CarbsPer100G = 79.95m
    };

    [Fact]
    public void ForFood_ScalesValuesPer100GramsToTheGivenAmount()
    {
        var facts = NutritionFacts.ForFood(Beets, 150m);

        Assert.Equal(new NutritionFacts(64.5m, 2.415m, 0.255m, 14.34m), facts);
    }

    [Fact]
    public void ForFood_ZeroGramsGivesZero()
    {
        Assert.Equal(new NutritionFacts(), NutritionFacts.ForFood(Beets, 0m));
    }

    [Fact]
    public void ForRecipe_SumsAllIngredients()
    {
        var recipe = new Recipe
        {
            Ingredients =
            {
                new RecipeIngredient { Food = Beets, AmountG = 200m },
                new RecipeIngredient { Food = Rice, AmountG = 150m }
            }
        };

        Assert.Equal(new NutritionFacts(633.5m, 13.915m, 1.33m, 139.045m), NutritionFacts.ForRecipe(recipe));
    }

    [Fact]
    public void ForRecipe_WithoutIngredientsGivesZero()
    {
        Assert.Equal(new NutritionFacts(), NutritionFacts.ForRecipe(new Recipe()));
    }

    [Fact]
    public void ForItem_MultipliesTheCopiedUnitValuesByTheAmount()
    {
        var item = new MealItem
        {
            Amount = 1.5m,
            KcalPerUnit = 200m,
            ProteinPerUnit = 10m,
            FatPerUnit = 4m,
            CarbsPerUnit = 30m
        };

        Assert.Equal(new NutritionFacts(300m, 15m, 6m, 45m), NutritionFacts.ForItem(item));
    }

    [Fact]
    public void Sum_OfNothingIsZero()
    {
        Assert.Equal(new NutritionFacts(), NutritionFacts.Sum(Array.Empty<NutritionFacts>()));
    }

    [Fact]
    public void Operators_ApplyToEveryField()
    {
        var a = new NutritionFacts(1m, 2m, 3m, 4m);

        Assert.Equal(new NutritionFacts(2m, 4m, 6m, 8m), a + a);
        Assert.Equal(new NutritionFacts(3m, 6m, 9m, 12m), a * 3m);
        Assert.Equal(new NutritionFacts(0.5m, 1m, 1.5m, 2m), a / 2m);
    }

    [Theory]
    [InlineData("2.415", "2.42")] // midpoint goes up, not to the nearest even digit
    [InlineData("2.425", "2.43")]
    [InlineData("2.414", "2.41")]
    [InlineData("0.005", "0.01")]
    public void Rounded_RoundsMidpointsAwayFromZero(string value, string expected)
    {
        var parsed = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        var wanted = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(wanted, new NutritionFacts(parsed, parsed, parsed, parsed).Rounded().Kcal);
    }

    [Fact]
    public void Rounded_AppliesToEveryField()
    {
        var rounded = new NutritionFacts(1.111m, 2.222m, 3.333m, 4.444m).Rounded();

        Assert.Equal(new NutritionFacts(1.11m, 2.22m, 3.33m, 4.44m), rounded);
    }

    [Fact]
    public void Rounded_SupportsSnapshotPrecision()
    {
        Assert.Equal(211.166667m, new NutritionFacts(633.5m / 3m, 0m, 0m, 0m).Rounded(6).Kcal);
    }

    [Fact]
    public void RoundingLast_GivesAMoreAccurateTotalThanRoundingEachPart()
    {
        var part = new NutritionFacts(0.004m, 0m, 0m, 0m);

        var roundedLast = NutritionFacts.Sum(new[] { part, part, part }).Rounded();
        var roundedFirst = NutritionFacts.Sum(new[] { part.Rounded(), part.Rounded(), part.Rounded() });

        Assert.Equal(0.01m, roundedLast.Kcal);
        Assert.Equal(0m, roundedFirst.Kcal);
    }
}
