namespace MealPlanningService.Extensions;

public static class LikeSearch
{
    public const string Escape = "\\";

    // Splits a search string into words that are safe to place inside an ILIKE pattern.
    public static List<string> Words(string? search) => (search ?? string.Empty)
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        .Select(word => word
            .Replace(Escape, Escape + Escape)
            .Replace("%", Escape + "%")
            .Replace("_", Escape + "_"))
        .ToList();
}
