using Microsoft.EntityFrameworkCore;

namespace MealPlanningService.Data;

public class MealPlanningDbContext : DbContext
{
    public MealPlanningDbContext(DbContextOptions<MealPlanningDbContext> options) : base(options)
    {
    }
}
