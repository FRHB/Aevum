using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoAevumStudio.Web.Models;

public enum ScheduleViewMode
{
    Day,
    Week,
    Month,
    Agenda
}

// ─────────────────────────────────────────────────────────────────────────────
// 1. CALENDAR OF EVENTS MODELS
// ─────────────────────────────────────────────────────────────────────────────

public class StudioSpaceResource
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Dimensions { get; set; } = string.Empty;
    public int MaxCapacity { get; set; }
    public string BadgeColor { get; set; } = "bg-blue-100 text-blue-800 border-blue-200";
    public string IconType { get; set; } = "studio"; // studio, outdoor, venue, commercial
}

public class EventBookingModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string StudioSpaceId { get; set; } = string.Empty;
    public string StudioSpaceName { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string ClientEmail { get; set; } = string.Empty;
    public string ClientPhone { get; set; } = string.Empty;
    public string ServiceType { get; set; } = "Full Studio Session"; // Express Headshot, Full Studio Session, Commercial Campaign, Bridal Prep, Product Catalog
    public DateTime Date { get; set; } = DateTime.Today;
    public TimeSpan StartTime { get; set; } = new(9, 0, 0);
    public TimeSpan EndTime { get; set; } = new(12, 0, 0);
    public string Status { get; set; } = "Confirmed"; // Confirmed, Pending, Completed, Cancelled
    public string AssignedPhotographer { get; set; } = "Elena Rostova";
    public string Notes { get; set; } = string.Empty;
    public decimal EstimatedPrice { get; set; } = 350.00m;
<<<<<<< HEAD

    // Category-driven associations (auto-filled when a category is picked, editable afterwards)
    public string CategoryCode { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public List<string> AssignedStaffIds { get; set; } = new();
    public List<string> AssignedEquipmentIds { get; set; } = new();
=======
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
    public bool HasConflict { get; set; }
    public string? ConflictMessage { get; set; }

    public double DurationHours => (EndTime - StartTime).TotalHours;

    public string FormattedTimeRange =>
        $"{DateTime.Today.Add(StartTime):hh:mm tt} - {DateTime.Today.Add(EndTime):hh:mm tt}";

    public bool OverlapsWith(EventBookingModel other)
    {
        if (Id == other.Id || StudioSpaceId != other.StudioSpaceId || Date.Date != other.Date.Date)
            return false;

        return StartTime < other.EndTime && EndTime > other.StartTime;
    }
<<<<<<< HEAD

    /// <summary>True when both events overlap in time and have at least one assigned staff member in common.</summary>
    public bool SharesStaffWith(EventBookingModel other)
    {
        if (Id == other.Id || Date.Date != other.Date.Date)
            return false;

        return StartTime < other.EndTime && EndTime > other.StartTime
               && AssignedStaffIds.Intersect(other.AssignedStaffIds).Any();
    }
=======
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
}

// ─────────────────────────────────────────────────────────────────────────────
// 2. STAFF SCHEDULE MODELS
// ─────────────────────────────────────────────────────────────────────────────

public class StaffMemberResource
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string StaffCode { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; // Senior Photographer, Lead Videographer, Lighting Specialist, Creative Director, Studio Assistant, Lead Editor
    public string AvatarInitials { get; set; } = string.Empty;
    public string AvatarColor { get; set; } = "bg-indigo-100 text-indigo-700";
    public int TargetHoursPerWeek { get; set; } = 40;
    public string Status { get; set; } = "Active"; // Active, On Leave, Off Duty
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
}

public class StaffShiftModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string? StaffId { get; set; } // null or empty for Open Shifts
    public string StaffName { get; set; } = "Open Shift";
    public string Role { get; set; } = "Photographer";
    public DateTime Date { get; set; } = DateTime.Today;
    public TimeSpan StartTime { get; set; } = new(9, 0, 0);
    public TimeSpan EndTime { get; set; } = new(17, 0, 0);
    public string Location { get; set; } = "Studio A"; // Studio A, Studio B, Outdoor Shoot, Edit Bay 1, Remote
    public string ShiftType { get; set; } = "Full Day"; // Full Day, Half Day, Sick Leave, Unpaid Leave, On-Call, Overtime
    public bool IsOpenShift => string.IsNullOrEmpty(StaffId);
    public string Notes { get; set; } = string.Empty;
    public bool HasConflict { get; set; }
    public string? ConflictMessage { get; set; }

    public double DurationHours => (EndTime - StartTime).TotalHours;

    public string FormattedTimeRange =>
        $"{DateTime.Today.Add(StartTime):hh:mm tt} - {DateTime.Today.Add(EndTime):hh:mm tt}";

    public bool OverlapsWith(StaffShiftModel other)
    {
        if (Id == other.Id || IsOpenShift || other.IsOpenShift || StaffId != other.StaffId || Date.Date != other.Date.Date)
            return false;

        return StartTime < other.EndTime && EndTime > other.StartTime;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 3. EQUIPMENTS SCHEDULE MODELS
// ─────────────────────────────────────────────────────────────────────────────

public class EquipmentItemResource
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // Camera Bodies, Cinema Rigs, Strobe & Continuous Lighting, Prime & Zoom Lenses, Audio & Grip
    public string Status { get; set; } = "Ready"; // Ready, In Use, In Maintenance, Calibration Due
    public string StorageBay { get; set; } = "Bay 1 - Shelf A";
    public string BadgeColor { get; set; } = "bg-emerald-50 text-emerald-700 border-emerald-200";
}

public class EquipmentReservationModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string EquipmentId { get; set; } = string.Empty;
    public string EquipmentName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Today;
    public TimeSpan StartTime { get; set; } = new(9, 0, 0);
    public TimeSpan EndTime { get; set; } = new(14, 0, 0);
    public string ProjectOrClient { get; set; } = string.Empty;
    public string TechnicianInCharge { get; set; } = "Marcus Vance";
    public string Location { get; set; } = "Studio A";
    public string MaintenanceStatus { get; set; } = "Ready"; // Ready, In-Use, Maintenance Required, Calibration Due
<<<<<<< HEAD

    /// <summary>Set when the reservation was created automatically for an event; removed with that event.</summary>
    public string? SourceEventId { get; set; }
=======
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
    public string Notes { get; set; } = string.Empty;
    public bool HasConflict { get; set; }
    public string? ConflictMessage { get; set; }

    public double DurationHours => (EndTime - StartTime).TotalHours;

    public string FormattedTimeRange =>
        $"{DateTime.Today.Add(StartTime):hh:mm tt} - {DateTime.Today.Add(EndTime):hh:mm tt}";

    public bool OverlapsWith(EquipmentReservationModel other)
    {
        if (Id == other.Id || EquipmentId != other.EquipmentId || Date.Date != other.Date.Date)
            return false;

        return StartTime < other.EndTime && EndTime > other.StartTime;
    }
}
<<<<<<< HEAD

// ─────────────────────────────────────────────────────────────────────────────
// 4. SHARED LOOKUPS
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Service types selectable on an event; also the valid values for a category's default service type.</summary>
public static class EventServiceTypes
{
    public static readonly string[] All =
    {
        "Express Headshot",
        "Full Studio Session",
        "Commercial Campaign",
        "Bridal Prep",
        "Product Catalog",
        "Event Coverage"
    };
}
=======
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
