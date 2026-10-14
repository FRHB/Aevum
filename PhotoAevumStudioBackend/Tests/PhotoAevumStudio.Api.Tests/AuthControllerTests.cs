using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PhotoAevumStudio.Application.Auth;
using Xunit;

namespace PhotoAevumStudio.Api.Tests;

[Collection("DatabaseIntegration")]
public class AuthControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _connectionString;

    public AuthControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
        });
        _client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        _connectionString = config.GetConnectionString("DefaultConnection")!;
    }

    [Fact]
    public async Task EndToEnd_FirstLogin_PasswordChange_And_SubsequentLogin_OverHttp()
    {
        // 1. Arrange: Reset ADM-001 to first-login state
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
            // 2. Act: Login as ADM-001 with initial temporary password
            var loginPayload = new LoginRequest("ADM-001", "AxiomBit@2026");
            var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginPayload);

            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
            var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.NotNull(loginBody);

            // Assert: Must require password change, and JWT MUST be withheld (null)
            Assert.True(loginBody.RequiresPasswordChange, "Response must require password change.");
            Assert.True(loginBody.MustChangePassword, "MustChangePassword flag must be true.");
            Assert.True(loginBody.IsFirstLogin, "IsFirstLogin flag must be true.");
            Assert.Null(loginBody.Token);
            Assert.Equal("ADM-001", loginBody.UserId);

            // 3. Act: Submit initial password change
            const string newPassword = "UpdatedSecurePass2026!";
            var changePayload = new InitialPasswordChangeRequest("ADM-001", "AxiomBit@2026", newPassword, newPassword);
            var changeResponse = await _client.PostAsJsonAsync("/api/auth/initial-password-change", changePayload);

            Assert.Equal(HttpStatusCode.OK, changeResponse.StatusCode);

            // 4. Assert: In SQL Server database, isFirstLogin is 0 and password is a BCrypt hash
            await using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                const string checkSql = "SELECT AdminPassword, isFirstLogin FROM dbo.Admin WHERE AdminID = 'ADM-001';";
                await using var cmd = new SqlCommand(checkSql, conn);
                await using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                var hash = reader.GetString(0);
                var isFirstLogin = reader.GetBoolean(1);

                Assert.False(isFirstLogin);
                Assert.StartsWith("$2", hash);
                Assert.True(BCrypt.Net.BCrypt.Verify(newPassword, hash));
            }

            // 5. Act: Subsequent login with new password
            var newLoginPayload = new LoginRequest("ADM-001", newPassword);
            var newLoginResponse = await _client.PostAsJsonAsync("/api/auth/login", newLoginPayload);

            Assert.Equal(HttpStatusCode.OK, newLoginResponse.StatusCode);
            var newLoginBody = await newLoginResponse.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.NotNull(newLoginBody);

            // Assert: Standard JWT token is issued, RequiresPasswordChange is false
            Assert.False(newLoginBody.RequiresPasswordChange);
            Assert.False(newLoginBody.MustChangePassword);
            Assert.False(newLoginBody.IsFirstLogin);
            Assert.NotNull(newLoginBody.Token);
            Assert.NotEmpty(newLoginBody.Token);

            // 6. Act: Login with old temporary password must now be rejected
            var oldLoginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginPayload);
            Assert.Equal(HttpStatusCode.Unauthorized, oldLoginResponse.StatusCode);
        }
        finally
        {
            // Reset ADM-001 to first-login state for ongoing dev work
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
    public async Task EndToEnd_Employee_FirstLogin_PasswordChange_And_SubsequentLogin_OverHttp()
    {
        const string empId = "EMP-888";
        const string empEmail = "emp888@photoaevum.studio";
        const string tempPass = "InitialEmpPass888!";
        const string newPass = "UpdatedEmpPass999!";

        // 1. Arrange: Ensure EMP-888 exists with isFirstLogin = 1
        await using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            const string setupSql = @"
                DELETE FROM dbo.Employee WHERE EmployeeID = @EmpId;
                INSERT INTO dbo.Employee (EmployeeID, EmployeeFirstName, EmployeeLastName, EmployeeMiddleName, EmployeeEmail, EmployeePassword, EmployeeStatus, isFirstLogin)
                VALUES (@EmpId, 'Http', 'Employee', NULL, @EmpEmail, @Pass, 'Active', 1);";
            await using var cmd = new SqlCommand(setupSql, conn);
            cmd.Parameters.AddWithValue("@EmpId", empId);
            cmd.Parameters.AddWithValue("@EmpEmail", empEmail);
            cmd.Parameters.AddWithValue("@Pass", tempPass);
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            // 2. Act: First login over HTTP
            var loginPayload = new LoginRequest(empId, tempPass);
            var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginPayload);

            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
            var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.NotNull(loginBody);

            Assert.True(loginBody.RequiresPasswordChange);
            Assert.True(loginBody.MustChangePassword);
            Assert.True(loginBody.IsFirstLogin);
            Assert.Null(loginBody.Token);
            Assert.Equal(empId, loginBody.UserId);

            // 3. Act: Submit initial password change over HTTP
            var changePayload = new InitialPasswordChangeRequest(empId, tempPass, newPass, newPass);
            var changeResponse = await _client.PostAsJsonAsync("/api/auth/initial-password-change", changePayload);

            Assert.Equal(HttpStatusCode.OK, changeResponse.StatusCode);

            // 4. Assert: In database, isFirstLogin is 0 and password is a BCrypt hash
            await using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                const string checkSql = "SELECT EmployeePassword, isFirstLogin FROM dbo.Employee WHERE EmployeeID = @EmpId;";
                await using var cmd = new SqlCommand(checkSql, conn);
                cmd.Parameters.AddWithValue("@EmpId", empId);
                await using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                var hash = reader.GetString(0);
                var isFirstLogin = reader.GetBoolean(1);

                Assert.False(isFirstLogin);
                Assert.StartsWith("$2", hash);
                Assert.True(BCrypt.Net.BCrypt.Verify(newPass, hash));
            }

            // 5. Act: Subsequent login with new password
            var newLoginPayload = new LoginRequest(empId, newPass);
            var newLoginResponse = await _client.PostAsJsonAsync("/api/auth/login", newLoginPayload);

            Assert.Equal(HttpStatusCode.OK, newLoginResponse.StatusCode);
            var newLoginBody = await newLoginResponse.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.NotNull(newLoginBody);

            Assert.False(newLoginBody.RequiresPasswordChange);
            Assert.False(newLoginBody.MustChangePassword);
            Assert.False(newLoginBody.IsFirstLogin);
            Assert.NotNull(newLoginBody.Token);
            Assert.NotEmpty(newLoginBody.Token);

            // 6. Act: Login with old temporary password must be rejected
            var oldLoginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginPayload);
            Assert.Equal(HttpStatusCode.Unauthorized, oldLoginResponse.StatusCode);
        }
        finally
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("DELETE FROM dbo.Employee WHERE EmployeeID = @EmpId;", conn);
            cmd.Parameters.AddWithValue("@EmpId", empId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Theory]
    [InlineData("", "AxiomBit@2026", HttpStatusCode.Unauthorized)]
    [InlineData("ADM-001", "", HttpStatusCode.Unauthorized)]
    [InlineData("NON_EXISTENT_USER", "Password123!", HttpStatusCode.Unauthorized)]
    public async Task Login_InvalidOrEmptyCredentials_ReturnsUnauthorized(string userId, string password, HttpStatusCode expectedStatus)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userId, password));
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData("ADM-001", "AxiomBit@2026", "short", "short")] // < 8 characters
    [InlineData("ADM-001", "AxiomBit@2026", "Password123!", "MismatchPass123!")] // Mismatched confirm
    [InlineData("ADM-001", "AxiomBit@2026", "AxiomBit@2026", "AxiomBit@2026")] // Same as current
    public async Task InitialPasswordChange_ValidationFailures_ReturnsBadRequest(
        string userId, string currentPassword, string newPassword, string confirmPassword)
    {
        var payload = new InitialPasswordChangeRequest(userId, currentPassword, newPassword, confirmPassword);
        var response = await _client.PostAsJsonAsync("/api/auth/initial-password-change", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EndToEnd_Authenticated_ChangePassword_Sets_isFirstLogin_To_0_And_Enables_Subsequent_StandardLogin()
    {
        const string empId = "EMP-444";
        const string empEmail = "emp444@photoaevum.studio";
        const string initialTempPass = "InitialEmpPass444!";
        const string firstChangePass = "FirstChangedPass555!";
        const string secondChangePass = "SecondChangedPass666!";

        // 1. Arrange: Employee with isFirstLogin = 1
        await using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            const string setupSql = @"
                DELETE FROM dbo.Employee WHERE EmployeeID = @EmpId;
                INSERT INTO dbo.Employee (EmployeeID, EmployeeFirstName, EmployeeLastName, EmployeeMiddleName, EmployeeEmail, EmployeePassword, EmployeeStatus, isFirstLogin)
                VALUES (@EmpId, 'AuthChange', 'Employee', NULL, @EmpEmail, @Pass, 'Active', 1);";
            await using var cmd = new SqlCommand(setupSql, conn);
            cmd.Parameters.AddWithValue("@EmpId", empId);
            cmd.Parameters.AddWithValue("@EmpEmail", empEmail);
            cmd.Parameters.AddWithValue("@Pass", initialTempPass);
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            // 2. Perform initial password change to obtain a valid authenticated state
            var initialChangePayload = new InitialPasswordChangeRequest(empId, initialTempPass, firstChangePass, firstChangePass);
            var initialChangeResp = await _client.PostAsJsonAsync("/api/auth/initial-password-change", initialChangePayload);
            Assert.Equal(HttpStatusCode.OK, initialChangeResp.StatusCode);

            // 3. Login to get JWT
            var loginResp = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(empId, firstChangePass));
            Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
            var loginBody = await loginResp.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.NotNull(loginBody?.Token);
            Assert.False(loginBody.IsFirstLogin);

            // 4. Call authenticated /api/auth/change-password endpoint with Bearer token
            using var authedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
            authedRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginBody.Token);
            authedRequest.Content = JsonContent.Create(new ChangePasswordRequest(firstChangePass, secondChangePass, secondChangePass));

            var authedResponse = await _client.SendAsync(authedRequest);
            Assert.Equal(HttpStatusCode.OK, authedResponse.StatusCode);

            // 5. Verify database directly: isFirstLogin is 0 and password is updated
            await using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                const string checkSql = "SELECT EmployeePassword, isFirstLogin FROM dbo.Employee WHERE EmployeeID = @EmpId;";
                await using var cmd = new SqlCommand(checkSql, conn);
                cmd.Parameters.AddWithValue("@EmpId", empId);
                await using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                var hash = reader.GetString(0);
                var isFirstLogin = reader.GetBoolean(1);

                Assert.False(isFirstLogin, "isFirstLogin MUST remain 0 after authenticated password change");
                Assert.StartsWith("$2", hash);
                Assert.True(BCrypt.Net.BCrypt.Verify(secondChangePass, hash));
            }

            // 6. Subsequent login with second changed password
            var subsequentLogin = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(empId, secondChangePass));
            Assert.Equal(HttpStatusCode.OK, subsequentLogin.StatusCode);
            var subsequentBody = await subsequentLogin.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.NotNull(subsequentBody?.Token);
            Assert.False(subsequentBody.RequiresPasswordChange);
            Assert.False(subsequentBody.IsFirstLogin);
        }
        finally
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("DELETE FROM dbo.Employee WHERE EmployeeID = @EmpId;", conn);
            cmd.Parameters.AddWithValue("@EmpId", empId);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
