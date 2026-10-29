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
            Id = "SP-03",
            Name = "Outdoor / On-Location Shoot",
            Dimensions = "Open Area",
            MaxCapacity = 50,
            IconType = "outdoor",
            BadgeColor = "bg-emerald-100 text-emerald-800 border-emerald-200",
            Description = "Field shoots and on-site client assignments."
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
        _connectionString = configuration?.GetConnectionString("DefaultConnection")
            ?? "Data Source=localhost\\SQLEXPRESS;Initial Catalog=PhotoAevumStudio;Integrated Security=True;Persist Security Info=False;Pooling=False;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=True;Application Name=\"SQL Server Management Studio\";Command Timeout=0";

        if (_useInMemory)
        {
            SeedInMemoryData();
        }
    }

    private SqlConnection CreateConnection() => new SqlConnection(_connectionString);

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
                ClientName = "Vogue Scandinavia",
                ClientEmail = "production@voguescan.com",
                ClientPhone = "+1 (555) 234-5678",
                ServiceType = "Commercial Campaign",
                Date = monday,
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(13, 0, 0),
                Status = "Confirmed",
                AssignedPhotographer = "Elena Rostova",
                EstimatedPrice = 1200.00m,
                Notes = "Autumn fashion editorial."
            },
            new EventBookingModel
            {
                Id = "evt-105",
                StudioSpaceId = "studio-a",
                StudioSpaceName = "Studio A (Main Hall)",
                ClientName = "Horizon Luxury Apparel",
                ClientEmail = "shoot@horizonapparel.com",
                ClientPhone = "+1 (555) 678-9012",
                ServiceType = "Commercial Campaign",
                Date = monday.AddDays(2),
                StartTime = new TimeSpan(10, 0, 0),
                EndTime = new TimeSpan(14, 0, 0),
                Status = "Confirmed",
                AssignedPhotographer = "Elena Rostova",
                EstimatedPrice = 1400.00m,
                Notes = "Winter outerwear collection lookbook."
            },
            new EventBookingModel
            {
                Id = "evt-106",
                StudioSpaceId = "studio-a",
                StudioSpaceName = "Studio A (Main Hall)",
                ClientName = "Sterling Capital Partners",
                ClientEmail = "partners@sterlingcap.com",
                ClientPhone = "+1 (555) 789-0123",
                ServiceType = "Express Headshot",
                Date = monday.AddDays(2),
                StartTime = new TimeSpan(12, 30, 0),
                EndTime = new TimeSpan(16, 0, 0),
                Status = "Pending",
                AssignedPhotographer = "Clara Oswald",
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

    public List<EventBookingModel> GetEventBookings()
    {
        if (_useInMemory)
        {
            RecalculateEventConflicts(_inMemoryBookings);
            return _inMemoryBookings;
        }

        var bookings = new List<EventBookingModel>();

        try
        {
            using var conn = CreateConnection();
            conn.Open();

            const string sql = @"
                SELECT ea.EventAppointmentID,
                       ea.EventName,
                       ea.EventDescription,
                       ea.CustomerID,
                       COALESCE(NULLIF(LTRIM(RTRIM(c.CustomerFirstName + ' ' + ISNULL(c.CustomerLastName, ''))), ''), ea.CustomerID, 'Studio Client') AS CustomerName,
                       c.CustomerEmail,
                       c.CustomerPhoneNumber,
                       CAST(ea.EventDate AS datetime2) AS EventDate,
                       ea.EventTime,
                       ea.EventStatus,
                       emp.EmployeeID,
                       COALESCE(NULLIF(LTRIM(RTRIM(emp.EmployeeFirstName + ' ' + ISNULL(emp.EmployeeLastName, ''))), ''), emp.EmployeeID, 'Unassigned') AS EmployeeName,
                       CAST(0 AS bit) AS IsConsultation,
                       NULL AS MeetingLink
                FROM dbo.EventAppointment ea
                LEFT JOIN dbo.Customer c ON c.CustomerID = ea.CustomerID
                LEFT JOIN dbo.EventEmployee ee ON ee.EventAppointmentID = ea.EventAppointmentID
                LEFT JOIN dbo.Employees emp ON emp.EmployeeID = ee.EmployeeID
                WHERE ea.EventAppointmentID NOT IN (
                    SELECT EventAppointmentID 
                    FROM dbo.BookingNegotiation 
                    WHERE EventAppointmentID IS NOT NULL 
                      AND NegotiationStatus IN ('Pending_Call', 'Rescheduled', 'Deal_Lost', 'Cancelled')
                )
                AND ea.EventAppointmentID NOT IN (
                    SELECT 'EVT-' + RIGHT(NegotiationID, 4)
                    FROM dbo.BookingNegotiation
                    WHERE NegotiationStatus IN ('Pending_Call', 'Rescheduled', 'Deal_Lost', 'Cancelled')
                )

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
                       bn.MeetingLink
                FROM dbo.BookingNegotiation bn
                LEFT JOIN dbo.Customer c ON c.CustomerID = bn.CustomerID
                LEFT JOIN dbo.Employees emp ON emp.EmployeeID = bn.EmployeeID
                WHERE bn.NegotiationStatus IN ('Pending_Call', 'Rescheduled')
                ORDER BY EventDate DESC, EventTime DESC;";

            using var cmd = new SqlCommand(sql, conn);
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

                // Default duration: 30 mins for consultation, 2 hours for shoot events
                var endTime = isConsultation
                    ? eventTime.Add(TimeSpan.FromMinutes(30))
                    : eventTime.Add(TimeSpan.FromHours(2));

                if (endTime > new TimeSpan(23, 59, 0))
                    endTime = new TimeSpan(23, 59, 0);

                var space = _defaultStudioSpaces[0];

                bookings.Add(new EventBookingModel
                {
                    Id = id,
                    StudioSpaceId = space.Id,
                    StudioSpaceName = space.Name,
                    ClientName = clientName,
                    ClientEmail = email,
                    ClientPhone = phone,
                    ServiceType = isConsultation ? "Online Consultation" : (string.IsNullOrWhiteSpace(eventName) ? "Studio Photography" : eventName),
                    Date = eventDate,
                    StartTime = eventTime,
                    EndTime = endTime,
                    Status = status,
                    AssignedPhotographer = empName,
                    Notes = desc,
                    EstimatedPrice = isConsultation ? 0.00m : 350.00m,
                    IsConsultation = isConsultation,
                    MeetingLink = meetingLink
                });
            }
        }
        catch
        {
            // Fail safely
        }

        RecalculateEventConflicts(bookings);
        return bookings;
    }

    public void AddEventBooking(EventBookingModel booking)
    {
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
                    SELECT TOP 1 @EmpId = EmployeeID 
                    FROM dbo.Employees 
                    WHERE EmployeeID = @EmpName 
                       OR (EmployeeFirstName + ' ' + ISNULL(EmployeeLastName, '')) LIKE '%' + @EmpName + '%';

                    IF @EmpId IS NOT NULL
                    BEGIN
                        IF NOT EXISTS (SELECT 1 FROM dbo.EventEmployee WHERE EventAppointmentID = @ApptId AND EmployeeID = @EmpId)
                        BEGIN
                            INSERT INTO dbo.EventEmployee (EventAppointmentID, EmployeeID)
                            VALUES (@ApptId, @EmpId);
                        END
                    END";

                using var assignCmd = new SqlCommand(assignSql, conn);
                assignCmd.Parameters.AddWithValue("@ApptId", booking.Id.Trim());
                assignCmd.Parameters.AddWithValue("@EmpName", booking.AssignedPhotographer.Trim());
                assignCmd.ExecuteNonQuery();
            }

            // Log activity
            const string logSql = @"
                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedBy, ChangedByType, ActionDetails, LoggedAt)
                    VALUES ('Create', 'EventAppointment', @Id, 'Staff', 'Staff', @Details, SYSUTCDATETIME());
                END";

            using var logCmd = new SqlCommand(logSql, conn);
            logCmd.Parameters.AddWithValue("@Id", booking.Id.Trim());
            logCmd.Parameters.AddWithValue("@Details", $"Scheduled event {booking.Id}: {booking.ClientName} ({booking.ServiceType}) on {booking.Date:yyyy-MM-dd} at {booking.StartTime}");
            logCmd.ExecuteNonQuery();
        }
        catch
        {
        }
    }

    public void UpdateEventBooking(EventBookingModel booking)
    {
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
                    DELETE FROM dbo.EventEmployee WHERE EventAppointmentID = @ApptId;

                    DECLARE @EmpId nvarchar(50) = NULL;
                    SELECT TOP 1 @EmpId = EmployeeID 
                    FROM dbo.Employees 
                    WHERE EmployeeID = @EmpName 
                       OR (EmployeeFirstName + ' ' + ISNULL(EmployeeLastName, '')) LIKE '%' + @EmpName + '%';

                    IF @EmpId IS NOT NULL
                    BEGIN
                        INSERT INTO dbo.EventEmployee (EventAppointmentID, EmployeeID)
                        VALUES (@ApptId, @EmpId);
                    END";

                using var reassignCmd = new SqlCommand(reassignSql, conn);
                reassignCmd.Parameters.AddWithValue("@ApptId", booking.Id.Trim());
                reassignCmd.Parameters.AddWithValue("@EmpName", booking.AssignedPhotographer.Trim());
                reassignCmd.ExecuteNonQuery();
            }

            // Log activity
            const string logSql = @"
                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedBy, ChangedByType, ActionDetails, LoggedAt)
                    VALUES ('Update', 'EventAppointment', @Id, 'Staff', 'Staff', @Details, SYSUTCDATETIME());
                END";

            using var logCmd = new SqlCommand(logSql, conn);
            logCmd.Parameters.AddWithValue("@Id", booking.Id.Trim());
            logCmd.Parameters.AddWithValue("@Details", $"Updated event {booking.Id}: Status={booking.Status}, Date={booking.Date:yyyy-MM-dd}");
            logCmd.ExecuteNonQuery();
        }
        catch
        {
        }
    }

    public void DeleteEventBooking(string id)
    {
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
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedBy, ChangedByType, ActionDetails, LoggedAt)
                    VALUES ('Delete', 'EventAppointment', @Id, 'Staff', 'Staff', @Details, SYSUTCDATETIME());
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

    public int GetEventConflictCount()
    {
        var bookings = GetEventBookings();
        return bookings.Count(b => b.HasConflict);
    }

    private static void RecalculateEventConflicts(List<EventBookingModel> bookings)
    {
        foreach (var b in bookings)
        {
            b.HasConflict = false;
            b.ConflictMessage = null;
        }

        for (int i = 0; i < bookings.Count; i++)
        {
            for (int j = i + 1; j < bookings.Count; j++)
            {
                var b1 = bookings[i];
                var b2 = bookings[j];

                if (b1.OverlapsWith(b2))
                {
                    b1.HasConflict = true;
                    b1.ConflictMessage = $"Double-booked with '{b2.ClientName}' ({b2.FormattedTimeRange})";

                    b2.HasConflict = true;
                    b2.ConflictMessage = $"Double-booked with '{b1.ClientName}' ({b1.FormattedTimeRange})";
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

        var shifts = new List<StaffShiftModel>();

        try
        {
            using var conn = CreateConnection();
            conn.Open();

            const string sql = @"
                SELECT ee.EventAppointmentID,
                       emp.EmployeeID,
                       COALESCE(NULLIF(LTRIM(RTRIM(emp.EmployeeFirstName + ' ' + ISNULL(emp.EmployeeLastName, ''))), ''), emp.EmployeeID) AS FullName,
                       ea.EventDate,
                       ea.EventTime,
                       ea.EventName,
                       COALESCE(es.SpecializationName, 'Photographer') AS Role
                FROM dbo.EventEmployee ee
                INNER JOIN dbo.EventAppointment ea ON ea.EventAppointmentID = ee.EventAppointmentID
                INNER JOIN dbo.Employees emp ON emp.EmployeeID = ee.EmployeeID
                LEFT JOIN dbo.EmployeeJointSpecialization ejs ON ejs.EmployeeID = emp.EmployeeID
                LEFT JOIN dbo.EmployeeSpecialization es ON es.SpecializationID = ejs.SpecializationID
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

                var endTime = startTime.Add(TimeSpan.FromHours(2));
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
                    Location = "Studio A",
                    ShiftType = "Production Shoot",
                    Notes = $"Assigned to event: {eventName}"
                });
            }
        }
        catch
        {
        }

        shifts.AddRange(_inMemoryShifts);
        RecalculateShiftConflicts(shifts);
        return shifts;
    }

    public void AddStaffShift(StaffShiftModel shift)
    {
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

        var list = new List<EquipmentItemResource>();

        try
        {
            using var conn = CreateConnection();
            conn.Open();

            const string sql = @"
                SELECT eq.EquipmentID,
                       eq.EquipmentName,
                       COALESCE(eq.SerialNo, eq.EquipmentID) AS SerialNumber,
                       COALESCE(et.EquipmentTypeName, eq.EquipmentTypeID, 'Gear') AS Category,
                       COALESCE(eq.EquipmentStatus, 'Ready') AS Status
                FROM dbo.Equipment eq
                LEFT JOIN dbo.EquipmentType et ON et.EquipmentTypeID = eq.EquipmentTypeID
                ORDER BY eq.EquipmentID;";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                list.Add(new EquipmentItemResource
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    SerialNumber = reader.GetString(2),
                    Category = reader.GetString(3),
                    Status = reader.GetString(4),
                    StorageBay = "Studio Inventory Rack",
                    BadgeColor = "bg-emerald-50 text-emerald-700 border-emerald-200"
                });
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
            RecalculateEquipmentConflicts(_inMemoryReservations);
            return _inMemoryReservations;
        }

        var reservations = new List<EquipmentReservationModel>();

        try
        {
            using var conn = CreateConnection();
            conn.Open();

            const string sql = @"
                SELECT eeq.EventAppointmentID,
                       eq.EquipmentID,
                       eq.EquipmentName,
                       COALESCE(et.EquipmentTypeName, eq.EquipmentTypeID, 'Gear') AS Category,
                       ea.EventDate,
                       ea.EventTime,
                       ea.EventName,
                       COALESCE(NULLIF(LTRIM(RTRIM(emp.EmployeeFirstName + ' ' + ISNULL(emp.EmployeeLastName, ''))), ''), emp.EmployeeID, 'Lead Technician') AS Technician
                FROM dbo.EventEquipment eeq
                INNER JOIN dbo.EventAppointment ea ON ea.EventAppointmentID = eeq.EventAppointmentID
                INNER JOIN dbo.Equipment eq ON eq.EquipmentID = eeq.EquipmentID
                LEFT JOIN dbo.EquipmentType et ON et.EquipmentTypeID = eq.EquipmentTypeID
                LEFT JOIN dbo.EventEmployee ee ON ee.EventAppointmentID = ea.EventAppointmentID
                LEFT JOIN dbo.Employees emp ON emp.EmployeeID = ee.EmployeeID
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

                var endTime = startTime.Add(TimeSpan.FromHours(2));
                if (endTime > new TimeSpan(23, 59, 0))
                    endTime = new TimeSpan(23, 59, 0);

                reservations.Add(new EquipmentReservationModel
                {
                    Id = $"{apptId}-{eqId}",
                    EquipmentId = eqId,
                    EquipmentName = eqName,
                    Category = cat,
                    Date = eventDate,
                    StartTime = startTime,
                    EndTime = endTime,
                    ProjectOrClient = eventName,
                    TechnicianInCharge = technician,
                    Location = "Studio A",
                    MaintenanceStatus = "Ready",
                    Notes = $"Reserved for shoot: {eventName}"
                });
            }
        }
        catch
        {
        }

        RecalculateEquipmentConflicts(reservations);
        return reservations;
    }

    public void AddEquipmentReservation(EquipmentReservationModel reservation)
    {
        if (string.IsNullOrWhiteSpace(reservation.EquipmentId)) return;

        if (_useInMemory)
        {
            if (string.IsNullOrWhiteSpace(reservation.Id))
                reservation.Id = Guid.NewGuid().ToString("N")[..8];

            var eq = _inMemoryEquipment.FirstOrDefault(e => e.Id == reservation.EquipmentId);
            if (eq != null)
            {
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
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedBy, ChangedByType, ActionDetails, LoggedAt)
                    VALUES ('Create', 'EventEquipment', @EqId, 'Staff', 'Staff', @Details, SYSUTCDATETIME());
                END";

            using var logCmd = new SqlCommand(logSql, conn);
            logCmd.Parameters.AddWithValue("@EqId", reservation.EquipmentId.Trim());
            logCmd.Parameters.AddWithValue("@Details", $"Reserved equipment {reservation.EquipmentId} for {reservation.ProjectOrClient} on {reservation.Date:yyyy-MM-dd}");
            logCmd.ExecuteNonQuery();
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

        if (_useInMemory)
        {
            _inMemoryReservations.RemoveAll(r => r.Id == id);
            RecalculateEquipmentConflicts(_inMemoryReservations);
            return;
        }

        var parts = id.Split('-');
        if (parts.Length >= 2)
        {
            var apptId = parts[0];
            var eqId = parts[1];

            try
            {
                using var conn = CreateConnection();
                conn.Open();
                using var cmd = new SqlCommand("DELETE FROM dbo.EventEquipment WHERE EventAppointmentID = @ApptId AND EquipmentID = @EqId;", conn);
                cmd.Parameters.AddWithValue("@ApptId", apptId);
                cmd.Parameters.AddWithValue("@EqId", eqId);
                cmd.ExecuteNonQuery();

                const string logSql = @"
                    IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                    BEGIN
                        INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedBy, ChangedByType, ActionDetails, LoggedAt)
                        VALUES ('Delete', 'EventEquipment', @EqId, 'Staff', 'Staff', @Details, SYSUTCDATETIME());
                    END";

                using var logCmd = new SqlCommand(logSql, conn);
                logCmd.Parameters.AddWithValue("@EqId", eqId);
                logCmd.Parameters.AddWithValue("@Details", $"Cancelled reservation for equipment {eqId} in appointment {apptId}");
                logCmd.ExecuteNonQuery();
            }
            catch
            {
            }
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
