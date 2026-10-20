namespace PhotoAevumStudio.Web.Models;

public sealed class MainServiceItem
{
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string CategorySpec { get; set; } = string.Empty;
    public List<(string Label, string ColorTag)> Deliverables { get; set; } = new();
    public string Duration { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "Active";
    public bool IsActive => Status.Equals("Active", StringComparison.OrdinalIgnoreCase);

    public string Id => Code;
    public string Name => Title;
    public decimal Price => BasePrice;
    public string DurationHrs => Duration;
}

public sealed class MainServiceModel
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string CategorySpec { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal DurationHours { get; set; }
    public decimal BasePrice { get; set; }
    public string Status { get; set; } = "Active";
    public List<(string Label, string ColorTag)> Deliverables { get; set; } = new();
}

public sealed class PackageServiceItem
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
}

public sealed class PackageServiceModel
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string TargetCategory { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string? SavingsNote { get; set; }
    public string Status { get; set; } = "Active";
    public List<(string Label, string ColorTag)> Inclusions { get; set; } = new();
}

public sealed class AddOnServiceItem
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
}

public sealed class AddOnServiceModel
{
    public string Title { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string TypeCategory { get; set; } = string.Empty;
    public string FilterCategory { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Rate { get; set; }
    public string RateUnit { get; set; } = "/ hr";
    public string Status { get; set; } = "Active";
    public List<(string Label, string ColorTag)> CompatibleServices { get; set; } = new();
}
