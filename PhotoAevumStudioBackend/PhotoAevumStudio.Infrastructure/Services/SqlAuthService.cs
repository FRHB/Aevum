using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Infrastructure.Services;

public class SqlAuthService : ISqlAuthService
{
    private readonly string _connectionString;
    private readonly ILogger<SqlAuthService> _logger;

    public SqlAuthService(IConfiguration configuration, ILogger<SqlAuthService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        _logger = logger;
    }

    private SqlConnection CreateConnection() => new(_connectionString);

    public async Task<AuthValidationResult> AuthenticateAsync(
        string userIdOrEmail,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userIdOrEmail) || string.IsNullOrWhiteSpace(password))
            return new AuthValidationResult(Success: false, ErrorMessage: "User ID and password are required.");

        var trimmedId = userIdOrEmail.Trim();
        var atIndex = trimmedId.IndexOf('@');
        var candidateAdminId = atIndex > 0 ? trimmedId[..atIndex] : trimmedId;

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        // 1. Check dbo.Admin
        const string adminQuery = @"
            SELECT AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName, AdminPassword, isFirstLogin
            FROM dbo.Admin
            WHERE AdminID = @UserId 
               OR AdminID = @CandidateAdminId 
               OR UPPER(AdminID) = UPPER(@CandidateAdminId) 
               OR UPPER(AdminID) = UPPER(@UserId);";

        await using (var adminCmd = new SqlCommand(adminQuery, conn))
        {
            adminCmd.Parameters.AddWithValue("@UserId", trimmedId);
            adminCmd.Parameters.AddWithValue("@CandidateAdminId", candidateAdminId);
            await using var reader = await adminCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var adminId = reader.GetString(reader.GetOrdinal("AdminID"));
                var adminRole = reader.GetString(reader.GetOrdinal("AdminRole"));
                var first = reader.GetString(reader.GetOrdinal("AdminFirstName"));
                var last = reader.GetString(reader.GetOrdinal("AdminLastName"));
                var middle = reader.IsDBNull(reader.GetOrdinal("AdminMiddleName")) ? null : reader.GetString(reader.GetOrdinal("AdminMiddleName"));
                var storedPassword = reader.GetString(reader.GetOrdinal("AdminPassword"));
                var isFirstLogin = reader.IsDBNull(reader.GetOrdinal("isFirstLogin")) || reader.GetBoolean(reader.GetOrdinal("isFirstLogin"));

                var fullName = string.Join(" ", new[] { first, middle, last }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var email = $"{adminId.ToLowerInvariant()}@photoaevum.studio";

                var authUser = new AuthUser(adminId, AppRoles.Admin, fullName, email, storedPassword, isFirstLogin);

                if (isFirstLogin)
                {
                    bool match = VerifyPasswordFlexible(password, storedPassword, allowPlainText: true);
                    if (!match)
                    {
                        _logger.LogWarning("Failed login for admin {AdminId} (invalid credentials on first login).", adminId);
                        return new AuthValidationResult(Success: false, ErrorMessage: "Invalid credentials.");
                    }

                    _logger.LogInformation("Admin {AdminId} first-time login detected. Access withheld until password change.", adminId);
                    return new AuthValidationResult(Success: true, RequiresPasswordChange: true, User: authUser, IsFirstLogin: true);
                }
                else
                {
                    bool match = VerifyPasswordStrict(password, storedPassword);
                    if (!match)
                    {
                        _logger.LogWarning("Failed login attempt for admin {AdminId}.", adminId);
                        return new AuthValidationResult(Success: false, ErrorMessage: "Invalid credentials.");
                    }

                    _logger.LogInformation("Successful standard login for admin {AdminId}.", adminId);
                    return new AuthValidationResult(Success: true, RequiresPasswordChange: false, User: authUser, IsFirstLogin: false);
                }
            }
        }

        // 2. Check dbo.Employee
        const string empQuery = @"
            SELECT EmployeeID, EmployeeFirstName, EmployeeLastName, EmployeeMiddleName, EmployeeEmail, EmployeePassword, EmployeeStatus, isFirstLogin
            FROM dbo.Employee
            WHERE EmployeeID = @UserId 
               OR EmployeeEmail = @UserId 
               OR EmployeeID = @CandidateAdminId 
               OR UPPER(EmployeeID) = UPPER(@CandidateAdminId) 
               OR UPPER(EmployeeID) = UPPER(@UserId) 
               OR UPPER(EmployeeEmail) = UPPER(@UserId);";

        await using (var empCmd = new SqlCommand(empQuery, conn))
        {
            empCmd.Parameters.AddWithValue("@UserId", trimmedId);
            empCmd.Parameters.AddWithValue("@CandidateAdminId", candidateAdminId);
            await using var reader = await empCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var empId = reader.GetString(reader.GetOrdinal("EmployeeID"));
                var first = reader.GetString(reader.GetOrdinal("EmployeeFirstName"));
                var last = reader.GetString(reader.GetOrdinal("EmployeeLastName"));
                var middle = reader.IsDBNull(reader.GetOrdinal("EmployeeMiddleName")) ? null : reader.GetString(reader.GetOrdinal("EmployeeMiddleName"));
                var empEmail = reader.IsDBNull(reader.GetOrdinal("EmployeeEmail")) ? $"{empId.ToLowerInvariant()}@photoaevum.studio" : reader.GetString(reader.GetOrdinal("EmployeeEmail"));
                var storedPassword = reader.IsDBNull(reader.GetOrdinal("EmployeePassword")) ? string.Empty : reader.GetString(reader.GetOrdinal("EmployeePassword"));
                var status = reader.GetString(reader.GetOrdinal("EmployeeStatus"));
                var isFirstLogin = reader.IsDBNull(reader.GetOrdinal("isFirstLogin")) || reader.GetBoolean(reader.GetOrdinal("isFirstLogin"));

                if (!string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Login rejected for inactive employee {EmployeeId}.", empId);
                    return new AuthValidationResult(Success: false, ErrorMessage: "Employee account is inactive.");
                }

                var fullName = string.Join(" ", new[] { first, middle, last }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var authUser = new AuthUser(empId, AppRoles.Employee, fullName, empEmail, storedPassword, isFirstLogin);

                if (isFirstLogin)
                {
                    bool match = VerifyPasswordFlexible(password, storedPassword, allowPlainText: true);
                    if (!match)
                    {
                        _logger.LogWarning("Failed login for employee {EmployeeId} (invalid credentials on first login).", empId);
                        return new AuthValidationResult(Success: false, ErrorMessage: "Invalid credentials.");
                    }

                    _logger.LogInformation("Employee {EmployeeId} first-time login detected. Access withheld until password change.", empId);
                    return new AuthValidationResult(Success: true, RequiresPasswordChange: true, User: authUser, IsFirstLogin: true);
                }
                else
                {
                    bool match = VerifyPasswordStrict(password, storedPassword);
                    if (!match)
                    {
                        _logger.LogWarning("Failed login attempt for employee {EmployeeId}.", empId);
                        return new AuthValidationResult(Success: false, ErrorMessage: "Invalid credentials.");
                    }

                    _logger.LogInformation("Successful standard login for employee {EmployeeId}.", empId);
                    return new AuthValidationResult(Success: true, RequiresPasswordChange: false, User: authUser, IsFirstLogin: false);
                }
            }
        }

        // 3. Check dbo.Customer
        const string custQuery = @"
            SELECT CustomerID, CustomerFirstName, CustomerLastName, CustomerMiddleName, CustomerEmail, CustomerPhoneNumber, CustomerPassword
            FROM dbo.Customer
            WHERE CustomerEmail = @UserId OR CustomerID = @UserId;";

        await using (var custCmd = new SqlCommand(custQuery, conn))
        {
            custCmd.Parameters.AddWithValue("@UserId", trimmedId);
            await using var reader = await custCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var custId = reader.GetString(reader.GetOrdinal("CustomerID"));
                var first = reader.GetString(reader.GetOrdinal("CustomerFirstName"));
                var last = reader.GetString(reader.GetOrdinal("CustomerLastName"));
                var middle = reader.IsDBNull(reader.GetOrdinal("CustomerMiddleName")) ? null : reader.GetString(reader.GetOrdinal("CustomerMiddleName"));
                var custEmail = reader.GetString(reader.GetOrdinal("CustomerEmail"));
                var storedPassword = reader.GetString(reader.GetOrdinal("CustomerPassword"));

                var fullName = string.Join(" ", new[] { first, middle, last }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var authUser = new AuthUser(custId, AppRoles.Customer, fullName, custEmail, storedPassword, IsFirstLogin: false);

                // Standard customer login strictly verifies against stored BCrypt hash
                bool match = VerifyPasswordStrict(password, storedPassword);

                if (!match)
                {
                    _logger.LogWarning("Failed login attempt for customer {Email}.", custEmail);
                    return new AuthValidationResult(Success: false, ErrorMessage: "Invalid credentials.");
                }

                _logger.LogInformation("Successful standard login for customer {Email}.", custEmail);
                return new AuthValidationResult(Success: true, RequiresPasswordChange: false, User: authUser, IsFirstLogin: false);
            }
        }

        _logger.LogWarning("Login attempt for unknown account {UserId}.", trimmedId);
        return new AuthValidationResult(Success: false, ErrorMessage: "Invalid credentials.");
    }

    public async Task<(bool Success, string Message)> ChangeInitialPasswordAsync(
        string userIdOrEmail,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userIdOrEmail) || string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
            return (false, "All fields are required.");

        if (currentPassword == newPassword)
            return (false, "New password cannot be the same as current password.");

        if (newPassword.Length < 8)
            return (false, "Password must be at least 8 characters long.");

        var trimmedId = userIdOrEmail.Trim();
        var atIndex = trimmedId.IndexOf('@');
        var candidateAdminId = atIndex > 0 ? trimmedId[..atIndex] : trimmedId;

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        // Check Admin
        const string adminCheck = @"
            SELECT AdminID, AdminPassword, isFirstLogin
            FROM dbo.Admin
            WHERE AdminID = @UserId 
               OR AdminID = @CandidateAdminId 
               OR UPPER(AdminID) = UPPER(@CandidateAdminId) 
               OR UPPER(AdminID) = UPPER(@UserId);";

        await using (var cmd = new SqlCommand(adminCheck, conn))
        {
            cmd.Parameters.AddWithValue("@UserId", trimmedId);
            cmd.Parameters.AddWithValue("@CandidateAdminId", candidateAdminId);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var adminId = reader.GetString(reader.GetOrdinal("AdminID"));
                var storedPassword = reader.GetString(reader.GetOrdinal("AdminPassword"));
                var isFirstLogin = reader.IsDBNull(reader.GetOrdinal("isFirstLogin")) || reader.GetBoolean(reader.GetOrdinal("isFirstLogin"));
                reader.Close();

                bool currentValid = VerifyPasswordFlexible(currentPassword, storedPassword, allowPlainText: isFirstLogin);
                if (!currentValid)
                    return (false, "Current password is incorrect.");

                // Hash new password using BCrypt
                string newHashed = BCrypt.Net.BCrypt.HashPassword(newPassword);

                // Update Admin password and isFirstLogin in DB
                const string updateSql = @"
                    UPDATE dbo.Admin
                    SET AdminPassword = @NewPassword, isFirstLogin = 0
                    WHERE AdminID = @AdminID;";
                await using var updateCmd = new SqlCommand(updateSql, conn);
                updateCmd.Parameters.AddWithValue("@NewPassword", newHashed);
                updateCmd.Parameters.AddWithValue("@AdminID", adminId);
                await updateCmd.ExecuteNonQueryAsync(cancellationToken);

                // Execute stored procedure dbo.sp_CompleteAdminFirstLogin
                await using (var spCmd = new SqlCommand("dbo.sp_CompleteAdminFirstLogin", conn)
                {
                    CommandType = CommandType.StoredProcedure
                })
                {
                    spCmd.Parameters.AddWithValue("@AdminID", adminId);
                    spCmd.Parameters.AddWithValue("@NewPassword", newHashed);
                    try
                    {
                        await spCmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                    catch (SqlException ex) when (ex.Message.Contains("too many arguments", StringComparison.OrdinalIgnoreCase))
                    {
                        spCmd.Parameters.RemoveAt("@NewPassword");
                        await spCmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Non-fatal error calling dbo.sp_CompleteAdminFirstLogin for {AdminId}.", adminId);
                    }
                }

                await TrySyncAspNetUsersAsync(conn, adminId, newHashed, cancellationToken);
                if (!string.Equals(adminId, trimmedId, StringComparison.OrdinalIgnoreCase))
                {
                    await TrySyncAspNetUsersAsync(conn, trimmedId, newHashed, cancellationToken);
                }

                _logger.LogInformation("Admin {AdminId} successfully completed initial password change and hashing.", adminId);
                return (true, "Password has been successfully updated.");
            }
        }

        // Check Employee
        const string empCheck = @"
            SELECT EmployeeID, EmployeeEmail, EmployeePassword, EmployeeStatus, isFirstLogin
            FROM dbo.Employee
            WHERE EmployeeID = @UserId 
               OR EmployeeEmail = @UserId 
               OR EmployeeID = @CandidateAdminId 
               OR UPPER(EmployeeID) = UPPER(@CandidateAdminId) 
               OR UPPER(EmployeeID) = UPPER(@UserId) 
               OR UPPER(EmployeeEmail) = UPPER(@UserId);";

        await using (var cmd = new SqlCommand(empCheck, conn))
        {
            cmd.Parameters.AddWithValue("@UserId", trimmedId);
            cmd.Parameters.AddWithValue("@CandidateAdminId", candidateAdminId);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var empId = reader.GetString(reader.GetOrdinal("EmployeeID"));
                var empEmail = reader.IsDBNull(reader.GetOrdinal("EmployeeEmail")) ? null : reader.GetString(reader.GetOrdinal("EmployeeEmail"));
                var storedPassword = reader.IsDBNull(reader.GetOrdinal("EmployeePassword")) ? string.Empty : reader.GetString(reader.GetOrdinal("EmployeePassword"));
                var status = reader.GetString(reader.GetOrdinal("EmployeeStatus"));
                var isFirstLogin = reader.IsDBNull(reader.GetOrdinal("isFirstLogin")) || reader.GetBoolean(reader.GetOrdinal("isFirstLogin"));
                reader.Close();

                if (!string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase))
                    return (false, "Employee account is inactive.");

                bool currentValid = VerifyPasswordFlexible(currentPassword, storedPassword, allowPlainText: isFirstLogin);
                if (!currentValid)
                    return (false, "Current password is incorrect.");

                // Hash new password using BCrypt
                string newHashed = BCrypt.Net.BCrypt.HashPassword(newPassword);

                // Update Employee password and isFirstLogin in DB
                const string updateSql = @"
                    UPDATE dbo.Employee
                    SET EmployeePassword = @NewPassword, isFirstLogin = 0
                    WHERE EmployeeID = @EmployeeID;";
                await using var updateCmd = new SqlCommand(updateSql, conn);
                updateCmd.Parameters.AddWithValue("@NewPassword", newHashed);
                updateCmd.Parameters.AddWithValue("@EmployeeID", empId);
                await updateCmd.ExecuteNonQueryAsync(cancellationToken);

                // Execute stored procedure dbo.sp_CompleteEmployeeFirstLogin
                await using (var spCmd = new SqlCommand("dbo.sp_CompleteEmployeeFirstLogin", conn)
                {
                    CommandType = CommandType.StoredProcedure
                })
                {
                    spCmd.Parameters.AddWithValue("@EmployeeID", empId);
                    spCmd.Parameters.AddWithValue("@NewPassword", newHashed);
                    try
                    {
                        await spCmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                    catch (SqlException ex) when (ex.Message.Contains("too many arguments", StringComparison.OrdinalIgnoreCase))
                    {
                        spCmd.Parameters.RemoveAt("@NewPassword");
                        await spCmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Non-fatal error calling dbo.sp_CompleteEmployeeFirstLogin for {EmployeeId}.", empId);
                    }
                }

                await TrySyncAspNetUsersAsync(conn, empId, newHashed, cancellationToken);
                if (!string.Equals(empId, trimmedId, StringComparison.OrdinalIgnoreCase))
                {
                    await TrySyncAspNetUsersAsync(conn, trimmedId, newHashed, cancellationToken);
                }
                if (!string.IsNullOrWhiteSpace(empEmail) && !string.Equals(empEmail, trimmedId, StringComparison.OrdinalIgnoreCase))
                {
                    await TrySyncAspNetUsersAsync(conn, empEmail, newHashed, cancellationToken);
                }

                _logger.LogInformation("Employee {EmployeeId} successfully completed initial password change and hashing.", empId);
                return (true, "Password has been successfully updated.");
            }
        }

        // Check Customer
        const string custCheck = @"
            SELECT CustomerID, CustomerPassword
            FROM dbo.Customer
            WHERE CustomerEmail = @UserId 
               OR CustomerID = @UserId 
               OR UPPER(CustomerEmail) = UPPER(@UserId) 
               OR UPPER(CustomerID) = UPPER(@UserId);";

        await using (var cmd = new SqlCommand(custCheck, conn))
        {
            cmd.Parameters.AddWithValue("@UserId", trimmedId);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var custId = reader.GetString(reader.GetOrdinal("CustomerID"));
                var storedPassword = reader.GetString(reader.GetOrdinal("CustomerPassword"));
                reader.Close();

                bool currentValid = VerifyPasswordFlexible(currentPassword, storedPassword, allowPlainText: false);
                if (!currentValid)
                    return (false, "Current password is incorrect.");

                string newHashed = BCrypt.Net.BCrypt.HashPassword(newPassword);

                const string updateCustSql = @"
                    UPDATE dbo.Customer
                    SET CustomerPassword = @NewPassword
                    WHERE CustomerID = @CustomerID;";
                await using var updateCmd = new SqlCommand(updateCustSql, conn);
                updateCmd.Parameters.AddWithValue("@NewPassword", newHashed);
                updateCmd.Parameters.AddWithValue("@CustomerID", custId);
                await updateCmd.ExecuteNonQueryAsync(cancellationToken);

                await TrySyncAspNetUsersAsync(conn, custId, newHashed, cancellationToken);
                if (!string.Equals(custId, trimmedId, StringComparison.OrdinalIgnoreCase))
                {
                    await TrySyncAspNetUsersAsync(conn, trimmedId, newHashed, cancellationToken);
                }

                _logger.LogInformation("Customer {CustomerId} successfully updated password.", custId);
                return (true, "Password has been successfully updated.");
            }
        }

        return (false, "User account not found.");
    }

    public async Task<(bool Success, string Message, string? CustomerId)> RegisterCustomerAsync(
        string firstName,
        string lastName,
        string? middleName,
        string email,
        string? phone,
        string password,
        CancellationToken cancellationToken = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        // Check if customer email already exists
        const string checkSql = "SELECT 1 FROM dbo.Customer WHERE CustomerEmail = @Email;";
        await using (var checkCmd = new SqlCommand(checkSql, conn))
        {
            checkCmd.Parameters.AddWithValue("@Email", email.Trim());
            var exists = await checkCmd.ExecuteScalarAsync(cancellationToken);
            if (exists is not null)
                return (false, "An account with that email already exists.", null);
        }

        // Generate next Customer ID: CUST-####
        const string maxIdSql = "SELECT TOP 1 CustomerID FROM dbo.Customer WHERE CustomerID LIKE 'CUST-%' ORDER BY CustomerID DESC;";
        int nextSeq = 1;
        await using (var maxCmd = new SqlCommand(maxIdSql, conn))
        {
            var maxVal = await maxCmd.ExecuteScalarAsync(cancellationToken) as string;
            if (!string.IsNullOrEmpty(maxVal) && maxVal.StartsWith("CUST-") && int.TryParse(maxVal[5..], out int curSeq))
            {
                nextSeq = curSeq + 1;
            }
        }
        string customerId = $"CUST-{nextSeq:D4}";

        // Hash password with BCrypt
        string hashedPassword = BCrypt.Net.BCrypt.HashPassword(password);

        // Execute stored procedure dbo.sp_CreateCustomer
        await using var cmd = new SqlCommand("dbo.sp_CreateCustomer", conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@CustomerID", customerId);
        cmd.Parameters.AddWithValue("@CustomerFirstName", firstName.Trim());
        cmd.Parameters.AddWithValue("@CustomerLastName", lastName.Trim());
        cmd.Parameters.AddWithValue("@CustomerMiddleName", string.IsNullOrWhiteSpace(middleName) ? DBNull.Value : middleName.Trim());
        cmd.Parameters.AddWithValue("@CustomerEmail", email.Trim());
        cmd.Parameters.AddWithValue("@CustomerPhoneNumber", string.IsNullOrWhiteSpace(phone) ? DBNull.Value : phone.Trim());
        cmd.Parameters.AddWithValue("@CustomerPassword", hashedPassword);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Created Customer {CustomerId} ({Email}) with BCrypt password hash.", customerId, email);

        return (true, "Customer registered successfully.", customerId);
    }

    public async Task<(bool Success, string Message)> RegisterEmployeeAsync(
        string employeeId,
        string firstName,
        string lastName,
        string? middleName,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        const string checkSql = "SELECT 1 FROM dbo.Employee WHERE EmployeeID = @EmpId OR EmployeeEmail = @Email;";
        await using (var checkCmd = new SqlCommand(checkSql, conn))
        {
            checkCmd.Parameters.AddWithValue("@EmpId", employeeId.Trim());
            checkCmd.Parameters.AddWithValue("@Email", email.Trim());
            var exists = await checkCmd.ExecuteScalarAsync(cancellationToken);
            if (exists is not null)
                return (false, "An employee with this ID or email already exists.");
        }

        // Hash starting password with BCrypt
        string hashedPassword = BCrypt.Net.BCrypt.HashPassword(password);

        // Execute stored procedure dbo.sp_CreateEmployee with isFirstLogin = 1
        await using var cmd = new SqlCommand("dbo.sp_CreateEmployee", conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@EmployeeID", employeeId.Trim());
        cmd.Parameters.AddWithValue("@EmployeeFirstName", firstName.Trim());
        cmd.Parameters.AddWithValue("@EmployeeLastName", lastName.Trim());
        cmd.Parameters.AddWithValue("@EmployeeMiddleName", string.IsNullOrWhiteSpace(middleName) ? DBNull.Value : middleName.Trim());
        cmd.Parameters.AddWithValue("@EmployeeEmail", email.Trim());
        cmd.Parameters.AddWithValue("@EmployeePassword", hashedPassword);
        cmd.Parameters.AddWithValue("@isFirstLogin", true);

        try
        {
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException)
        {
            const string insertSql = @"
                IF COL_LENGTH(N'dbo.Employee', N'isFirstLogin') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.Employee (EmployeeID, EmployeeFirstName, EmployeeLastName, EmployeeMiddleName, EmployeeEmail, EmployeePassword, EmployeeStatus, isFirstLogin)
                    VALUES (@EmployeeID, @EmployeeFirstName, @EmployeeLastName, @EmployeeMiddleName, @EmployeeEmail, @EmployeePassword, 'Active', 1);
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.Employee (EmployeeID, EmployeeFirstName, EmployeeLastName, EmployeeMiddleName, EmployeeEmail, EmployeePassword, EmployeeStatus)
                    VALUES (@EmployeeID, @EmployeeFirstName, @EmployeeLastName, @EmployeeMiddleName, @EmployeeEmail, @EmployeePassword, 'Active');
                END";
            await using var insertCmd = new SqlCommand(insertSql, conn);
            insertCmd.Parameters.AddWithValue("@EmployeeID", employeeId.Trim());
            insertCmd.Parameters.AddWithValue("@EmployeeFirstName", firstName.Trim());
            insertCmd.Parameters.AddWithValue("@EmployeeLastName", lastName.Trim());
            insertCmd.Parameters.AddWithValue("@EmployeeMiddleName", string.IsNullOrWhiteSpace(middleName) ? DBNull.Value : middleName.Trim());
            insertCmd.Parameters.AddWithValue("@EmployeeEmail", email.Trim());
            insertCmd.Parameters.AddWithValue("@EmployeePassword", hashedPassword);
            await insertCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        _logger.LogInformation("Created Employee {EmployeeId} ({Email}) with isFirstLogin = true.", employeeId, email);

        return (true, "Employee registered successfully.");
    }

    public async Task<(bool Success, string Message)> ResetPasswordAsync(
        string email,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        string hashedPassword = BCrypt.Net.BCrypt.HashPassword(newPassword);

        var trimmedEmail = email.Trim();
        var atIndex = trimmedEmail.IndexOf('@');
        var candidateAdminId = atIndex > 0 ? trimmedEmail[..atIndex] : trimmedEmail;

        // Try Admin
        const string updateAdmin = @"
            UPDATE dbo.Admin 
            SET AdminPassword = @Hash, isFirstLogin = 0 
            WHERE AdminID = @Email 
               OR AdminID = @CandidateAdminId 
               OR UPPER(AdminID) = UPPER(@CandidateAdminId) 
               OR UPPER(AdminID) = UPPER(@Email);";
        await using (var cmd = new SqlCommand(updateAdmin, conn))
        {
            cmd.Parameters.AddWithValue("@Hash", hashedPassword);
            cmd.Parameters.AddWithValue("@Email", trimmedEmail);
            cmd.Parameters.AddWithValue("@CandidateAdminId", candidateAdminId);
            if (await cmd.ExecuteNonQueryAsync(cancellationToken) > 0)
            {
                await TrySyncAspNetUsersAsync(conn, trimmedEmail, hashedPassword, cancellationToken);
                if (atIndex > 0)
                {
                    await TrySyncAspNetUsersAsync(conn, candidateAdminId, hashedPassword, cancellationToken);
                }
                return (true, "Password has been successfully reset.");
            }
        }

        // Try Employee
        const string updateEmp = @"
            UPDATE dbo.Employee 
            SET EmployeePassword = @Hash, isFirstLogin = 0 
            WHERE EmployeeEmail = @Email 
               OR EmployeeID = @Email 
               OR EmployeeID = @CandidateAdminId 
               OR UPPER(EmployeeID) = UPPER(@CandidateAdminId) 
               OR UPPER(EmployeeID) = UPPER(@Email) 
               OR UPPER(EmployeeEmail) = UPPER(@Email);";
        await using (var cmd = new SqlCommand(updateEmp, conn))
        {
            cmd.Parameters.AddWithValue("@Hash", hashedPassword);
            cmd.Parameters.AddWithValue("@Email", trimmedEmail);
            cmd.Parameters.AddWithValue("@CandidateAdminId", candidateAdminId);
            if (await cmd.ExecuteNonQueryAsync(cancellationToken) > 0)
            {
                await TrySyncAspNetUsersAsync(conn, trimmedEmail, hashedPassword, cancellationToken);
                if (atIndex > 0)
                {
                    await TrySyncAspNetUsersAsync(conn, candidateAdminId, hashedPassword, cancellationToken);
                }
                return (true, "Password has been successfully reset.");
            }
        }

        // Try Customer
        const string updateCust = @"
            UPDATE dbo.Customer 
            SET CustomerPassword = @Hash 
            WHERE CustomerEmail = @Email 
               OR CustomerID = @Email 
               OR UPPER(CustomerEmail) = UPPER(@Email) 
               OR UPPER(CustomerID) = UPPER(@Email);";
        await using (var cmd = new SqlCommand(updateCust, conn))
        {
            cmd.Parameters.AddWithValue("@Hash", hashedPassword);
            cmd.Parameters.AddWithValue("@Email", trimmedEmail);
            if (await cmd.ExecuteNonQueryAsync(cancellationToken) > 0)
            {
                await TrySyncAspNetUsersAsync(conn, trimmedEmail, hashedPassword, cancellationToken);
                return (true, "Password has been successfully reset.");
            }
        }

        return (false, "Account not found.");
    }

    private static async Task TrySyncAspNetUsersAsync(
        SqlConnection conn,
        string userIdentifier,
        string? passwordHash,
        CancellationToken cancellationToken)
    {
        try
        {
            const string syncSql = @"
                IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
                BEGIN
                    UPDATE dbo.AspNetUsers
                    SET MustChangePassword = 0,
                        PasswordChangedAt = SYSUTCDATETIME(),
                        PasswordHash = COALESCE(@PasswordHash, PasswordHash)
                    WHERE StudioId = @UserId 
                       OR UserName = @UserId 
                       OR Email = @UserId 
                       OR Id = @UserId 
                       OR UPPER(StudioId) = UPPER(@UserId) 
                       OR UPPER(UserName) = UPPER(@UserId) 
                       OR UPPER(Email) = UPPER(@UserId);

                    IF COL_LENGTH(N'dbo.AspNetUsers', N'isFirstLogin') IS NOT NULL
                    BEGIN
                        EXEC(N'UPDATE dbo.AspNetUsers SET isFirstLogin = 0 WHERE StudioId = @U OR UserName = @U OR Email = @U OR Id = @U OR UPPER(StudioId) = UPPER(@U) OR UPPER(UserName) = UPPER(@U) OR UPPER(Email) = UPPER(@U);', N'@U nvarchar(256)', @U = @UserId);
                    END;
                END;";
            await using var cmd = new SqlCommand(syncSql, conn);
            cmd.Parameters.AddWithValue("@UserId", userIdentifier);
            cmd.Parameters.AddWithValue("@PasswordHash", (object?)passwordHash ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            // Best effort identity sync
        }
    }

    private static bool VerifyPasswordStrict(string provided, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(provided) || string.IsNullOrWhiteSpace(storedHash))
            return false;

        try
        {
            return BCrypt.Net.BCrypt.Verify(provided, storedHash);
        }
        catch
        {
            // Standard login endpoints must strictly verify against stored hash, not plain text
            return false;
        }
    }

    private static bool VerifyPasswordFlexible(string provided, string stored, bool allowPlainText)
    {
        if (string.IsNullOrWhiteSpace(provided) || string.IsNullOrWhiteSpace(stored))
            return false;

        try
        {
            if (BCrypt.Net.BCrypt.Verify(provided, stored))
                return true;
        }
        catch
        {
            // Fall through to plain text check if allowed
        }

        if (allowPlainText && stored == provided)
            return true;

        return false;
    }
}
