<<<<<<< HEAD
namespace PhotoAevumStudio.Application.Entities;

public class Employee
{
    public int Id { get; set; }
    
    /// <summary>Matches ApplicationUser.Id</summary>
    public string IdentityUserId { get; set; } = string.Empty;
    public ApplicationUser IdentityUser { get; set; } = null!;
    
    public string Status { get; set; } = "Active";
    
    /// <summary>EF Core 8/9 supports primitive collections for simple JSON/array columns</summary>
    public List<string> Specializations { get; set; } = new();
    
    public ICollection<Equipment> AssignedEquipment { get; set; } = new List<Equipment>();
}
=======
namespace PhotoAevumStudio.Application.Entities;

public class Employee
{
    public int Id { get; set; }
    
    /// <summary>Matches ApplicationUser.Id</summary>
    public string IdentityUserId { get; set; } = string.Empty;
    public ApplicationUser IdentityUser { get; set; } = null!;
    
    public string Status { get; set; } = "Active";
    
    /// <summary>EF Core 8/9 supports primitive collections for simple JSON/array columns</summary>
    public List<string> Specializations { get; set; } = new();
    
    public ICollection<Equipment> AssignedEquipment { get; set; } = new List<Equipment>();
}
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
