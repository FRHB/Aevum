namespace PhotoAevumStudio.Application.Entities;

public class Employee
{
    public int Id { get; set; }
    
    /// <summary>Matches ApplicationUser.Id</summary>
    public string IdentityUserId { get; set; } = string.Empty;
    public ApplicationUser IdentityUser { get; set; } = null!;
    
    public string Status { get; set; } = "Active";
    public string DaysOff { get; set; } = "Sunday";
    
    public string? Address { get; set; }
    public int? BirthYear { get; set; }
    public string? DepartmentID { get; set; }
    public Department? DepartmentEntity { get; set; }
    
    /// <summary>EF Core 8/9 supports primitive collections for simple JSON/array columns</summary>
    public List<string> Specializations { get; set; } = new();
    
    public ICollection<Equipment> AssignedEquipment { get; set; } = new List<Equipment>();
}
