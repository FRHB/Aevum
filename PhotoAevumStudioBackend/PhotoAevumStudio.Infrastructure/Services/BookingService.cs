using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Infrastructure.Services;

public class BookingService : IBookingService
{
    private readonly ApplicationDbContext _db;

    public BookingService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<BookingResultDto> CreateEventAppointmentAsync(
        CreateBookingRequest r,
        CancellationToken cancellationToken = default)
    {
        // Reuses the connection string already configured for ApplicationDbContext
        // appsettings.json -> ConnectionStrings:DefaultConnection.
        var connectionString = _db.Database.GetConnectionString();

        await using var conn = new SqlConnection(connectionString);
        await using var cmd = new SqlCommand("dbo.usp_CreateEventAppointment", conn)
        {
            CommandType = CommandType.StoredProcedure
        };

        cmd.Parameters.Add("@CustomerID", SqlDbType.NVarChar, 30).Value = r.CustomerId;
        cmd.Parameters.Add("@EventName", SqlDbType.NVarChar, 255).Value = r.EventName;
        cmd.Parameters.Add("@EventDescription", SqlDbType.NVarChar, 255).Value = r.EventDescription;
        cmd.Parameters.Add("@EventDate", SqlDbType.Date).Value = r.EventDate.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@EventTime", SqlDbType.Time).Value = r.EventTime.ToTimeSpan();

        // The procedure expects comma-separated ID lists.
        cmd.Parameters.Add("@ServicesIDs", SqlDbType.NVarChar, -1).Value = ToCsv(r.ServicesIds);
        cmd.Parameters.Add("@AdditionalServicesIDs", SqlDbType.NVarChar, -1).Value = ToCsv(r.AdditionalServicesIds);
        cmd.Parameters.Add("@ComboPackageIDs", SqlDbType.NVarChar, -1).Value = ToCsv(r.ComboPackageIds);
        cmd.Parameters.Add("@EmployeeIDs", SqlDbType.NVarChar, -1).Value = ToCsv(r.EmployeeIds);
        cmd.Parameters.Add("@EquipmentIDs", SqlDbType.NVarChar, -1).Value = ToCsv(r.EquipmentIds);

        try
        {
            await conn.OpenAsync(cancellationToken);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("The booking procedure returned no result.");

            return new BookingResultDto
            {
                EventAppointmentId = reader.GetString(reader.GetOrdinal("EventAppointmentID")),
                EventName = reader.GetString(reader.GetOrdinal("EventName")),
                EventDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("EventDate"))),
                EventTime = TimeOnly.FromTimeSpan(reader.GetFieldValue<TimeSpan>(reader.GetOrdinal("EventTime"))),
                CustomerName = reader.GetString(reader.GetOrdinal("CustomerName")),
                LineItemCount = reader.GetInt32(reader.GetOrdinal("LineItemCount")),
                EmployeesAssigned = reader.GetInt32(reader.GetOrdinal("EmployeesAssigned")),
                EquipmentReserved = reader.GetInt32(reader.GetOrdinal("EquipmentReserved"))
            };
        }
        // 50000-50099 are the custom errors raised by THROW in the stored procedure.
        catch (SqlException ex) when (ex.Number is >= 50000 and < 50100)
        {
            throw new BookingRuleException(ex.Message);
        }
    }

    private static object ToCsv(IEnumerable<string>? ids)
    {
        var cleaned = ids?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToList();

        return cleaned is { Count: > 0 } ? string.Join(",", cleaned) : DBNull.Value;
    }
}