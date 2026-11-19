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

public sealed record EventCrewSummary(string EmployeeId, string FullName, string Specialization, bool IsTeamLeader);
public sealed record EventEquipmentSummary(string EquipmentId, string EquipmentName, string EquipmentType);

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
    public bool HasConflict { get; set; }
    public string? ConflictMessage { get; set; }
    public bool IsConsultation { get; set; }
    public string? MeetingLink { get; set; }
    public List<string> AssignedCrewIds { get; set; } = new();
    public List<string> AssignedEquipmentIds { get; set; } = new();
    public List<string> AssignedEquipmentNames { get; set; } = new();
    public List<EventEquipmentSummary> AssignedEquipment { get; set; } = new();
    public List<EventCrewSummary> AssignedCrew { get; set; } = new();
    public List<string> AssignedServices { get; set; } = new();
    public int PrepBufferMinutes { get; set; } = 120;
    public int PostBufferMinutes { get; set; } = 60;
    public string? EventLocation { get; set; }
    public string? LinkedEventAppointmentId { get; set; }
    public string? LinkedNegotiationId { get; set; }

    public double DurationHours => (EndTime - StartTime).TotalHours;

    public TimeSpan EffectiveEndTime => EndTime > StartTime ? EndTime : StartTime.Add(TimeSpan.FromHours(1));

    public TimeSpan CombinedStartTime =>
        StartTime >= TimeSpan.FromMinutes(PrepBufferMinutes)
            ? StartTime.Subtract(TimeSpan.FromMinutes(PrepBufferMinutes))
            : TimeSpan.Zero;

    public TimeSpan CombinedEndTime
    {
        get
        {
            var end = EffectiveEndTime.Add(TimeSpan.FromMinutes(PostBufferMinutes));
            return end > new TimeSpan(23, 59, 59) ? new TimeSpan(23, 59, 59) : end;
        }
    }

    public bool IsCompleted =>
        Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
        Status.Equals("Complete", StringComparison.OrdinalIgnoreCase) ||
        Status.Equals("Shot", StringComparison.OrdinalIgnoreCase) ||
        Status.Equals("Delivered", StringComparison.OrdinalIgnoreCase);

    public bool IsPendingPayment =>
        Status.Equals("Pending Payment", StringComparison.OrdinalIgnoreCase) ||
        Status.Equals("Pending_Payment", StringComparison.OrdinalIgnoreCase) ||
        Status.Equals("Awaiting_Downpayment", StringComparison.OrdinalIgnoreCase);

    public bool IsAppointmentBooked =>
        Status.Equals("Appointment Booked", StringComparison.OrdinalIgnoreCase) ||
        Status.Equals("Deal_Won", StringComparison.OrdinalIgnoreCase);

    public string DisplayStatus
    {
        get
        {
            if (IsCompleted) return "Completed";
            if (IsPendingPayment) return IsConsultation ? "Pending Payment" : "Awaiting Reserve Fee Payment";
            if (IsAppointmentBooked) return "Appointment Booked";
            if (Status.Equals("Pending_Call", StringComparison.OrdinalIgnoreCase)) return "Pending Call";
            return Status;
        }
    }

    public string FormattedTimeRange =>
        $"{DateTime.Today.Add(StartTime):h:mm tt} - {DateTime.Today.Add(EffectiveEndTime):h:mm tt}";

    public bool CombinedWindowOverlapsWith(EventBookingModel other)
    {
        if (IsConsultation || other.IsConsultation || Id == other.Id || Date.Date != other.Date.Date)
            return false;

        return CombinedStartTime < other.CombinedEndTime && other.CombinedStartTime < CombinedEndTime;
    }

    public bool OverlapsWith(EventBookingModel other)
    {
        if (IsConsultation || other.IsConsultation)
            return false;

        if (Id == other.Id || Date.Date != other.Date.Date)
            return false;

        // Same-day equipment lock: sharing any equipment on the same calendar date is always a conflict, regardless of time slot.
        if (SharesEquipmentWith(other))
            return true;

        if (!CombinedWindowOverlapsWith(other))
            return false;

        return SharesCrewWith(other);
    }

    public bool SharesCrewWith(EventBookingModel other)
    {
        if (AssignedCrewIds.Count > 0 && other.AssignedCrewIds.Count > 0)
        {
            if (AssignedCrewIds.Any(id => other.AssignedCrewIds.Contains(id, StringComparer.OrdinalIgnoreCase)))
                return true;
        }

        var names1 = (AssignedPhotographer ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(n => !string.Equals(n, "Unassigned", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var names2 = (other.AssignedPhotographer ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(n => !string.Equals(n, "Unassigned", StringComparison.OrdinalIgnoreCase));

        return names2.Any(n => names1.Contains(n));
    }

    public bool SharesEquipmentWith(EventBookingModel other)
    {
        if (AssignedEquipmentIds.Count == 0 || other.AssignedEquipmentIds.Count == 0)
            return false;

        return AssignedEquipmentIds.Any(id => other.AssignedEquipmentIds.Contains(id, StringComparer.OrdinalIgnoreCase));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 2. STAFF SCHEDULE MODELS
// ─────────────────────────────────────────────────────────────────────────────

public class StaffMemberResource
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string EmployeeID { get; set; } = string.Empty;
    public string StaffCode
    {
        get => string.IsNullOrEmpty(EmployeeID) ? Id : EmployeeID;
        set => EmployeeID = value;
    }
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
    public string? EventAppointmentId { get; set; }
    public string EventStatus { get; set; } = "Confirmed";
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

        // Same-day calendar lock: booked equipment is unavailable for the entire calendar day.
        return true;
    }
}
