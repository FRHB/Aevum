using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public class ScheduleService : IScheduleService
{
    private readonly string _connectionString;
    private readonly bool _useInMemory;

    private readonly List<StudioSpaceResource> _inMemorySpaces = new();
    private readonly List<EventBookingModel> _inMemoryBookings = new();
    private readonly List<StaffMemberResource> _inMemoryStaff = new();
    private readonly List<StaffShiftModel> _inMemoryShifts = new();
    private readonly List<EquipmentItemResource> _inMemoryEquipment = new();
    private readonly List<EquipmentReservationModel> _inMemoryReservations = new();

    private readonly object _eventLock = new();
    private static readonly object _sqlCacheLock = new();
    private static (DateTime? start, DateTime? end, DateTime fetchedAt, List<EventBookingModel> bookings)? _sqlCachedEventBookings;
    private static (DateTime fetchedAt, List<StaffMemberResource> staff)? _sqlCachedStaffMembers;
    private static (DateTime fetchedAt, List<StaffShiftModel> shifts)? _sqlCachedStaffShifts;
    private static (DateTime fetchedAt, List<EquipmentItemResource> equipment)? _sqlCachedEquipmentResources;
    private static (DateTime fetchedAt, List<EquipmentReservationModel> reservations)? _sqlCachedEquipmentReservations;
    private static readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(30);

    public void InvalidateCache() => InvalidateStaticCache();

    public static void InvalidateStaticCache()
    {
        lock (_sqlCacheLock)
        {
            _sqlCachedEventBookings = null;
            _sqlCachedStaffMembers = null;
            _sqlCachedStaffShifts = null;
            _sqlCachedEquipmentResources = null;
            _sqlCachedEquipmentReservations = null;
        }
    }

    private static EventBookingModel CloneBooking(EventBookingModel b) => new()
    {
        Id = b.Id,
        StudioSpaceId = b.StudioSpaceId,
        StudioSpaceName = b.StudioSpaceName,
        ClientName = b.ClientName,
        ClientEmail = b.ClientEmail,
        ClientPhone = b.ClientPhone,
        ServiceType = b.ServiceType,
        Date = b.Date,
        StartTime = b.StartTime,
        EndTime = b.EndTime,
        Status = b.Status,
        AssignedPhotographer = b.AssignedPhotographer,
        Notes = b.Notes,
        EstimatedPrice = b.EstimatedPrice,
        HasConflict = b.HasConflict,
        ConflictMessage = b.ConflictMessage,
        IsConsultation = b.IsConsultation,
        MeetingLink = b.MeetingLink,
        AssignedCrewIds = b.AssignedCrewIds.ToList(),
        AssignedEquipmentIds = b.AssignedEquipmentIds.ToList(),
        AssignedEquipmentNames = b.AssignedEquipmentNames.ToList(),
        AssignedEquipment = b.AssignedEquipment.ToList(),
        AssignedCrew = b.AssignedCrew.ToList(),
        AssignedServices = b.AssignedServices.ToList(),
        PrepBufferMinutes = b.PrepBufferMinutes,
        PostBufferMinutes = b.PostBufferMinutes,
        EventLocation = b.EventLocation,
        LinkedEventAppointmentId = b.LinkedEventAppointmentId,
        LinkedNegotiationId = b.LinkedNegotiationId
    };

    private static StaffMemberResource CloneStaffMember(StaffMemberResource s) => new()
    {
        Id = s.Id,
        StaffCode = s.StaffCode,
        FullName = s.FullName,
        Role = s.Role,
        Email = s.Email,
        Status = s.Status,
        AvatarInitials = s.AvatarInitials,
        AvatarColor = s.AvatarColor,
        TargetHoursPerWeek = s.TargetHoursPerWeek
    };

    private static StaffShiftModel CloneShift(StaffShiftModel s) => new()
    {
        Id = s.Id,
        StaffId = s.StaffId,
        StaffName = s.StaffName,
        Role = s.Role,
        Date = s.Date,
        StartTime = s.StartTime,
        EndTime = s.EndTime,
        Location = s.Location,
        ShiftType = s.ShiftType,
        Notes = s.Notes,
        HasConflict = s.HasConflict,
        ConflictMessage = s.ConflictMessage
    };

    private static EquipmentItemResource CloneEquipment(EquipmentItemResource e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        SerialNumber = e.SerialNumber,
        Category = e.Category,
        Status = e.Status,
        StorageBay = e.StorageBay,
        BadgeColor = e.BadgeColor
    };

    private static EquipmentReservationModel CloneReservation(EquipmentReservationModel r) => new()
    {
        Id = r.Id,
        EventAppointmentId = r.EventAppointmentId,
        EventStatus = r.EventStatus,
        EquipmentId = r.EquipmentId,
        EquipmentName = r.EquipmentName,
        Category = r.Category,
        Date = r.Date,
        StartTime = r.StartTime,
        EndTime = r.EndTime,
        ProjectOrClient = r.ProjectOrClient,
        TechnicianInCharge = r.TechnicianInCharge,
        Location = r.Location,
        MaintenanceStatus = r.MaintenanceStatus,
        MaintenanceSubStatus = r.MaintenanceSubStatus,
        MaintenanceNotes = r.MaintenanceNotes,
        Notes = r.Notes,
        HasConflict = r.HasConflict,
        ConflictMessage = r.ConflictMessage
    };

    private readonly List<StudioSpaceResource> _defaultStudioSpaces = new()
    {
        new()
        {
            Id = "studio-a",
            Name = "Studio A (Main Hall)",
            Dimensions = "1,200 sq.ft",
            MaxCapacity = 25,
            IconType = "studio",
            BadgeColor = "bg-blue-100 text-blue-800 border-blue-200",
            Description = "Cyclorama wall with high grid lighting."
        },
        new()
        {
            Id = "SP-02",
            Name = "Studio B (Portrait & Editorial)",
            Dimensions = "650 sq.ft",
            MaxCapacity = 10,
            IconType = "studio",
            BadgeColor = "bg-indigo-100 text-indigo-800 border-indigo-200",
            Description = "Daylight windows and multiple backdrop rollers."
        },
        
        new()
        {
            Id = "SP-04",
            Name = "Edit Bay & Audio Suite",
            Dimensions = "220 sq.ft",
            MaxCapacity = 4,
            IconType = "commercial",
            BadgeColor = "bg-amber-100 text-amber-800 border-amber-200",
            Description = "Color grading and live sound monitoring."
        }
    };

    public ScheduleService(IConfiguration? configuration = null)
    {
        _useInMemory = configuration is null;
        _connectionString = new[]
            {
                configuration?.GetConnectionString("DefaultConnection"),
                configuration?["ConnectionStrings__DefaultConnection"],
                configuration?["AEVUM_DB_CONNECTION"],
                Environment.GetEnvironmentVariable("AEVUM_DB_CONNECTION"),
                Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
            }.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

        if (!_useInMemory && string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
        }

        if (_useInMemory)
        {
            SeedInMemoryData();
        }
    }

    private SqlConnection CreateConnection() => SqlResilienceHelper.CreateConnection(_connectionString);

    private void SeedInMemoryData()
    {
        var today = DateTime.Today;
        int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
        var monday = today.AddDays(-1 * diff);

        _inMemorySpaces.AddRange(_defaultStudioSpaces);

        _inMemoryBookings.AddRange(new[]
        {
            new EventBookingModel
            {
                Id = "evt-101",
                StudioSpaceId = "studio-a",
                StudioSpaceName = "Studio A (Main Hall)",
                EventLocation = "Studio A (Main Hall)",
                ClientName = "Vogue Scandinavia",
                ClientEmail = "production@voguescan.com",
                ClientPhone = "+1 (555) 234-5678",
                ServiceType = "Commercial Campaign",
                AssignedServices = new List<string> { "Commercial Campaign" },
                Date = monday,
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(13, 0, 0),
                Status = "Confirmed",
                AssignedPhotographer = "Elena Rostova",
                AssignedCrewIds = new List<string> { "staff-01" },
                AssignedCrew = new List<EventCrewSummary> { new("staff-01", "Elena Rostova", "Senior Photographer", true) },
                EstimatedPrice = 1200.00m,
                Notes = "Autumn fashion editorial."
            },
            new EventBookingModel
            {
                Id = "evt-105",
                StudioSpaceId = "studio-a",
                StudioSpaceName = "Studio A (Main Hall)",
                EventLocation = "Studio A (Main Hall)",
                ClientName = "Horizon Luxury Apparel",
                ClientEmail = "shoot@horizonapparel.com",
                ClientPhone = "+1 (555) 678-9012",
                ServiceType = "Commercial Campaign",
                AssignedServices = new List<string> { "Commercial Campaign" },
                Date = monday.AddDays(2),
                StartTime = new TimeSpan(10, 0, 0),
                EndTime = new TimeSpan(14, 0, 0),
                Status = "Confirmed",
                AssignedPhotographer = "Elena Rostova",
                AssignedCrewIds = new List<string> { "staff-01" },
                AssignedCrew = new List<EventCrewSummary> { new("staff-01", "Elena Rostova", "Senior Photographer", true) },
                AssignedEquipmentIds = new List<string> { "eq-01" },
                AssignedEquipmentNames = new List<string> { "Canon EOS R5 Mark II (#1)" },
                AssignedEquipment = new List<EventEquipmentSummary> { new("eq-01", "Canon EOS R5 Mark II (#1)", "Camera Bodies") },
                EstimatedPrice = 1400.00m,
                Notes = "Winter outerwear collection lookbook."
            },
            new EventBookingModel
            {
                Id = "evt-106",
                StudioSpaceId = "studio-a",
                StudioSpaceName = "Studio A (Main Hall)",
                EventLocation = "Studio A (Main Hall)",
                ClientName = "Sterling Capital Partners",
                ClientEmail = "partners@sterlingcap.com",
                ClientPhone = "+1 (555) 789-0123",
                ServiceType = "Express Headshot",
                AssignedServices = new List<string> { "Express Headshot" },
                Date = monday.AddDays(2),
                StartTime = new TimeSpan(12, 30, 0),
                EndTime = new TimeSpan(16, 0, 0),
                Status = "Pending",
                AssignedPhotographer = "Clara Oswald",
                AssignedCrewIds = new List<string> { "staff-03" },
                AssignedCrew = new List<EventCrewSummary> { new("staff-03", "Clara Oswald", "Photographer", true) },
                AssignedEquipmentIds = new List<string> { "eq-01" },
                AssignedEquipmentNames = new List<string> { "Canon EOS R5 Mark II (#1)" },
                AssignedEquipment = new List<EventEquipmentSummary> { new("eq-01", "Canon EOS R5 Mark II (#1)", "Camera Bodies") },
                EstimatedPrice = 850.00m,
                Notes = "Board of Directors corporate portrait session."
            }
        });

        _inMemoryStaff.AddRange(new[]
        {
            new StaffMemberResource
            {
                Id = "staff-01",
                FullName = "Elena Rostova",
                StaffCode = "PA-001",
                Role = "Senior Photographer",
                AvatarInitials = "ER",
                AvatarColor = "bg-rose-100 text-rose-700",
                TargetHoursPerWeek = 40,
                Status = "Active",
                Email = "elena.rostova@photoaevum.com",
                Phone = "+1 (555) 101-0001"
            },
            new StaffMemberResource
            {
                Id = "staff-02",
                FullName = "Marcus Vance",
                StaffCode = "PA-002",
                Role = "Lead Videographer",
                AvatarInitials = "MV",
                AvatarColor = "bg-blue-100 text-blue-700",
                TargetHoursPerWeek = 40,
                Status = "Active",
                Email = "marcus.vance@photoaevum.com",
                Phone = "+1 (555) 101-0002"
            }
        });

        _inMemoryShifts.AddRange(new[]
        {
            new StaffShiftModel
            {
                Id = "shift-open-01",
                StaffId = null,
                StaffName = "Open Shift",
                Role = "Grip & Audio Rigging",
                Date = monday.AddDays(1),
                StartTime = new TimeSpan(10, 0, 0),
                EndTime = new TimeSpan(16, 0, 0),
                Location = "Studio C",
                ShiftType = "Full Day",
                Notes = "Second boom operator needed."
            },
            new StaffShiftModel
            {
                Id = "shift-103",
                StaffId = "staff-01",
                StaffName = "Elena Rostova",
                Role = "Senior Photographer",
                Date = monday.AddDays(3),
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(14, 0, 0),
                Location = "Studio A",
                ShiftType = "Full Day",
                Notes = "Horizon campaign session."
            },
            new StaffShiftModel
            {
                Id = "shift-104",
                StaffId = "staff-01",
                StaffName = "Elena Rostova",
                Role = "Senior Photographer",
                Date = monday.AddDays(3),
                StartTime = new TimeSpan(12, 30, 0),
                EndTime = new TimeSpan(17, 30, 0),
                Location = "Outdoor Shoot",
                ShiftType = "Half Day",
                Notes = "Assigned to outdoor sunset bridal preview."
            }
        });

        _inMemoryEquipment.AddRange(new[]
        {
            new EquipmentItemResource
            {
                Id = "eq-01",
                Name = "Canon EOS R5 Mark II (#1)",
                SerialNumber = "SN-CAN-88214",
                Category = "Camera Bodies",
                Status = "Ready",
                StorageBay = "Locker A-01",
                BadgeColor = "bg-blue-50 text-blue-700 border-blue-200"
            },
            new EquipmentItemResource
            {
                Id = "eq-03",
                Name = "Godox AD600 Pro Strobe Kit A",
                SerialNumber = "SN-GDX-10229",
                Category = "Strobe Lighting",
                Status = "Ready",
                StorageBay = "Lighting Bay Shelf 2",
                BadgeColor = "bg-amber-50 text-amber-700 border-amber-200"
            }
        });

        _inMemoryReservations.AddRange(new[]
        {
            new EquipmentReservationModel
            {
                Id = "res-401",
                EquipmentId = "eq-03",
                EquipmentName = "Godox AD600 Pro Strobe Kit A",
                Category = "Strobe Lighting",
                Date = monday.AddDays(3),
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(14, 0, 0),
                ProjectOrClient = "Nordic Roast Product Shoot",
                TechnicianInCharge = "Alex Rivera",
                Location = "Studio B",
                MaintenanceStatus = "Ready"
            },
            new EquipmentReservationModel
            {
                Id = "res-402",
                EquipmentId = "eq-03",
                EquipmentName = "Godox AD600 Pro Strobe Kit A",
                Category = "Strobe Lighting",
                Date = monday.AddDays(3),
                StartTime = new TimeSpan(12, 0, 0),
                EndTime = new TimeSpan(17, 0, 0),
                ProjectOrClient = "Commercial Lookbook",
                TechnicianInCharge = "Clara Oswald",
                Location = "Studio A",
                MaintenanceStatus = "Ready"
            }
        });

        RecalculateEventConflicts(_inMemoryBookings);
        RecalculateShiftConflicts(_inMemoryShifts);
        RecalculateEquipmentConflicts(_inMemoryReservations);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. STUDIO SPACES & EVENTS
    // ─────────────────────────────────────────────────────────────────────────

    public List<StudioSpaceResource> GetStudioSpaces() => _useInMemory ? _inMemorySpaces : _defaultStudioSpaces;

    public List<EventBookingModel> GetEventBookings(DateTime? startDate = null, DateTime? endDate = null)
    {
        if (_useInMemory)
        {
            var eqStatusLookup = _inMemoryEquipment.ToDictionary(e => e.Id, e => e.Status, StringComparer.OrdinalIgnoreCase);
            foreach (var b in _inMemoryBookings)
            {
                if (b.AssignedEquipment.Count > 0)
                {
                    b.AssignedEquipment = b.AssignedEquipment
                        .Select(ae => eqStatusLookup.TryGetValue(ae.EquipmentId, out var st)
                            ? ae with { EquipmentStatus = st }
                            : ae)
                        .ToList();
                }
            }

            RecalculateEventConflicts(_inMemoryBookings);
            var inMemQuery = _inMemoryBookings.AsEnumerable();
            if (startDate.HasValue)
                inMemQuery = inMemQuery.Where(b => b.Date.Date >= startDate.Value.Date);
            if (endDate.HasValue)
                inMemQuery = inMemQuery.Where(b => b.Date.Date <= endDate.Value.Date);

            return inMemQuery.ToList();
        }

        lock (_sqlCacheLock)
        {
            if (_sqlCachedEventBookings.HasValue &&
                (DateTime.UtcNow - _sqlCachedEventBookings.Value.fetchedAt) < _cacheDuration)
            {
                var cached = _sqlCachedEventBookings.Value;
                bool coversRange =
                    (cached.start == null || (startDate.HasValue && startDate.Value.Date >= cached.start.Value.Date)) &&
                    (cached.end == null || (endDate.HasValue && endDate.Value.Date <= cached.end.Value.Date));

                if (coversRange)
                {
                    var cachedQuery = cached.bookings.AsEnumerable();
                    if (startDate.HasValue)
                        cachedQuery = cachedQuery.Where(b => b.Date.Date >= startDate.Value.Date);
                    if (endDate.HasValue)
                        cachedQuery = cachedQuery.Where(b => b.Date.Date <= endDate.Value.Date);
                    return cachedQuery.Select(CloneBooking).ToList();
                }
            }
        }

        var bookings = new List<EventBookingModel>();

        try
        {
            using var conn = CreateConnection();
            conn.Open();

            bool hasGranularCols = false;
            bool hasLocationCol = false;
            bool hasFinalPayCols = false;
            try
            {
                using var chkCmd = new SqlCommand(@"
                    SELECT
                        CASE WHEN COL_LENGTH(N'dbo.EventAppointment', N'EventEndTime') IS NOT NULL AND COL_LENGTH(N'dbo.EventAppointment', N'DurationMinutes') IS NOT NULL THEN 1 ELSE 0 END,
                        CASE WHEN COL_LENGTH(N'dbo.EventAppointment', N'EventLocation') IS NOT NULL THEN 1 ELSE 0 END,
                        CASE WHEN COL_LENGTH(N'dbo.EventAppointment', N'FinalPaymentStatus') IS NOT NULL AND COL_LENGTH(N'dbo.EventAppointment', N'FinalPaymentReferenceId') IS NOT NULL THEN 1 ELSE 0 END;", conn);
                using var chkReader = chkCmd.ExecuteReader();
                if (chkReader.Read())
                {
                    hasGranularCols = chkReader.GetInt32(0) == 1;
                    hasLocationCol = chkReader.GetInt32(1) == 1;
                    hasFinalPayCols = chkReader.GetInt32(2) == 1;
                }
            }
            catch { }

            var endTimeSelect = hasGranularCols
                ? "ea.EventEndTime, ISNULL(ea.DurationMinutes, 120) AS DurationMinutes,"
                : "CAST(NULL AS time) AS EventEndTime, CAST(120 AS int) AS DurationMinutes,";

            var locationSelect = hasLocationCol
                ? "ea.EventLocation,"
                : "CAST(NULL AS nvarchar(255)) AS EventLocation,";

            var statusSelect = hasFinalPayCols
                ? @"CASE
                        WHEN ea.EventStatus = 'Pending Payment' AND NULLIF(ea.FinalPaymentReferenceId, '') IS NOT NULL
                            THEN COALESCE(NULLIF(ea.FinalPaymentStatus, ''), 'Payment Verification (QR:Pending Payment)')
                        ELSE ea.EventStatus
                    END AS EventStatus,"
                : "ea.EventStatus,";

            string sql = $@"
                SELECT ea.EventAppointmentID,
                       ea.EventName,
                       ea.EventDescription,
                       ea.CustomerID,
                       COALESCE(NULLIF(LTRIM(RTRIM(c.CustomerFirstName + ' ' + ISNULL(c.CustomerLastName, ''))), ''), ea.CustomerID, 'Studio Client') AS CustomerName,
                       c.CustomerEmail,
                       c.CustomerPhoneNumber,
                       CAST(ea.EventDate AS datetime2) AS EventDate,
                       ea.EventTime,
                       {statusSelect}
                       leadEmp.EmployeeID,
                       COALESCE(leadEmp.EmployeeName, 'Unassigned') AS EmployeeName,
                       CAST(0 AS bit) AS IsConsultation,
                       NULL AS MeetingLink,
                       {endTimeSelect}
                       ISNULL(ea.TotalAmount, 0) AS TotalAmount,
                       crewIds.CrewIdList,
                       eqIds.EquipIdList,
                       {locationSelect}
                       equipDetails.EquipDetailList,
                       crewDetails.CrewDetailList,
                       serviceDetails.ServiceList,
                       CAST(NULL AS nvarchar(30)) AS LinkedEventAppointmentId,
                       linkedBn.NegotiationID AS LinkedNegotiationId
                FROM dbo.EventAppointment ea
                LEFT JOIN dbo.Customer c ON c.CustomerID = ea.CustomerID
                OUTER APPLY (
                    SELECT TOP 1 NegotiationID
                    FROM dbo.BookingNegotiation
                    WHERE EventAppointmentID = ea.EventAppointmentID
                    ORDER BY CreatedAt DESC
                ) linkedBn
                OUTER APPLY (
                    SELECT STUFF((
                        SELECT ', ' + COALESCE(NULLIF(LTRIM(RTRIM(emp.EmployeeFirstName + ' ' + ISNULL(emp.EmployeeLastName, ''))), ''), emp.EmployeeID)
                        FROM dbo.EventEmployee ee
                        LEFT JOIN dbo.Employees emp ON emp.EmployeeID = ee.EmployeeID
                        WHERE ee.EventAppointmentID = ea.EventAppointmentID
                        ORDER BY ISNULL(ee.IsTeamLeader, 0) DESC, ee.EmployeeID
                        FOR XML PATH(''), TYPE
                    ).value('.', 'nvarchar(max)'), 1, 2, '') AS EmployeeName,
                    (
                        SELECT TOP 1 ee.EmployeeID
                        FROM dbo.EventEmployee ee
                        WHERE ee.EventAppointmentID = ea.EventAppointmentID
                        ORDER BY ISNULL(ee.IsTeamLeader, 0) DESC, ee.EmployeeID
                    ) AS EmployeeID
                ) leadEmp
                OUTER APPLY (
                    SELECT STUFF((
                        SELECT ',' + ee.EmployeeID
                        FROM dbo.EventEmployee ee
                        WHERE ee.EventAppointmentID = ea.EventAppointmentID AND ee.EmployeeID IS NOT NULL
                        FOR XML PATH(''), TYPE
                    ).value('.', 'nvarchar(max)'), 1, 1, '') AS CrewIdList
                ) crewIds
                OUTER APPLY (
                    SELECT STUFF((
                        SELECT ',' + eeq.EquipmentID
                        FROM dbo.EventEquipment eeq
                        WHERE eeq.EventAppointmentID = ea.EventAppointmentID AND eeq.EquipmentID IS NOT NULL
                        FOR XML PATH(''), TYPE
                    ).value('.', 'nvarchar(max)'), 1, 1, '') AS EquipIdList
                ) eqIds
                OUTER APPLY (
                    SELECT STUFF((
                        SELECT ';;' + eeq.EquipmentID + '|' + ISNULL(eq.EquipmentName, eeq.EquipmentID) + '|' + COALESCE(et.EquipmentTypeName, eq.EquipmentTypeID, 'Equipment') + '|' + COALESCE(eq.EquipmentStatus, 'Available')
                        FROM dbo.EventEquipment eeq
                        LEFT JOIN dbo.Equipment eq ON eq.EquipmentID = eeq.EquipmentID
                        LEFT JOIN dbo.EquipmentType et ON et.EquipmentTypeID = eq.EquipmentTypeID
                        WHERE eeq.EventAppointmentID = ea.EventAppointmentID AND eeq.EquipmentID IS NOT NULL
                        ORDER BY eeq.EventEquipmentID ASC
                        FOR XML PATH(''), TYPE
                    ).value('.', 'nvarchar(max)'), 1, 2, '') AS EquipDetailList
                ) equipDetails
                OUTER APPLY (
                    SELECT STUFF((
                        SELECT ';;' + ee.EmployeeID + '|' + COALESCE(NULLIF(LTRIM(RTRIM(ISNULL(emp.EmployeeFirstName, '') + ' ' + ISNULL(emp.EmployeeLastName, ''))), ''), NULLIF(ee.AssignedEmployeeName, ''), ee.EmployeeID) + '|' + CASE WHEN ISNULL(ee.IsTeamLeader, 0) = 1 THEN '1' ELSE '0' END
                        FROM dbo.EventEmployee ee
                        LEFT JOIN dbo.Employees emp ON emp.EmployeeID = ee.EmployeeID
                        WHERE ee.EventAppointmentID = ea.EventAppointmentID AND ee.EmployeeID IS NOT NULL
                        ORDER BY ISNULL(ee.IsTeamLeader, 0) DESC, ee.EmployeeID ASC
                        FOR XML PATH(''), TYPE
                    ).value('.', 'nvarchar(max)'), 1, 2, '') AS CrewDetailList
                ) crewDetails
                OUTER APPLY (
                    SELECT STUFF((
                        SELECT ';;' + COALESCE(s.ServiceName, cp.ComboPackageName, ads.AdditionalServiceName, eli.EventLineItemID)
                        FROM dbo.EventLineItem eli
                        LEFT JOIN dbo.Services s ON s.ServicesID = eli.ServicesID
                        LEFT JOIN dbo.ComboPackage cp ON cp.ComboPackageID = eli.ComboPackageID
                        LEFT JOIN dbo.AdditionalServices ads ON ads.AdditionalServicesID = eli.AdditionalServicesID
                        WHERE eli.EventAppointmentID = ea.EventAppointmentID
                        FOR XML PATH(''), TYPE
                    ).value('.', 'nvarchar(max)'), 1, 2, '') AS ServiceList
                ) serviceDetails
                WHERE ISNULL(ea.EventStatus, '') NOT IN ('Cancelled', 'Expired')
                AND (
                    ISNULL(ea.DownpaymentStatus, 'Pending') = 'Paid'
                    OR ea.EventStatus IN ('Confirmed', 'Ready', 'In Execution', 'Preparation', 'Preparing', 'Dispatch', 'Ongoing', 'On Going', 'Event Done', 'Post Event', 'Payment Verification (Cash)', 'Pending Verification', 'Paid Cash Pending Verification', 'Complete', 'Completed', 'Shot', 'Delivered')
                    OR (
                        ea.EventAppointmentID NOT IN (
                            SELECT EventAppointmentID 
                            FROM dbo.BookingNegotiation 
                            WHERE EventAppointmentID IS NOT NULL 
                              AND NegotiationStatus IN ('Pending_Call', 'Rescheduled', 'Deal_Lost', 'Cancelled')
                        )
                        AND ea.EventAppointmentID NOT IN (
                            SELECT 'EVT-' + RIGHT(NegotiationID, 4)
                            FROM dbo.BookingNegotiation
                            WHERE EventAppointmentID IS NULL
                              AND NegotiationStatus IN ('Pending_Call', 'Rescheduled', 'Deal_Lost', 'Cancelled')
                        )
                    )
                )
                AND (@StartDate IS NULL OR CAST(ea.EventDate AS date) >= @StartDate)
                AND (@EndDate IS NULL OR CAST(ea.EventDate AS date) <= @EndDate)

                UNION ALL

                SELECT bn.NegotiationID AS EventAppointmentID,
                       'Consultation: ' + ISNULL(NULLIF(bn.InquiryNotes, ''), 'Client Video Inquiry') AS EventName,
                       bn.InquiryNotes AS EventDescription,
                       bn.CustomerID,
                       COALESCE(NULLIF(LTRIM(RTRIM(c.CustomerFirstName + ' ' + ISNULL(c.CustomerLastName, ''))), ''), bn.CustomerID, 'Studio Client') AS CustomerName,
                       c.CustomerEmail,
                       c.CustomerPhoneNumber,
                       CAST(bn.ConsultationDate AS datetime2) AS EventDate,
                       bn.ConsultationTime AS EventTime,
                       bn.NegotiationStatus AS EventStatus,
                       emp.EmployeeID,
                       COALESCE(NULLIF(LTRIM(RTRIM(emp.EmployeeFirstName + ' ' + ISNULL(emp.EmployeeLastName, ''))), ''), emp.EmployeeID, 'Office Clerk') AS EmployeeName,
                       CAST(1 AS bit) AS IsConsultation,
                       bn.MeetingLink,
                       CAST(NULL AS time) AS EventEndTime,
                       CAST(30 AS int) AS DurationMinutes,
                       CAST(0 AS decimal(18,2)) AS TotalAmount,
                       emp.EmployeeID AS CrewIdList,
                       CAST(NULL AS nvarchar(max)) AS EquipIdList,
                       CAST(NULL AS nvarchar(255)) AS EventLocation,
                       CAST(NULL AS nvarchar(max)) AS EquipDetailList,
                       CAST(NULL AS nvarchar(max)) AS CrewDetailList,
                       CAST(NULL AS nvarchar(max)) AS ServiceList,
                       bn.EventAppointmentID AS LinkedEventAppointmentId,
                       CAST(NULL AS nvarchar(30)) AS LinkedNegotiationId
                FROM dbo.BookingNegotiation bn
                LEFT JOIN dbo.Customer c ON c.CustomerID = bn.CustomerID
                LEFT JOIN dbo.Employees emp ON emp.EmployeeID = bn.EmployeeID
                WHERE bn.NegotiationStatus IN ('Pending_Call', 'Rescheduled', 'Pending Payment', 'Pending_Payment', 'Appointment Booked', 'Deal_Won')
                  AND (@StartDate IS NULL OR CAST(bn.ConsultationDate AS date) >= @StartDate)
                  AND (@EndDate IS NULL OR CAST(bn.ConsultationDate AS date) <= @EndDate)
                ORDER BY EventDate DESC, EventTime DESC;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@StartDate", (object?)startDate?.Date ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@EndDate", (object?)endDate?.Date ?? DBNull.Value);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var id = reader.GetString(0);
                var eventName = reader.GetString(1);
                var desc = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var custId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                var clientName = reader.IsDBNull(4) ? "Studio Client" : reader.GetString(4);
                var email = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
                var phone = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
                var eventDate = reader.GetDateTime(7);
                var eventTime = reader.GetFieldValue<TimeSpan>(8);
                var status = reader.IsDBNull(9) ? "Confirmed" : reader.GetString(9);
                var empName = reader.IsDBNull(11) ? "Unassigned" : reader.GetString(11);
                var isConsultation = !reader.IsDBNull(12) && reader.GetBoolean(12);
                var meetingLink = reader.IsDBNull(13) ? null : reader.GetString(13);
                var dbEndTime = reader.FieldCount > 14 && !reader.IsDBNull(14) ? reader.GetFieldValue<TimeSpan>(14) : (TimeSpan?)null;
                var durationMins = reader.FieldCount > 15 && !reader.IsDBNull(15) ? Math.Max(15, reader.GetInt32(15)) : (isConsultation ? 30 : 120);
                var totalAmount = reader.FieldCount > 16 && !reader.IsDBNull(16) ? reader.GetDecimal(16) : 0m;
                var crewListRaw = reader.FieldCount > 17 && !reader.IsDBNull(17) ? reader.GetString(17) : string.Empty;
                var equipListRaw = reader.FieldCount > 18 && !reader.IsDBNull(18) ? reader.GetString(18) : string.Empty;
                var eventLocation = reader.FieldCount > 19 && !reader.IsDBNull(19) ? reader.GetString(19) : null;
                var equipDetailsRaw = reader.FieldCount > 20 && !reader.IsDBNull(20) ? reader.GetString(20) : string.Empty;
                var crewDetailsRaw = reader.FieldCount > 21 && !reader.IsDBNull(21) ? reader.GetString(21) : string.Empty;
                var serviceListRaw = reader.FieldCount > 22 && !reader.IsDBNull(22) ? reader.GetString(22) : string.Empty;
                var linkedEventApptId = reader.FieldCount > 23 && !reader.IsDBNull(23) ? reader.GetString(23) : null;
                var linkedNegId = reader.FieldCount > 24 && !reader.IsDBNull(24) ? reader.GetString(24) : null;

                var endTime = dbEndTime.HasValue && dbEndTime.Value > eventTime
                    ? dbEndTime.Value
                    : (isConsultation ? eventTime.Add(TimeSpan.FromMinutes(30)) : eventTime.Add(TimeSpan.FromMinutes(durationMins)));

                if (endTime > new TimeSpan(23, 59, 0))
                    endTime = new TimeSpan(23, 59, 0);

                var space = _defaultStudioSpaces[0];

                var parsedEquip = equipDetailsRaw
                    .Split(";;", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(seg => seg.Split('|'))
                    .Where(parts => parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[0]))
                    .Select(parts => new EventEquipmentSummary(
                        parts[0].Trim(),
                        parts[1].Trim(),
                        parts.Length > 2 ? parts[2].Trim() : "Equipment",
                        parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]) ? parts[3].Trim() : "Available"))
                    .ToList();

                var parsedCrew = crewDetailsRaw
                    .Split(";;", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(seg => seg.Split('|'))
                    .Where(parts => parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[0]))
                    .Select(parts => new EventCrewSummary(parts[0].Trim(), parts[1].Trim(), "Crew", parts.Length > 2 && parts[2].Trim() == "1"))
                    .ToList();

                var parsedServices = serviceListRaw
                    .Split(";;", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var serviceDisplay = isConsultation
                    ? "Online Consultation"
                    : (parsedServices.Count > 0 ? string.Join(", ", parsedServices) : (string.IsNullOrWhiteSpace(eventName) ? "Studio Photography" : eventName));

                if (!isConsultation && parsedServices.Count == 0 && !string.IsNullOrWhiteSpace(eventName))
                    parsedServices.Add(eventName);

                bookings.Add(new EventBookingModel
                {
                    Id = id,
                    StudioSpaceId = space.Id,
                    StudioSpaceName = space.Name,
                    EventLocation = string.IsNullOrWhiteSpace(eventLocation) ? space.Name : eventLocation,
                    ClientName = clientName,
                    ClientEmail = email,
                    ClientPhone = phone,
                    ServiceType = serviceDisplay,
                    AssignedServices = parsedServices,
                    Date = eventDate,
                    StartTime = eventTime,
                    EndTime = endTime,
                    Status = status,
                    AssignedPhotographer = empName,
                    AssignedCrewIds = crewListRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                    AssignedCrew = parsedCrew,
                    AssignedEquipmentIds = equipListRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                    AssignedEquipmentNames = parsedEquip.Select(e => e.EquipmentName).ToList(),
                    AssignedEquipment = parsedEquip,
                    Notes = desc,
                    EstimatedPrice = isConsultation ? 0.00m : (totalAmount > 0 ? totalAmount : 350.00m),
                    IsConsultation = isConsultation,
                    MeetingLink = meetingLink,
                    LinkedEventAppointmentId = linkedEventApptId,
                    LinkedNegotiationId = linkedNegId
                });
            }
        }
        catch
        {
            // Fail safely
        }

        RecalculateEventConflicts(bookings);

        lock (_sqlCacheLock)
        {
            _sqlCachedEventBookings = (startDate, endDate, DateTime.UtcNow, bookings.Select(CloneBooking).ToList());
        }

        return bookings;
    }

    public void AddEventBooking(EventBookingModel booking)
    {
        InvalidateCache();

        if (string.IsNullOrWhiteSpace(booking.Id))
            booking.Id = "EVT-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();

        if (_useInMemory)
        {
            var space = _inMemorySpaces.FirstOrDefault(s => s.Id == booking.StudioSpaceId);
            if (space != null)
                booking.StudioSpaceName = space.Name;
            _inMemoryBookings.Add(booking);
            RecalculateEventConflicts(_inMemoryBookings);
            return;
        }

        try
        {
            using var conn = CreateConnection();
            conn.Open();

            // Pre-validate assigned employee against Approved Leaves and DaysOff before creating EventAppointment
            if (!string.IsNullOrWhiteSpace(booking.AssignedPhotographer) && !booking.AssignedPhotographer.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
            {
                const string validateSql = @"
                    DECLARE @VId nvarchar(50) = NULL;
                    DECLARE @VDaysOff nvarchar(100) = NULL;
                    SELECT TOP 1 @VId = EmployeeID, @VDaysOff = ISNULL(DaysOff, 'Sunday')
                    FROM dbo.Employees
                    WHERE EmployeeID = @EmpName
                       OR (EmployeeFirstName + ' ' + ISNULL(EmployeeLastName, '')) LIKE '%' + @EmpName + '%';

                    IF @VId IS NOT NULL
                    BEGIN
                        IF EXISTS (
                            SELECT 1
                            FROM dbo.Employees e
                            LEFT JOIN dbo.EmployeeJointSpecialization ejs ON ejs.EmployeeID = e.EmployeeID
                            LEFT JOIN dbo.EmployeeSpecialization es ON es.SpecializationID = ejs.SpecializationID
                            WHERE e.EmployeeID = @VId
                              AND (
                                  e.EmployeeID LIKE 'CLK-%'
                                  OR UPPER(LTRIM(RTRIM(COALESCE(NULLIF(es.SpecializationName, ''), es.Specialization, '')))) = 'OFFICE CLERK'
                                  OR UPPER(ISNULL(e.Specializations, '')) LIKE '%OFFICE CLERK%'
                              )
                        )
                        BEGIN
                            THROW 50005, 'Cannot assign employee: Office Clerks cannot be dispatched as event crew.', 1;
                        END;

                        IF OBJECT_ID(N'dbo.EmployeeLeaves', N'U') IS NOT NULL
                           AND EXISTS (
                               SELECT 1 FROM dbo.EmployeeLeaves el
                               WHERE el.EmployeeStudioId = @VId
                                 AND el.Status = 'Approved'
                                 AND @EvtDate BETWEEN el.StartDate AND el.EndDate
                           )
                        BEGIN
                            THROW 50004, 'Cannot assign employee: Employee is on approved leave during this date.', 1;
                        END;

                        IF CHARINDEX(DATENAME(dw, @EvtDate), ISNULL(@VDaysOff, 'Sunday')) > 0
                        BEGIN
                            THROW 50003, 'Cannot assign employee: Scheduled Day Off on event date.', 1;
                        END;
                    END";

                using var valCmd = new SqlCommand(validateSql, conn);
                valCmd.Parameters.AddWithValue("@EmpName", booking.AssignedPhotographer.Trim());
                valCmd.Parameters.AddWithValue("@EvtDate", booking.Date.Date);
                valCmd.ExecuteNonQuery();
            }

            const string insertSql = @"
                IF NOT EXISTS (SELECT 1 FROM dbo.EventAppointment WHERE EventAppointmentID = @Id)
                BEGIN
                    INSERT INTO dbo.EventAppointment 
                        (EventAppointmentID, EventName, EventDescription, CustomerID, EventDate, EventTime, EventStatus, CreatedAt)
                    VALUES 
                        (@Id, @Name, @Desc, @CustId, @Date, @Time, @Status, SYSUTCDATETIME());
                END";

            using (var cmd = new SqlCommand(insertSql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", booking.Id.Trim());
                cmd.Parameters.AddWithValue("@Name", string.IsNullOrWhiteSpace(booking.ServiceType) ? "Studio Event" : booking.ServiceType.Trim());
                cmd.Parameters.AddWithValue("@Desc", ((object?)booking.Notes?.Trim()) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CustId", "CUST-0001");
                cmd.Parameters.AddWithValue("@Date", booking.Date.Date);
                cmd.Parameters.AddWithValue("@Time", booking.StartTime);
                cmd.Parameters.AddWithValue("@Status", string.IsNullOrWhiteSpace(booking.Status) ? "Confirmed" : booking.Status.Trim());
                cmd.ExecuteNonQuery();
            }

            // Assign employee if specified
            if (!string.IsNullOrWhiteSpace(booking.AssignedPhotographer) && !booking.AssignedPhotographer.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
            {
                const string assignSql = @"
                    DECLARE @EmpId nvarchar(50) = NULL;
                    DECLARE @EmpDaysOff nvarchar(100) = NULL;
                    SELECT TOP 1 @EmpId = EmployeeID, @EmpDaysOff = ISNULL(DaysOff, 'Sunday')
                    FROM dbo.Employees 
                    WHERE EmployeeID = @EmpName 
                       OR (EmployeeFirstName + ' ' + ISNULL(EmployeeLastName, '')) LIKE '%' + @EmpName + '%';

                    IF @EmpId IS NOT NULL
                    BEGIN
                        IF OBJECT_ID(N'dbo.EmployeeLeaves', N'U') IS NOT NULL
                           AND EXISTS (
                               SELECT 1 FROM dbo.EmployeeLeaves el
                               WHERE el.EmployeeStudioId = @EmpId
                                 AND el.Status = 'Approved'
                                 AND @EvtDate BETWEEN el.StartDate AND el.EndDate
                           )
                        BEGIN
                            THROW 50004, 'Cannot assign employee: Employee is on approved leave during this date.', 1;
                        END;

                        IF CHARINDEX(DATENAME(dw, @EvtDate), ISNULL(@EmpDaysOff, 'Sunday')) > 0
                        BEGIN
                            THROW 50003, 'Cannot assign employee: Scheduled Day Off on event date.', 1;
                        END;

                        IF NOT EXISTS (SELECT 1 FROM dbo.EventEmployee WHERE EventAppointmentID = @ApptId AND EmployeeID = @EmpId)
                        BEGIN
                            IF COL_LENGTH(N'dbo.EventEmployee', N'IsTeamLeader') IS NOT NULL
                                INSERT INTO dbo.EventEmployee (EventAppointmentID, EmployeeID, IsTeamLeader)
                                VALUES (@ApptId, @EmpId, 1);
                            ELSE
                                INSERT INTO dbo.EventEmployee (EventAppointmentID, EmployeeID)
                                VALUES (@ApptId, @EmpId);
                        END
                        UPDATE dbo.EventAppointment
                        SET EmployeeID = COALESCE(NULLIF(LTRIM(RTRIM(EmployeeID)), ''), @EmpId)
                        WHERE EventAppointmentID = @ApptId;
                    END";

                using var assignCmd = new SqlCommand(assignSql, conn);
                assignCmd.Parameters.AddWithValue("@ApptId", booking.Id.Trim());
                assignCmd.Parameters.AddWithValue("@EmpName", booking.AssignedPhotographer.Trim());
                assignCmd.Parameters.AddWithValue("@EvtDate", booking.Date.Date);
                assignCmd.ExecuteNonQuery();
            }

            // Log activity
            const string logSql = @"
                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedByType, ActionDetails, LoggedAt)
                    VALUES ('CREATE', 'EventAppointment', @Id, 'System', @Details, SYSUTCDATETIME());
                END";

            using var logCmd = new SqlCommand(logSql, conn);
            logCmd.Parameters.AddWithValue("@Id", booking.Id.Trim());
            logCmd.Parameters.AddWithValue("@Details", $"Scheduled event {booking.Id}: {booking.ClientName} ({booking.ServiceType}) on {booking.Date:yyyy-MM-dd} at {booking.StartTime}");
            logCmd.ExecuteNonQuery();
        }
        catch (SqlException ex) when (ex.Number == 50003 || ex.Number == 50004 || ex.Number == 50005)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
        catch
        {
        }
    }

    public void UpdateEventBooking(EventBookingModel booking)
    {
        InvalidateCache();

        if (string.IsNullOrWhiteSpace(booking.Id)) return;

        if (_useInMemory)
        {
            var idx = _inMemoryBookings.FindIndex(b => b.Id == booking.Id);
            if (idx >= 0)
            {
                _inMemoryBookings[idx] = booking;
                RecalculateEventConflicts(_inMemoryBookings);
            }
            return;
        }

        try
        {
            using var conn = CreateConnection();
            conn.Open();

            // Pre-validate assigned employee against Approved Leaves and DaysOff before updating EventAppointment
            if (!string.IsNullOrWhiteSpace(booking.AssignedPhotographer) && !booking.AssignedPhotographer.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
            {
                const string validateSql = @"
                    DECLARE @VId nvarchar(50) = NULL;
                    DECLARE @VDaysOff nvarchar(100) = NULL;
                    SELECT TOP 1 @VId = EmployeeID, @VDaysOff = ISNULL(DaysOff, 'Sunday')
                    FROM dbo.Employees
                    WHERE EmployeeID = @EmpName
                       OR (EmployeeFirstName + ' ' + ISNULL(EmployeeLastName, '')) LIKE '%' + @EmpName + '%';

                    IF @VId IS NOT NULL
                    BEGIN
                        IF EXISTS (
                            SELECT 1
                            FROM dbo.Employees e
                            LEFT JOIN dbo.EmployeeJointSpecialization ejs ON ejs.EmployeeID = e.EmployeeID
                            LEFT JOIN dbo.EmployeeSpecialization es ON es.SpecializationID = ejs.SpecializationID
                            WHERE e.EmployeeID = @VId
                              AND (
                                  e.EmployeeID LIKE 'CLK-%'
                                  OR UPPER(LTRIM(RTRIM(COALESCE(NULLIF(es.SpecializationName, ''), es.Specialization, '')))) = 'OFFICE CLERK'
                                  OR UPPER(ISNULL(e.Specializations, '')) LIKE '%OFFICE CLERK%'
                              )
                        )
                        BEGIN
                            THROW 50005, 'Cannot assign employee: Office Clerks cannot be dispatched as event crew.', 1;
                        END;

                        IF OBJECT_ID(N'dbo.EmployeeLeaves', N'U') IS NOT NULL
                           AND EXISTS (
                               SELECT 1 FROM dbo.EmployeeLeaves el
                               WHERE el.EmployeeStudioId = @VId
                                 AND el.Status = 'Approved'
                                 AND @EvtDate BETWEEN el.StartDate AND el.EndDate
                           )
                        BEGIN
                            THROW 50004, 'Cannot assign employee: Employee is on approved leave during this date.', 1;
                        END;

                        IF CHARINDEX(DATENAME(dw, @EvtDate), ISNULL(@VDaysOff, 'Sunday')) > 0
                        BEGIN
                            THROW 50003, 'Cannot assign employee: Scheduled Day Off on event date.', 1;
                        END;
                    END";

                using var valCmd = new SqlCommand(validateSql, conn);
                valCmd.Parameters.AddWithValue("@EmpName", booking.AssignedPhotographer.Trim());
                valCmd.Parameters.AddWithValue("@EvtDate", booking.Date.Date);
                valCmd.ExecuteNonQuery();
            }

            const string updateSql = @"
                UPDATE dbo.EventAppointment
                SET EventName = @Name,
                    EventDescription = @Desc,
                    EventDate = @Date,
                    EventTime = @Time,
                    EventStatus = @Status
                WHERE EventAppointmentID = @Id;";

            using (var cmd = new SqlCommand(updateSql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", booking.Id.Trim());
                cmd.Parameters.AddWithValue("@Name", string.IsNullOrWhiteSpace(booking.ServiceType) ? "Studio Event" : booking.ServiceType.Trim());
                cmd.Parameters.AddWithValue("@Desc", ((object?)booking.Notes?.Trim()) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Date", booking.Date.Date);
                cmd.Parameters.AddWithValue("@Time", booking.StartTime);
                cmd.Parameters.AddWithValue("@Status", string.IsNullOrWhiteSpace(booking.Status) ? "Confirmed" : booking.Status.Trim());
                cmd.ExecuteNonQuery();
            }

            // Update employee assignment
            if (!string.IsNullOrWhiteSpace(booking.AssignedPhotographer) && !booking.AssignedPhotographer.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
            {
                const string reassignSql = @"
                    DECLARE @EmpId nvarchar(50) = NULL;
                    DECLARE @EmpDaysOff nvarchar(100) = NULL;
                    SELECT TOP 1 @EmpId = EmployeeID, @EmpDaysOff = ISNULL(DaysOff, 'Sunday')
                    FROM dbo.Employees 
                    WHERE EmployeeID = @EmpName 
                       OR (EmployeeFirstName + ' ' + ISNULL(EmployeeLastName, '')) LIKE '%' + @EmpName + '%';

                    IF @EmpId IS NOT NULL
                    BEGIN
                        IF OBJECT_ID(N'dbo.EmployeeLeaves', N'U') IS NOT NULL
                           AND EXISTS (
                               SELECT 1 FROM dbo.EmployeeLeaves el
                               WHERE el.EmployeeStudioId = @EmpId
                                 AND el.Status = 'Approved'
                                 AND @EvtDate BETWEEN el.StartDate AND el.EndDate
                           )
                        BEGIN
                            THROW 50004, 'Cannot assign employee: Employee is on approved leave during this date.', 1;
                        END;

                        IF CHARINDEX(DATENAME(dw, @EvtDate), ISNULL(@EmpDaysOff, 'Sunday')) > 0
                        BEGIN
                            THROW 50003, 'Cannot assign employee: Scheduled Day Off on event date.', 1;
                        END;

                        DELETE FROM dbo.EventEmployee WHERE EventAppointmentID = @ApptId;
                        IF COL_LENGTH(N'dbo.EventEmployee', N'IsTeamLeader') IS NOT NULL
                            INSERT INTO dbo.EventEmployee (EventAppointmentID, EmployeeID, IsTeamLeader)
                            VALUES (@ApptId, @EmpId, 1);
                        ELSE
                            INSERT INTO dbo.EventEmployee (EventAppointmentID, EmployeeID)
                            VALUES (@ApptId, @EmpId);
                        UPDATE dbo.EventAppointment
                        SET EmployeeID = @EmpId
                        WHERE EventAppointmentID = @ApptId;
                    END";

                using var reassignCmd = new SqlCommand(reassignSql, conn);
                reassignCmd.Parameters.AddWithValue("@ApptId", booking.Id.Trim());
                reassignCmd.Parameters.AddWithValue("@EmpName", booking.AssignedPhotographer.Trim());
                reassignCmd.Parameters.AddWithValue("@EvtDate", booking.Date.Date);
                reassignCmd.ExecuteNonQuery();
            }

            // Log activity
            const string logSql = @"
                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedByType, ActionDetails, LoggedAt)
                    VALUES ('UPDATE', 'EventAppointment', @Id, 'System', @Details, SYSUTCDATETIME());
                END";

            using var logCmd = new SqlCommand(logSql, conn);
            logCmd.Parameters.AddWithValue("@Id", booking.Id.Trim());
            logCmd.Parameters.AddWithValue("@Details", $"Updated event {booking.Id}: Status={booking.Status}, Date={booking.Date:yyyy-MM-dd}");
            logCmd.ExecuteNonQuery();
        }
        catch (SqlException ex) when (ex.Number == 50003 || ex.Number == 50004 || ex.Number == 50005)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
        catch
        {
        }
    }

    public void DeleteEventBooking(string id)
    {
        InvalidateCache();

        if (string.IsNullOrWhiteSpace(id)) return;

        if (_useInMemory)
        {
            _inMemoryBookings.RemoveAll(b => b.Id == id);
            RecalculateEventConflicts(_inMemoryBookings);
            return;
        }

        try
        {
            using var conn = CreateConnection();
            conn.Open();

            const string deleteSql = @"
                DELETE FROM dbo.EventEquipment WHERE EventAppointmentID = @Id;
                DELETE FROM dbo.EventEmployee WHERE EventAppointmentID = @Id;
                DELETE FROM dbo.EventAppointment WHERE EventAppointmentID = @Id;";

            using (var cmd = new SqlCommand(deleteSql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", id.Trim());
                cmd.ExecuteNonQuery();
            }

            const string logSql = @"
                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedByType, ActionDetails, LoggedAt)
                    VALUES ('DELETE', 'EventAppointment', @Id, 'System', @Details, SYSUTCDATETIME());
                END";

            using var logCmd = new SqlCommand(logSql, conn);
            logCmd.Parameters.AddWithValue("@Id", id.Trim());
            logCmd.Parameters.AddWithValue("@Details", $"Deleted event appointment {id}");
            logCmd.ExecuteNonQuery();
        }
        catch
        {
        }
    }

    public int GetEventConflictCount(DateTime? startDate = null, DateTime? endDate = null)
    {
        var bookings = GetEventBookings(startDate, endDate);
        return bookings.Count(b => b.HasConflict);
    }

    private static void RecalculateEventConflicts(List<EventBookingModel> bookings)
    {
        foreach (var b in bookings)
        {
            b.HasConflict = false;
            b.ConflictMessage = null;
        }

        // Partition by Date only: overlapping studio times are permitted when different crews & equipment are assigned.
        // Conflicts occur only when a crew member OR equipment item overlaps within combined Prep (2h) + Event + Post (1h).
        var dateGroups = bookings
            .Where(b => !b.IsConsultation &&
                        !b.IsCompleted &&
                        !string.Equals(b.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(b.Status, "Expired", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(b.Status, "Deal_Lost", StringComparison.OrdinalIgnoreCase))
            .GroupBy(b => b.Date.Date);

        foreach (var group in dateGroups)
        {
            var list = group.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    var b1 = list[i];
                    var b2 = list[j];

                    if (b1.OverlapsWith(b2))
                    {
                        var reason = b1.SharesEquipmentWith(b2)
                            ? "Same-day equipment double-booking"
                            : "Crew buffer overlap (2h prep + event + 1h post)";

                        b1.HasConflict = true;
                        b1.ConflictMessage = $"Double-booked ({reason}) with '{b2.ClientName}' ({b2.FormattedTimeRange})";

                        b2.HasConflict = true;
                        b2.ConflictMessage = $"Double-booked ({reason}) with '{b1.ClientName}' ({b1.FormattedTimeRange})";
                    }
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. STAFF & SHIFTS
    // ─────────────────────────────────────────────────────────────────────────

    public List<StaffMemberResource> GetStaffMembers()
    {
        if (_useInMemory)
        {
            return _inMemoryStaff;
        }

        lock (_sqlCacheLock)
        {
            if (_sqlCachedStaffMembers.HasValue &&
                (DateTime.UtcNow - _sqlCachedStaffMembers.Value.fetchedAt) < _cacheDuration)
            {
                return _sqlCachedStaffMembers.Value.staff.Select(CloneStaffMember).ToList();
            }
        }

        var staff = new List<StaffMemberResource>();

        try
        {
            using var conn = CreateConnection();
            conn.Open();

            const string sql = @"
                SELECT emp.EmployeeID,
                       COALESCE(NULLIF(LTRIM(RTRIM(emp.EmployeeFirstName + ' ' + ISNULL(emp.EmployeeLastName, ''))), ''), emp.EmployeeID) AS FullName,
                       emp.EmployeeEmail,
                       emp.EmployeeStatus,
                       STRING_AGG(es.SpecializationName, ', ') AS Specializations
                FROM dbo.Employees emp
                LEFT JOIN dbo.EmployeeJointSpecialization ejs ON ejs.EmployeeID = emp.EmployeeID
                LEFT JOIN dbo.EmployeeSpecialization es ON es.SpecializationID = ejs.SpecializationID
                WHERE ISNULL(emp.EmployeeStatus, 'Active') NOT IN ('Archive', 'Inactive')
                GROUP BY emp.EmployeeID, emp.EmployeeFirstName, emp.EmployeeLastName, emp.EmployeeEmail, emp.EmployeeStatus
                ORDER BY emp.EmployeeID;";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var id = reader.GetString(0);
                var name = reader.GetString(1);
                var email = reader.IsDBNull(2) ? $"{id.ToLowerInvariant()}@photoaevum.studio" : reader.GetString(2);
                var status = reader.IsDBNull(3) ? "Active" : reader.GetString(3);
                var role = reader.IsDBNull(4) ? "Studio Staff" : reader.GetString(4);

                var initials = "ST";
                var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                    initials = $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
                else if (parts.Length == 1 && parts[0].Length > 0)
                    initials = parts[0][..1].ToUpperInvariant();

                staff.Add(new StaffMemberResource
                {
                    Id = id,
                    StaffCode = id,
                    FullName = name,
                    Role = string.IsNullOrWhiteSpace(role) ? "Staff Member" : role,
                    Email = email,
                    Status = status,
                    AvatarInitials = initials,
                    AvatarColor = "bg-indigo-100 text-indigo-700",
                    TargetHoursPerWeek = 40
                });
            }

            lock (_sqlCacheLock)
            {
                _sqlCachedStaffMembers = (DateTime.UtcNow, staff.Select(CloneStaffMember).ToList());
            }
        }
        catch
        {
        }

        return staff;
    }

    public List<StaffShiftModel> GetStaffShifts()
    {
        if (_useInMemory)
        {
            RecalculateShiftConflicts(_inMemoryShifts);
            return _inMemoryShifts;
        }

        lock (_sqlCacheLock)
        {
            if (_sqlCachedStaffShifts.HasValue &&
                (DateTime.UtcNow - _sqlCachedStaffShifts.Value.fetchedAt) < _cacheDuration)
            {
                return _sqlCachedStaffShifts.Value.shifts.Select(CloneShift).ToList();
            }
        }

        var shifts = new List<StaffShiftModel>();

        try
        {
            using var conn = CreateConnection();
            conn.Open();

            bool hasGranularCols = false;
            bool hasLocationCol = false;
            try
            {
                using var chkCmd = new SqlCommand(@"
                    SELECT
                        CASE WHEN COL_LENGTH(N'dbo.EventAppointment', N'EventEndTime') IS NOT NULL AND COL_LENGTH(N'dbo.EventAppointment', N'DurationMinutes') IS NOT NULL THEN 1 ELSE 0 END,
                        CASE WHEN COL_LENGTH(N'dbo.EventAppointment', N'EventLocation') IS NOT NULL THEN 1 ELSE 0 END;", conn);
                using var chkReader = chkCmd.ExecuteReader();
                if (chkReader.Read())
                {
                    hasGranularCols = chkReader.GetInt32(0) == 1;
                    hasLocationCol = chkReader.GetInt32(1) == 1;
                }
            }
            catch { }

            var endTimeSelect = hasGranularCols
                ? "ea.EventEndTime, ISNULL(ea.DurationMinutes, 120) AS DurationMinutes,"
                : "CAST(NULL AS time) AS EventEndTime, CAST(120 AS int) AS DurationMinutes,";

            var locationSelect = hasLocationCol
                ? "COALESCE(NULLIF(ea.EventLocation, ''), N'Studio A (Main Hall)') AS EventLocation,"
                : "N'Studio A (Main Hall)' AS EventLocation,";

            string sql = $@"
                SELECT ee.EventAppointmentID,
                       emp.EmployeeID,
                       COALESCE(NULLIF(LTRIM(RTRIM(emp.EmployeeFirstName + ' ' + ISNULL(emp.EmployeeLastName, ''))), ''), emp.EmployeeID) AS FullName,
                       ea.EventDate,
                       ea.EventTime,
                       ea.EventName,
                       COALESCE(spec.SpecializationName, 'Photographer') AS Role,
                       {endTimeSelect}
                       {locationSelect}
                       ISNULL(ea.EventStatus, 'Confirmed') AS EventStatus,
                       ISNULL(ee.IsTeamLeader, 0) AS IsTeamLeader
                FROM dbo.EventEmployee ee
                INNER JOIN dbo.EventAppointment ea ON ea.EventAppointmentID = ee.EventAppointmentID
                INNER JOIN dbo.Employees emp ON emp.EmployeeID = ee.EmployeeID
                OUTER APPLY (
                    SELECT TOP 1 es.SpecializationName
                    FROM dbo.EmployeeJointSpecialization ejs
                    INNER JOIN dbo.EmployeeSpecialization es ON es.SpecializationID = ejs.SpecializationID
                    WHERE ejs.EmployeeID = emp.EmployeeID
                    ORDER BY es.SpecializationID
                ) spec
                WHERE ISNULL(ea.EventStatus, '') NOT IN ('Cancelled', 'Expired')
                  AND (ISNULL(ea.EventStatus, '') NOT IN ('Pending Payment', 'Awaiting_Downpayment')
                       OR ISNULL(ea.DownpaymentStatus, 'Pending') = 'Paid')
                  AND (
                      ISNULL(ea.DownpaymentStatus, 'Pending') = 'Paid'
                      OR ea.EventStatus IN ('Confirmed', 'Ready', 'In Execution', 'Preparation', 'Preparing', 'Dispatch', 'Ongoing', 'On Going', 'Event Done', 'Post Event', 'Payment Verification (Cash)', 'Pending Verification', 'Paid Cash Pending Verification', 'Complete', 'Completed', 'Shot', 'Delivered')
                      OR ea.EventAppointmentID NOT IN (
                          SELECT EventAppointmentID
                          FROM dbo.BookingNegotiation
                          WHERE EventAppointmentID IS NOT NULL
                            AND NegotiationStatus IN ('Pending_Call', 'Rescheduled', 'Deal_Lost', 'Cancelled')
                      )
                  )
                ORDER BY ea.EventDate DESC, ea.EventTime DESC;";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var apptId = reader.GetString(0);
                var empId = reader.GetString(1);
                var empName = reader.GetString(2);
                var eventDate = reader.GetDateTime(3);
                var startTime = reader.GetFieldValue<TimeSpan>(4);
                var eventName = reader.GetString(5);
                var role = reader.IsDBNull(6) ? "Photographer" : reader.GetString(6);
                var dbEndTime = reader.FieldCount > 7 && !reader.IsDBNull(7) ? reader.GetFieldValue<TimeSpan>(7) : (TimeSpan?)null;
                var durationMins = reader.FieldCount > 8 && !reader.IsDBNull(8) ? Math.Max(15, reader.GetInt32(8)) : 120;
                var location = reader.FieldCount > 9 && !reader.IsDBNull(9) ? reader.GetString(9) : "Studio A";
                var evtStatus = reader.FieldCount > 10 && !reader.IsDBNull(10) ? reader.GetString(10) : "Confirmed";
                var isTeamLeader = reader.FieldCount > 11 && !reader.IsDBNull(11) && reader.GetBoolean(11);

                var endTime = dbEndTime.HasValue && dbEndTime.Value > startTime
                    ? dbEndTime.Value
                    : startTime.Add(TimeSpan.FromMinutes(durationMins));
                if (endTime > new TimeSpan(23, 59, 0))
                    endTime = new TimeSpan(23, 59, 0);

                shifts.Add(new StaffShiftModel
                {
                    Id = $"{apptId}-{empId}",
                    StaffId = empId,
                    StaffName = empName,
                    Role = role,
                    Date = eventDate,
                    StartTime = startTime,
                    EndTime = endTime,
                    Location = location,
                    ShiftType = isTeamLeader ? "Team Leader" : "Production Shoot",
                    Notes = $"{(isTeamLeader ? "[Team Lead] " : "")}{evtStatus} • {eventName}"
                });
            }
        }
        catch
        {
        }

        shifts.AddRange(_inMemoryShifts);
        RecalculateShiftConflicts(shifts);

        lock (_sqlCacheLock)
        {
            _sqlCachedStaffShifts = (DateTime.UtcNow, shifts.Select(CloneShift).ToList());
        }

        return shifts;
    }

    public void AddStaffShift(StaffShiftModel shift)
    {
        InvalidateCache();
        if (string.IsNullOrWhiteSpace(shift.Id))
            shift.Id = Guid.NewGuid().ToString("N")[..8];

        if (!string.IsNullOrEmpty(shift.StaffId))
        {
            var staff = _inMemoryStaff.FirstOrDefault(s => s.Id == shift.StaffId);
            if (staff != null)
            {
                shift.StaffName = staff.FullName;
                shift.Role = staff.Role;
            }
        }
        else if (string.IsNullOrEmpty(shift.StaffName))
        {
            shift.StaffName = "Open Shift";
        }

        _inMemoryShifts.Add(shift);
        RecalculateShiftConflicts(_inMemoryShifts);
    }

    public void UpdateStaffShift(StaffShiftModel shift)
    {
        var idx = _inMemoryShifts.FindIndex(s => s.Id == shift.Id);
        if (idx >= 0)
        {
            _inMemoryShifts[idx] = shift;
            RecalculateShiftConflicts(_inMemoryShifts);
        }
    }

    public void DeleteStaffShift(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        InvalidateCache();

        if (_inMemoryShifts.RemoveAll(s => s.Id == id) > 0)
        {
            RecalculateShiftConflicts(_inMemoryShifts);
            if (_useInMemory) return;
        }

        var parts = id.Split('-');
        if (parts.Length >= 2)
        {
            var apptId = parts[0];
            var empId = parts[1];

            try
            {
                using var conn = CreateConnection();
                conn.Open();
                using var cmd = new SqlCommand("DELETE FROM dbo.EventEmployee WHERE EventAppointmentID = @ApptId AND EmployeeID = @EmpId;", conn);
                cmd.Parameters.AddWithValue("@ApptId", apptId);
                cmd.Parameters.AddWithValue("@EmpId", empId);
                cmd.ExecuteNonQuery();
            }
            catch
            {
            }
        }
    }

    public int GetShiftConflictCount()
    {
        var shifts = GetStaffShifts();
        return shifts.Count(s => s.HasConflict);
    }

    private static void RecalculateShiftConflicts(List<StaffShiftModel> shifts)
    {
        foreach (var s in shifts)
        {
            s.HasConflict = false;
            s.ConflictMessage = null;
        }

        for (int i = 0; i < shifts.Count; i++)
        {
            for (int j = i + 1; j < shifts.Count; j++)
            {
                var s1 = shifts[i];
                var s2 = shifts[j];

                if (s1.OverlapsWith(s2))
                {
                    s1.HasConflict = true;
                    s1.ConflictMessage = $"Shift overlaps with '{s2.Notes}' ({s2.FormattedTimeRange})";

                    s2.HasConflict = true;
                    s2.ConflictMessage = $"Shift overlaps with '{s1.Notes}' ({s1.FormattedTimeRange})";
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. EQUIPMENT RESOURCES & RESERVATIONS
    // ─────────────────────────────────────────────────────────────────────────

    public List<EquipmentItemResource> GetEquipmentResources()
    {
        if (_useInMemory)
        {
            return _inMemoryEquipment;
        }

        lock (_sqlCacheLock)
        {
            if (_sqlCachedEquipmentResources.HasValue &&
                (DateTime.UtcNow - _sqlCachedEquipmentResources.Value.fetchedAt) < _cacheDuration)
            {
                return _sqlCachedEquipmentResources.Value.equipment.Select(CloneEquipment).ToList();
            }
        }

        var list = new List<EquipmentItemResource>();

        try
        {
            using var conn = CreateConnection();
            conn.Open();

            bool hasIsTakenCol = false;
            bool hasFinalPaymentCol = false;
            try
            {
                using var chkCmd = new SqlCommand(@"
                    SELECT
                        CASE WHEN COL_LENGTH(N'dbo.EventEquipment', N'IsTaken') IS NOT NULL THEN 1 ELSE 0 END,
                        CASE WHEN COL_LENGTH(N'dbo.EventAppointment', N'FinalPaymentStatus') IS NOT NULL THEN 1 ELSE 0 END;", conn);
                using var chkReader = chkCmd.ExecuteReader();
                if (chkReader.Read())
                {
                    hasIsTakenCol = chkReader.GetInt32(0) == 1;
                    hasFinalPaymentCol = chkReader.GetInt32(1) == 1;
                }
            }
            catch { }

            var isTakenCond = hasIsTakenCol ? "ISNULL(ee.IsTaken, 0) = 1 OR " : string.Empty;
            var finalPayFilter = hasFinalPaymentCol
                ? "AND ISNULL(ea.FinalPaymentStatus, '') NOT IN ('Paid', 'Payment Verification (QR:Paid)')"
                : string.Empty;

            var sql = $@"
                SELECT eq.EquipmentID,
                       eq.EquipmentName,
                       COALESCE(eq.SerialNo, eq.EquipmentID) AS SerialNumber,
                       COALESCE(et.EquipmentTypeName, eq.EquipmentTypeID, 'Gear') AS Category,
                       COALESCE(eq.EquipmentStatus, 'Available') AS Status,
                       CASE WHEN EXISTS (
                           SELECT 1
                           FROM dbo.EventEquipment ee
                           INNER JOIN dbo.EventAppointment ea ON ea.EventAppointmentID = ee.EventAppointmentID
                           WHERE ee.EquipmentID = eq.EquipmentID
                             AND ISNULL(ea.EventStatus, '') NOT IN ('Cancelled', 'Delivered', 'Completed', 'Complete', 'Expired', 'Deal_Lost', 'Shot')
                             {finalPayFilter}
                             AND ({isTakenCond}ea.EventStatus IN ('Dispatch', 'Dispatched', 'Ongoing', 'On Going', 'In Execution', 'Event Done', 'Post Event', 'Pending Verification', 'Paid Cash Pending Verification', 'Payment Verification (Cash)', 'Payment Verification (QR:Pending Payment)'))
                       ) THEN 1 ELSE 0 END AS IsInUse
                FROM dbo.Equipment eq
                LEFT JOIN dbo.EquipmentType et ON et.EquipmentTypeID = eq.EquipmentTypeID
                ORDER BY eq.EquipmentID;";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var rawStatus = reader.GetString(4);
                var isInUse = !reader.IsDBNull(5) && reader.GetInt32(5) == 1;
                var displayStatus = rawStatus.Equals("Maintenance", StringComparison.OrdinalIgnoreCase)
                    ? "In-Maintenance"
                    : (isInUse && rawStatus.Equals("Available", StringComparison.OrdinalIgnoreCase))
                        ? "In-Use"
                        : (rawStatus.Equals("Available", StringComparison.OrdinalIgnoreCase) ? "Ready" : rawStatus);

                var badgeColor = displayStatus switch
                {
                    "In-Maintenance" => "bg-amber-50 text-amber-700 border-amber-200",
                    "Needs Replacement" => "bg-rose-50 text-rose-700 border-rose-200",
                    "In-Use" => "bg-blue-50 text-blue-700 border-blue-200",
                    "Ready" => "bg-emerald-50 text-emerald-700 border-emerald-200",
                    _ => "bg-slate-100 text-slate-700 border-slate-200"
                };

                list.Add(new EquipmentItemResource
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    SerialNumber = reader.GetString(2),
                    Category = reader.GetString(3),
                    Status = displayStatus,
                    StorageBay = "Studio Inventory Rack",
                    BadgeColor = badgeColor
                });
            }

            lock (_sqlCacheLock)
            {
                _sqlCachedEquipmentResources = (DateTime.UtcNow, list.Select(CloneEquipment).ToList());
            }
        }
        catch
        {
        }

        return list;
    }

    public List<EquipmentReservationModel> GetEquipmentReservations()
    {
        if (_useInMemory)
        {
            var eqMap = _inMemoryEquipment.ToDictionary(e => e.Id, e => e.Status, StringComparer.OrdinalIgnoreCase);
            foreach (var r in _inMemoryReservations)
            {
                if (eqMap.TryGetValue(r.EquipmentId, out var st))
                {
                    r.MaintenanceStatus = st.Equals("Available", StringComparison.OrdinalIgnoreCase) ? "Ready" : st;
                }
            }
            RecalculateEquipmentConflicts(_inMemoryReservations);
            return _inMemoryReservations;
        }

        lock (_sqlCacheLock)
        {
            if (_sqlCachedEquipmentReservations.HasValue &&
                (DateTime.UtcNow - _sqlCachedEquipmentReservations.Value.fetchedAt) < _cacheDuration)
            {
                return _sqlCachedEquipmentReservations.Value.reservations.Select(CloneReservation).ToList();
            }
        }

        var reservations = new List<EquipmentReservationModel>();

        try
        {
            using var conn = CreateConnection();
            conn.Open();

            bool hasGranularCols = false;
            bool hasLocationCol = false;
            bool hasMaintScheduleTable = false;
            try
            {
                using var chkCmd = new SqlCommand(@"
                    SELECT
                        CASE WHEN COL_LENGTH(N'dbo.EventAppointment', N'EventEndTime') IS NOT NULL AND COL_LENGTH(N'dbo.EventAppointment', N'DurationMinutes') IS NOT NULL THEN 1 ELSE 0 END,
                        CASE WHEN COL_LENGTH(N'dbo.EventAppointment', N'EventLocation') IS NOT NULL THEN 1 ELSE 0 END,
                        CASE WHEN OBJECT_ID(N'dbo.MaintenanceSchedule', N'U') IS NOT NULL THEN 1 ELSE 0 END;", conn);
                using var chkReader = chkCmd.ExecuteReader();
                if (chkReader.Read())
                {
                    hasGranularCols = chkReader.GetInt32(0) == 1;
                    hasLocationCol = chkReader.GetInt32(1) == 1;
                    hasMaintScheduleTable = chkReader.GetInt32(2) == 1;
                }
            }
            catch { }

            var endTimeSelect = hasGranularCols
                ? "ea.EventEndTime, ISNULL(ea.DurationMinutes, 120) AS DurationMinutes,"
                : "CAST(NULL AS time) AS EventEndTime, CAST(120 AS int) AS DurationMinutes,";

            var locationSelect = hasLocationCol
                ? "COALESCE(NULLIF(ea.EventLocation, ''), N'Studio A (Main Hall)') AS EventLocation,"
                : "N'Studio A (Main Hall)' AS EventLocation,";

            var maintSelect = hasMaintScheduleTable
                ? "maint.SubStatus AS MaintSubStatus, maint.IssueDetails AS MaintIssueDetails"
                : "CAST(NULL AS nvarchar(50)) AS MaintSubStatus, CAST(NULL AS nvarchar(1000)) AS MaintIssueDetails";

            var maintApply = hasMaintScheduleTable
                ? @"OUTER APPLY (
                    SELECT TOP 1 ms.SubStatus, ms.IssueDetails
                    FROM dbo.MaintenanceSchedule ms
                    WHERE ms.EquipmentID = eq.EquipmentID
                      AND ms.Status IN ('In Maintenance', 'Needs Replacement')
                    ORDER BY ms.UpdatedAt DESC, ms.MaintenanceScheduleID DESC
                ) maint"
                : string.Empty;

            string sql = $@"
                SELECT eeq.EventAppointmentID,
                       eq.EquipmentID,
                       eq.EquipmentName,
                       COALESCE(et.EquipmentTypeName, eq.EquipmentTypeID, 'Gear') AS Category,
                       ea.EventDate,
                       ea.EventTime,
                       ea.EventName,
                       COALESCE(leadEmp.EmployeeName, 'Lead Technician') AS Technician,
                       {endTimeSelect}
                       ISNULL(ea.EventStatus, 'Confirmed') AS EventStatus,
                       {locationSelect}
                       COALESCE(NULLIF(LTRIM(RTRIM(c.CustomerFirstName + ' ' + ISNULL(c.CustomerLastName, ''))), ''), ea.EventName) AS CustomerName,
                       ISNULL(eq.EquipmentStatus, 'Ready') AS EquipmentStatus,
                       {maintSelect}
                FROM dbo.EventEquipment eeq
                INNER JOIN dbo.EventAppointment ea ON ea.EventAppointmentID = eeq.EventAppointmentID
                INNER JOIN dbo.Equipment eq ON eq.EquipmentID = eeq.EquipmentID
                LEFT JOIN dbo.EquipmentType et ON et.EquipmentTypeID = eq.EquipmentTypeID
                LEFT JOIN dbo.Customer c ON c.CustomerID = ea.CustomerID
                OUTER APPLY (
                    SELECT TOP 1 COALESCE(NULLIF(LTRIM(RTRIM(emp.EmployeeFirstName + ' ' + ISNULL(emp.EmployeeLastName, ''))), ''), emp.EmployeeID) AS EmployeeName
                    FROM dbo.EventEmployee ee
                    INNER JOIN dbo.Employees emp ON emp.EmployeeID = ee.EmployeeID
                    WHERE ee.EventAppointmentID = ea.EventAppointmentID
                    ORDER BY ISNULL(ee.IsTeamLeader, 0) DESC, ee.EmployeeID
                ) leadEmp
                {maintApply}
                WHERE ISNULL(ea.EventStatus, '') NOT IN ('Cancelled', 'Expired')
                  AND (
                      ISNULL(ea.DownpaymentStatus, 'Pending') = 'Paid'
                      OR ea.EventStatus IN ('Confirmed', 'Ready', 'In Execution', 'Preparation', 'Preparing', 'Dispatch', 'Ongoing', 'On Going', 'Event Done', 'Post Event', 'Payment Verification (Cash)', 'Pending Verification', 'Paid Cash Pending Verification', 'Complete', 'Completed', 'Shot', 'Delivered')
                      OR ea.EventAppointmentID NOT IN (
                          SELECT EventAppointmentID
                          FROM dbo.BookingNegotiation
                          WHERE EventAppointmentID IS NOT NULL
                            AND NegotiationStatus IN ('Pending_Call', 'Rescheduled', 'Deal_Lost', 'Cancelled')
                      )
                  )
                ORDER BY ea.EventDate DESC, ea.EventTime DESC;";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var apptId = reader.GetString(0);
                var eqId = reader.GetString(1);
                var eqName = reader.GetString(2);
                var cat = reader.GetString(3);
                var eventDate = reader.GetDateTime(4);
                var startTime = reader.GetFieldValue<TimeSpan>(5);
                var eventName = reader.GetString(6);
                var technician = reader.IsDBNull(7) ? "Studio Staff" : reader.GetString(7);
                var dbEndTime = reader.FieldCount > 8 && !reader.IsDBNull(8) ? reader.GetFieldValue<TimeSpan>(8) : (TimeSpan?)null;
                var durationMins = reader.FieldCount > 9 && !reader.IsDBNull(9) ? Math.Max(15, reader.GetInt32(9)) : 120;
                var evtStatus = reader.FieldCount > 10 && !reader.IsDBNull(10) ? reader.GetString(10) : "Confirmed";
                var evtLocation = reader.FieldCount > 11 && !reader.IsDBNull(11) ? reader.GetString(11) : "Studio A (Main Hall)";
                var custName = reader.FieldCount > 12 && !reader.IsDBNull(12) ? reader.GetString(12) : eventName;
                var eqStatus = reader.FieldCount > 13 && !reader.IsDBNull(13) ? reader.GetString(13) : "Ready";
                var maintSubStatus = reader.FieldCount > 14 && !reader.IsDBNull(14) ? reader.GetString(14) : null;
                var maintNotes = reader.FieldCount > 15 && !reader.IsDBNull(15) ? reader.GetString(15) : null;

                var endTime = dbEndTime.HasValue && dbEndTime.Value > startTime
                    ? dbEndTime.Value
                    : startTime.Add(TimeSpan.FromMinutes(durationMins));
                if (endTime > new TimeSpan(23, 59, 0))
                    endTime = new TimeSpan(23, 59, 0);

                var normalizedMaintStatus = eqStatus.Equals("Available", StringComparison.OrdinalIgnoreCase)
                    ? "Ready"
                    : (eqStatus.Equals("Maintenance", StringComparison.OrdinalIgnoreCase) ? "In-Maintenance" : eqStatus);

                reservations.Add(new EquipmentReservationModel
                {
                    Id = $"{apptId}-{eqId}",
                    EventAppointmentId = apptId,
                    EventStatus = evtStatus,
                    EquipmentId = eqId,
                    EquipmentName = eqName,
                    Category = cat,
                    Date = eventDate,
                    StartTime = startTime,
                    EndTime = endTime,
                    ProjectOrClient = string.Equals(custName, eventName, StringComparison.OrdinalIgnoreCase) ? eventName : $"{custName} — {eventName}",
                    TechnicianInCharge = technician,
                    Location = evtLocation,
                    MaintenanceStatus = normalizedMaintStatus,
                    MaintenanceSubStatus = maintSubStatus,
                    MaintenanceNotes = maintNotes,
                    Notes = $"Reserved for shoot: {eventName} (#{apptId})"
                });
            }
        }
        catch
        {
        }

        RecalculateEquipmentConflicts(reservations);

        lock (_sqlCacheLock)
        {
            _sqlCachedEquipmentReservations = (DateTime.UtcNow, reservations.Select(CloneReservation).ToList());
        }

        return reservations;
    }

    public void AddEquipmentReservation(EquipmentReservationModel reservation)
    {
        if (string.IsNullOrWhiteSpace(reservation.EquipmentId)) return;
        InvalidateCache();

        if (_useInMemory)
        {
            if (string.IsNullOrWhiteSpace(reservation.Id))
                reservation.Id = Guid.NewGuid().ToString("N")[..8];

            var eq = _inMemoryEquipment.FirstOrDefault(e => e.Id.Equals(reservation.EquipmentId, StringComparison.OrdinalIgnoreCase));
            if (eq != null)
            {
                if (EquipmentItemModel.IsUnavailableStatus(eq.Status))
                    throw new InvalidOperationException($"Equipment '{eq.Name}' ({eq.Id}) is currently '{eq.Status}' and cannot be reserved.");

                reservation.EquipmentName = eq.Name;
                reservation.Category = eq.Category;
            }
            _inMemoryReservations.Add(reservation);
            RecalculateEquipmentConflicts(_inMemoryReservations);
            return;
        }

        try
        {
            using var conn = CreateConnection();
            conn.Open();

            using (var statusCmd = new SqlCommand("SELECT EquipmentName, ISNULL(EquipmentStatus, 'Available') FROM dbo.Equipment WHERE EquipmentID = @EqId;", conn))
            {
                statusCmd.Parameters.AddWithValue("@EqId", reservation.EquipmentId.Trim());
                using var sr = statusCmd.ExecuteReader();
                if (sr.Read())
                {
                    var eqName = sr.GetString(0);
                    var eqStatus = sr.GetString(1);
                    if (EquipmentItemModel.IsUnavailableStatus(eqStatus))
                    {
                        throw new InvalidOperationException($"Equipment '{eqName}' ({reservation.EquipmentId}) is currently '{eqStatus}' and cannot be reserved.");
                    }
                }
            }

            // Enforce entire calendar-day lock across non-cancelled/non-expired events
            const string sameDayCheckSql = @"
                SELECT TOP 1 ea.EventAppointmentID, COALESCE(NULLIF(ea.EventName, ''), ea.EventAppointmentID) AS EventName
                FROM dbo.EventEquipment eeq
                INNER JOIN dbo.EventAppointment ea ON ea.EventAppointmentID = eeq.EventAppointmentID
                WHERE eeq.EquipmentID = @EqId
                  AND CAST(ea.EventDate AS date) = @TargetDate
                  AND ISNULL(ea.EventStatus, '') NOT IN ('Cancelled', 'Expired')
                  AND NOT EXISTS (
                      SELECT 1 FROM dbo.BookingNegotiation bn
                      WHERE bn.EventAppointmentID = ea.EventAppointmentID
                        AND bn.NegotiationStatus IN ('Deal_Lost', 'Cancelled')
                  );";

            using (var lockCmd = new SqlCommand(sameDayCheckSql, conn))
            {
                lockCmd.Parameters.AddWithValue("@EqId", reservation.EquipmentId.Trim());
                lockCmd.Parameters.AddWithValue("@TargetDate", reservation.Date.Date);
                using var lockReader = lockCmd.ExecuteReader();
                if (lockReader.Read())
                {
                    var conflictEvtId = lockReader.GetString(0);
                    var conflictEvtName = lockReader.GetString(1);
                    throw new InvalidOperationException(
                        $"Equipment {reservation.EquipmentId} is already booked on {reservation.Date:yyyy-MM-dd} for '{conflictEvtName}' ({conflictEvtId}) and is unavailable for the entire day.");
                }
            }

            // Find or create an appointment for this reservation
            string apptId = reservation.Id;
            if (string.IsNullOrWhiteSpace(apptId) || apptId.Contains("-"))
            {
                apptId = "EVT-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
                const string createEventSql = @"
                    INSERT INTO dbo.EventAppointment 
                        (EventAppointmentID, EventName, EventDescription, CustomerID, EventDate, EventTime, EventStatus, CreatedAt)
                    VALUES 
                        (@Id, @Name, @Desc, 'CUST-0001', @Date, @Time, 'Confirmed', SYSUTCDATETIME());";

                using var createCmd = new SqlCommand(createEventSql, conn);
                createCmd.Parameters.AddWithValue("@Id", apptId);
                createCmd.Parameters.AddWithValue("@Name", string.IsNullOrWhiteSpace(reservation.ProjectOrClient) ? "Equipment Reservation" : reservation.ProjectOrClient.Trim());
                createCmd.Parameters.AddWithValue("@Desc", ((object?)reservation.Notes?.Trim()) ?? DBNull.Value);
                createCmd.Parameters.AddWithValue("@Date", reservation.Date.Date);
                createCmd.Parameters.AddWithValue("@Time", reservation.StartTime);
                createCmd.ExecuteNonQuery();
            }

            const string reserveSql = @"
                IF NOT EXISTS (SELECT 1 FROM dbo.EventEquipment WHERE EventAppointmentID = @ApptId AND EquipmentID = @EqId)
                BEGIN
                    INSERT INTO dbo.EventEquipment (EventAppointmentID, EquipmentID)
                    VALUES (@ApptId, @EqId);
                END";

            using var cmd = new SqlCommand(reserveSql, conn);
            cmd.Parameters.AddWithValue("@ApptId", apptId);
            cmd.Parameters.AddWithValue("@EqId", reservation.EquipmentId.Trim());
            cmd.ExecuteNonQuery();

            // Log activity
            const string logSql = @"
                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedByType, ActionDetails, LoggedAt)
                    VALUES ('CREATE', 'EventEquipment', @EqId, 'System', @Details, SYSUTCDATETIME());
                END";

            using var logCmd = new SqlCommand(logSql, conn);
            logCmd.Parameters.AddWithValue("@EqId", reservation.EquipmentId.Trim());
            logCmd.Parameters.AddWithValue("@Details", $"Reserved equipment {reservation.EquipmentId} for {reservation.ProjectOrClient} on {reservation.Date:yyyy-MM-dd}");
            logCmd.ExecuteNonQuery();
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
        }
    }

    public void UpdateEquipmentReservation(EquipmentReservationModel reservation)
    {
        if (_useInMemory)
        {
            var idx = _inMemoryReservations.FindIndex(r => r.Id == reservation.Id);
            if (idx >= 0)
            {
                _inMemoryReservations[idx] = reservation;
                RecalculateEquipmentConflicts(_inMemoryReservations);
            }
        }
    }

    public void DeleteEquipmentReservation(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        InvalidateCache();

        if (_useInMemory)
        {
            _inMemoryReservations.RemoveAll(r => r.Id == id);
            RecalculateEquipmentConflicts(_inMemoryReservations);
            return;
        }

        try
        {
            using var conn = CreateConnection();
            conn.Open();

            string apptId = string.Empty;
            string eqId = string.Empty;

            using (var lookupCmd = new SqlCommand(
                "SELECT TOP 1 EventAppointmentID, EquipmentID FROM dbo.EventEquipment WHERE (EventAppointmentID + '-' + EquipmentID) = @CompositeId;",
                conn))
            {
                lookupCmd.Parameters.AddWithValue("@CompositeId", id.Trim());
                using var r = lookupCmd.ExecuteReader();
                if (r.Read())
                {
                    apptId = r.GetString(0);
                    eqId = r.GetString(1);
                }
            }

            if (string.IsNullOrWhiteSpace(apptId))
            {
                var eqBoundary = id.IndexOf("-EQ", StringComparison.OrdinalIgnoreCase);
                if (eqBoundary > 0)
                {
                    apptId = id[..eqBoundary];
                    eqId = id[(eqBoundary + 1)..];
                }
                else
                {
                    var parts = id.Split('-');
                    if (parts.Length >= 2)
                    {
                        apptId = parts[0];
                        eqId = parts[1];
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(apptId) && !string.IsNullOrWhiteSpace(eqId))
            {
                using var cmd = new SqlCommand("DELETE FROM dbo.EventEquipment WHERE EventAppointmentID = @ApptId AND EquipmentID = @EqId;", conn);
                cmd.Parameters.AddWithValue("@ApptId", apptId);
                cmd.Parameters.AddWithValue("@EqId", eqId);
                cmd.ExecuteNonQuery();

                const string logSql = @"
                    IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                    BEGIN
                        INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, EventAppointmentID, ChangedByType, ActionDetails, LoggedAt)
                        VALUES ('DELETE', 'EventEquipment', @EqId, @ApptId, 'System', @Details, SYSUTCDATETIME());
                    END";

                using var logCmd = new SqlCommand(logSql, conn);
                logCmd.Parameters.AddWithValue("@EqId", eqId);
                logCmd.Parameters.AddWithValue("@ApptId", apptId);
                logCmd.Parameters.AddWithValue("@Details", $"Cancelled reservation for equipment {eqId} in appointment {apptId}");
                logCmd.ExecuteNonQuery();
            }
        }
        catch
        {
        }
    }

    public (bool Success, string? Error) ReplaceReservationEquipment(
        string reservationId,
        string? eventAppointmentId,
        string oldEquipmentId,
        string newEquipmentId,
        string actorId = "ADM-001")
    {
        if (string.IsNullOrWhiteSpace(oldEquipmentId) || string.IsNullOrWhiteSpace(newEquipmentId))
            return (false, "Both the current and replacement equipment IDs are required.");

        var cleanOldId = oldEquipmentId.Trim();
        var cleanNewId = newEquipmentId.Trim();
        if (cleanOldId.Equals(cleanNewId, StringComparison.OrdinalIgnoreCase))
            return (false, "Replacement equipment must be different from the current equipment.");

        InvalidateCache();

        if (_useInMemory)
        {
            var res = _inMemoryReservations.FirstOrDefault(r =>
                string.Equals(r.Id, reservationId, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(eventAppointmentId) &&
                 string.Equals(r.EventAppointmentId, eventAppointmentId, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(r.EquipmentId, cleanOldId, StringComparison.OrdinalIgnoreCase)));

            if (res == null)
                return (false, "Equipment reservation record was not found.");

            var newEq = _inMemoryEquipment.FirstOrDefault(e => e.Id.Equals(cleanNewId, StringComparison.OrdinalIgnoreCase));
            if (newEq == null)
                return (false, $"Replacement equipment '{cleanNewId}' was not found.");

            if (EquipmentItemModel.IsUnavailableStatus(newEq.Status))
                return (false, $"Equipment '{newEq.Name}' ({cleanNewId}) is currently '{newEq.Status}' and cannot be scheduled.");

            var targetDate = res.Date.Date;
            var conflict = _inMemoryReservations.FirstOrDefault(r =>
                !string.Equals(r.Id, res.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.EquipmentId, cleanNewId, StringComparison.OrdinalIgnoreCase) &&
                r.Date.Date == targetDate &&
                !EquipmentItemModel.IsEventStageCompletedOrInactive(r.EventStatus));
            if (conflict != null)
                return (false, $"Equipment '{newEq.Name}' is already booked on {targetDate:yyyy-MM-dd} for '{conflict.ProjectOrClient}'.");

            res.EquipmentId = newEq.Id;
            res.EquipmentName = newEq.Name;
            res.Category = newEq.Category;
            res.MaintenanceStatus = newEq.Status.Equals("Available", StringComparison.OrdinalIgnoreCase) ? "Ready" : newEq.Status;
            res.MaintenanceSubStatus = null;
            res.MaintenanceNotes = null;
            if (!string.IsNullOrWhiteSpace(res.EventAppointmentId))
                res.Id = $"{res.EventAppointmentId}-{newEq.Id}";

            foreach (var b in _inMemoryBookings.Where(b =>
                (!string.IsNullOrWhiteSpace(res.EventAppointmentId) && string.Equals(b.Id, res.EventAppointmentId, StringComparison.OrdinalIgnoreCase)) ||
                (b.Date.Date == targetDate && b.AssignedEquipmentIds.Contains(cleanOldId, StringComparer.OrdinalIgnoreCase))))
            {
                b.AssignedEquipmentIds.RemoveAll(id => id.Equals(cleanOldId, StringComparison.OrdinalIgnoreCase));
                if (!b.AssignedEquipmentIds.Contains(newEq.Id, StringComparer.OrdinalIgnoreCase))
                    b.AssignedEquipmentIds.Add(newEq.Id);
                b.AssignedEquipment.RemoveAll(ae => ae.EquipmentId.Equals(cleanOldId, StringComparison.OrdinalIgnoreCase));
                if (!b.AssignedEquipment.Any(ae => ae.EquipmentId.Equals(newEq.Id, StringComparison.OrdinalIgnoreCase)))
                    b.AssignedEquipment.Add(new EventEquipmentSummary(newEq.Id, newEq.Name, newEq.Category, newEq.Status));
                b.AssignedEquipmentNames = b.AssignedEquipment.Select(ae => ae.EquipmentName).ToList();
            }

            RecalculateEquipmentConflicts(_inMemoryReservations);
            RecalculateEventConflicts(_inMemoryBookings);
            return (true, null);
        }

        try
        {
            using var conn = CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            try
            {
                var apptId = eventAppointmentId?.Trim();
                if (string.IsNullOrWhiteSpace(apptId) && !string.IsNullOrWhiteSpace(reservationId))
                {
                    using var lookupCmd = new SqlCommand(
                        "SELECT TOP 1 EventAppointmentID FROM dbo.EventEquipment WHERE (EventAppointmentID + '-' + EquipmentID) = @CompositeId;",
                        conn, tx);
                    lookupCmd.Parameters.AddWithValue("@CompositeId", reservationId.Trim());
                    apptId = lookupCmd.ExecuteScalar()?.ToString();
                }

                if (string.IsNullOrWhiteSpace(apptId))
                    return (false, "Could not resolve the event appointment for this reservation.");

                DateTime evtDate;
                string evtName;
                string evtStatus;
                using (var evtCmd = new SqlCommand(
                    "SELECT EventDate, ISNULL(EventName, EventAppointmentID), ISNULL(EventStatus, '') FROM dbo.EventAppointment WHERE EventAppointmentID = @ApptId;",
                    conn, tx))
                {
                    evtCmd.Parameters.AddWithValue("@ApptId", apptId);
                    using var r = evtCmd.ExecuteReader();
                    if (!r.Read())
                        return (false, $"Event appointment '{apptId}' not found.");
                    evtDate = r.GetDateTime(0).Date;
                    evtName = r.GetString(1);
                    evtStatus = r.GetString(2);
                }

                if (EquipmentItemModel.IsEventStageCompletedOrInactive(evtStatus))
                    return (false, $"Cannot modify equipment for a '{evtStatus}' event.");

                string newEqName;
                string newEqStatus;
                using (var eqCmd = new SqlCommand(
                    "SELECT EquipmentName, ISNULL(EquipmentStatus, 'Available') FROM dbo.Equipment WHERE EquipmentID = @NewEqId;",
                    conn, tx))
                {
                    eqCmd.Parameters.AddWithValue("@NewEqId", cleanNewId);
                    using var r = eqCmd.ExecuteReader();
                    if (!r.Read())
                        return (false, $"Replacement equipment '{cleanNewId}' was not found.");
                    newEqName = r.GetString(0);
                    newEqStatus = r.GetString(1);
                }

                if (EquipmentItemModel.IsUnavailableStatus(newEqStatus))
                    return (false, $"Equipment '{newEqName}' ({cleanNewId}) is currently '{newEqStatus}' and cannot be scheduled.");

                const string sameDayCheckSql = @"
                    SELECT TOP 1 ea.EventAppointmentID, COALESCE(NULLIF(ea.EventName, ''), ea.EventAppointmentID) AS EventName
                    FROM dbo.EventEquipment eeq
                    INNER JOIN dbo.EventAppointment ea ON ea.EventAppointmentID = eeq.EventAppointmentID
                    WHERE eeq.EquipmentID = @NewEqId
                      AND eeq.EventAppointmentID <> @ApptId
                      AND CAST(ea.EventDate AS date) = @TargetDate
                      AND ISNULL(ea.EventStatus, '') NOT IN ('Cancelled', 'Expired')
                      AND NOT EXISTS (
                          SELECT 1 FROM dbo.BookingNegotiation bn
                          WHERE bn.EventAppointmentID = ea.EventAppointmentID
                            AND bn.NegotiationStatus IN ('Deal_Lost', 'Cancelled')
                      );";
                using (var lockCmd = new SqlCommand(sameDayCheckSql, conn, tx))
                {
                    lockCmd.Parameters.AddWithValue("@NewEqId", cleanNewId);
                    lockCmd.Parameters.AddWithValue("@ApptId", apptId);
                    lockCmd.Parameters.AddWithValue("@TargetDate", evtDate);
                    using var lr = lockCmd.ExecuteReader();
                    if (lr.Read())
                        return (false, $"Equipment '{newEqName}' is already booked on {evtDate:yyyy-MM-dd} for '{lr.GetString(1)}' ({lr.GetString(0)}).");
                }

                string oldEqName = cleanOldId;
                using (var oldCmd = new SqlCommand("SELECT EquipmentName FROM dbo.Equipment WHERE EquipmentID = @OldEqId;", conn, tx))
                {
                    oldCmd.Parameters.AddWithValue("@OldEqId", cleanOldId);
                    var o = oldCmd.ExecuteScalar()?.ToString();
                    if (!string.IsNullOrWhiteSpace(o)) oldEqName = o;
                }

                using (var delCmd = new SqlCommand(
                    "DELETE FROM dbo.EventEquipment WHERE EventAppointmentID = @ApptId AND EquipmentID = @OldEqId;",
                    conn, tx))
                {
                    delCmd.Parameters.AddWithValue("@ApptId", apptId);
                    delCmd.Parameters.AddWithValue("@OldEqId", cleanOldId);
                    delCmd.ExecuteNonQuery();
                }

                using (var insCmd = new SqlCommand(@"
                    IF NOT EXISTS (SELECT 1 FROM dbo.EventEquipment WHERE EventAppointmentID = @ApptId AND EquipmentID = @NewEqId)
                    BEGIN
                        INSERT INTO dbo.EventEquipment (EventAppointmentID, EquipmentID)
                        VALUES (@ApptId, @NewEqId);
                    END;", conn, tx))
                {
                    insCmd.Parameters.AddWithValue("@ApptId", apptId);
                    insCmd.Parameters.AddWithValue("@NewEqId", cleanNewId);
                    insCmd.ExecuteNonQuery();
                }

                const string logSql = @"
                    IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                    BEGIN
                        INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, EventAppointmentID, ChangedByType, ActionDetails, LoggedAt)
                        VALUES ('UPDATE', 'EventEquipment', @NewEqId, @ApptId, 'Admin', @Details, SYSUTCDATETIME());
                    END;";
                using (var logCmd = new SqlCommand(logSql, conn, tx))
                {
                    logCmd.Parameters.AddWithValue("@NewEqId", cleanNewId);
                    logCmd.Parameters.AddWithValue("@ApptId", apptId);
                    logCmd.Parameters.AddWithValue("@Details", $"Scheduled replacement equipment '{newEqName}' ({cleanNewId}) in place of '{oldEqName}' ({cleanOldId}) for '{evtName}' (#{apptId}) on {evtDate:yyyy-MM-dd}.");
                    logCmd.ExecuteNonQuery();
                }

                tx.Commit();
                InvalidateCache();
                return (true, null);
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch { }
                return (false, ex.Message);
            }
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public int GetEquipmentConflictCount()
    {
        var reservations = GetEquipmentReservations();
        return reservations.Count(r => r.HasConflict);
    }

    private static void RecalculateEquipmentConflicts(List<EquipmentReservationModel> reservations)
    {
        foreach (var r in reservations)
        {
            r.HasConflict = false;
            r.ConflictMessage = null;
        }

        for (int i = 0; i < reservations.Count; i++)
        {
            for (int j = i + 1; j < reservations.Count; j++)
            {
                var r1 = reservations[i];
                var r2 = reservations[j];

                if (r1.OverlapsWith(r2))
                {
                    r1.HasConflict = true;
                    r1.ConflictMessage = $"Gear overlap with '{r2.ProjectOrClient}' ({r2.FormattedTimeRange})";

                    r2.HasConflict = true;
                    r2.ConflictMessage = $"Gear overlap with '{r1.ProjectOrClient}' ({r1.FormattedTimeRange})";
                }
            }
        }
    }
}
