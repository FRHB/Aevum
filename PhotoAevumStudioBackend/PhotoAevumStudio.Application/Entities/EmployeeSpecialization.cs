namespace PhotoAevumStudio.Application.Entities;

public class EmployeeSpecialization
{
    public string SpecializationID { get; set; } = string.Empty;
    public string SpecializationName { get; set; } = string.Empty;
    public string? SpecializationDescription { get; set; }
    public string Specialization { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ColorTag { get; set; } = "Blue";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
