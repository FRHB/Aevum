namespace PhotoAevumStudio.Application.Entities;

/// <summary>
/// Domain entity representing an Event / Service Category in Photo Aevum Studio.
/// Maps to dbo.ServiceCategory in SQL Server.
/// </summary>
public class EventCategory
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Navigation property: Automatic or explicitly assigned associations linked to this category.
    /// Maps to dbo.ServiceCategoryJoins in SQL Server.
    /// </summary>
    public ICollection<ServiceCategoryJoin> Associations { get; set; } = new List<ServiceCategoryJoin>();
}

/// <summary>
/// Domain join entity linking a Category with a Service, AdditionalService, or ComboPackage.
/// Maps to dbo.ServiceCategoryJoins in SQL Server.
/// </summary>
public class ServiceCategoryJoin
{
    public string Id { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
    public string? ServiceId { get; set; }
    public string? AdditionalServiceId { get; set; }
    public string? ComboPackageId { get; set; }

    /// <summary>
    /// Navigation property back to the parent Category.
    /// </summary>
    public EventCategory? Category { get; set; }
}

/// <summary>
/// Domain alias class for Category Association.
/// </summary>
public class EventCategoryAssociation : ServiceCategoryJoin
{
}
