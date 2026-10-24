using Microsoft.EntityFrameworkCore;

namespace PhotoAevumStudio.Infrastructure.Scheduling;

public static class SchedulingSchema
{
    /// <summary>Creates the wizard tables/indexes if missing (idempotent).</summary>
    public static async Task EnsureAsync(SchedulingDbContext db, CancellationToken cancellationToken = default)
    {
        await using var stream = typeof(SchedulingSchema).Assembly
            .GetManifestResourceStream("PhotoAevumStudio.Infrastructure.Scheduling.SchedulingSchema.sql")
            ?? throw new InvalidOperationException("Embedded SchedulingSchema.sql not found.");
        using var reader = new StreamReader(stream);
        await db.Database.ExecuteSqlRawAsync(await reader.ReadToEndAsync(cancellationToken), cancellationToken);
    }
}

public sealed class SchedulingOptions
{
    /// <summary>Studio time zone used to decide which slots are already in the past.</summary>
    public string TimeZoneId { get; set; } = "Asia/Manila";

    /// <summary>How far ahead clients may book.</summary>
    public int BookingWindowDays { get; set; } = 90;
}
