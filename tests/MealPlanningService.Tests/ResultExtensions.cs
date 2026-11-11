using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace MealPlanningService.Tests;

public static class ResultExtensions
{
    // Returns the response body of a successful result, failing the test for any other result.
    public static T Body<T>(this ActionResult<T> result)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.InRange(objectResult.StatusCode ?? 200, 200, 299);
        return Assert.IsAssignableFrom<T>(objectResult.Value);
    }

    public static int? Status<T>(this ActionResult<T> result) => (result.Result as IStatusCodeActionResult)?.StatusCode;

    public static int? Status(this IActionResult result) => (result as IStatusCodeActionResult)?.StatusCode;
}
