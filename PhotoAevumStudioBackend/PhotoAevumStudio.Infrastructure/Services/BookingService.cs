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
        catch (SqlException ex) when (ex.Number is >= 50000 and < 50100)
        {
            throw new BookingRuleException(ex.Message);
        }
    }

    public async Task<IReadOnlyList<BookingListItemDto>> GetBookingsAsync(
        CancellationToken cancellationToken = default)
    {
        var connectionString = _db.Database.GetConnectionString();
        var bookings = new List<BookingListItemDto>();

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        const string sql = @"
            IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
            BEGIN
                SELECT ea.EventAppointmentID,
                       ea.EventName,
                       ea.EventDescription,
                       ea.CustomerID,
                       COALESCE(c.CustomerFirstName + ' ' + c.CustomerLastName, ea.CustomerID) AS CustomerName,
                       ea.EventDate,
                       ea.EventTime,
                       ea.EventStatus,
                       ea.CreatedAt
                FROM dbo.EventAppointment ea
                LEFT JOIN dbo.Customer c ON c.CustomerID = ea.CustomerID
                ORDER BY ea.EventDate DESC, ea.EventTime DESC;
            END";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            bookings.Add(new BookingListItemDto
            {
                EventAppointmentId = reader.GetString(0),
                EventName = reader.GetString(1),
                EventDescription = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                CustomerId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                CustomerName = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                EventDate = DateOnly.FromDateTime(reader.GetDateTime(5)),
                EventTime = TimeOnly.FromTimeSpan(reader.GetFieldValue<TimeSpan>(6)),
                EventStatus = reader.IsDBNull(7) ? "Scheduled" : reader.GetString(7),
                CreatedAt = reader.IsDBNull(8) ? DateTime.UtcNow : reader.GetDateTime(8)
            });
        }

        return bookings;
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
