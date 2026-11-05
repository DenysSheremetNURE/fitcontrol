namespace MealPlanningService.Entities;

public class Food
{
    // The USDA fdc_id, so a row can be traced back to its source.
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal KcalPer100G { get; set; }
    public decimal ProteinPer100G { get; set; }
    public decimal FatPer100G { get; set; }
    public decimal CarbsPer100G { get; set; }
}
