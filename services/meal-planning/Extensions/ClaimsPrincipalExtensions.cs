using System.Security.Claims;

namespace MealPlanningService.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out var userId)
            ? userId
            : throw new InvalidOperationException("Token has no valid user id.");
    }
}
