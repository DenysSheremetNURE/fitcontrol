using MealPlanningService.Entities;
using Microsoft.EntityFrameworkCore;

namespace MealPlanningService.Data;

public class MealPlanningDbContext : DbContext
{
    public MealPlanningDbContext(DbContextOptions<MealPlanningDbContext> options) : base(options)
    {
    }

    public DbSet<Food> Foods => Set<Food>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Food>(entity =>
        {
            entity.Property(f => f.Id).ValueGeneratedNever();

            entity.Property(f => f.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(f => f.Category)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(f => f.KcalPer100G).HasPrecision(6, 2);
            entity.Property(f => f.ProteinPer100G).HasPrecision(6, 2);
            entity.Property(f => f.FatPer100G).HasPrecision(6, 2);
            entity.Property(f => f.CarbsPer100G).HasPrecision(6, 2);
        });
    }
}
