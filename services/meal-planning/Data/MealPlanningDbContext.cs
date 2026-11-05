using MealPlanningService.Entities;
using Microsoft.EntityFrameworkCore;

namespace MealPlanningService.Data;

public class MealPlanningDbContext : DbContext
{
    public MealPlanningDbContext(DbContextOptions<MealPlanningDbContext> options) : base(options)
    {
    }

    public DbSet<Food> Foods => Set<Food>();
    public DbSet<Recipe> Recipes => Set<Recipe>();

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

        modelBuilder.Entity<Recipe>(entity =>
        {
            entity.Property(r => r.Title)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(r => r.Description).HasMaxLength(2000);
            entity.Property(r => r.Instructions).HasMaxLength(10000);

            entity.HasIndex(r => r.OwnerUserId);

            entity.HasMany(r => r.Ingredients)
                .WithOne(i => i.Recipe)
                .HasForeignKey(i => i.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecipeIngredient>(entity =>
        {
            entity.ToTable("RecipeIngredients");

            entity.Property(i => i.AmountG).HasPrecision(8, 2);

            entity.HasOne(i => i.Food)
                .WithMany()
                .HasForeignKey(i => i.FoodId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
