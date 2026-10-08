namespace PhotoAevumStudio.Application.Entities;

public class Equipment
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string Status { get; set; } = "Available"; // Available, In Use, Maintenance
    
    // Optional link to an employee if assigned
    public int? AssignedEmployeeId { get; set; }
    public Employee? AssignedEmployee { get; set; }
}
