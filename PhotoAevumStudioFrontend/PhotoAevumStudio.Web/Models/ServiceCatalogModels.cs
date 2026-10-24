using System.ComponentModel.DataAnnotations;

namespace PhotoAevumStudio.Web.Models;

/// <summary>
/// Data item representing a primary photography service in the catalog.
/// </summary>
public class MainServiceItem
{
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string CategorySpec { get; set; } = string.Empty;
    public List<(string Label, string ColorTag)> Deliverables { get; set; } = new();
    public decimal BasePrice { get; set; }
    public string Duration { get; set; } = "1.5 Hours";
    public string? Description { get; set; }
    public string Status { get; set; } = "Active";
    public bool IsActive => Status.Equals("Active", StringComparison.OrdinalIgnoreCase);

<<<<<<< HEAD
    /// <summary>True when an admin has marked this service as featured on the public site.</summary>
    public bool IsFeatured { get; set; }

=======
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
    public string Id => Code;
    public string Name => Title;
    public decimal Price => BasePrice;
    public string DurationHrs => Duration;
}

/// <summary>
/// Form model for the "Add New Main Service" modal wizard.
/// </summary>
public class MainServiceModel
{
    [Required(ErrorMessage = "Service name is required.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "Service name must be between 2 and 80 characters.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Service code is required.")]
    [StringLength(20, MinimumLength = 3, ErrorMessage = "Service code must be between 3 and 20 characters.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please select a category.")]
    public string Category { get; set; } = "Portrait";

    public string CategorySpec { get; set; } = "Studio Session";

    [StringLength(300, ErrorMessage = "Description cannot exceed 300 characters.")]
    public string? Description { get; set; }

    [Range(0.25, 48, ErrorMessage = "Duration must be between 0.25 and 48 hours.")]
    public decimal DurationHours { get; set; } = 1.5m;

    [Range(0, 50000, ErrorMessage = "Price must be a valid non-negative value.")]
    public decimal BasePrice { get; set; } = 150.00m;

    public List<(string Label, string ColorTag)> Deliverables { get; set; } = new();

    public string Status { get; set; } = "Active";
}

/// <summary>
/// Data item representing a bundled package service tier.
/// </summary>
public class PackageServiceItem
{
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string BundleCategory { get; set; } = string.Empty;
    public List<(string Label, string ColorTag)> Inclusions { get; set; } = new();
    public string Price { get; set; } = string.Empty;
    public string? SavingsNote { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "Active";
    public bool IsActive => Status.Equals("Active", StringComparison.OrdinalIgnoreCase);
<<<<<<< HEAD

    /// <summary>True when an admin has marked this package as featured on the public site.</summary>
    public bool IsFeatured { get; set; }
=======
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
}

/// <summary>
/// Form model for the "Create New Package" modal wizard.
/// </summary>
public class PackageServiceModel
{
    [Required(ErrorMessage = "Package name is required.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "Package name must be between 2 and 80 characters.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Package code is required.")]
    [StringLength(20, MinimumLength = 3, ErrorMessage = "Package code must be between 3 and 20 characters.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please select a target category.")]
    public string TargetCategory { get; set; } = "Portrait Bundles";

    [StringLength(300, ErrorMessage = "Description cannot exceed 300 characters.")]
    public string? Description { get; set; }

    [Range(0, 100000, ErrorMessage = "Package price must be a valid positive amount.")]
    public decimal Price { get; set; } = 499.00m;

    public string? SavingsNote { get; set; } = "Save $100 vs individual services";

    public List<(string Label, string ColorTag)> Inclusions { get; set; } = new();

    public string Status { get; set; } = "Active";
}

/// <summary>
/// Data item representing an additional service or add-on.
/// </summary>
public class AddOnServiceItem
{
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string TypeCategory { get; set; } = string.Empty;
    public string PriceText { get; set; } = string.Empty;
    public string FilterCategory { get; set; } = string.Empty;
    public List<(string Label, string ColorTag)> CompatibleServices { get; set; } = new();
    public string? Description { get; set; }
    public string Status { get; set; } = "Active";
    public bool IsActive => Status.Equals("Active", StringComparison.OrdinalIgnoreCase);
<<<<<<< HEAD

    /// <summary>True when an admin has marked this add-on as featured on the public site.</summary>
    public bool IsFeatured { get; set; }
=======
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
}

/// <summary>
/// Form model for the "Add New Add-On" modal wizard.
/// </summary>
public class AddOnServiceModel
{
    [Required(ErrorMessage = "Add-On title is required.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "Add-On title must be between 2 and 80 characters.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Add-On code is required.")]
    [StringLength(20, MinimumLength = 3, ErrorMessage = "Add-On code must be between 3 and 20 characters.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please select a type category.")]
    public string TypeCategory { get; set; } = "Post-Processing";

    public string FilterCategory { get; set; } = "Expedited Delivery";

    [Range(0, 10000, ErrorMessage = "Rate must be a non-negative amount.")]
    public decimal Rate { get; set; } = 50.00m;

    public string RateUnit { get; set; } = "/ session";

    [StringLength(300, ErrorMessage = "Description cannot exceed 300 characters.")]
    public string? Description { get; set; }

    public List<(string Label, string ColorTag)> CompatibleServices { get; set; } = new();

    public string Status { get; set; } = "Active";
}


/// <summary>
/// Data item representing a service category in the master catalog.
/// </summary>
public class ServiceCategoryItem
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty; // e.g. "CAT-001" or "CAT-PORT"
    public string Name { get; set; } = string.Empty; // e.g. "Portrait Photography"
    public string? Description { get; set; }
    public string Type { get; set; } = "Main Services"; // "Main Services", "Packages", "Add-Ons", "Specialty"
    public string Status { get; set; } = "Active"; // "Active", "Draft", "Inactive", "Archived"
    public bool IsFeatured { get; set; } = false;
<<<<<<< HEAD

    /// <summary>When true the category is listed in the public Portfolio sidebar.</summary>
    public bool ShowInPortfolio { get; set; } = false;

    /// <summary>Parent portfolio folder: "Photography" or "Videography".</summary>
    public string PortfolioSection { get; set; } = PortfolioSections.Photography;

=======
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
    public string ColorTag { get; set; } = "Blue"; // "Blue", "Rose", "Purple", "Emerald", "Amber", "Indigo", "Cyan"
    public List<string> AssociatedServices { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive => Status.Equals("Active", StringComparison.OrdinalIgnoreCase);

    public string Initials
    {
        get
        {
            var parts = Name.Split(new[] { ' ', '-', '&', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "SC";
            if (parts.Length == 1) return parts[0].Length >= 2 ? parts[0][..2].ToUpperInvariant() : parts[0].ToUpperInvariant();
            return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
        }
    }
}

/// <summary>
/// Form model for creating or updating a Service Category.
/// </summary>
public class CategoryModel
{
    [Required(ErrorMessage = "Category name is required.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "Category name must be between 2 and 80 characters.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Category code is required.")]
    [StringLength(20, MinimumLength = 3, ErrorMessage = "Category code must be between 3 and 20 characters.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please select a category type.")]
    public string Type { get; set; } = "Main Services";

    [StringLength(300, ErrorMessage = "Description cannot exceed 300 characters.")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Please select a color tag.")]
    public string ColorTag { get; set; } = "Blue";

    public string Status { get; set; } = "Active";

    public bool IsFeatured { get; set; } = false;

<<<<<<< HEAD
    public bool ShowInPortfolio { get; set; } = false;

    public string PortfolioSection { get; set; } = PortfolioSections.Photography;

    public string ServicesRaw { get; set; } = string.Empty;
}


/// <summary>Entity-type keys used to store featured flags for each service table.</summary>
public static class FeaturedEntityTypes
{
    public const string Main = "Main";
    public const string Package = "Package";
    public const string AddOn = "AddOn";
}

/// <summary>The two parent folders of the public Portfolio sidebar.</summary>
public static class PortfolioSections
{
    public const string Photography = "Photography";
    public const string Videography = "Videography";

    public static readonly string[] All = { Photography, Videography };
}

/// <summary>
/// A category that an admin has published to the public Portfolio, with the
/// service names mapped to it. Persisted by <c>IServicesApiClient</c>.
/// </summary>
public class PortfolioCategoryDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ParentSection { get; set; } = PortfolioSections.Photography;
    public string ColorTag { get; set; } = "Blue";
    public bool IsActive { get; set; } = true;

    /// <summary>Admin-side switch. When false, saving removes the category from the portfolio.</summary>
    public bool ShowInPortfolio { get; set; } = true;

    public List<string> ServiceNames { get; set; } = new();
}

/// <summary>Unified card shown in the Home page "Featured services" pool.</summary>
public record FeaturedServiceCard(
    string Code,
    string Kind,          // "Main Service" | "Package" | "Add-On"
    string Title,
    string PriceText,
    string Description,
    string Meta);


/// <summary>
/// Default associations that are auto-assigned to an event when its category is selected:
/// the studio space, service type, pricing/duration defaults, and the staff and equipment
/// that normally work that kind of shoot. One profile per category (matched by code or name).
/// </summary>
public class CategoryAssociationProfile
{
    public string CategoryCode { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;

    /// <summary><see cref="StudioSpaceResource.Id"/> of the default space (e.g. "studio-a").</summary>
    public string? DefaultSpaceId { get; set; }

    /// <summary>One of <see cref="EventServiceTypes.All"/>.</summary>
    public string? DefaultServiceType { get; set; }

    public decimal DefaultDurationHours { get; set; } = 3m;
    public decimal DefaultPrice { get; set; } = 350m;

    /// <summary><see cref="StaffMemberResource.Id"/> values. The first photographer in the list becomes the event lead.</summary>
    public List<string> StaffIds { get; set; } = new();

    /// <summary><see cref="EquipmentItemResource.Id"/> values reserved automatically with the event.</summary>
    public List<string> EquipmentIds { get; set; } = new();

    /// <summary>True when at least one association is configured (otherwise the profile is not worth storing).</summary>
    public bool HasAny =>
        !string.IsNullOrWhiteSpace(DefaultSpaceId) ||
        !string.IsNullOrWhiteSpace(DefaultServiceType) ||
        StaffIds.Count > 0 ||
        EquipmentIds.Count > 0;

    /// <summary>Matches on category code/id first, then on a case-insensitive name.</summary>
    public bool Matches(string? idOrCode, string? name)
    {
        if (!string.IsNullOrWhiteSpace(idOrCode) &&
            CategoryCode.Equals(idOrCode.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        return !string.IsNullOrWhiteSpace(name) &&
               !string.IsNullOrWhiteSpace(CategoryName) &&
               CategoryName.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public CategoryAssociationProfile Clone() => new()
    {
        CategoryCode = CategoryCode,
        CategoryName = CategoryName,
        DefaultSpaceId = DefaultSpaceId,
        DefaultServiceType = DefaultServiceType,
        DefaultDurationHours = DefaultDurationHours,
        DefaultPrice = DefaultPrice,
        StaffIds = StaffIds.ToList(),
        EquipmentIds = EquipmentIds.ToList()
    };
}
=======
    public string ServicesRaw { get; set; } = string.Empty;
}
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
