using System.Text.RegularExpressions;
using System.Net.Http.Json;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

/// <summary>
/// Singleton service managing synchronized state and data operations for Equipment,
/// Equipment Types, EventEquipment Dispatches, and Maintenance History Logs.
/// Faithfully mirrors SQL Server stored procedures and table integrity.
/// </summary>
public sealed class EquipmentService : IEquipmentService
{
    private readonly object _lock = new();

    // In-memory relational tables corresponding to SQL Server dbo tables
    private readonly List<EquipmentTypeModel> _types = new();
    private readonly List<EquipmentItemModel> _equipments = new();
    private readonly List<EventAppointmentModel> _events = new();
    private readonly List<DispatchItemModel> _dispatches = new();
    private readonly List<MaintenanceLogModel> _maintenanceLogs = new();

    private int _nextEventEquipmentId = 1001;
    private long _nextHistoryLogId = 5001;

    public event Action? OnChange;

    private readonly IHttpClientFactory? _httpClientFactory;

    public EquipmentService(IHttpClientFactory? httpClientFactory = null)
    {
        _httpClientFactory = httpClientFactory;
        SeedDatabaseEntities();
    }

    public void NotifyStateChanged()
    {
        OnChange?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. EQUIPMENT CATALOG & TYPES (dbo.Equipment & dbo.EquipmentType)
    // ─────────────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<EquipmentItemModel>> GetEquipmentsAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            // Sync with current type names and active dispatches
            foreach (var eq in _equipments)
            {
                var type = _types.FirstOrDefault(t => t.EquipmentTypeID.Equals(eq.EquipmentTypeID, StringComparison.OrdinalIgnoreCase));
                if (type != null) eq.EquipmentTypeName = type.EquipmentTypeName;

                var activeDispatch = _dispatches.FirstOrDefault(d => 
                    d.EquipmentID.Equals(eq.EquipmentID, StringComparison.OrdinalIgnoreCase) && 
                    d.DispatchStatus.Equals("Dispatched", StringComparison.OrdinalIgnoreCase));
                
                if (activeDispatch != null)
                {
                    eq.CurrentEventId = activeDispatch.EventAppointmentID;
                    eq.CurrentEventName = activeDispatch.EventName;
                }
                else
                {
                    eq.CurrentEventId = null;
                    eq.CurrentEventName = null;
                }
            }

            return Task.FromResult<IReadOnlyList<EquipmentItemModel>>(_equipments.OrderBy(e => e.EquipmentID).ToList());
        }
    }

    public Task<EquipmentItemModel?> GetEquipmentByIdAsync(string equipmentId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var item = _equipments.FirstOrDefault(e => e.EquipmentID.Equals(equipmentId, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(item);
        }
    }

    public Task<IReadOnlyList<EquipmentTypeModel>> GetEquipmentTypesAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            // Recalculate associated items and breakdown counts
            foreach (var t in _types)
            {
                var itemsForType = _equipments.Where(e => e.EquipmentTypeID.Equals(t.EquipmentTypeID, StringComparison.OrdinalIgnoreCase)).ToList();
                t.AssociatedItemsCount = itemsForType.Count;
                t.AvailableCount = itemsForType.Count(e => e.IsAvailable);
                t.MaintenanceCount = itemsForType.Count(e => e.IsMaintenance);
            }

            return Task.FromResult<IReadOnlyList<EquipmentTypeModel>>(_types.OrderBy(t => t.EquipmentTypeName).ToList());
        }
    }


    public Task<string> GetNextEquipmentIdPreviewAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            int maxNum = 0;
            foreach (var eq in _equipments)
            {
                var match = Regex.Match(eq.EquipmentID, @"\d+");
                if (match.Success && int.TryParse(match.Value, out int n) && n > maxNum)
                    maxNum = n;
            }
            return Task.FromResult($"EQP-{(maxNum + 1):D4}");
        }
    }

    public Task<(bool Success, string? Error, EquipmentItemModel? CreatedItem)> CreateEquipmentAsync(
        string name,
        string? description,
        string typeId,
        string? serialNo,
        string? imagePath,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Task.FromResult((false, (string?)"Equipment name cannot be empty.", (EquipmentItemModel?)null));

            var type = _types.FirstOrDefault(t => t.EquipmentTypeID.Equals(typeId, StringComparison.OrdinalIgnoreCase));
            if (type == null)
            {
                type = new EquipmentTypeModel
                {
                    EquipmentTypeID = typeId.Trim(),
                    EquipmentTypeName = typeId.Trim(),
                    ColorTag = "Blue",
                    Status = "Active"
                };
                _types.Add(type);
            }

            if (!string.IsNullOrWhiteSpace(serialNo) && _equipments.Any(e => !string.IsNullOrEmpty(e.SerialNo) && e.SerialNo.Equals(serialNo.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult((false, (string?)$"An equipment item with Serial Number '{serialNo}' already exists in the registry.", (EquipmentItemModel?)null));
            }

            int maxNum = 0;
            foreach (var eq in _equipments)
            {
                var match = Regex.Match(eq.EquipmentID, @"\d+");
                if (match.Success && int.TryParse(match.Value, out int n) && n > maxNum)
                    maxNum = n;
            }
            var generatedId = $"EQP-{(maxNum + 1):D4}";

            var newItem = new EquipmentItemModel
            {
                EquipmentID = generatedId,
                EquipmentName = name.Trim(),
                EquipmentDescription = description?.Trim(),
                EquipmentTypeID = type.EquipmentTypeID,
                EquipmentTypeName = type.EquipmentTypeName,
                EquipmentStatus = "Available",
                SerialNo = string.IsNullOrWhiteSpace(serialNo) ? null : serialNo.Trim(),
                ImagePath = imagePath
            };

            _equipments.Insert(0, newItem);

            _maintenanceLogs.Insert(0, new MaintenanceLogModel
            {
                HistoryLogID = _nextHistoryLogId++,
                ActionType = "INSERT",
                EntityName = "Equipment",
                EntityID = newItem.EquipmentID,
                EquipmentName = newItem.EquipmentName,
                SerialNo = newItem.SerialNo,
                EquipmentStatus = "Available",
                ActionDetails = $"Equipment registered via wizard with custom ID {newItem.EquipmentID} under category '{type.EquipmentTypeName}'.",
                ChangedByType = "Admin",
                ChangedByAdminID = "ADM-001",
                LoggedAt = DateTime.UtcNow
            });

            NotifyStateChanged();
            return Task.FromResult((true, (string?)null, (EquipmentItemModel?)newItem));
        }
    }

    /// <summary>
    /// Executes the logic of dbo.sp_AddEquipment:
    /// Inserts into dbo.Equipment (EquipmentID, EquipmentName, EquipmentTypeID, EquipmentStatus, SerialNo)
    /// Values (@EquipmentID, @EquipmentName, @EquipmentTypeID, 'Available', @SerialNo)
    /// </summary>
    public Task<(bool Success, string? Error)> AddEquipmentAsync(string name, string typeId, string? serialNo, string? equipmentId = null, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Task.FromResult((false, (string?)"Equipment name cannot be empty."));

            var type = _types.FirstOrDefault(t => t.EquipmentTypeID.Equals(typeId, StringComparison.OrdinalIgnoreCase));
            if (type == null)
                return Task.FromResult((false, (string?)$"Invalid EquipmentTypeID '{typeId}'."));

            // Check duplicate serial number if provided (matching UX_Equipment_SerialNo unique index)
            if (!string.IsNullOrWhiteSpace(serialNo) && _equipments.Any(e => !string.IsNullOrEmpty(e.SerialNo) && e.SerialNo.Equals(serialNo.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult((false, (string?)$"An equipment item with Serial Number '{serialNo}' already exists in the registry."));
            }

            var generatedId = equipmentId;
            if (string.IsNullOrWhiteSpace(generatedId))
            {
                int maxNum = 0;
                foreach (var eq in _equipments)
                {
                    var match = Regex.Match(eq.EquipmentID, @"\d+");
                    if (match.Success && int.TryParse(match.Value, out int n) && n > maxNum)
                        maxNum = n;
                }
                generatedId = $"EQ-{(maxNum + 1):D3}";
            }

            var newItem = new EquipmentItemModel
            {
                EquipmentID = generatedId,
                EquipmentName = name.Trim(),
                EquipmentTypeID = type.EquipmentTypeID,
                EquipmentTypeName = type.EquipmentTypeName,
                EquipmentStatus = "Available",
                SerialNo = string.IsNullOrWhiteSpace(serialNo) ? null : serialNo.Trim()
            };

            _equipments.Add(newItem);

            // Audit log in dbo.HistoryLog
            _maintenanceLogs.Insert(0, new MaintenanceLogModel
            {
                HistoryLogID = _nextHistoryLogId++,
                ActionType = "INSERT",
                EntityName = "Equipment",
                EntityID = newItem.EquipmentID,
                EquipmentName = newItem.EquipmentName,
                SerialNo = newItem.SerialNo,
                EquipmentStatus = "Available",
                ActionDetails = $"Equipment added to catalog via sp_AddEquipment under category '{type.EquipmentTypeName}'.",
                ChangedByType = "Admin",
                ChangedByAdminID = "ADM-001",
                LoggedAt = DateTime.UtcNow
            });

            NotifyStateChanged();
            return Task.FromResult((true, (string?)null));
        }
    }

    public async Task<string> GetNextEquipmentTypeIdAsync(CancellationToken ct = default)
    {
        if (_httpClientFactory != null)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("AevumApi");
                var response = await client.GetFromJsonAsync<GeneratedIdResponseModel>("api/equipment-types/next-id", ct);
                if (!string.IsNullOrWhiteSpace(response?.NextId))
                {
                    return response.NextId;
                }
            }
            catch
            {
                // Fall back to in-memory sequence calculation below
            }
        }

        lock (_lock)
        {
            int maxId = 0;
            foreach (var t in _types)
            {
                if (t.EquipmentTypeID.StartsWith("ETYP-", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(t.EquipmentTypeID[5..], out var parsed) && parsed > maxId)
                {
                    maxId = parsed;
                }
            }
            return $"ETYP-{(maxId + 1):D4}";
        }
    }

    public async Task<(bool Success, string? Error)> AddEquipmentTypeAsync(string typeId, string typeName, string colorTag = "Blue", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(typeId) || string.IsNullOrWhiteSpace(typeName))
            return (false, "Category ID and Name are required.");

        var cleanId = typeId.Trim().ToUpperInvariant();
        var cleanName = typeName.Trim();

        if (_httpClientFactory != null)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("AevumApi.Authorized");
                var response = await client.PostAsJsonAsync("api/equipment-types", new
                {
                    EquipmentTypeId = cleanId,
                    EquipmentTypeName = cleanName
                }, ct);

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                    {
                        var err = await response.Content.ReadFromJsonAsync<ApiErrorMessageModel>(cancellationToken: ct);
                        return (false, err?.Message ?? "An Equipment Type with this name or ID already exists.");
                    }
                    return (false, $"API Error: {response.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EquipmentService] API sync note: {ex.Message}");
            }
        }

        lock (_lock)
        {
            if (_types.Any(t => t.EquipmentTypeID.Equals(cleanId, StringComparison.OrdinalIgnoreCase)))
                return (false, $"Equipment Type '{cleanId}' already exists.");

            var newType = new EquipmentTypeModel
            {
                EquipmentTypeID = cleanId,
                EquipmentTypeName = cleanName,
                ColorTag = colorTag,
                Status = "Active",
                AssociatedItemsCount = 0,
                AvailableCount = 0,
                MaintenanceCount = 0
            };

            _types.Add(newType);

            _maintenanceLogs.Insert(0, new MaintenanceLogModel
            {
                HistoryLogID = _nextHistoryLogId++,
                ActionType = "INSERT",
                EntityName = "EquipmentType",
                EntityID = newType.EquipmentTypeID,
                EquipmentName = newType.EquipmentTypeName,
                EquipmentStatus = "Active",
                ActionDetails = $"New Equipment Type '{newType.EquipmentTypeName}' registered in master catalog.",
                ChangedByType = "Admin",
                ChangedByAdminID = "ADM-001",
                LoggedAt = DateTime.UtcNow
            });

            NotifyStateChanged();
            return (true, null);
        }
    }

    public Task<(bool Success, string? Error)> UpdateEquipmentStatusAsync(string equipmentId, string newStatus, string details, string actorId, string actorType = "Admin", CancellationToken ct = default)
    {
        lock (_lock)
        {
            var eq = _equipments.FirstOrDefault(e => e.EquipmentID.Equals(equipmentId, StringComparison.OrdinalIgnoreCase));
            if (eq == null)
                return Task.FromResult((false, (string?)$"Equipment '{equipmentId}' was not found."));

            string oldStatus = eq.EquipmentStatus;
            eq.EquipmentStatus = newStatus;

            // Audit log in dbo.HistoryLog
            _maintenanceLogs.Insert(0, new MaintenanceLogModel
            {
                HistoryLogID = _nextHistoryLogId++,
                ActionType = "STATUS_CHANGE",
                EntityName = "Equipment",
                EntityID = eq.EquipmentID,
                EquipmentName = eq.EquipmentName,
                SerialNo = eq.SerialNo,
                EquipmentStatus = newStatus,
                ActionDetails = $"Status changed from {oldStatus} to {newStatus}. Details: {details}",
                ChangedByType = actorType,
                ChangedByAdminID = actorType == "Admin" ? actorId : null,
                ChangedByEmployeeID = actorType == "Employee" ? actorId : null,
                LoggedAt = DateTime.UtcNow
            });

            NotifyStateChanged();
            return Task.FromResult((true, (string?)null));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. DISPATCH & CHECKOUT (dbo.EventEquipment & dbo.EventAppointment)
    // ─────────────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<DispatchItemModel>> GetDispatchesAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<DispatchItemModel>>(_dispatches.OrderByDescending(d => d.DispatchedAt).ToList());
        }
    }

    public Task<IReadOnlyList<EventAppointmentModel>> GetUpcomingEventsAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<EventAppointmentModel>>(_events.OrderBy(e => e.EventDate).ThenBy(e => e.EventTime).ToList());
        }
    }

    /// <summary>
    /// Executes the logic of dbo.sp_AssignEquipmentToEvent:
    /// Assigns equipment to an event appointment.
    /// Checks out the gear, preventing duplicate assignments and blocking gear under Maintenance.
    /// </summary>
    public Task<(bool Success, string? Error)> AssignEquipmentToEventAsync(string eventId, string equipmentId, string actorId = "ADM-001", CancellationToken ct = default)
    {
        lock (_lock)
        {
            var evt = _events.FirstOrDefault(e => e.EventAppointmentID.Equals(eventId, StringComparison.OrdinalIgnoreCase));
            if (evt == null)
                return Task.FromResult((false, (string?)$"Event appointment '{eventId}' not found."));

            var eq = _equipments.FirstOrDefault(e => e.EquipmentID.Equals(equipmentId, StringComparison.OrdinalIgnoreCase));
            if (eq == null)
                return Task.FromResult((false, (string?)$"Equipment '{equipmentId}' not found."));

            // STATUS SYNCHRONICITY CHECK: Block gear under Maintenance or Retired!
            if (eq.IsMaintenance)
                return Task.FromResult((false, (string?)$"Cannot dispatch '{eq.EquipmentName}' ({eq.EquipmentID}). It is currently flagged in MAINTENANCE."));

            if (eq.IsRetired)
                return Task.FromResult((false, (string?)$"Cannot dispatch '{eq.EquipmentName}'. It has been retired from the active studio inventory."));

            // Check if already assigned to this event
            var existing = _dispatches.FirstOrDefault(d => 
                d.EventAppointmentID.Equals(eventId, StringComparison.OrdinalIgnoreCase) && 
                d.EquipmentID.Equals(equipmentId, StringComparison.OrdinalIgnoreCase) &&
                !d.DispatchStatus.Equals("Returned", StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                return Task.FromResult((false, (string?)$"Equipment '{eq.EquipmentName}' is already assigned to this event."));

            // Determine if dispatch is today/active or scheduled for later
            bool isToday = evt.EventDate.Date <= DateTime.Today;
            string status = isToday ? "Dispatched" : "Scheduled";

            var dispatch = new DispatchItemModel
            {
                EventEquipmentID = _nextEventEquipmentId++,
                EventAppointmentID = evt.EventAppointmentID,
                EventName = evt.EventName,
                EventDate = evt.EventDate,
                EventTime = evt.EventTime,
                CustomerID = evt.CustomerID,
                CustomerName = evt.CustomerName,
                EquipmentID = eq.EquipmentID,
                EquipmentName = eq.EquipmentName,
                SerialNo = eq.SerialNo,
                EquipmentTypeName = eq.EquipmentTypeName,
                DispatchStatus = status,
                DispatchedAt = DateTime.UtcNow,
                VerifiedBy = null,
                Notes = $"Checked out for {evt.EventName} by {actorId} via sp_AssignEquipmentToEvent"
            };

            _dispatches.Insert(0, dispatch);

            if (isToday)
            {
                eq.CurrentEventId = evt.EventAppointmentID;
                eq.CurrentEventName = evt.EventName;
            }

            // Record action in history log
            _maintenanceLogs.Insert(0, new MaintenanceLogModel
            {
                HistoryLogID = _nextHistoryLogId++,
                ActionType = "UPDATE",
                EntityName = "Equipment",
                EntityID = eq.EquipmentID,
                EquipmentName = eq.EquipmentName,
                SerialNo = eq.SerialNo,
                EquipmentStatus = eq.EquipmentStatus,
                ActionDetails = $"Dispatched gear for event '{evt.EventName}' (Appt ID: {evt.EventAppointmentID}).",
                ChangedByType = "Admin",
                ChangedByAdminID = actorId,
                LoggedAt = DateTime.UtcNow
            });

            NotifyStateChanged();
            return Task.FromResult((true, (string?)null));
        }
    }

    public async Task<(bool Success, string? Error)> AssignMultipleEquipmentToEventAsync(string eventId, IEnumerable<string> equipmentIds, string actorId = "ADM-001", CancellationToken ct = default)
    {
        foreach (var id in equipmentIds)
        {
            var (success, error) = await AssignEquipmentToEventAsync(eventId, id, actorId, ct);
            if (!success) return (false, error);
        }
        return (true, null);
    }

    /// <summary>
    /// Verifies gear return from an on-location or studio shoot.
    /// Updates return timestamp, sets status to 'Returned', and either returns equipment to 'Available'
    /// or, if flagged, transitions it immediately to 'Maintenance'.
    /// </summary>
    public Task<(bool Success, string? Error)> VerifyReturnAsync(int eventEquipmentId, string notes, string actorId = "ADM-001", bool flagMaintenance = false, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var dispatch = _dispatches.FirstOrDefault(d => d.EventEquipmentID == eventEquipmentId);
            if (dispatch == null)
                return Task.FromResult((false, (string?)$"Dispatch assignment ID #{eventEquipmentId} not found."));

            dispatch.DispatchStatus = "Returned";
            dispatch.ReturnedAt = DateTime.UtcNow;
            dispatch.VerifiedBy = actorId;
            if (!string.IsNullOrWhiteSpace(notes))
                dispatch.Notes = (dispatch.Notes + " | Return: " + notes).Trim();

            var eq = _equipments.FirstOrDefault(e => e.EquipmentID.Equals(dispatch.EquipmentID, StringComparison.OrdinalIgnoreCase));
            if (eq != null)
            {
                eq.CurrentEventId = null;
                eq.CurrentEventName = null;

                if (flagMaintenance)
                {
                    eq.EquipmentStatus = "Maintenance";

                    _maintenanceLogs.Insert(0, new MaintenanceLogModel
                    {
                        HistoryLogID = _nextHistoryLogId++,
                        ActionType = "STATUS_CHANGE",
                        EntityName = "Equipment",
                        EntityID = eq.EquipmentID,
                        EquipmentName = eq.EquipmentName,
                        SerialNo = eq.SerialNo,
                        EquipmentStatus = "Maintenance",
                        ActionDetails = $"FLAGGED FOR MAINTENANCE upon return from '{dispatch.EventName}'. Inspection notes: {notes}",
                        ChangedByType = "Admin",
                        ChangedByAdminID = actorId,
                        LoggedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    eq.EquipmentStatus = "Available";

                    _maintenanceLogs.Insert(0, new MaintenanceLogModel
                    {
                        HistoryLogID = _nextHistoryLogId++,
                        ActionType = "UPDATE",
                        EntityName = "Equipment",
                        EntityID = eq.EquipmentID,
                        EquipmentName = eq.EquipmentName,
                        SerialNo = eq.SerialNo,
                        EquipmentStatus = "Available",
                        ActionDetails = $"Gear return verified in good order from '{dispatch.EventName}' by {actorId}.",
                        ChangedByType = "Admin",
                        ChangedByAdminID = actorId,
                        LoggedAt = DateTime.UtcNow
                    });
                }
            }

            NotifyStateChanged();
            return Task.FromResult((true, (string?)null));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. MAINTENANCE & SERVICING (dbo.HistoryLog & dbo.Equipment)
    // ─────────────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<MaintenanceLogModel>> GetMaintenanceLogsAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<MaintenanceLogModel>>(_maintenanceLogs.OrderByDescending(m => m.LoggedAt).ToList());
        }
    }

    public Task<(bool Success, string? Error)> LogMaintenanceAsync(string equipmentId, string serviceType, string issueDetails, string actorId, bool setUnderMaintenance = true, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var eq = _equipments.FirstOrDefault(e => e.EquipmentID.Equals(equipmentId, StringComparison.OrdinalIgnoreCase));
            if (eq == null)
                return Task.FromResult((false, (string?)$"Equipment '{equipmentId}' not found."));

            if (setUnderMaintenance)
            {
                eq.EquipmentStatus = "Maintenance";
                // Clear any active dispatch lock
                eq.CurrentEventId = null;
                eq.CurrentEventName = null;
            }

            _maintenanceLogs.Insert(0, new MaintenanceLogModel
            {
                HistoryLogID = _nextHistoryLogId++,
                ActionType = setUnderMaintenance ? "STATUS_CHANGE" : "UPDATE",
                EntityName = "Equipment",
                EntityID = eq.EquipmentID,
                EquipmentName = eq.EquipmentName,
                SerialNo = eq.SerialNo,
                EquipmentStatus = eq.EquipmentStatus,
                ActionDetails = $"[{serviceType}] {issueDetails.Trim()}",
                ChangedByType = "Admin",
                ChangedByAdminID = actorId,
                LoggedAt = DateTime.UtcNow
            });

            NotifyStateChanged();
            return Task.FromResult((true, (string?)null));
        }
    }

    public Task<(bool Success, string? Error)> ResolveMaintenanceAsync(string equipmentId, string resolutionNotes, string actorId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var eq = _equipments.FirstOrDefault(e => e.EquipmentID.Equals(equipmentId, StringComparison.OrdinalIgnoreCase));
            if (eq == null)
                return Task.FromResult((false, (string?)$"Equipment '{equipmentId}' not found."));

            eq.EquipmentStatus = "Available";

            _maintenanceLogs.Insert(0, new MaintenanceLogModel
            {
                HistoryLogID = _nextHistoryLogId++,
                ActionType = "STATUS_CHANGE",
                EntityName = "Equipment",
                EntityID = eq.EquipmentID,
                EquipmentName = eq.EquipmentName,
                SerialNo = eq.SerialNo,
                EquipmentStatus = "Available",
                ActionDetails = $"MAINTENANCE RESOLVED: Equipment tested and returned to active service. Resolution notes: {resolutionNotes.Trim()}",
                ChangedByType = "Admin",
                ChangedByAdminID = actorId,
                LoggedAt = DateTime.UtcNow
            });

            NotifyStateChanged();
            return Task.FromResult((true, (string?)null));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. DATABASE SEEDING
    // ─────────────────────────────────────────────────────────────────────────

    private void SeedDatabaseEntities()
    {
        // 1. dbo.EquipmentType
        _types.AddRange(new[]
        {
            new EquipmentTypeModel { EquipmentTypeID = "ET-CAM", EquipmentTypeName = "Cameras", ColorTag = "Purple", Status = "Active" },
            new EquipmentTypeModel { EquipmentTypeID = "ET-LEN", EquipmentTypeName = "Lenses", ColorTag = "Blue", Status = "Active" },
            new EquipmentTypeModel { EquipmentTypeID = "ET-LGT", EquipmentTypeName = "Lighting", ColorTag = "Amber", Status = "Active" },
            new EquipmentTypeModel { EquipmentTypeID = "ET-MOD", EquipmentTypeName = "Modifiers", ColorTag = "Emerald", Status = "Active" },
            new EquipmentTypeModel { EquipmentTypeID = "ET-AUD", EquipmentTypeName = "Audio", ColorTag = "Rose", Status = "Active" },
            new EquipmentTypeModel { EquipmentTypeID = "ET-GRP", EquipmentTypeName = "Grips", ColorTag = "Slate", Status = "Active" },
            new EquipmentTypeModel { EquipmentTypeID = "ET-GIM", EquipmentTypeName = "Gimbals & Support", ColorTag = "Cyan", Status = "Active" }
        });

        // 2. dbo.Equipment
        _equipments.AddRange(new[]
        {
            new EquipmentItemModel { EquipmentID = "EQ-101", EquipmentName = "Sony FX3 Full-Frame Cinema Camera", EquipmentTypeID = "ET-CAM", EquipmentTypeName = "Cameras", EquipmentStatus = "Available", SerialNo = "SN-FX3-88210" },
            new EquipmentItemModel { EquipmentID = "EQ-102", EquipmentName = "Canon EOS R5 Mark II Body", EquipmentTypeID = "ET-CAM", EquipmentTypeName = "Cameras", EquipmentStatus = "Available", SerialNo = "SN-R5M2-44102" },
            new EquipmentItemModel { EquipmentID = "EQ-103", EquipmentName = "Hasselblad X2D 100C Medium Format", EquipmentTypeID = "ET-CAM", EquipmentTypeName = "Cameras", EquipmentStatus = "Maintenance", SerialNo = "SN-HBL-99312" },
            
            new EquipmentItemModel { EquipmentID = "EQ-201", EquipmentName = "Sony FE 24-70mm f/2.8 GM II", EquipmentTypeID = "ET-LEN", EquipmentTypeName = "Lenses", EquipmentStatus = "Available", SerialNo = "SN-GM2470-1102" },
            new EquipmentItemModel { EquipmentID = "EQ-202", EquipmentName = "Canon RF 70-200mm f/2.8L IS USM", EquipmentTypeID = "ET-LEN", EquipmentTypeName = "Lenses", EquipmentStatus = "Available", SerialNo = "SN-RF70200-5591" },
            new EquipmentItemModel { EquipmentID = "EQ-203", EquipmentName = "Sony FE 50mm f/1.2 GM Prime", EquipmentTypeID = "ET-LEN", EquipmentTypeName = "Lenses", EquipmentStatus = "Available", SerialNo = "SN-GM50-77341" },

            new EquipmentItemModel { EquipmentID = "EQ-301", EquipmentName = "Godox AD600Pro Strobe Flash", EquipmentTypeID = "ET-LGT", EquipmentTypeName = "Lighting", EquipmentStatus = "Available", SerialNo = "SN-AD600-33201" },
            new EquipmentItemModel { EquipmentID = "EQ-302", EquipmentName = "Aputure LS 600d Pro Daylight LED", EquipmentTypeID = "ET-LGT", EquipmentTypeName = "Lighting", EquipmentStatus = "Available", SerialNo = "SN-AP600-88192" },
            new EquipmentItemModel { EquipmentID = "EQ-303", EquipmentName = "Profoto B10X Plus Battery Strobe", EquipmentTypeID = "ET-LGT", EquipmentTypeName = "Lighting", EquipmentStatus = "Maintenance", SerialNo = "SN-PRF-66023" },

            new EquipmentItemModel { EquipmentID = "EQ-401", EquipmentName = "Profoto OCF Beauty Dish Silver 24\"", EquipmentTypeID = "ET-MOD", EquipmentTypeName = "Modifiers", EquipmentStatus = "Available", SerialNo = "SN-BD24-441" },
            new EquipmentItemModel { EquipmentID = "EQ-402", EquipmentName = "Nanlite Parabolic Softbox 120cm", EquipmentTypeID = "ET-MOD", EquipmentTypeName = "Modifiers", EquipmentStatus = "Available", SerialNo = "SN-SB120-990" },

            new EquipmentItemModel { EquipmentID = "EQ-501", EquipmentName = "Sennheiser EW-DP ME2 Wireless Mic Kit", EquipmentTypeID = "ET-AUD", EquipmentTypeName = "Audio", EquipmentStatus = "Available", SerialNo = "SN-SNH-10293" },
            new EquipmentItemModel { EquipmentID = "EQ-502", EquipmentName = "Røde Wireless PRO Dual Kit", EquipmentTypeID = "ET-AUD", EquipmentTypeName = "Audio", EquipmentStatus = "Available", SerialNo = "SN-ROD-88231" },

            new EquipmentItemModel { EquipmentID = "EQ-601", EquipmentName = "Matthews 40\" Heavy-Duty C-Stand Kit", EquipmentTypeID = "ET-GRP", EquipmentTypeName = "Grips", EquipmentStatus = "Available", SerialNo = "SN-MAT-CST1" },
            new EquipmentItemModel { EquipmentID = "EQ-602", EquipmentName = "Avenger Turtle Base Century Stand", EquipmentTypeID = "ET-GRP", EquipmentTypeName = "Grips", EquipmentStatus = "Available", SerialNo = "SN-AVG-CST2" },

            new EquipmentItemModel { EquipmentID = "EQ-701", EquipmentName = "DJI RS 3 Pro Gimbal Stabilizer Combo", EquipmentTypeID = "ET-GIM", EquipmentTypeName = "Gimbals & Support", EquipmentStatus = "Available", SerialNo = "SN-DJI-RS3P1" }
        });

        // 3. dbo.EventAppointment
        var today = DateTime.Today;
        _events.AddRange(new[]
        {
            new EventAppointmentModel
            {
                EventAppointmentID = "EV-2026-001",
                EventName = "Vogue Scandinavia Editorial Shoot",
                EventDescription = "Full-day editorial portrait & haute couture studio shoot.",
                CustomerID = "CUST-101",
                CustomerName = "Elena Rostova (Vogue Editorial)",
                EventDate = today,
                EventTime = new TimeSpan(9, 30, 0),
                EventStatus = "Confirmed"
            },
            new EventAppointmentModel
            {
                EventAppointmentID = "EV-2026-002",
                EventName = "Grand Hyatt Luxury Wedding Gala",
                EventDescription = "On-location wedding ceremony and grand ballroom coverage.",
                CustomerID = "CUST-102",
                CustomerName = "Marcus & Clara Vance",
                EventDate = today,
                EventTime = new TimeSpan(14, 0, 0),
                EventStatus = "Confirmed"
            },
            new EventAppointmentModel
            {
                EventAppointmentID = "EV-2026-003",
                EventName = "TechCorp Global Keynote & Product Demo",
                EventDescription = "Commercial promotional video and 4K interview production.",
                CustomerID = "CUST-103",
                CustomerName = "Sophia Lin (TechCorp VP)",
                EventDate = today.AddDays(1),
                EventTime = new TimeSpan(10, 0, 0),
                EventStatus = "Confirmed"
            },
            new EventAppointmentModel
            {
                EventAppointmentID = "EV-2026-004",
                EventName = "Harbor Lights Sunset Fashion Lookbook",
                EventDescription = "Golden hour outdoor on-location portrait shoot.",
                CustomerID = "CUST-104",
                CustomerName = "Julian Thorne (Fashion Dir)",
                EventDate = today.AddDays(2),
                EventTime = new TimeSpan(16, 30, 0),
                EventStatus = "Pending"
            }
        });

        // 4. dbo.EventEquipment (Dispatches)
        _dispatches.AddRange(new[]
        {
            // Active Dispatches (today)
            new DispatchItemModel
            {
                EventEquipmentID = _nextEventEquipmentId++,
                EventAppointmentID = "EV-2026-001",
                EventName = "Vogue Scandinavia Editorial Shoot",
                EventDate = today,
                EventTime = new TimeSpan(9, 30, 0),
                CustomerID = "CUST-101",
                CustomerName = "Elena Rostova",
                EquipmentID = "EQ-101",
                EquipmentName = "Sony FX3 Full-Frame Cinema Camera",
                SerialNo = "SN-FX3-88210",
                EquipmentTypeName = "Cameras",
                DispatchStatus = "Dispatched",
                DispatchedAt = today.AddHours(8).AddMinutes(30),
                VerifiedBy = null,
                Notes = "Dispatched with Pelican hardcase, 3x V-Mount batteries, and CFexpress A cards."
            },
            new DispatchItemModel
            {
                EventEquipmentID = _nextEventEquipmentId++,
                EventAppointmentID = "EV-2026-001",
                EventName = "Vogue Scandinavia Editorial Shoot",
                EventDate = today,
                EventTime = new TimeSpan(9, 30, 0),
                CustomerID = "CUST-101",
                CustomerName = "Elena Rostova",
                EquipmentID = "EQ-201",
                EquipmentName = "Sony FE 24-70mm f/2.8 GM II",
                SerialNo = "SN-GM2470-1102",
                EquipmentTypeName = "Lenses",
                DispatchStatus = "Dispatched",
                DispatchedAt = today.AddHours(8).AddMinutes(30),
                VerifiedBy = null,
                Notes = "Paired with FX3 Body. Checked front element and autofocus calibration."
            },
            new DispatchItemModel
            {
                EventEquipmentID = _nextEventEquipmentId++,
                EventAppointmentID = "EV-2026-002",
                EventName = "Grand Hyatt Luxury Wedding Gala",
                EventDate = today,
                EventTime = new TimeSpan(14, 0, 0),
                CustomerID = "CUST-102",
                CustomerName = "Marcus & Clara Vance",
                EquipmentID = "EQ-102",
                EquipmentName = "Canon EOS R5 Mark II Body",
                SerialNo = "SN-R5M2-44102",
                EquipmentTypeName = "Cameras",
                DispatchStatus = "Dispatched",
                DispatchedAt = today.AddHours(11),
                VerifiedBy = null,
                Notes = "Checked out with dual LP-E6NH batteries and battery grip."
            },
            new DispatchItemModel
            {
                EventEquipmentID = _nextEventEquipmentId++,
                EventAppointmentID = "EV-2026-002",
                EventName = "Grand Hyatt Luxury Wedding Gala",
                EventDate = today,
                EventTime = new TimeSpan(14, 0, 0),
                CustomerID = "CUST-102",
                CustomerName = "Marcus & Clara Vance",
                EquipmentID = "EQ-202",
                EquipmentName = "Canon RF 70-200mm f/2.8L IS USM",
                SerialNo = "SN-RF70200-5591",
                EquipmentTypeName = "Lenses",
                DispatchStatus = "Dispatched",
                DispatchedAt = today.AddHours(11),
                VerifiedBy = null,
                Notes = "Includes tripod mount ring and hood."
            },

            // Scheduled Checkouts (Upcoming)
            new DispatchItemModel
            {
                EventEquipmentID = _nextEventEquipmentId++,
                EventAppointmentID = "EV-2026-003",
                EventName = "TechCorp Global Keynote & Product Demo",
                EventDate = today.AddDays(1),
                EventTime = new TimeSpan(10, 0, 0),
                CustomerID = "CUST-103",
                CustomerName = "Sophia Lin",
                EquipmentID = "EQ-302",
                EquipmentName = "Aputure LS 600d Pro Daylight LED",
                SerialNo = "SN-AP600-88192",
                EquipmentTypeName = "Lighting",
                DispatchStatus = "Scheduled",
                DispatchedAt = today.AddDays(1).AddHours(8),
                VerifiedBy = null,
                Notes = "Pre-allocated for keynote main stage interview backdrop."
            },
            new DispatchItemModel
            {
                EventEquipmentID = _nextEventEquipmentId++,
                EventAppointmentID = "EV-2026-003",
                EventName = "TechCorp Global Keynote & Product Demo",
                EventDate = today.AddDays(1),
                EventTime = new TimeSpan(10, 0, 0),
                CustomerID = "CUST-103",
                CustomerName = "Sophia Lin",
                EquipmentID = "EQ-501",
                EquipmentName = "Sennheiser EW-DP ME2 Wireless Mic Kit",
                SerialNo = "SN-SNH-10293",
                EquipmentTypeName = "Audio",
                DispatchStatus = "Scheduled",
                DispatchedAt = today.AddDays(1).AddHours(8),
                VerifiedBy = null,
                Notes = "Pre-allocated wireless bodypack receiver."
            },

            // Completed Returns (Returned today)
            new DispatchItemModel
            {
                EventEquipmentID = _nextEventEquipmentId++,
                EventAppointmentID = "EV-2026-000",
                EventName = "Morning Studio Headshots Session",
                EventDate = today,
                EventTime = new TimeSpan(8, 0, 0),
                CustomerID = "CUST-099",
                CustomerName = "David Chen",
                EquipmentID = "EQ-301",
                EquipmentName = "Godox AD600Pro Strobe Flash",
                SerialNo = "SN-AD600-33201",
                EquipmentTypeName = "Lighting",
                DispatchStatus = "Returned",
                DispatchedAt = today.AddHours(7),
                ReturnedAt = today.AddHours(10).AddMinutes(15),
                VerifiedBy = "ADM-001",
                Notes = "Clean return. Battery recharged, strobe bulb inspected and intact."
            },
            new DispatchItemModel
            {
                EventEquipmentID = _nextEventEquipmentId++,
                EventAppointmentID = "EV-2026-000",
                EventName = "Morning Studio Headshots Session",
                EventDate = today,
                EventTime = new TimeSpan(8, 0, 0),
                CustomerID = "CUST-099",
                CustomerName = "David Chen",
                EquipmentID = "EQ-401",
                EquipmentName = "Profoto OCF Beauty Dish Silver 24\"",
                SerialNo = "SN-BD24-441",
                EquipmentTypeName = "Modifiers",
                DispatchStatus = "Returned",
                DispatchedAt = today.AddHours(7),
                ReturnedAt = today.AddHours(10).AddMinutes(15),
                VerifiedBy = "ADM-001",
                Notes = "Clean return. Diffuser cloth inspected."
            }
        });

        // 5. dbo.HistoryLog (Maintenance Servicing Logs)
        _maintenanceLogs.AddRange(new[]
        {
            new MaintenanceLogModel
            {
                HistoryLogID = _nextHistoryLogId++,
                ActionType = "STATUS_CHANGE",
                EntityName = "Equipment",
                EntityID = "EQ-103",
                EquipmentName = "Hasselblad X2D 100C Medium Format",
                SerialNo = "SN-HBL-99312",
                EquipmentStatus = "Maintenance",
                ActionDetails = "[Repair] Shutter mechanism error code ERR-42 on high burst shoots. Sent to authorized Hasselblad center for diagnostic calibration.",
                ChangedByType = "Admin",
                ChangedByAdminID = "ADM-001",
                LoggedAt = DateTime.UtcNow.AddDays(-2)
            },
            new MaintenanceLogModel
            {
                HistoryLogID = _nextHistoryLogId++,
                ActionType = "STATUS_CHANGE",
                EntityName = "Equipment",
                EntityID = "EQ-303",
                EquipmentName = "Profoto B10X Plus Battery Strobe",
                SerialNo = "SN-PRF-66023",
                EquipmentStatus = "Maintenance",
                ActionDetails = "[Repair] Intermittent flash recycling failure when shooting over 1/4 power. Replaced auxiliary capacitor.",
                ChangedByType = "Admin",
                ChangedByAdminID = "ADM-002",
                LoggedAt = DateTime.UtcNow.AddDays(-1)
            },
            new MaintenanceLogModel
            {
                HistoryLogID = _nextHistoryLogId++,
                ActionType = "UPDATE",
                EntityName = "Equipment",
                EntityID = "EQ-101",
                EquipmentName = "Sony FX3 Full-Frame Cinema Camera",
                SerialNo = "SN-FX3-88210",
                EquipmentStatus = "Available",
                ActionDetails = "[Calibration] Routine sensor ultrasonic cleaning, firmware upgrade to v5.00, and heat dissipation fan inspection completed.",
                ChangedByType = "Admin",
                ChangedByAdminID = "ADM-001",
                LoggedAt = DateTime.UtcNow.AddDays(-5)
            },
            new MaintenanceLogModel
            {
                HistoryLogID = _nextHistoryLogId++,
                ActionType = "UPDATE",
                EntityName = "Equipment",
                EntityID = "EQ-201",
                EquipmentName = "Sony FE 24-70mm f/2.8 GM II",
                SerialNo = "SN-GM2470-1102",
                EquipmentStatus = "Available",
                ActionDetails = "[Calibration] Optical collimation test and autofocus alignment calibrated to factory tolerances on bench test chart.",
                ChangedByType = "Admin",
                ChangedByAdminID = "ADM-001",
                LoggedAt = DateTime.UtcNow.AddDays(-8)
            },
            new MaintenanceLogModel
            {
                HistoryLogID = _nextHistoryLogId++,
                ActionType = "UPDATE",
                EntityName = "Equipment",
                EntityID = "EQ-701",
                EquipmentName = "DJI RS 3 Pro Gimbal Stabilizer Combo",
                SerialNo = "SN-DJI-RS3P1",
                EquipmentStatus = "Available",
                ActionDetails = "[Routine Servicing] IMU recalibration, motor axis lubrication, and firmware upgrade.",
                ChangedByType = "Admin",
                ChangedByAdminID = "ADM-003",
                LoggedAt = DateTime.UtcNow.AddDays(-12)
            }
        });
    }
}
