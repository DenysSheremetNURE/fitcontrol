using MealPlanningService.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace MealPlanningService.Controllers;

// Temporary: checks the gateway route and JWT validation until the real endpoints exist.
[ApiController]
[Route("api/meals/whoami")]
public sealed class WhoAmIController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { UserId = User.GetUserId() });
}
