using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Infrastructure.Data;
using PhotoAevumStudio.Web.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class SpecializationSchemaTests
{
    private const string ConnectionString = "Data Source=localhost\\SQLEXPRESS;Initial Catalog=PhotoAevumStudio;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";

    [Fact]
    public void EmployeeSpecialization_Model_ContainsRequiredColumns()
    {
        var type = typeof(EmployeeSpecialization);
        Assert.NotNull(type.GetProperty("SpecializationID"));
        Assert.NotNull(type.GetProperty("SpecializationName"));
        Assert.NotNull(type.GetProperty("SpecializationDescription"));
        Assert.NotNull(type.GetProperty("Specialization"));
        Assert.NotNull(type.GetProperty("Description"));
        Assert.NotNull(type.GetProperty("ColorTag"));
        Assert.NotNull(type.GetProperty("CreatedAt"));
    }

    [Fact]
    public void Specialization_InheritsFrom_EmployeeSpecialization()
    {
        var type = typeof(Specialization);
        Assert.True(typeof(EmployeeSpecialization).IsAssignableFrom(type));
    }

    [Fact]
    public void ApplicationDbContext_HasEmployeeSpecialization_DbSet()
    {
        var dbContextType = typeof(ApplicationDbContext);
        Assert.NotNull(dbContextType.GetProperty("EmployeeSpecializations"));
        Assert.NotNull(dbContextType.GetProperty("Specializations"));
    }

    [Fact]
    public void UserService_GetSpecializationLookups_ExecutesSuccessfullyWithoutInvalidColumnErrors()
    {
        var userService = new UserService();
        var lookups = userService.GetSpecializationLookups();
        Assert.NotNull(lookups);
    }

    [Fact]
    public async Task ApplicationDbContext_LinqQuery_ExecutesSuccessfullyAgainstDatabase()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using var db = new ApplicationDbContext(options);
        if (await db.Database.CanConnectAsync())
        {
            var specs = await db.EmployeeSpecializations
                .Where(s => s.ColorTag != null)
                .OrderBy(s => s.SpecializationID)
                .ToListAsync();

            Assert.NotNull(specs);
        }
    }

    [Fact]
    public async Task RawQuery_SelectingSpecializationColumns_Succeeds()
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        const string sql = @"
            SELECT SpecializationID,
                   SpecializationName,
                   SpecializationDescription,
                   Specialization,
                   Description,
                   ColorTag,
                   CreatedAt
            FROM dbo.EmployeeSpecialization;";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Assert.False(reader.IsDBNull(0)); // SpecializationID
        }
    }
}
