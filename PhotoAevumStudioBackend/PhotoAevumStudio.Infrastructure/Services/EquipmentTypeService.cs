namespace PhotoAevumStudio.Infrastructure.Services;

using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.DTOs;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;

public class EquipmentTypeService(ApplicationDbContext db) : IEquipmentTypeService
{
    private string ConnectionString =>
        db.Database.GetConnectionString()
        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

    public async Task<string> GetNextEquipmentTypeIdAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await using var cmd = new SqlCommand("dbo.sp_GetNextEquipmentTypeID", conn)
        {
            CommandType = CommandType.StoredProcedure
        };

        var outputParam = new SqlParameter("@NextID", SqlDbType.NVarChar, 30)
        {
            Direction = ParameterDirection.Output
        };
        cmd.Parameters.Add(outputParam);

        await conn.OpenAsync(cancellationToken);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        return outputParam.Value?.ToString()
            ?? throw new InvalidOperationException("Failed to generate sequential Equipment Type ID.");
    }

    public async Task<EquipmentTypeResponse> CreateEquipmentTypeAsync(
        CreateEquipmentTypeRequest request, 
        string? adminId, 
        CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await using var cmd = new SqlCommand("dbo.sp_CreateEquipmentType", conn)
        {
            CommandType = CommandType.StoredProcedure
        };

        cmd.Parameters.Add(new SqlParameter("@EquipmentTypeName", SqlDbType.NVarChar, 255) { Value = request.EquipmentTypeName.Trim() });
        cmd.Parameters.Add(new SqlParameter("@EquipmentTypeID", SqlDbType.NVarChar, 30) { Value = request.EquipmentTypeId.Trim() });
        cmd.Parameters.Add(new SqlParameter("@CreatedByAdminID", SqlDbType.NVarChar, 30) { Value = (object?)adminId ?? DBNull.Value });

        await conn.OpenAsync(cancellationToken);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            return new EquipmentTypeResponse(
                EquipmentTypeId: reader.GetString(0),
                EquipmentTypeName: reader.GetString(1)
            );
        }

        throw new InvalidOperationException("Failed to create Equipment Type record.");
    }

    public async Task<IReadOnlyList<EquipmentTypeResponse>> GetAllEquipmentTypesAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await using var cmd = new SqlCommand("dbo.sp_GetEquipmentTypes", conn)
        {
            CommandType = CommandType.StoredProcedure
        };

        await conn.OpenAsync(cancellationToken);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var list = new List<EquipmentTypeResponse>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new EquipmentTypeResponse(
                EquipmentTypeId: reader.GetString(0),
                EquipmentTypeName: reader.GetString(1)
            ));
        }

        return list;
    }
}
