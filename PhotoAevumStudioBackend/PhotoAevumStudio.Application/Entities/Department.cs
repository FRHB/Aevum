namespace PhotoAevumStudio.Application.Entities;

/// <summary>
/// Domain entity representing a studio Department (e.g. Photography, Videography, Creative Direction).
/// Maps to dbo.Departments in SQL Server.
/// </summary>
public class Department
{
    public string DepartmentID { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string? DepartmentDescription { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
