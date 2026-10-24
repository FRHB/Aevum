namespace PhotoAevumStudio.Application.Interfaces;

using PhotoAevumStudio.Application.DTOs;

public interface IEquipmentTypeService
{
    Task<string> GetNextEquipmentTypeIdAsync(CancellationToken cancellationToken = default);
    Task<EquipmentTypeResponse> CreateEquipmentTypeAsync(CreateEquipmentTypeRequest request, string? adminId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EquipmentTypeResponse>> GetAllEquipmentTypesAsync(CancellationToken cancellationToken = default);
}
