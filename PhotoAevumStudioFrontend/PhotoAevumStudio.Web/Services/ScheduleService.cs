using System;
using System.Collections.Generic;
using System.Linq;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public class ScheduleService : IScheduleService
{
    private readonly List<StudioSpaceResource> _studioSpaces = new();
    private readonly List<EventBookingModel> _eventBookings = new();

    private readonly List<StaffMemberResource> _staffMembers = new();
    private readonly List<StaffShiftModel> _staffShifts = new();

    private readonly List<EquipmentItemResource> _equipmentResources = new();
    private readonly List<EquipmentReservationModel> _equipmentReservations = new();

<<<<<<< HEAD
    private readonly List<CategoryAssociationProfile> _categoryProfiles = new();

=======
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
    public ScheduleService()
    {
        SeedStudioSpaces();
        SeedEventBookings();
        SeedStaffMembers();
        SeedStaffShifts();
        SeedEquipment();
        SeedEquipmentReservations();
<<<<<<< HEAD
        SeedCategoryProfiles();
=======
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957

        RecalculateEventConflicts();
        RecalculateShiftConflicts();
        RecalculateEquipmentConflicts();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STUDIO SPACES & EVENTS
    // ─────────────────────────────────────────────────────────────────────────

    public List<StudioSpaceResource> GetStudioSpaces() => _studioSpaces;

    public List<EventBookingModel> GetEventBookings() => _eventBookings;

    public void AddEventBooking(EventBookingModel booking)
    {
        if (string.IsNullOrWhiteSpace(booking.Id))
            booking.Id = Guid.NewGuid().ToString("N")[..8];

        var space = _studioSpaces.FirstOrDefault(s => s.Id == booking.StudioSpaceId);
        if (space != null)
            booking.StudioSpaceName = space.Name;

        _eventBookings.Add(booking);
        RecalculateEventConflicts();
<<<<<<< HEAD
        SyncEventAssociations(booking);
=======
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
    }

    public void UpdateEventBooking(EventBookingModel booking)
    {
        var existingIndex = _eventBookings.FindIndex(b => b.Id == booking.Id);
        if (existingIndex >= 0)
        {
            var space = _studioSpaces.FirstOrDefault(s => s.Id == booking.StudioSpaceId);
            if (space != null)
                booking.StudioSpaceName = space.Name;

            _eventBookings[existingIndex] = booking;
            RecalculateEventConflicts();
<<<<<<< HEAD
            SyncEventAssociations(booking);
=======
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
        }
    }

    public void DeleteEventBooking(string id)
    {
        _eventBookings.RemoveAll(b => b.Id == id);
        RecalculateEventConflicts();
<<<<<<< HEAD

        // Release the equipment that was auto-reserved for this event.
        _equipmentReservations.RemoveAll(r => r.SourceEventId == id);
        RecalculateEquipmentConflicts();
=======
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
    }

    public int GetEventConflictCount()
    {
        return _eventBookings.Count(b => b.HasConflict);
    }

    private void RecalculateEventConflicts()
    {
        foreach (var b in _eventBookings)
        {
            b.HasConflict = false;
            b.ConflictMessage = null;
        }

        for (int i = 0; i < _eventBookings.Count; i++)
        {
            for (int j = i + 1; j < _eventBookings.Count; j++)
            {
                var b1 = _eventBookings[i];
                var b2 = _eventBookings[j];

                if (b1.OverlapsWith(b2))
                {
                    b1.HasConflict = true;
                    b1.ConflictMessage = $"Double-booked with '{b2.ClientName}' ({b2.FormattedTimeRange})";

                    b2.HasConflict = true;
                    b2.ConflictMessage = $"Double-booked with '{b1.ClientName}' ({b1.FormattedTimeRange})";
                }
<<<<<<< HEAD
                else if (b1.SharesStaffWith(b2))
                {
                    b1.HasConflict = true;
                    b1.ConflictMessage = $"Assigned staff also booked on '{b2.ClientName}' ({b2.FormattedTimeRange})";

                    b2.HasConflict = true;
                    b2.ConflictMessage = $"Assigned staff also booked on '{b1.ClientName}' ({b1.FormattedTimeRange})";
                }
=======
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STAFF & SHIFTS
    // ─────────────────────────────────────────────────────────────────────────

    public List<StaffMemberResource> GetStaffMembers() => _staffMembers;

    public List<StaffShiftModel> GetStaffShifts() => _staffShifts;

    public void AddStaffShift(StaffShiftModel shift)
    {
        if (string.IsNullOrWhiteSpace(shift.Id))
            shift.Id = Guid.NewGuid().ToString("N")[..8];

        if (!string.IsNullOrEmpty(shift.StaffId))
        {
            var staff = _staffMembers.FirstOrDefault(s => s.Id == shift.StaffId);
            if (staff != null)
            {
                shift.StaffName = staff.FullName;
                shift.Role = staff.Role;
            }
        }
        else
        {
            shift.StaffName = "Open Shift";
        }

        _staffShifts.Add(shift);
        RecalculateShiftConflicts();
    }

    public void UpdateStaffShift(StaffShiftModel shift)
    {
        var existingIndex = _staffShifts.FindIndex(s => s.Id == shift.Id);
        if (existingIndex >= 0)
        {
            if (!string.IsNullOrEmpty(shift.StaffId))
            {
                var staff = _staffMembers.FirstOrDefault(s => s.Id == shift.StaffId);
                if (staff != null)
                {
                    shift.StaffName = staff.FullName;
                    shift.Role = staff.Role;
                }
            }
            else
            {
                shift.StaffName = "Open Shift";
            }

            _staffShifts[existingIndex] = shift;
            RecalculateShiftConflicts();
        }
    }

    public void DeleteStaffShift(string id)
    {
        _staffShifts.RemoveAll(s => s.Id == id);
        RecalculateShiftConflicts();
    }

    public int GetShiftConflictCount()
    {
        return _staffShifts.Count(s => s.HasConflict);
    }

    private void RecalculateShiftConflicts()
    {
        foreach (var s in _staffShifts)
        {
            s.HasConflict = false;
            s.ConflictMessage = null;
        }

        for (int i = 0; i < _staffShifts.Count; i++)
        {
            for (int j = i + 1; j < _staffShifts.Count; j++)
            {
                var s1 = _staffShifts[i];
                var s2 = _staffShifts[j];

                if (s1.OverlapsWith(s2))
                {
                    s1.HasConflict = true;
                    s1.ConflictMessage = $"Overlapping shift at '{s2.Location}' ({s2.FormattedTimeRange})";

                    s2.HasConflict = true;
                    s2.ConflictMessage = $"Overlapping shift at '{s1.Location}' ({s1.FormattedTimeRange})";
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // EQUIPMENT & RESERVATIONS
    // ─────────────────────────────────────────────────────────────────────────

    public List<EquipmentItemResource> GetEquipmentResources() => _equipmentResources;

    public List<EquipmentReservationModel> GetEquipmentReservations() => _equipmentReservations;

    public void AddEquipmentReservation(EquipmentReservationModel reservation)
    {
        if (string.IsNullOrWhiteSpace(reservation.Id))
            reservation.Id = Guid.NewGuid().ToString("N")[..8];

        var eq = _equipmentResources.FirstOrDefault(e => e.Id == reservation.EquipmentId);
        if (eq != null)
        {
            reservation.EquipmentName = eq.Name;
            reservation.Category = eq.Category;
        }

        _equipmentReservations.Add(reservation);
        RecalculateEquipmentConflicts();
    }

    public void UpdateEquipmentReservation(EquipmentReservationModel reservation)
    {
        var existingIndex = _equipmentReservations.FindIndex(r => r.Id == reservation.Id);
        if (existingIndex >= 0)
        {
            var eq = _equipmentResources.FirstOrDefault(e => e.Id == reservation.EquipmentId);
            if (eq != null)
            {
                reservation.EquipmentName = eq.Name;
                reservation.Category = eq.Category;
            }

            _equipmentReservations[existingIndex] = reservation;
            RecalculateEquipmentConflicts();
        }
    }

    public void DeleteEquipmentReservation(string id)
    {
        _equipmentReservations.RemoveAll(r => r.Id == id);
        RecalculateEquipmentConflicts();
    }

    public int GetEquipmentConflictCount()
    {
        return _equipmentReservations.Count(r => r.HasConflict);
    }

    private void RecalculateEquipmentConflicts()
    {
        foreach (var r in _equipmentReservations)
        {
            r.HasConflict = false;
            r.ConflictMessage = null;
        }

        for (int i = 0; i < _equipmentReservations.Count; i++)
        {
            for (int j = i + 1; j < _equipmentReservations.Count; j++)
            {
                var r1 = _equipmentReservations[i];
                var r2 = _equipmentReservations[j];

                if (r1.OverlapsWith(r2))
                {
                    r1.HasConflict = true;
                    r1.ConflictMessage = $"Double-reserved for '{r2.ProjectOrClient}' ({r2.FormattedTimeRange})";

                    r2.HasConflict = true;
                    r2.ConflictMessage = $"Double-reserved for '{r1.ProjectOrClient}' ({r1.FormattedTimeRange})";
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
<<<<<<< HEAD
    // CATEGORY → ASSOCIATION PROFILES (auto-assignment when a category is chosen)
    // ─────────────────────────────────────────────────────────────────────────

    public List<CategoryAssociationProfile> GetCategoryProfiles() => _categoryProfiles;

    public CategoryAssociationProfile? GetCategoryProfile(string? categoryIdOrCode, string? categoryName = null)
        => _categoryProfiles.FirstOrDefault(p => p.Matches(categoryIdOrCode, categoryName));

    /// <summary>
    /// Inserts or replaces the profile for a category. Replaces any existing profile with the same
    /// code OR the same name, so a database-saved profile supersedes the built-in seed for that category.
    /// A profile with nothing configured removes the mapping.
    /// </summary>
    public void SaveCategoryProfile(CategoryAssociationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.CategoryCode)) return;

        var clean = profile.Clone();
        clean.CategoryCode = clean.CategoryCode.Trim();
        clean.CategoryName = (clean.CategoryName ?? string.Empty).Trim();
        clean.StaffIds = clean.StaffIds.Distinct().Where(id => _staffMembers.Any(s => s.Id == id)).ToList();
        clean.EquipmentIds = clean.EquipmentIds.Distinct().Where(id => _equipmentResources.Any(e => e.Id == id)).ToList();
        if (!_studioSpaces.Any(s => s.Id == clean.DefaultSpaceId)) clean.DefaultSpaceId = null;
        if (!EventServiceTypes.All.Contains(clean.DefaultServiceType ?? string.Empty)) clean.DefaultServiceType = null;
        clean.DefaultDurationHours = Math.Clamp(clean.DefaultDurationHours, 0.25m, 48m);
        clean.DefaultPrice = Math.Max(0m, clean.DefaultPrice);

        _categoryProfiles.RemoveAll(p => p.Matches(clean.CategoryCode, clean.CategoryName));
        if (clean.HasAny)
            _categoryProfiles.Add(clean);
    }

    public void RemoveCategoryProfile(string categoryCode)
    {
        if (string.IsNullOrWhiteSpace(categoryCode)) return;
        _categoryProfiles.RemoveAll(p => p.CategoryCode.Equals(categoryCode.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Merges profiles loaded from persistent storage into the in-memory set (idempotent).</summary>
    public void ApplyCategoryProfiles(IEnumerable<CategoryAssociationProfile> profiles)
    {
        foreach (var p in profiles)
            SaveCategoryProfile(p);
    }

    /// <summary>Staff who cannot take a new assignment in the window (inactive, on leave, or already on another event).</summary>
    public HashSet<string> GetBusyStaffIds(DateTime date, TimeSpan start, TimeSpan end, string? ignoreEventId = null)
    {
        var busy = new HashSet<string>();

        foreach (var m in _staffMembers.Where(m => !m.Status.Equals("Active", StringComparison.OrdinalIgnoreCase)))
            busy.Add(m.Id);

        foreach (var e in _eventBookings.Where(e => e.Id != ignoreEventId
                                                    && e.Date.Date == date.Date
                                                    && e.StartTime < end && e.EndTime > start))
            foreach (var id in e.AssignedStaffIds)
                busy.Add(id);

        foreach (var sh in _staffShifts.Where(sh => !sh.IsOpenShift
                                                    && sh.Date.Date == date.Date
                                                    && sh.StartTime < end && sh.EndTime > start
                                                    && (sh.ShiftType == "Sick Leave" || sh.ShiftType == "Unpaid Leave")))
            busy.Add(sh.StaffId!);

        return busy;
    }

    /// <summary>Equipment that is in maintenance or already reserved in the window.</summary>
    public HashSet<string> GetBusyEquipmentIds(DateTime date, TimeSpan start, TimeSpan end, string? ignoreEventId = null)
    {
        var busy = new HashSet<string>();

        foreach (var eq in _equipmentResources.Where(e => e.Status.Equals("In Maintenance", StringComparison.OrdinalIgnoreCase)))
            busy.Add(eq.Id);

        foreach (var r in _equipmentReservations.Where(r => (ignoreEventId == null || r.SourceEventId != ignoreEventId)
                                                            && r.Date.Date == date.Date
                                                            && r.StartTime < end && r.EndTime > start))
            busy.Add(r.EquipmentId);

        return busy;
    }

    /// <summary>The first assigned photographer (by role) or, failing that, the first assigned staff member.</summary>
    public string ResolveLeadStaffName(IEnumerable<string> staffIds, string fallback)
    {
        var members = staffIds
            .Select(id => _staffMembers.FirstOrDefault(s => s.Id == id))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();

        var lead = members.FirstOrDefault(s => s.Role.Contains("Photographer", StringComparison.OrdinalIgnoreCase))
                   ?? members.FirstOrDefault();

        return lead?.FullName ?? fallback;
    }

    /// <summary>
    /// Keeps equipment reservations in step with an event: removes the ones previously created for it
    /// and re-creates one per assigned equipment item, so double-booked gear shows up as a conflict.
    /// </summary>
    private void SyncEventAssociations(EventBookingModel booking)
    {
        _equipmentReservations.RemoveAll(r => r.SourceEventId == booking.Id);

        foreach (var eqId in booking.AssignedEquipmentIds.Distinct())
        {
            var eq = _equipmentResources.FirstOrDefault(e => e.Id == eqId);
            if (eq is null) continue;

            var note = string.IsNullOrWhiteSpace(booking.CategoryName)
                ? $"Auto-reserved for event '{booking.ClientName}'."
                : $"Auto-reserved for event '{booking.ClientName}' ({booking.CategoryName}).";

            _equipmentReservations.Add(new EquipmentReservationModel
            {
                EquipmentId = eq.Id,
                EquipmentName = eq.Name,
                Category = eq.Category,
                Date = booking.Date,
                StartTime = booking.StartTime,
                EndTime = booking.EndTime,
                ProjectOrClient = booking.ClientName,
                TechnicianInCharge = booking.AssignedPhotographer,
                Location = booking.StudioSpaceName,
                MaintenanceStatus = "Ready",
                Notes = note,
                SourceEventId = booking.Id
            });
        }

        RecalculateEquipmentConflicts();
    }

    private void SeedCategoryProfiles()
    {
        // Codes/names mirror the default categories on the Service Category page.
        // Anything an admin saves there replaces these (see SaveCategoryProfile).
        _categoryProfiles.AddRange(new[]
        {
            new CategoryAssociationProfile
            {
                CategoryCode = "CAT-PORT", CategoryName = "Portrait Photography",
                DefaultSpaceId = "studio-a", DefaultServiceType = "Full Studio Session",
                DefaultDurationHours = 2m, DefaultPrice = 350m,
                StaffIds = { "staff-01", "staff-05" },
                EquipmentIds = { "eq-01", "eq-06", "eq-03" }
            },
            new CategoryAssociationProfile
            {
                CategoryCode = "CAT-WEDD", CategoryName = "Wedding & Nuptials",
                DefaultSpaceId = "wedding-venue", DefaultServiceType = "Bridal Prep",
                DefaultDurationHours = 8m, DefaultPrice = 2400m,
                StaffIds = { "staff-01", "staff-02", "staff-05" },
                EquipmentIds = { "eq-01", "eq-05", "eq-04", "eq-02" }
            },
            new CategoryAssociationProfile
            {
                CategoryCode = "CAT-COMM", CategoryName = "Commercial & Branding",
                DefaultSpaceId = "studio-b", DefaultServiceType = "Commercial Campaign",
                DefaultDurationHours = 4m, DefaultPrice = 1200m,
                StaffIds = { "staff-06", "staff-03" },
                EquipmentIds = { "eq-01", "eq-05", "eq-03" }
            },
            new CategoryAssociationProfile
            {
                CategoryCode = "CAT-EVENT", CategoryName = "Event & Gala Coverage",
                DefaultSpaceId = "wedding-venue", DefaultServiceType = "Event Coverage",
                DefaultDurationHours = 5m, DefaultPrice = 1500m,
                StaffIds = { "staff-06", "staff-02" },
                EquipmentIds = { "eq-01", "eq-05", "eq-04" }
            },
            new CategoryAssociationProfile
            {
                CategoryCode = "CAT-AER", CategoryName = "Aerial & Drone Operations",
                DefaultSpaceId = "outdoor-shoot", DefaultServiceType = "Commercial Campaign",
                DefaultDurationHours = 3m, DefaultPrice = 900m,
                StaffIds = { "staff-02", "staff-03" },
                EquipmentIds = { "eq-02", "eq-07" }
            },
            new CategoryAssociationProfile
            {
                CategoryCode = "CAT-PROD", CategoryName = "Product Photography",
                DefaultSpaceId = "studio-b", DefaultServiceType = "Product Catalog",
                DefaultDurationHours = 3m, DefaultPrice = 600m,
                StaffIds = { "staff-06", "staff-03" },
                EquipmentIds = { "eq-01", "eq-05", "eq-03" }
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
=======
>>>>>>> 89cb0506c91bf3c01f732797627baaa00fea2957
    // SEED DATA INITIALIZERS
    // ─────────────────────────────────────────────────────────────────────────

    private void SeedStudioSpaces()
    {
        _studioSpaces.AddRange(new[]
        {
            new StudioSpaceResource
            {
                Id = "studio-a",
                Name = "Studio A (Main Cyclorama)",
                Description = "Infinite white cyc wall, 14ft clearance, overhead Profoto softbox grid",
                Dimensions = "1,200 sq ft",
                MaxCapacity = 20,
                BadgeColor = "bg-blue-50 text-blue-700 border-blue-200",
                IconType = "studio"
            },
            new StudioSpaceResource
            {
                Id = "studio-b",
                Name = "Studio B (Still Life & Product)",
                Description = "Motorized tabletop rig, high CRI continuous lighting, macro isolation bay",
                Dimensions = "850 sq ft",
                MaxCapacity = 10,
                BadgeColor = "bg-indigo-50 text-indigo-700 border-indigo-200",
                IconType = "commercial"
            },
            new StudioSpaceResource
            {
                Id = "studio-c",
                Name = "Studio C (Green Screen & Cinema)",
                Description = "Sound-dampened acoustic walls, full chroma green cyc, teleprompter suite",
                Dimensions = "1,450 sq ft",
                MaxCapacity = 25,
                BadgeColor = "bg-emerald-50 text-emerald-700 border-emerald-200",
                IconType = "cinema"
            },
            new StudioSpaceResource
            {
                Id = "outdoor-shoot",
                Name = "Outdoor & Field Production",
                Description = "Permitted urban architecture, waterfront daylight shoots, mobile generator support",
                Dimensions = "External Location",
                MaxCapacity = 15,
                BadgeColor = "bg-amber-50 text-amber-700 border-amber-200",
                IconType = "outdoor"
            },
            new StudioSpaceResource
            {
                Id = "wedding-venue",
                Name = "Wedding & Gala Venues",
                Description = "On-site sanctuary, cathedral halls, ballroom setups, bridal preparation lounges",
                Dimensions = "Grand Venue",
                MaxCapacity = 60,
                BadgeColor = "bg-purple-50 text-purple-700 border-purple-200",
                IconType = "venue"
            }
        });
    }

    private void SeedEventBookings()
    {
        // Anchor to start of current week (Monday)
        var today = DateTime.Today;
        int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
        var monday = today.AddDays(-1 * diff);

        _eventBookings.AddRange(new[]
        {
            // Monday
            new EventBookingModel
            {
                Id = "evt-101",
                StudioSpaceId = "studio-a",
                StudioSpaceName = "Studio A (Main Cyclorama)",
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
                Notes = "Autumn fashion editorial with 4 models and rapid wardrobe changes."
            },
            new EventBookingModel
            {
                Id = "evt-102",
                StudioSpaceId = "studio-b",
                StudioSpaceName = "Studio B (Still Life & Product)",
                ClientName = "Lumina Skincare",
                ClientEmail = "marketing@luminaskin.com",
                ClientPhone = "+1 (555) 345-6789",
                ServiceType = "Product Catalog",
                Date = monday,
                StartTime = new TimeSpan(14, 0, 0),
                EndTime = new TimeSpan(17, 30, 0),
                Status = "Confirmed",
                AssignedPhotographer = "Marcus Vance",
                EstimatedPrice = 650.00m,
                Notes = "Cosmetic bottles with reflective glass and water splash textures."
            },

            // Tuesday
            new EventBookingModel
            {
                Id = "evt-103",
                StudioSpaceId = "studio-c",
                StudioSpaceName = "Studio C (Green Screen & Cinema)",
                ClientName = "Apex Financial Tech",
                ClientEmail = "media@apexfintech.io",
                ClientPhone = "+1 (555) 456-7890",
                ServiceType = "Full Studio Session",
                Date = monday.AddDays(1),
                StartTime = new TimeSpan(10, 0, 0),
                EndTime = new TimeSpan(15, 0, 0),
                Status = "Confirmed",
                AssignedPhotographer = "Marcus Vance",
                EstimatedPrice = 900.00m,
                Notes = "CEO keynote recording with dynamic backdrop visual overlays."
            },
            new EventBookingModel
            {
                Id = "evt-104",
                StudioSpaceId = "outdoor-shoot",
                StudioSpaceName = "Outdoor & Field Production",
                ClientName = "Marcus & Sarah Jenkins",
                ClientEmail = "jenkins.sarah@gmail.com",
                ClientPhone = "+1 (555) 567-8901",
                ServiceType = "Express Headshot",
                Date = monday.AddDays(1),
                StartTime = new TimeSpan(15, 30, 0),
                EndTime = new TimeSpan(18, 0, 0),
                Status = "Pending",
                AssignedPhotographer = "Clara Oswald",
                EstimatedPrice = 450.00m,
                Notes = "Golden hour couple portrait session at city botanical garden."
            },

            // Wednesday (Intentional Conflict in Studio A to satisfy prompt requirement!)
            new EventBookingModel
            {
                Id = "evt-105",
                StudioSpaceId = "studio-a",
                StudioSpaceName = "Studio A (Main Cyclorama)",
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
                StudioSpaceId = "studio-a", // Intentional overlap with evt-105!
                StudioSpaceName = "Studio A (Main Cyclorama)",
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
                Notes = "Board of Directors corporate portrait session (Double-booked room)."
            },

            // Thursday
            new EventBookingModel
            {
                Id = "evt-107",
                StudioSpaceId = "studio-b",
                StudioSpaceName = "Studio B (Still Life & Product)",
                ClientName = "Nordic Roast Coffee",
                ClientEmail = "info@nordicroast.co",
                ClientPhone = "+1 (555) 890-1234",
                ServiceType = "Product Catalog",
                Date = monday.AddDays(3),
                StartTime = new TimeSpan(9, 30, 0),
                EndTime = new TimeSpan(13, 30, 0),
                Status = "Completed",
                AssignedPhotographer = "Sophia Lin",
                EstimatedPrice = 550.00m,
                Notes = "Packaging design lifestyle shoots with espresso machinery steam."
            },

            // Friday
            new EventBookingModel
            {
                Id = "evt-108",
                StudioSpaceId = "wedding-venue",
                StudioSpaceName = "Wedding & Gala Venues",
                ClientName = "Katherine & Daniel Vance",
                ClientEmail = "dan.vance@yahoo.com",
                ClientPhone = "+1 (555) 901-2345",
                ServiceType = "Bridal Prep",
                Date = monday.AddDays(4),
                StartTime = new TimeSpan(11, 0, 0),
                EndTime = new TimeSpan(19, 0, 0),
                Status = "Confirmed",
                AssignedPhotographer = "Elena Rostova",
                EstimatedPrice = 2800.00m,
                Notes = "Full day bridal preparation, vow exchange, and evening cocktail gala."
            },

            // Saturday
            new EventBookingModel
            {
                Id = "evt-109",
                StudioSpaceId = "studio-a",
                StudioSpaceName = "Studio A (Main Cyclorama)",
                ClientName = "Urban Beats Records",
                ClientEmail = "press@urbanbeats.com",
                ClientPhone = "+1 (555) 012-3456",
                ServiceType = "Full Studio Session",
                Date = monday.AddDays(5),
                StartTime = new TimeSpan(13, 0, 0),
                EndTime = new TimeSpan(18, 0, 0),
                Status = "Confirmed",
                AssignedPhotographer = "David Chen",
                EstimatedPrice = 750.00m,
                Notes = "Album cover photography and promotional press kits."
            }
        });
    }

    private void SeedStaffMembers()
    {
        _staffMembers.AddRange(new[]
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
            },
            new StaffMemberResource
            {
                Id = "staff-03",
                FullName = "Alex Rivera",
                StaffCode = "PA-003",
                Role = "Lighting Specialist",
                AvatarInitials = "AR",
                AvatarColor = "bg-amber-100 text-amber-700",
                TargetHoursPerWeek = 35,
                Status = "Active",
                Email = "alex.rivera@photoaevum.com",
                Phone = "+1 (555) 101-0003"
            },
            new StaffMemberResource
            {
                Id = "staff-04",
                FullName = "Sophia Lin",
                StaffCode = "PA-004",
                Role = "Master Retoucher",
                AvatarInitials = "SL",
                AvatarColor = "bg-purple-100 text-purple-700",
                TargetHoursPerWeek = 40,
                Status = "Active",
                Email = "sophia.lin@photoaevum.com",
                Phone = "+1 (555) 101-0004"
            },
            new StaffMemberResource
            {
                Id = "staff-05",
                FullName = "David Chen",
                StaffCode = "PA-005",
                Role = "Studio Assistant",
                AvatarInitials = "DC",
                AvatarColor = "bg-emerald-100 text-emerald-700",
                TargetHoursPerWeek = 30,
                Status = "Active",
                Email = "david.chen@photoaevum.com",
                Phone = "+1 (555) 101-0005"
            },
            new StaffMemberResource
            {
                Id = "staff-06",
                FullName = "Clara Oswald",
                StaffCode = "PA-006",
                Role = "Editorial Photographer",
                AvatarInitials = "CO",
                AvatarColor = "bg-cyan-100 text-cyan-700",
                TargetHoursPerWeek = 40,
                Status = "Active",
                Email = "clara.oswald@photoaevum.com",
                Phone = "+1 (555) 101-0006"
            }
        });
    }

    private void SeedStaffShifts()
    {
        var today = DateTime.Today;
        int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
        var monday = today.AddDays(-1 * diff);

        _staffShifts.AddRange(new[]
        {
            // OPEN SHIFTS (Pool row)
            new StaffShiftModel
            {
                Id = "shift-open-01",
                StaffId = null,
                StaffName = "Open Shift",
                Role = "Grip & Audio Rigging",
                Date = monday.AddDays(1), // Tuesday
                StartTime = new TimeSpan(10, 0, 0),
                EndTime = new TimeSpan(16, 0, 0),
                Location = "Studio C",
                ShiftType = "Full Day",
                Notes = "Second boom operator and lighting grip needed for commercial recording."
            },
            new StaffShiftModel
            {
                Id = "shift-open-02",
                StaffId = null,
                StaffName = "Open Shift",
                Role = "Second Shooter",
                Date = monday.AddDays(5), // Saturday
                StartTime = new TimeSpan(11, 0, 0),
                EndTime = new TimeSpan(18, 0, 0),
                Location = "Wedding Venue",
                ShiftType = "Full Day",
                Notes = "Second camera shooter for reception and guest candids."
            },

            // Elena Rostova
            new StaffShiftModel
            {
                Id = "shift-101",
                StaffId = "staff-01",
                StaffName = "Elena Rostova",
                Role = "Senior Photographer",
                Date = monday,
                StartTime = new TimeSpan(8, 30, 0),
                EndTime = new TimeSpan(14, 30, 0),
                Location = "Studio A",
                ShiftType = "Full Day",
                Notes = "Lead on Vogue editorial."
            },
            new StaffShiftModel
            {
                Id = "shift-102",
                StaffId = "staff-01",
                StaffName = "Elena Rostova",
                Role = "Senior Photographer",
                Date = monday.AddDays(1),
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(17, 0, 0),
                Location = "Studio A",
                ShiftType = "Full Day"
            },
            // Intentional shift conflict for Elena on Thursday!
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
                StaffId = "staff-01", // Overlap with shift-103!
                StaffName = "Elena Rostova",
                Role = "Senior Photographer",
                Date = monday.AddDays(3),
                StartTime = new TimeSpan(12, 30, 0),
                EndTime = new TimeSpan(17, 30, 0),
                Location = "Outdoor Shoot",
                ShiftType = "Half Day",
                Notes = "Assigned to outdoor sunset bridal preview (Double rostered)."
            },
            new StaffShiftModel
            {
                Id = "shift-105",
                StaffId = "staff-01",
                StaffName = "Elena Rostova",
                Role = "Senior Photographer",
                Date = monday.AddDays(4),
                StartTime = new TimeSpan(10, 0, 0),
                EndTime = new TimeSpan(19, 0, 0),
                Location = "Wedding Venue",
                ShiftType = "Overtime",
                Notes = "Katherine Vance wedding day shoot."
            },

            // Marcus Vance
            new StaffShiftModel
            {
                Id = "shift-201",
                StaffId = "staff-02",
                StaffName = "Marcus Vance",
                Role = "Lead Videographer",
                Date = monday,
                StartTime = new TimeSpan(13, 30, 0),
                EndTime = new TimeSpan(18, 0, 0),
                Location = "Studio B",
                ShiftType = "Half Day"
            },
            new StaffShiftModel
            {
                Id = "shift-202",
                StaffId = "staff-02",
                StaffName = "Marcus Vance",
                Role = "Lead Videographer",
                Date = monday.AddDays(1),
                StartTime = new TimeSpan(9, 30, 0),
                EndTime = new TimeSpan(16, 30, 0),
                Location = "Studio C",
                ShiftType = "Full Day"
            },
            new StaffShiftModel
            {
                Id = "shift-203",
                StaffId = "staff-02",
                StaffName = "Marcus Vance",
                Role = "Lead Videographer",
                Date = monday.AddDays(2),
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(17, 0, 0),
                Location = "Edit Bay 1",
                ShiftType = "Full Day",
                Notes = "Color grading and raw cinema assembly."
            },
            new StaffShiftModel
            {
                Id = "shift-204",
                StaffId = "staff-02",
                StaffName = "Marcus Vance",
                Role = "Lead Videographer",
                Date = monday.AddDays(4),
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(17, 0, 0),
                Location = "Studio C",
                ShiftType = "Full Day"
            },

            // Alex Rivera (Sick Leave example)
            new StaffShiftModel
            {
                Id = "shift-301",
                StaffId = "staff-03",
                StaffName = "Alex Rivera",
                Role = "Lighting Specialist",
                Date = monday,
                StartTime = new TimeSpan(8, 0, 0),
                EndTime = new TimeSpan(16, 0, 0),
                Location = "Studio A",
                ShiftType = "Full Day"
            },
            new StaffShiftModel
            {
                Id = "shift-302",
                StaffId = "staff-03",
                StaffName = "Alex Rivera",
                Role = "Lighting Specialist",
                Date = monday.AddDays(2), // Wednesday
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(17, 0, 0),
                Location = "Studio A",
                ShiftType = "Sick Leave",
                Notes = "Medical certificate filed with HR."
            },
            new StaffShiftModel
            {
                Id = "shift-303",
                StaffId = "staff-03",
                StaffName = "Alex Rivera",
                Role = "Lighting Specialist",
                Date = monday.AddDays(4),
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(17, 0, 0),
                Location = "Studio B",
                ShiftType = "Full Day"
            },

            // Sophia Lin
            new StaffShiftModel
            {
                Id = "shift-401",
                StaffId = "staff-04",
                StaffName = "Sophia Lin",
                Role = "Master Retoucher",
                Date = monday,
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(17, 0, 0),
                Location = "Edit Bay 2",
                ShiftType = "Full Day"
            },
            new StaffShiftModel
            {
                Id = "shift-402",
                StaffId = "staff-04",
                StaffName = "Sophia Lin",
                Role = "Master Retoucher",
                Date = monday.AddDays(3),
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(17, 0, 0),
                Location = "Studio B",
                ShiftType = "Full Day"
            },

            // David Chen
            new StaffShiftModel
            {
                Id = "shift-501",
                StaffId = "staff-05",
                StaffName = "David Chen",
                Role = "Studio Assistant",
                Date = monday.AddDays(1),
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(14, 0, 0),
                Location = "Studio C",
                ShiftType = "Half Day"
            },
            new StaffShiftModel
            {
                Id = "shift-502",
                StaffId = "staff-05",
                StaffName = "David Chen",
                Role = "Studio Assistant",
                Date = monday.AddDays(5),
                StartTime = new TimeSpan(12, 0, 0),
                EndTime = new TimeSpan(19, 0, 0),
                Location = "Studio A",
                ShiftType = "Full Day"
            },

            // Clara Oswald
            new StaffShiftModel
            {
                Id = "shift-601",
                StaffId = "staff-06",
                StaffName = "Clara Oswald",
                Role = "Editorial Photographer",
                Date = monday.AddDays(1),
                StartTime = new TimeSpan(15, 0, 0),
                EndTime = new TimeSpan(19, 0, 0),
                Location = "Outdoor Shoot",
                ShiftType = "Half Day"
            },
            new StaffShiftModel
            {
                Id = "shift-602",
                StaffId = "staff-06",
                StaffName = "Clara Oswald",
                Role = "Editorial Photographer",
                Date = monday.AddDays(3),
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(17, 0, 0),
                Location = "Studio A",
                ShiftType = "Full Day"
            }
        });
    }

    private void SeedEquipment()
    {
        _equipmentResources.AddRange(new[]
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
                Id = "eq-02",
                Name = "Sony FX3 Full-Frame Cinema (#2)",
                SerialNumber = "SN-SNY-44901",
                Category = "Cinema Rigs",
                Status = "Ready",
                StorageBay = "Locker A-03",
                BadgeColor = "bg-indigo-50 text-indigo-700 border-indigo-200"
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
            },
            new EquipmentItemResource
            {
                Id = "eq-04",
                Name = "Profoto B10X Duo Location Kit",
                SerialNumber = "SN-PRF-30918",
                Category = "Strobe Lighting",
                Status = "In-Use",
                StorageBay = "Mobile Pelican 04",
                BadgeColor = "bg-purple-50 text-purple-700 border-purple-200"
            },
            new EquipmentItemResource
            {
                Id = "eq-05",
                Name = "Sony FE 24-70mm f/2.8 GM II",
                SerialNumber = "SN-LNS-99120",
                Category = "Lenses",
                Status = "Ready",
                StorageBay = "Optics Safe #1",
                BadgeColor = "bg-cyan-50 text-cyan-700 border-cyan-200"
            },
            new EquipmentItemResource
            {
                Id = "eq-06",
                Name = "Canon RF 85mm f/1.2L USM",
                SerialNumber = "SN-LNS-77341",
                Category = "Lenses",
                Status = "Ready",
                StorageBay = "Optics Safe #2",
                BadgeColor = "bg-cyan-50 text-cyan-700 border-cyan-200"
            },
            new EquipmentItemResource
            {
                Id = "eq-07",
                Name = "DJI Ronin RS3 Pro Gimbal",
                SerialNumber = "SN-DJI-55238",
                Category = "Grip & Audio",
                Status = "In-Maintenance",
                StorageBay = "Tech Bench 3",
                BadgeColor = "bg-rose-50 text-rose-700 border-rose-200"
            },
            new EquipmentItemResource
            {
                Id = "eq-08",
                Name = "Sennheiser MKH 416 Boom & Mic Kit",
                SerialNumber = "SN-SNH-12095",
                Category = "Grip & Audio",
                Status = "Ready",
                StorageBay = "Audio Bag 01",
                BadgeColor = "bg-emerald-50 text-emerald-700 border-emerald-200"
            }
        });
    }

    private void SeedEquipmentReservations()
    {
        var today = DateTime.Today;
        int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
        var monday = today.AddDays(-1 * diff);

        _equipmentReservations.AddRange(new[]
        {
            // Monday
            new EquipmentReservationModel
            {
                Id = "res-101",
                EquipmentId = "eq-01",
                EquipmentName = "Canon EOS R5 Mark II (#1)",
                Category = "Camera Bodies",
                Date = monday,
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(13, 0, 0),
                ProjectOrClient = "Vogue Scandinavia Editorial",
                TechnicianInCharge = "Elena Rostova",
                Location = "Studio A",
                MaintenanceStatus = "Ready",
                Notes = "Battery grip and 2x 512GB CFexpress Type B cards checked out."
            },
            new EquipmentReservationModel
            {
                Id = "res-102",
                EquipmentId = "eq-03",
                EquipmentName = "Godox AD600 Pro Strobe Kit A",
                Category = "Strobe Lighting",
                Date = monday,
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(13, 0, 0),
                ProjectOrClient = "Vogue Scandinavia Editorial",
                TechnicianInCharge = "Alex Rivera",
                Location = "Studio A",
                MaintenanceStatus = "Ready"
            },
            new EquipmentReservationModel
            {
                Id = "res-103",
                EquipmentId = "eq-05",
                EquipmentName = "Sony FE 24-70mm f/2.8 GM II",
                Category = "Lenses",
                Date = monday,
                StartTime = new TimeSpan(14, 0, 0),
                EndTime = new TimeSpan(18, 0, 0),
                ProjectOrClient = "Lumina Skincare Catalog",
                TechnicianInCharge = "Marcus Vance",
                Location = "Studio B",
                MaintenanceStatus = "Ready"
            },

            // Tuesday
            new EquipmentReservationModel
            {
                Id = "res-201",
                EquipmentId = "eq-02",
                EquipmentName = "Sony FX3 Full-Frame Cinema (#2)",
                Category = "Cinema Rigs",
                Date = monday.AddDays(1),
                StartTime = new TimeSpan(9, 30, 0),
                EndTime = new TimeSpan(15, 30, 0),
                ProjectOrClient = "Apex Financial Tech Keynote",
                TechnicianInCharge = "Marcus Vance",
                Location = "Studio C",
                MaintenanceStatus = "Ready",
                Notes = "HDMI raw output monitor feed and cage with top handle."
            },
            new EquipmentReservationModel
            {
                Id = "res-202",
                EquipmentId = "eq-08",
                EquipmentName = "Sennheiser MKH 416 Boom & Mic Kit",
                Category = "Grip & Audio",
                Date = monday.AddDays(1),
                StartTime = new TimeSpan(9, 30, 0),
                EndTime = new TimeSpan(15, 30, 0),
                ProjectOrClient = "Apex Financial Tech Keynote",
                TechnicianInCharge = "David Chen",
                Location = "Studio C",
                MaintenanceStatus = "Ready"
            },

            // Wednesday
            new EquipmentReservationModel
            {
                Id = "res-301",
                EquipmentId = "eq-06",
                EquipmentName = "Canon RF 85mm f/1.2L USM",
                Category = "Lenses",
                Date = monday.AddDays(2),
                StartTime = new TimeSpan(10, 0, 0),
                EndTime = new TimeSpan(15, 0, 0),
                ProjectOrClient = "Horizon Luxury Apparel",
                TechnicianInCharge = "Elena Rostova",
                Location = "Studio A",
                MaintenanceStatus = "Ready"
            },

            // Thursday (Intentional double booking conflict on Godox AD600!)
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
                EquipmentId = "eq-03", // Overlap with res-401!
                EquipmentName = "Godox AD600 Pro Strobe Kit A",
                Category = "Strobe Lighting",
                Date = monday.AddDays(3),
                StartTime = new TimeSpan(12, 0, 0),
                EndTime = new TimeSpan(17, 0, 0),
                ProjectOrClient = "Commercial Lookbook",
                TechnicianInCharge = "Clara Oswald",
                Location = "Studio A",
                MaintenanceStatus = "Ready",
                Notes = "Requested for fast recycling flashes on white cyclorama (Double-booked)."
            },

            // Friday
            new EquipmentReservationModel
            {
                Id = "res-501",
                EquipmentId = "eq-04",
                EquipmentName = "Profoto B10X Duo Location Kit",
                Category = "Strobe Lighting",
                Date = monday.AddDays(4),
                StartTime = new TimeSpan(10, 0, 0),
                EndTime = new TimeSpan(19, 0, 0),
                ProjectOrClient = "Katherine & Daniel Vance Wedding",
                TechnicianInCharge = "Elena Rostova",
                Location = "Wedding Venue",
                MaintenanceStatus = "In-Use",
                Notes = "Packed into rolling pelican case with 4 high-capacity lithium batteries."
            },

            // Maintenance tag item on Saturday
            new EquipmentReservationModel
            {
                Id = "res-601",
                EquipmentId = "eq-07",
                EquipmentName = "DJI Ronin RS3 Pro Gimbal",
                Category = "Grip & Audio",
                Date = monday.AddDays(5),
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(18, 0, 0),
                ProjectOrClient = "Scheduled Firmware & Motor Calibration",
                TechnicianInCharge = "David Chen",
                Location = "Tech Bench 3",
                MaintenanceStatus = "In-Maintenance",
                Notes = "Pitch axis firmware recalibration and balancing weight check."
            }
        });
    }
}
