using System.ComponentModel.DataAnnotations;

namespace PhotoAevumStudio.Web.Models;

/// <summary>
/// Data model representing dbo.Equipment entity joined with dbo.EquipmentType.
/// </summary>
public class EquipmentItemModel
{
    public string EquipmentID { get; set; } = string.Empty;
    public string EquipmentName { get; set; } = string.Empty;
    public string EquipmentTypeID { get; set; } = string.Empty;
    public string EquipmentTypeName { get; set; } = string.Empty;
    public string EquipmentStatus { get; set; } = "Available"; // 'Available', 'Maintenance', 'Retired'
    public string? SerialNo { get; set; }
    public byte[]? EquipmentImage { get; set; }

    // Computed and display helpers
    public bool IsAvailable => EquipmentStatus.Equals("Available", StringComparison.OrdinalIgnoreCase);
    public bool IsMaintenance => EquipmentStatus.Equals("Maintenance", StringComparison.OrdinalIgnoreCase);
    public bool IsRetired => EquipmentStatus.Equals("Retired", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// If currently checked out or dispatched, holds active event appointment information.
    /// </summary>
    public string? CurrentEventId { get; set; }
    public string? CurrentEventName { get; set; }
    public bool IsDispatched => !string.IsNullOrEmpty(CurrentEventId);
}

/// <summary>
/// Data model representing dbo.EquipmentType entity with associated inventory metrics.
/// </summary>
public class EquipmentTypeModel
{
    public string EquipmentTypeID { get; set; } = string.Empty;
    public string EquipmentTypeName { get; set; } = string.Empty;
    public int AssociatedItemsCount { get; set; }
    public int AvailableCount { get; set; }
    public int MaintenanceCount { get; set; }
    public string Status { get; set; } = "Active";
    public string ColorTag { get; set; } = "Blue";
}

/// <summary>
/// Data model representing dbo.EventAppointment entity for dispatch scheduling.
/// </summary>
public class EventAppointmentModel
{
    public string EventAppointmentID { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string EventDescription { get; set; } = string.Empty;
    public string CustomerID { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateTime EventDate { get; set; }
    public TimeSpan EventTime { get; set; }
    public string EventStatus { get; set; } = "Confirmed"; // 'Pending','Confirmed','Shot','Editing','Delivered','Cancelled'

    public string FormattedDateTime => $"{EventDate:MMM dd, yyyy} at {DateTime.Today.Add(EventTime):hh:mm tt}";
}

/// <summary>
/// Data model representing dbo.EventEquipment joined with EventAppointment and Equipment.
/// </summary>
public class DispatchItemModel
{
    public int EventEquipmentID { get; set; }
    public string EventAppointmentID { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public DateTime EventDate { get; set; }
    public TimeSpan EventTime { get; set; }
    public string CustomerID { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;

    public string EquipmentID { get; set; } = string.Empty;
    public string EquipmentName { get; set; } = string.Empty;
    public string? SerialNo { get; set; }
    public string EquipmentTypeName { get; set; } = string.Empty;

    public string DispatchStatus { get; set; } = "Dispatched"; // 'Dispatched', 'Scheduled', 'Returned'
    public DateTime DispatchedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReturnedAt { get; set; }
    public string? VerifiedBy { get; set; }
    public string? Notes { get; set; }

    public string FormattedEventSchedule => $"{EventDate:MMM dd, yyyy} ({DateTime.Today.Add(EventTime):hh:mm tt})";
    public bool IsActiveCheckout => DispatchStatus.Equals("Dispatched", StringComparison.OrdinalIgnoreCase);
    public bool IsScheduled => DispatchStatus.Equals("Scheduled", StringComparison.OrdinalIgnoreCase);
    public bool IsReturned => DispatchStatus.Equals("Returned", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Grouped dispatch manifest for displaying in tables where one event has multiple assigned equipment items.
/// </summary>
public class EventDispatchManifestGroup
{
    public string EventAppointmentID { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public DateTime EventDate { get; set; }
    public TimeSpan EventTime { get; set; }
    public string CustomerID { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string OverallStatus { get; set; } = "Dispatched"; // 'Dispatched', 'Scheduled', 'Returned'
    public List<DispatchItemModel> Items { get; set; } = new();

    public string FormattedEventSchedule => $"{EventDate:MMM dd, yyyy} ({DateTime.Today.Add(EventTime):hh:mm tt})";
}

/// <summary>
/// Data model representing maintenance records stored in dbo.HistoryLog for EntityName = 'Equipment'.
/// </summary>
public class MaintenanceLogModel
{
    public long HistoryLogID { get; set; }
    public string ActionType { get; set; } = "STATUS_CHANGE"; // 'INSERT','UPDATE','DELETE','STATUS_CHANGE'
    public string EntityName { get; set; } = "Equipment";
    public string EntityID { get; set; } = string.Empty; // EquipmentID
    public string EquipmentName { get; set; } = string.Empty;
    public string? SerialNo { get; set; }
    public string EquipmentStatus { get; set; } = "Maintenance"; // 'Maintenance', 'Available', 'Retired'
    public string ActionDetails { get; set; } = string.Empty;
    public string ChangedByType { get; set; } = "Admin";
    public string? ChangedByAdminID { get; set; }
    public string? ChangedByEmployeeID { get; set; }
    public string ActorID => ChangedByAdminID ?? ChangedByEmployeeID ?? "ADM-001";
    public DateTime LoggedAt { get; set; } = DateTime.UtcNow;

    public bool IsCalibration => ActionDetails.Contains("Calibration", StringComparison.OrdinalIgnoreCase);
    public bool IsResolved => ActionDetails.Contains("Resolved", StringComparison.OrdinalIgnoreCase) || EquipmentStatus.Equals("Available", StringComparison.OrdinalIgnoreCase);
}

// ─────────────────────────────────────────────────────────────────────────────
// Form Validation Models
// ─────────────────────────────────────────────────────────────────────────────

public class NewEquipmentFormModel
{
    public string? EquipmentID { get; set; }

    [Required(ErrorMessage = "Equipment name is required.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 100 characters.")]
    public string EquipmentName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please select an equipment category type.")]
    public string EquipmentTypeID { get; set; } = string.Empty;

    [RegularExpression(@"^[a-zA-Z0-9\-_.]*$", ErrorMessage = "Serial number contains invalid characters.")]
    public string? SerialNo { get; set; }

    public string EquipmentStatus { get; set; } = "Available";
}

public class NewEquipmentTypeFormModel
{
    [Required(ErrorMessage = "Category Type ID is required (e.g. ET-CAM).")]
    [StringLength(30, MinimumLength = 3, ErrorMessage = "ID must be between 3 and 30 characters.")]
    public string EquipmentTypeID { get; set; } = string.Empty;

    [Required(ErrorMessage = "Category Name is required.")]
    [StringLength(255, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 255 characters.")]
    public string EquipmentTypeName { get; set; } = string.Empty;

    public string ColorTag { get; set; } = "Blue";
}

public class NewDispatchFormModel
{
    [Required(ErrorMessage = "Please select a target event appointment.")]
    public string EventAppointmentID { get; set; } = string.Empty;

    [MinLength(1, ErrorMessage = "Select at least one equipment item to dispatch.")]
    public List<string> SelectedEquipmentIDs { get; set; } = new();

    public string? Notes { get; set; }
    public string ActorID { get; set; } = "ADM-001";
}

public class NewMaintenanceFormModel
{
    [Required(ErrorMessage = "Please select an equipment item.")]
    public string EquipmentID { get; set; } = string.Empty;

    [Required(ErrorMessage = "Service category/type is required.")]
    public string ServiceType { get; set; } = "Repair"; // 'Repair', 'Calibration', 'Routine Servicing'

    [Required(ErrorMessage = "Maintenance details and diagnosis are required.")]
    [StringLength(1000, MinimumLength = 5, ErrorMessage = "Details must be at least 5 characters.")]
    public string IssueDetails { get; set; } = string.Empty;

    public bool SetUnderMaintenance { get; set; } = true;

    [Required(ErrorMessage = "Staff/Admin ID is required.")]
    public string ActorID { get; set; } = "ADM-001";
}
