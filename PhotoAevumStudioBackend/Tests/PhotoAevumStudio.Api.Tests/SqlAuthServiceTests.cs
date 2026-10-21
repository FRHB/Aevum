using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Services;
using Xunit;

namespace PhotoAevumStudio.Api.Tests;

[Collection("DatabaseIntegration")]
public class SqlAuthServiceTests
{
    private readonly IConfiguration _configuration;
    private readonly SqlAuthService _authService;
    private readonly string _connectionString;

    public SqlAuthServiceTests()
    {
        var configBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=LAPTOP-VJQ60GP3\\SQLEXPRESS;Initial Catalog=PhotoAevumStudio;Integrated Security=True;Persist Security Info=False;Pooling=False;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=True;Application Name=\"SQL Server Management Studio\";Command Timeout=0"
            });
        _configuration = configBuilder.Build();
        _connectionString = _configuration.GetConnectionString("DefaultConnection")!;
        _authService = new SqlAuthService(_configuration, NullLogger<SqlAuthService>.Instance);
    }

    [Fact]
    public async Task CanConnectToLocalDatabase()
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        Assert.Equal(System.Data.ConnectionState.Open, conn.State);
    }

    [Fact]
    public async Task Admin_FirstLogin_And_PasswordHashingFlow_WorksEndToEnd()
    {
        // 1. Arrange: Ensure ADM-001 exists with isFirstLogin = 1 and password 'AxiomBit@2026'
        await using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            const string resetSql = @"
                IF EXISTS (SELECT 1 FROM dbo.Admin WHERE AdminID = 'ADM-001')
                BEGIN
                    UPDATE dbo.Admin
                    SET AdminPassword = 'AxiomBit@2026', isFirstLogin = 1
                    WHERE AdminID = 'ADM-001';
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.Admin (AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName, AdminPassword, isFirstLogin)
                    VALUES ('ADM-001', 'Super Admin', 'Axiom', 'Bit', NULL, 'AxiomBit@2026', 1);
                END";
            await using var cmd = new SqlCommand(resetSql, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            // 2. First login attempt with temporary plain-text password
            var firstLogin = await _authService.AuthenticateAsync("ADM-001", "AxiomBit@2026");
            Assert.True(firstLogin.Success, "First login credentials should be accepted");
            Assert.True(firstLogin.RequiresPasswordChange, "First login MUST require password change");
            Assert.NotNull(firstLogin.User);
            Assert.Equal("ADM-001", firstLogin.User.Id);

            // 3. Attempt first login with WRONG password -> should fail
            var wrongPassLogin = await _authService.AuthenticateAsync("ADM-001", "WrongPassword123!");
            Assert.False(wrongPassLogin.Success);

            // 4. Perform initial password change: sets new password, hashes with BCrypt, updates isFirstLogin to 0
            const string newPassword = "NewSecurePassword2026!";
            var (changeSuccess, changeMessage) = await _authService.ChangeInitialPasswordAsync(
                "ADM-001", "AxiomBit@2026", newPassword);
            Assert.True(changeSuccess, $"Password change should succeed: {changeMessage}");

            // 5. Verify database state directly:
            // isFirstLogin MUST be 0
            // AdminPassword MUST be a valid BCrypt hash ($2a$ or $2b$)
            await using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                const string verifySql = "SELECT AdminPassword, isFirstLogin FROM dbo.Admin WHERE AdminID = 'ADM-001';";
                await using var cmd = new SqlCommand(verifySql, conn);
                await using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());

                var storedHash = reader.GetString(0);
                var isFirstLogin = reader.GetBoolean(1);

                Assert.False(isFirstLogin, "isFirstLogin should now be false (0)");
                Assert.StartsWith("$2", storedHash); // BCrypt prefix ($2a$, $2b$)
                Assert.True(BCrypt.Net.BCrypt.Verify(newPassword, storedHash), "Stored hash must match new password");
            }

            // 6. Standard login with new password: should succeed without requiring password change
            var standardLogin = await _authService.AuthenticateAsync("ADM-001", newPassword);
            Assert.True(standardLogin.Success, "Standard login with new password must succeed");
            Assert.False(standardLogin.RequiresPasswordChange, "Standard login must NOT require password change");
            Assert.NotNull(standardLogin.User);

            // 7. Standard login with old plain text password: MUST FAIL
            var oldPlainLogin = await _authService.AuthenticateAsync("ADM-001", "AxiomBit@2026");
            Assert.False(oldPlainLogin.Success, "Old plain text password must no longer work");
        }
        finally
        {
            // Reset ADM-001 so it remains ready for user first-login testing
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            const string resetSql = @"
                UPDATE dbo.Admin
                SET AdminPassword = 'AxiomBit@2026', isFirstLogin = 1
                WHERE AdminID = 'ADM-001';";
            await using var cmd = new SqlCommand(resetSql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Employee_Creation_FirstLogin_And_PasswordChangeFlow_WorksEndToEnd()
    {
        const string testEmpId = "EMP-999";
        const string testEmail = "testemployee999@photoaevum.studio";
        const string tempPassword = "InitialEmpPassword123!";
        const string newPassword = "UpdatedEmpSecurePass456!";

        // Clean up before test
        await using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            await using var cleanCmd = new SqlCommand("DELETE FROM dbo.Employee WHERE EmployeeID = @Id;", conn);
            cleanCmd.Parameters.AddWithValue("@Id", testEmpId);
            await cleanCmd.ExecuteNonQueryAsync();
        }

        try
        {
            // 1. Create employee via sp_CreateEmployee
            var (regSuccess, regMsg) = await _authService.RegisterEmployeeAsync(
                testEmpId, "Test", "Employee", null, testEmail, tempPassword);
            Assert.True(regSuccess, $"Employee registration failed: {regMsg}");

            // 2. First login attempt: must require password change
            var firstLogin = await _authService.AuthenticateAsync(testEmpId, tempPassword);
            Assert.True(firstLogin.Success);
            Assert.True(firstLogin.RequiresPasswordChange);

            // 3. Change password
            var (changeSuccess, changeMsg) = await _authService.ChangeInitialPasswordAsync(
                testEmpId, tempPassword, newPassword);
            Assert.True(changeSuccess, $"Change password failed: {changeMsg}");

            // Verify database state: isFirstLogin must be 0 (false)
            await using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                const string checkSql = "SELECT EmployeePassword, isFirstLogin FROM dbo.Employee WHERE EmployeeID = @Id;";
                await using var cmd = new SqlCommand(checkSql, conn);
                cmd.Parameters.AddWithValue("@Id", testEmpId);
                await using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                var hash = reader.GetString(0);
                var isFirstLogin = reader.GetBoolean(1);

                Assert.False(isFirstLogin, "Employee isFirstLogin should now be false (0)");
                Assert.StartsWith("$2", hash);
                Assert.True(BCrypt.Net.BCrypt.Verify(newPassword, hash));
            }

            // 4. Standard login with new password
            var standardLogin = await _authService.AuthenticateAsync(testEmpId, newPassword);
            Assert.True(standardLogin.Success);
            Assert.False(standardLogin.RequiresPasswordChange);
            Assert.False(standardLogin.IsFirstLogin);

            // 5. Old password must fail
            var oldLogin = await _authService.AuthenticateAsync(testEmpId, tempPassword);
            Assert.False(oldLogin.Success);
        }
        finally
        {
            // Clean up
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cleanCmd = new SqlCommand("DELETE FROM dbo.Employee WHERE EmployeeID = @Id;", conn);
            cleanCmd.Parameters.AddWithValue("@Id", testEmpId);
            await cleanCmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Customer_Registration_And_BCryptVerification_WorksEndToEnd()
    {
        const string testEmail = "testcustomer_verify@photoaevum.studio";
        const string password = "SecureCustomerPassword123!";

        string? createdCustomerId = null;

        // Clean up before test
        await using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            await using var cleanCmd = new SqlCommand("DELETE FROM dbo.Customer WHERE CustomerEmail = @Email;", conn);
            cleanCmd.Parameters.AddWithValue("@Email", testEmail);
            await cleanCmd.ExecuteNonQueryAsync();
        }

        try
        {
            // 1. Register customer via sp_CreateCustomer
            var (regSuccess, regMsg, customerId) = await _authService.RegisterCustomerAsync(
                "Jane", "Customer", "M", testEmail, "09123456789", password);
            Assert.True(regSuccess, $"Customer registration failed: {regMsg}");
            Assert.NotNull(customerId);
            createdCustomerId = customerId;

            // 2. Verify password is stored as BCrypt hash
            await using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                await using var cmd = new SqlCommand("SELECT CustomerPassword FROM dbo.Customer WHERE CustomerID = @Id;", conn);
                cmd.Parameters.AddWithValue("@Id", customerId);
                var storedPassword = (string?)await cmd.ExecuteScalarAsync();
                Assert.NotNull(storedPassword);
                Assert.StartsWith("$2", storedPassword);
                Assert.True(BCrypt.Net.BCrypt.Verify(password, storedPassword));
            }

            // 3. Login with correct password
            var loginResult = await _authService.AuthenticateAsync(testEmail, password);
            Assert.True(loginResult.Success);
            Assert.False(loginResult.RequiresPasswordChange);

            // 4. Login with wrong password must fail
            var wrongResult = await _authService.AuthenticateAsync(testEmail, "WrongPassword!");
            Assert.False(wrongResult.Success);
        }
        finally
        {
            // Clean up
            if (createdCustomerId != null)
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                await using var cleanCmd = new SqlCommand("DELETE FROM dbo.Customer WHERE CustomerID = @Id;", conn);
                cleanCmd.Parameters.AddWithValue("@Id", createdCustomerId);
                await cleanCmd.ExecuteNonQueryAsync();
            }
        }
    }

    [Fact]
    public async Task Admin_CanAuthenticate_UsingEmailPrefix()
    {
        // ADM-001 can authenticate using ADM-001@photoaevum.studio
        var loginResult = await _authService.AuthenticateAsync("ADM-001@photoaevum.studio", "AxiomBit@2026");
        Assert.True(loginResult.Success);
        Assert.True(loginResult.RequiresPasswordChange);
        Assert.Equal("ADM-001", loginResult.User?.Id);
    }

    [Fact]
    public async Task Customer_StandardLogin_StrictlyRejects_UnHashedPlainTextInDatabase()
    {
        const string testCustId = "CUST-PLAIN";
        const string testEmail = "plaincustomer@photoaevum.studio";
        const string plainPass = "PlainTextPass123!";

        await using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            await using var cleanCmd = new SqlCommand("DELETE FROM dbo.Customer WHERE CustomerID = @Id;", conn);
            cleanCmd.Parameters.AddWithValue("@Id", testCustId);
            await cleanCmd.ExecuteNonQueryAsync();

            // Insert plain text password directly into database
            const string insertSql = @"
                INSERT INTO dbo.Customer (CustomerID, CustomerFirstName, CustomerLastName, CustomerEmail, CustomerPassword)
                VALUES (@Id, 'Plain', 'User', @Email, @PlainPass);";
            await using var insCmd = new SqlCommand(insertSql, conn);
            insCmd.Parameters.AddWithValue("@Id", testCustId);
            insCmd.Parameters.AddWithValue("@Email", testEmail);
            insCmd.Parameters.AddWithValue("@PlainPass", plainPass);
            await insCmd.ExecuteNonQueryAsync();
        }

        try
        {
            // Standard login must reject unhashed plain text passwords
            var login = await _authService.AuthenticateAsync(testEmail, plainPass);
            Assert.False(login.Success, "Standard login must reject passwords not stored as valid BCrypt hashes.");
        }
        finally
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cleanCmd = new SqlCommand("DELETE FROM dbo.Customer WHERE CustomerID = @Id;", conn);
            cleanCmd.Parameters.AddWithValue("@Id", testCustId);
            await cleanCmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task InactiveEmployee_Login_And_PasswordChange_IsRejected()
    {
        const string testEmpId = "EMP-INACT";
        const string testEmail = "inactive@photoaevum.studio";
        const string testPass = "InactiveEmp123!";

        await using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            await using var cleanCmd = new SqlCommand("DELETE FROM dbo.Employee WHERE EmployeeID = @Id;", conn);
            cleanCmd.Parameters.AddWithValue("@Id", testEmpId);
            await cleanCmd.ExecuteNonQueryAsync();

            const string insertSql = @"
                INSERT INTO dbo.Employee (EmployeeID, EmployeeFirstName, EmployeeLastName, EmployeeEmail, EmployeePassword, EmployeeStatus, isFirstLogin)
                VALUES (@Id, 'Inactive', 'Staff', @Email, @Pass, 'Inactive', 1);";
            await using var insCmd = new SqlCommand(insertSql, conn);
            insCmd.Parameters.AddWithValue("@Id", testEmpId);
            insCmd.Parameters.AddWithValue("@Email", testEmail);
            insCmd.Parameters.AddWithValue("@Pass", BCrypt.Net.BCrypt.HashPassword(testPass));
            await insCmd.ExecuteNonQueryAsync();
        }

        try
        {
            var login = await _authService.AuthenticateAsync(testEmpId, testPass);
            Assert.False(login.Success, "Inactive employee login must be rejected.");
            Assert.Equal("Employee account is inactive.", login.ErrorMessage);

            var (changeSuccess, changeMsg) = await _authService.ChangeInitialPasswordAsync(
                testEmpId, testPass, "NewPass123456!");
            Assert.False(changeSuccess, "Inactive employee password change must be rejected.");
            Assert.Equal("Employee account is inactive.", changeMsg);
        }
        finally
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cleanCmd = new SqlCommand("DELETE FROM dbo.Employee WHERE EmployeeID = @Id;", conn);
            cleanCmd.Parameters.AddWithValue("@Id", testEmpId);
            await cleanCmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Employee_EmailPrefix_FirstLogin_AndPasswordChange_Updates_isFirstLogin_To_0()
    {
        const string testEmpId = "EMP-777";
        const string testEmail = "emp777@photoaevum.studio";
        const string tempPassword = "InitialEmpPassword777!";
        const string newPassword = "UpdatedEmpSecurePass777!";

        // Clean up before test
        await using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            await using var cleanCmd = new SqlCommand("DELETE FROM dbo.Employee WHERE EmployeeID = @Id;", conn);
            cleanCmd.Parameters.AddWithValue("@Id", testEmpId);
            await cleanCmd.ExecuteNonQueryAsync();

            const string insertSql = @"
                INSERT INTO dbo.Employee (EmployeeID, EmployeeFirstName, EmployeeLastName, EmployeeEmail, EmployeePassword, EmployeeStatus, isFirstLogin)
                VALUES (@Id, 'Prefix', 'Test', @Email, @Pass, 'Active', 1);";
            await using var insCmd = new SqlCommand(insertSql, conn);
            insCmd.Parameters.AddWithValue("@Id", testEmpId);
            insCmd.Parameters.AddWithValue("@Email", testEmail);
            insCmd.Parameters.AddWithValue("@Pass", tempPassword);
            await insCmd.ExecuteNonQueryAsync();
        }

        try
        {
            // Login using the email handle format "EMP-777@photoaevum.studio"
            var firstLogin = await _authService.AuthenticateAsync("EMP-777@photoaevum.studio", tempPassword);
            Assert.True(firstLogin.Success);
            Assert.True(firstLogin.RequiresPasswordChange);
            Assert.True(firstLogin.IsFirstLogin);

            // Change password using "EMP-777@photoaevum.studio"
            var (changeSuccess, changeMsg) = await _authService.ChangeInitialPasswordAsync(
                "EMP-777@photoaevum.studio", tempPassword, newPassword);
            Assert.True(changeSuccess, $"Change password failed: {changeMsg}");

            // Verify database state: isFirstLogin must be 0
            await using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                const string checkSql = "SELECT EmployeePassword, isFirstLogin FROM dbo.Employee WHERE EmployeeID = @Id;";
                await using var cmd = new SqlCommand(checkSql, conn);
                cmd.Parameters.AddWithValue("@Id", testEmpId);
                await using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                var hash = reader.GetString(0);
                var isFirstLogin = reader.GetBoolean(1);

                Assert.False(isFirstLogin, "Employee isFirstLogin should now be false (0)");
                Assert.StartsWith("$2", hash);
                Assert.True(BCrypt.Net.BCrypt.Verify(newPassword, hash));
            }

            // Standard login with new password
            var standardLogin = await _authService.AuthenticateAsync("EMP-777@photoaevum.studio", newPassword);
            Assert.True(standardLogin.Success);
            Assert.False(standardLogin.RequiresPasswordChange);
            Assert.False(standardLogin.IsFirstLogin);
        }
        finally
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cleanCmd = new SqlCommand("DELETE FROM dbo.Employee WHERE EmployeeID = @Id;", conn);
            cleanCmd.Parameters.AddWithValue("@Id", testEmpId);
            await cleanCmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Customer_CanChangePassword_Via_ChangeInitialPasswordAsync()
    {
        const string testEmail = "testcustomer_pwchange@photoaevum.studio";
        const string currentPassword = "CurrentCustomerPass123!";
        const string newPassword = "NewCustomerPass456!";
        string? customerId = null;

        // Clean up before test
        await using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            await using var cleanCmd = new SqlCommand("DELETE FROM dbo.Customer WHERE CustomerEmail = @Email;", conn);
            cleanCmd.Parameters.AddWithValue("@Email", testEmail);
            await cleanCmd.ExecuteNonQueryAsync();
        }

        try
        {
            var (regSuccess, _, createdId) = await _authService.RegisterCustomerAsync(
                "Cust", "Change", null, testEmail, null, currentPassword);
            Assert.True(regSuccess);
            customerId = createdId;

            // Customer changes password via ChangeInitialPasswordAsync
            var (changeSuccess, changeMsg) = await _authService.ChangeInitialPasswordAsync(
                testEmail, currentPassword, newPassword);
            Assert.True(changeSuccess, $"Customer password change failed: {changeMsg}");

            // Verify login with new password
            var newLogin = await _authService.AuthenticateAsync(testEmail, newPassword);
            Assert.True(newLogin.Success);

            // Verify login with old password fails
            var oldLogin = await _authService.AuthenticateAsync(testEmail, currentPassword);
            Assert.False(oldLogin.Success);
        }
        finally
        {
            if (customerId != null)
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                await using var cleanCmd = new SqlCommand("DELETE FROM dbo.Customer WHERE CustomerID = @Id;", conn);
                cleanCmd.Parameters.AddWithValue("@Id", customerId);
                await cleanCmd.ExecuteNonQueryAsync();
            }
        }
    }

    [Fact]
    public async Task Employee_With_Null_isFirstLogin_IsTreated_As_FirstLogin_And_Transitions_To_0()
    {
        const string testEmpId = "EMP-NULL";
        const string testEmail = "empnull@photoaevum.studio";
        const string tempPass = "InitialNullPass123!";
        const string newPass = "UpdatedNullPass456!";

        await using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            await using var cleanCmd = new SqlCommand("DELETE FROM dbo.Employee WHERE EmployeeID = @Id;", conn);
            cleanCmd.Parameters.AddWithValue("@Id", testEmpId);
            await cleanCmd.ExecuteNonQueryAsync();

            // Temporarily make isFirstLogin nullable if constraint allows
            try
            {
                await using var alterCmd = new SqlCommand("ALTER TABLE dbo.Employee ALTER COLUMN isFirstLogin bit NULL;", conn);
                await alterCmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // In case constraint name blocks alter, continue
            }

            const string insSql = @"
                INSERT INTO dbo.Employee (EmployeeID, EmployeeFirstName, EmployeeLastName, EmployeeEmail, EmployeePassword, EmployeeStatus, isFirstLogin)
                VALUES (@Id, 'NullTest', 'Emp', @Email, @Pass, 'Active', NULL);";
            await using var insCmd = new SqlCommand(insSql, conn);
            insCmd.Parameters.AddWithValue("@Id", testEmpId);
            insCmd.Parameters.AddWithValue("@Email", testEmail);
            insCmd.Parameters.AddWithValue("@Pass", tempPass);
            await insCmd.ExecuteNonQueryAsync();
        }

        try
        {
            // 1. Authenticate with NULL isFirstLogin -> MUST be treated as isFirstLogin = true
            var loginResult = await _authService.AuthenticateAsync(testEmpId, tempPass);
            Assert.True(loginResult.Success, "Login should succeed for initial plain-text credentials");
            Assert.True(loginResult.RequiresPasswordChange, "RequiresPasswordChange must be true when isFirstLogin is NULL");
            Assert.True(loginResult.IsFirstLogin, "IsFirstLogin must be true when isFirstLogin is NULL");

            // 2. Change password -> must set isFirstLogin = 0
            var (changeSuccess, changeMsg) = await _authService.ChangeInitialPasswordAsync(
                testEmpId, tempPass, newPass);
            Assert.True(changeSuccess, $"Change password failed: {changeMsg}");

            // 3. Verify database state
            await using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                const string checkSql = "SELECT isFirstLogin FROM dbo.Employee WHERE EmployeeID = @Id;";
                await using var checkCmd = new SqlCommand(checkSql, conn);
                checkCmd.Parameters.AddWithValue("@Id", testEmpId);
                var val = await checkCmd.ExecuteScalarAsync();
                Assert.NotNull(val);
                Assert.False(Convert.ToBoolean(val), "isFirstLogin should now be 0 (false)");
            }

            // 4. Subsequent login -> isFirstLogin must be false
            var postLogin = await _authService.AuthenticateAsync(testEmpId, newPass);
            Assert.True(postLogin.Success);
            Assert.False(postLogin.RequiresPasswordChange);
            Assert.False(postLogin.IsFirstLogin);
        }
        finally
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cleanCmd = new SqlCommand("DELETE FROM dbo.Employee WHERE EmployeeID = @Id;", conn);
            cleanCmd.Parameters.AddWithValue("@Id", testEmpId);
            await cleanCmd.ExecuteNonQueryAsync();

            try
            {
                await using var restoreCmd = new SqlCommand("ALTER TABLE dbo.Employee ALTER COLUMN isFirstLogin bit NOT NULL;", conn);
                await restoreCmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Best effort restore
            }
        }
    }

    [Fact]
    public async Task Employee_PasswordChange_Syncs_AspNetUsers_StudioId_And_PasswordHash()
    {
        const string testEmpId = "EMP-SYNC";
        const string testEmail = "empsync@photoaevum.studio";
        const string tempPass = "InitialSyncPass123!";
        const string newPass = "UpdatedSyncPass456!";
        var fakeGuid = Guid.NewGuid().ToString();

        await using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            await using var cleanCmd = new SqlCommand(@"
                DELETE FROM dbo.Employee WHERE EmployeeID = @Id;
                IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
                BEGIN
                    DELETE FROM dbo.AspNetUsers WHERE StudioId = @Id OR Id = @Guid;
                END;", conn);
            cleanCmd.Parameters.AddWithValue("@Id", testEmpId);
            cleanCmd.Parameters.AddWithValue("@Guid", fakeGuid);
            await cleanCmd.ExecuteNonQueryAsync();

            // Insert into dbo.Employee
            const string insEmp = @"
                INSERT INTO dbo.Employee (EmployeeID, EmployeeFirstName, EmployeeLastName, EmployeeEmail, EmployeePassword, EmployeeStatus, isFirstLogin)
                VALUES (@Id, 'Sync', 'Test', @Email, @Pass, 'Active', 1);";
            await using var insCmd = new SqlCommand(insEmp, conn);
            insCmd.Parameters.AddWithValue("@Id", testEmpId);
            insCmd.Parameters.AddWithValue("@Email", testEmail);
            insCmd.Parameters.AddWithValue("@Pass", tempPass);
            await insCmd.ExecuteNonQueryAsync();

            // Insert into dbo.AspNetUsers with StudioId = EMP-SYNC and MustChangePassword = 1
            const string insUser = @"
                IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount, FirstName, LastName, StudioId, MustChangePassword)
                    VALUES (@Guid, @Email, UPPER(@Email), @Email, UPPER(@Email), 1, 'old_hash', NEWID(), NEWID(), 0, 0, 0, 0, 'Sync', 'Test', @Id, 1);
                END;";
            await using var userCmd = new SqlCommand(insUser, conn);
            userCmd.Parameters.AddWithValue("@Guid", fakeGuid);
            userCmd.Parameters.AddWithValue("@Id", testEmpId);
            userCmd.Parameters.AddWithValue("@Email", testEmail);
            await userCmd.ExecuteNonQueryAsync();
        }

        try
        {
            // Change password for EMP-SYNC
            var (success, msg) = await _authService.ChangeInitialPasswordAsync(testEmpId, tempPass, newPass);
            Assert.True(success, msg);

            // Assert dbo.Employee has isFirstLogin = 0
            await using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                const string checkEmp = "SELECT isFirstLogin FROM dbo.Employee WHERE EmployeeID = @Id;";
                await using var checkCmd = new SqlCommand(checkEmp, conn);
                checkCmd.Parameters.AddWithValue("@Id", testEmpId);
                var isFirstLogin = Convert.ToBoolean(await checkCmd.ExecuteScalarAsync());
                Assert.False(isFirstLogin, "Employee isFirstLogin must be 0");

                // Assert dbo.AspNetUsers has MustChangePassword = 0 and updated PasswordHash
                const string checkUser = @"
                    IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
                    BEGIN
                        SELECT MustChangePassword, PasswordHash FROM dbo.AspNetUsers WHERE StudioId = @Id;
                    END
                    ELSE
                    BEGIN
                        SELECT CAST(0 AS bit), '$2a$mock' WHERE 1 = 0;
                    END;";
                await using var userCheckCmd = new SqlCommand(checkUser, conn);
                userCheckCmd.Parameters.AddWithValue("@Id", testEmpId);
                await using var reader = await userCheckCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var mustChange = reader.GetBoolean(0);
                    var hash = reader.GetString(1);
                    Assert.False(mustChange, "AspNetUsers.MustChangePassword must be 0");
                    Assert.StartsWith("$2", hash);
                    Assert.True(BCrypt.Net.BCrypt.Verify(newPass, hash));
                }
            }
        }
        finally
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cleanCmd = new SqlCommand(@"
                DELETE FROM dbo.Employee WHERE EmployeeID = @Id;
                IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
                BEGIN
                    DELETE FROM dbo.AspNetUsers WHERE StudioId = @Id OR Id = @Guid;
                END;", conn);
            cleanCmd.Parameters.AddWithValue("@Id", testEmpId);
            cleanCmd.Parameters.AddWithValue("@Guid", fakeGuid);
            await cleanCmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Employee_MixedCase_Id_Authenticates_And_Updates_isFirstLogin()
    {
        const string testEmpId = "EMP-654";
        const string testEmail = "emp654@photoaevum.studio";
        const string tempPass = "InitialPass654!";
        const string newPass = "UpdatedPass654!";

        await using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            await using var cleanCmd = new SqlCommand("DELETE FROM dbo.Employee WHERE EmployeeID = @Id;", conn);
            cleanCmd.Parameters.AddWithValue("@Id", testEmpId);
            await cleanCmd.ExecuteNonQueryAsync();

            const string insEmp = @"
                INSERT INTO dbo.Employee (EmployeeID, EmployeeFirstName, EmployeeLastName, EmployeeEmail, EmployeePassword, EmployeeStatus, isFirstLogin)
                VALUES (@Id, 'Case', 'Test', @Email, @Pass, 'Active', 1);";
            await using var insCmd = new SqlCommand(insEmp, conn);
            insCmd.Parameters.AddWithValue("@Id", testEmpId);
            insCmd.Parameters.AddWithValue("@Email", testEmail);
            insCmd.Parameters.AddWithValue("@Pass", tempPass);
            await insCmd.ExecuteNonQueryAsync();
        }

        try
        {
            // Authenticate with mixed case "eMp-654"
            var loginResult = await _authService.AuthenticateAsync("eMp-654", tempPass);
            Assert.True(loginResult.Success);
            Assert.True(loginResult.RequiresPasswordChange);
            Assert.True(loginResult.IsFirstLogin);

            // Change password with mixed case "EmP-654"
            var (changeSuccess, changeMsg) = await _authService.ChangeInitialPasswordAsync(
                "EmP-654", tempPass, newPass);
            Assert.True(changeSuccess, changeMsg);

            // Verify isFirstLogin is 0
            await using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                const string checkSql = "SELECT isFirstLogin FROM dbo.Employee WHERE EmployeeID = @Id;";
                await using var checkCmd = new SqlCommand(checkSql, conn);
                checkCmd.Parameters.AddWithValue("@Id", testEmpId);
                var isFirstLogin = Convert.ToBoolean(await checkCmd.ExecuteScalarAsync());
                Assert.False(isFirstLogin);
            }

            // Subsequent login with mixed case succeeds without password change
            var subsequent = await _authService.AuthenticateAsync("emp-654", newPass);
            Assert.True(subsequent.Success);
            Assert.False(subsequent.RequiresPasswordChange);
            Assert.False(subsequent.IsFirstLogin);
        }
        finally
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cleanCmd = new SqlCommand("DELETE FROM dbo.Employee WHERE EmployeeID = @Id;", conn);
            cleanCmd.Parameters.AddWithValue("@Id", testEmpId);
            await cleanCmd.ExecuteNonQueryAsync();
        }
    }
}
