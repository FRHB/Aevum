namespace PhotoAevumStudio.Application.Entities;

public class StudioService
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    
    /// <summary>Duration in minutes</summary>
    public int Duration { get; set; }
    
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public string RateType { get; set; } = "per hour";
}
