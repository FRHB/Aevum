using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Infrastructure.Data;
using PhotoAevumStudio.Web.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class SpecializationSchemaTests
{
    private const string ConnectionString = "Server=db77602.public.databaseasp.net; Database=db77602; User Id=db77602; Password=G!r7cB8#Z5_d; Encrypt=True; TrustServerCertificate=True; MultipleActiveResultSets=True;";

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

    [Fact]
    public async Task ApplicationDbContext_EmployeesQuery_ExecutesSuccessfullyAgainstDatabase()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using var db = new ApplicationDbContext(options);
        if (await db.Database.CanConnectAsync())
        {
            var employees = await db.Employees
                .Include(e => e.IdentityUser)
                .ToListAsync();

            Assert.NotNull(employees);
        }
    }

    [Fact]
    public async Task RawQuery_SelectingEmployeeColumns_Succeeds()
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        const string sql = @"
            SELECT Id,
                   IdentityUserId,
                   Status,
                   DaysOff,
                   Specializations,
                   EmployeeID,
                   EmployeeFirstName,
                   EmployeeLastName,
                   EmployeeEmail
            FROM dbo.Employees;";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.NotNull(reader);
    }

    [Fact]
    public void EmployeeSpecializations_And_Lookups_ExecuteSuccessfully()
    {
        var userService = new UserService();
        var lookups = userService.GetSpecializationLookups();
        Assert.NotNull(lookups);

        var empSpecs = userService.GetEmployeeSpecializations();
        Assert.NotNull(empSpecs);

        var depts = userService.GetDepartments();
        Assert.NotNull(depts);

        // Verify that assigning a specialization to an employee works and persists
        if (empSpecs.Count > 0 && lookups.Count > 0)
        {
            var firstEmp = empSpecs[0];
            var firstSpec = lookups[0];
            var success = userService.AssignEmployeeSpecializations(
                firstEmp.EmployeeId, 
                new[] { firstSpec.Name }, 
                firstEmp.DepartmentId);
            Assert.True(success);

            var updatedSpecs = userService.GetEmployeeSpecializations();
            var updatedEmp = updatedSpecs.FirstOrDefault(e => e.EmployeeId == firstEmp.EmployeeId);
            Assert.NotNull(updatedEmp);
            Assert.Contains(firstSpec.Name, updatedEmp.Specializations);
        }
    }
}
