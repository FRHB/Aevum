namespace PhotoAevumStudio.Application.Entities;

public class Equipment
{
    public int Id { get; set; }
    public string EquipmentID { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string EquipmentTypeID { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string Status { get; set; } = "Available"; // Available, In Use, Maintenance, Retired
    public string? ImagePath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Optional link to an employee if assigned
    public int? AssignedEmployeeId { get; set; }
    public Employee? AssignedEmployee { get; set; }
}
