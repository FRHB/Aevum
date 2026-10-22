namespace PhotoAevumStudio.Web.Services;

using PhotoAevumStudio.Web.Models;

public interface IEquipmentService
{
    event Action? OnChange;

    Task<IReadOnlyList<EquipmentItemModel>> GetEquipmentsAsync(CancellationToken ct = default);
    Task<EquipmentItemModel?> GetEquipmentByIdAsync(string equipmentId, CancellationToken ct = default);
    Task<IReadOnlyList<EquipmentTypeModel>> GetEquipmentTypesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DispatchItemModel>> GetDispatchesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<EventAppointmentModel>> GetUpcomingEventsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MaintenanceLogModel>> GetMaintenanceLogsAsync(CancellationToken ct = default);

    Task<(bool Success, string? Error)> AddEquipmentAsync(string name, string typeId, string? serialNo, string? equipmentId = null, CancellationToken ct = default);
    Task<(bool Success, string? Error)> UpdateEquipmentStatusAsync(string equipmentId, string newStatus, string details, string actorId, string actorType = "Admin", CancellationToken ct = default);
    Task<(bool Success, string? Error)> AddEquipmentTypeAsync(string typeId, string typeName, string colorTag = "Blue", CancellationToken ct = default);
    Task<(bool Success, string? Error)> AssignEquipmentToEventAsync(string eventId, string equipmentId, string actorId = "ADM-001", CancellationToken ct = default);
    Task<(bool Success, string? Error)> AssignMultipleEquipmentToEventAsync(string eventId, IEnumerable<string> equipmentIds, string actorId = "ADM-001", CancellationToken ct = default);
    Task<(bool Success, string? Error)> VerifyReturnAsync(int eventEquipmentId, string notes, string actorId = "ADM-001", bool flagMaintenance = false, CancellationToken ct = default);
    Task<(bool Success, string? Error)> LogMaintenanceAsync(string equipmentId, string serviceType, string issueDetails, string actorId, bool setUnderMaintenance = true, CancellationToken ct = default);
    Task<(bool Success, string? Error)> ResolveMaintenanceAsync(string equipmentId, string resolutionNotes, string actorId, CancellationToken ct = default);

    void NotifyStateChanged();
}
