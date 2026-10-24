using System.Data;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public class ServicesApiClient : IServicesApiClient
{
    private readonly HttpClient _http;
    private readonly ITokenStore _tokenStore;
    private readonly string _connectionString;
    private readonly ILogger<ServicesApiClient> _logger;

    public ServicesApiClient(
        IHttpClientFactory httpClientFactory,
        ITokenStore tokenStore,
        IConfiguration configuration,
        ILogger<ServicesApiClient> logger)
    {
        _http = httpClientFactory.CreateClient("AevumApi.Authorized");
        _tokenStore = tokenStore;
        _logger = logger;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
    }

    public async Task<string> GetNextServiceIdAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _http.GetFromJsonAsync<JsonElement>("api/services/next-id", cancellationToken);
            if (res.TryGetProperty("nextId", out var nextIdProp) && !string.IsNullOrWhiteSpace(nextIdProp.GetString()))
            {
                return nextIdProp.GetString()!;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "API call to api/services/next-id failed. Falling back to direct database query.");
        }

        // Direct SQL query fallback
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            const string sql = @"
                SELECT 'SRV-' + RIGHT('0000' + CAST(
                    ISNULL(MAX(TRY_CAST(SUBSTRING(ServicesID, CHARINDEX('-', ServicesID) + 1, 20) AS INT)), 0) + 1
                AS VARCHAR(10)), 4)
                FROM dbo.Services
                WHERE ServicesID LIKE 'SRV-%';";

            await using var cmd = new SqlCommand(sql, conn);
            var val = await cmd.ExecuteScalarAsync(cancellationToken);
            return val?.ToString() ?? "SRV-0001";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Direct SQL fallback for next service ID failed.");
            return "SRV-0001";
        }
    }

    public async Task<string> GetNextCategoryIdAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _http.GetFromJsonAsync<JsonElement>("api/categories/next-id", cancellationToken);
            if (res.TryGetProperty("nextId", out var nextIdProp) && !string.IsNullOrWhiteSpace(nextIdProp.GetString()))
            {
                return nextIdProp.GetString()!;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "API call to api/categories/next-id failed. Falling back to direct database query.");
        }

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            const string sql = @"
                SELECT 'CAT-' + RIGHT('0000' + CAST(
                    ISNULL(MAX(TRY_CAST(SUBSTRING(ServiceCategoryID, CHARINDEX('-', ServiceCategoryID) + 1, 20) AS INT)), 0) + 1
                AS VARCHAR(10)), 4)
                FROM dbo.ServiceCategory
                WHERE ServiceCategoryID LIKE 'CAT-%';";

            await using var cmd = new SqlCommand(sql, conn);
            var val = await cmd.ExecuteScalarAsync(cancellationToken);
            return val?.ToString() ?? "CAT-0001";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Direct SQL fallback for next category ID failed.");
            return "CAT-0001";
        }
    }

    public async Task<IReadOnlyList<CategoryLookupItem>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _http.GetFromJsonAsync<List<CategoryLookupItem>>("api/categories", cancellationToken);
            if (res != null && res.Count > 0)
                return res;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "API call to api/categories failed. Querying database directly.");
        }

        var list = new List<CategoryLookupItem>();
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            const string sql = @"
                SELECT ServiceCategoryID, ServiceCategoryName, ServiceCategoryDescription
                FROM dbo.ServiceCategory
                ORDER BY ServiceCategoryName;";

            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new CategoryLookupItem
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? string.Empty : reader.GetString(2)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Direct query for categories failed.");
        }

        // Default baseline fallback if database has no categories yet
        if (list.Count == 0)
        {
            list.AddRange(new[]
            {
                new CategoryLookupItem { Id = "CAT-0001", Name = "Portrait Photography", Description = "Individual and family studio portraits." },
                new CategoryLookupItem { Id = "CAT-0002", Name = "Commercial & Branding", Description = "High-impact visual branding and product catalog." },
                new CategoryLookupItem { Id = "CAT-0003", Name = "Event & Wedding", Description = "Comprehensive wedding and milestone event coverage." },
                new CategoryLookupItem { Id = "CAT-0004", Name = "Product Photography", Description = "Studio product lighting and e-commerce setups." }
            });
        }

        return list;
    }

    public async Task<(bool Success, string? ErrorMessage, string? CreatedId)> CreateServiceAsync(MainServiceWizardModel model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        await _tokenStore.GetTokenAsync(cancellationToken);

        try
        {
            using var formData = new MultipartFormDataContent();
            formData.Add(new StringContent(model.Name.Trim()), "Name");
            formData.Add(new StringContent(model.Description.Trim()), "Description");
            formData.Add(new StringContent(model.Price.ToString(CultureInfo.InvariantCulture)), "Price");
            formData.Add(new StringContent(model.CategoryId.Trim()), "CategoryId");

            if (model.ImageBytes != null && model.ImageBytes.Length > 0)
            {
                var fileContent = new ByteArrayContent(model.ImageBytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(model.ImageMimeType ?? "image/jpeg");
                formData.Add(fileContent, "Image", model.ImageFileName ?? "service-upload.jpg");
            }

            var response = await _http.PostAsync("api/services", formData, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                string? createdId = null;
                if (json.TryGetProperty("id", out var idProp))
                {
                    createdId = idProp.GetString();
                }
                return (true, null, createdId ?? model.GeneratedId);
            }

            var errorDetail = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("API returned error: {StatusCode} {ErrorDetail}", response.StatusCode, errorDetail);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HTTP POST to api/services failed. Attempting database fallback.");
        }

        // Direct SQL fallback
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var trans = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);

            try
            {
                // Generate atomic next ID
                const string nextSql = @"
                    SELECT 'SRV-' + RIGHT('0000' + CAST(
                        ISNULL(MAX(TRY_CAST(SUBSTRING(ServicesID, CHARINDEX('-', ServicesID) + 1, 20) AS INT)), 0) + 1
                    AS VARCHAR(10)), 4)
                    FROM dbo.Services WITH (UPDLOCK, HOLDLOCK)
                    WHERE ServicesID LIKE 'SRV-%';";

                await using var idCmd = new SqlCommand(nextSql, conn, trans);
                var nextId = (await idCmd.ExecuteScalarAsync(cancellationToken))?.ToString() ?? "SRV-0001";

                const string insertSql = @"
                    INSERT INTO dbo.Services (
                        ServicesID, ServiceName, ServicesDescription, ServicePrice, ServiceImage, ServiceImageMimeType
                    )
                    VALUES (
                        @Id, @Name, @Desc, @Price, @Img, @Mime
                    );";

                await using var insertCmd = new SqlCommand(insertSql, conn, trans);
                insertCmd.Parameters.AddWithValue("@Id", nextId);
                insertCmd.Parameters.AddWithValue("@Name", model.Name.Trim());
                insertCmd.Parameters.AddWithValue("@Desc", model.Description.Trim());
                insertCmd.Parameters.AddWithValue("@Price", model.Price);
                insertCmd.Parameters.AddWithValue("@Img", (object?)model.ImageBytes ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@Mime", (object?)model.ImageMimeType ?? "image/jpeg");
                await insertCmd.ExecuteNonQueryAsync(cancellationToken);

                // Category Join
                if (!string.IsNullOrWhiteSpace(model.CategoryId))
                {
                    var joinId = "SCJ-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
                    const string joinSql = @"
                        IF EXISTS (SELECT 1 FROM dbo.ServiceCategory WHERE ServiceCategoryID = @CatId)
                        BEGIN
                            INSERT INTO dbo.ServiceCategoryJoins (ServiceCategoryJoinID, ServiceCategoryID, ServicesID)
                            VALUES (@JoinId, @CatId, @SrvId);
                        END";

                    await using var joinCmd = new SqlCommand(joinSql, conn, trans);
                    joinCmd.Parameters.AddWithValue("@JoinId", joinId);
                    joinCmd.Parameters.AddWithValue("@CatId", model.CategoryId.Trim());
                    joinCmd.Parameters.AddWithValue("@SrvId", nextId);
                    await joinCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                await trans.CommitAsync(cancellationToken);
                return (true, null, nextId);
            }
            catch
            {
                await trans.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception dbEx)
        {
            _logger.LogError(dbEx, "Failed to create service in database fallback.");
            return (false, dbEx.Message, null);
        }
    }

    public async Task<(bool Success, string? ErrorMessage, string? CreatedId)> CreateCategoryAsync(CategoryWizardModel model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        await _tokenStore.GetTokenAsync(cancellationToken);

        try
        {
            var payload = new
            {
                Name = model.Name.Trim(),
                Description = model.Description.Trim()
            };

            var response = await _http.PostAsJsonAsync("api/categories", payload, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                string? createdId = null;
                if (json.TryGetProperty("id", out var idProp))
                {
                    createdId = idProp.GetString();
                }
                return (true, null, createdId ?? model.GeneratedId);
            }

            var errorDetail = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("API returned error: {StatusCode} {ErrorDetail}", response.StatusCode, errorDetail);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HTTP POST to api/categories failed. Attempting database fallback.");
        }

        // Direct SQL fallback
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var trans = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);

            try
            {
                const string nextSql = @"
                    SELECT 'CAT-' + RIGHT('0000' + CAST(
                        ISNULL(MAX(TRY_CAST(SUBSTRING(ServiceCategoryID, CHARINDEX('-', ServiceCategoryID) + 1, 20) AS INT)), 0) + 1
                    AS VARCHAR(10)), 4)
                    FROM dbo.ServiceCategory WITH (UPDLOCK, HOLDLOCK)
                    WHERE ServiceCategoryID LIKE 'CAT-%';";

                await using var idCmd = new SqlCommand(nextSql, conn, trans);
                var nextId = (await idCmd.ExecuteScalarAsync(cancellationToken))?.ToString() ?? "CAT-0001";

                const string insertSql = @"
                    INSERT INTO dbo.ServiceCategory (ServiceCategoryID, ServiceCategoryName, ServiceCategoryDescription)
                    VALUES (@Id, @Name, @Desc);";

                await using var insertCmd = new SqlCommand(insertSql, conn, trans);
                insertCmd.Parameters.AddWithValue("@Id", nextId);
                insertCmd.Parameters.AddWithValue("@Name", model.Name.Trim());
                insertCmd.Parameters.AddWithValue("@Desc", model.Description.Trim());
                await insertCmd.ExecuteNonQueryAsync(cancellationToken);

                await trans.CommitAsync(cancellationToken);
                return (true, null, nextId);
            }
            catch
            {
                await trans.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception dbEx)
        {
            _logger.LogError(dbEx, "Failed to create category in database fallback.");
            return (false, dbEx.Message, null);
        }
    }

    public async Task<string> GetNextAdditionalServiceIdAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _http.GetFromJsonAsync<JsonElement>("api/additionalservices/next-id", cancellationToken);
            if (res.TryGetProperty("nextId", out var nextIdProp) && !string.IsNullOrWhiteSpace(nextIdProp.GetString()))
            {
                return nextIdProp.GetString()!;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "API call to api/additionalservices/next-id failed. Falling back to database sequence query.");
        }

        // Direct SQL query fallback
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            const string sql = @"
                IF OBJECT_ID(N'dbo.sp_GetNextAdditionalServiceID', N'P') IS NOT NULL
                BEGIN
                    EXEC dbo.sp_GetNextAdditionalServiceID;
                END
                ELSE
                BEGIN
                    SELECT 'ADD-' + RIGHT('0000' + CAST(
                        ISNULL(MAX(TRY_CAST(SUBSTRING(AdditionalServicesID, CHARINDEX('-', AdditionalServicesID) + 1, 20) AS INT)), 0) + 1
                    AS VARCHAR(10)), 4)
                    FROM dbo.AdditionalServices
                    WHERE AdditionalServicesID LIKE 'ADD-%';
                END";

            await using var cmd = new SqlCommand(sql, conn);
            var val = await cmd.ExecuteScalarAsync(cancellationToken);
            if (val != null && val != DBNull.Value && !string.IsNullOrWhiteSpace(val.ToString()))
            {
                return val.ToString()!;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get next Additional Service ID from database fallback.");
        }

        return "ADD-0001";
    }

    public async Task<IReadOnlyList<string>> GetAdditionalServiceCategoriesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var list = await _http.GetFromJsonAsync<List<string>>("api/additionalservices/categories", cancellationToken);
            if (list != null && list.Count > 0)
            {
                return list;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "API call to api/additionalservices/categories failed. Falling back to database query.");
        }

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            const string sql = @"
                IF OBJECT_ID(N'dbo.ServiceCategory', N'U') IS NOT NULL
                BEGIN
                    SELECT ServiceCategoryName FROM dbo.ServiceCategory ORDER BY ServiceCategoryName;
                END";

            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            var categories = new List<string>();
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                {
                    categories.Add(reader.GetString(0));
                }
            }

            if (categories.Count > 0) return categories;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get categories from database fallback.");
        }

        return new[]
        {
            "Portrait Photography",
            "Event & Gala Coverage",
            "Commercial & Branding",
            "Wedding & Nuptials",
            "Aerial & Drone Operations",
            "Post-Processing & Retouching",
            "Custom Framing & Prints",
            "Equipment & Studio Props"
        };
    }

    public async Task<(bool Success, string? ErrorMessage, string? CreatedId)> CreateAdditionalServiceAsync(
        AdditionalServiceWizardFormModel model,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        await _tokenStore.GetTokenAsync(cancellationToken);

        // Try API endpoint first
        try
        {
            using var formData = new MultipartFormDataContent();
            formData.Add(new StringContent(model.ServiceCategoryName.Trim()), "ServiceName");
            formData.Add(new StringContent(model.ServiceCategoryDescription.Trim()), "ServiceCategoryDescription");
            formData.Add(new StringContent(model.ServiceCategory.Trim()), "ServiceCategory");
            formData.Add(new StringContent(model.ServicePrice.ToString(CultureInfo.InvariantCulture)), "ServicePrice");

            if (model.ImageBytes != null && model.ImageBytes.Length > 0)
            {
                var byteContent = new ByteArrayContent(model.ImageBytes);
                byteContent.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(model.ImageMimeType) ? "image/jpeg" : model.ImageMimeType);
                formData.Add(byteContent, "ImageFile", string.IsNullOrWhiteSpace(model.ImageFileName) ? "addon-image.jpg" : model.ImageFileName);
            }

            var response = await _http.PostAsync("api/additionalservices", formData, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                string? createdId = null;
                if (result.TryGetProperty("id", out var idProp))
                {
                    createdId = idProp.GetString();
                }
                return (true, null, createdId ?? model.GeneratedId);
            }

            var errorDetail = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("API returned status {StatusCode}: {ErrorDetail}", response.StatusCode, errorDetail);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HTTP POST to api/additionalservices failed. Attempting database fallback.");
        }

        // Direct database fallback with atomic sequence stored procedure
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            const string procSql = @"
                IF OBJECT_ID(N'dbo.sp_CreateAdditionalService', N'P') IS NOT NULL
                BEGIN
                    DECLARE @OutID NVARCHAR(30);
                    EXEC dbo.sp_CreateAdditionalService
                        @AdditionalServiceName = @Name,
                        @AdditionalServicesDescription = @Desc,
                        @ServicePrice = @Price,
                        @CategoryName = @Category,
                        @AdditionalServiceImage = @Img,
                        @AdditionalServiceImageMimeType = @Mime,
                        @CreatedByAdminID = @AdminId,
                        @GeneratedID = @OutID OUTPUT;
                    SELECT @OutID;
                END
                ELSE
                BEGIN
                    DECLARE @NextVal INT;
                    IF EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'Seq_AdditionalServicesID' AND schema_id = SCHEMA_ID('dbo'))
                        SET @NextVal = NEXT VALUE FOR dbo.Seq_AdditionalServicesID;
                    ELSE
                        SELECT @NextVal = ISNULL(MAX(TRY_CAST(SUBSTRING(AdditionalServicesID, CHARINDEX('-', AdditionalServicesID) + 1, 20) AS INT)), 0) + 1
                        FROM dbo.AdditionalServices WITH (UPDLOCK, HOLDLOCK);

                    DECLARE @NewID NVARCHAR(30) = 'ADD-' + RIGHT('0000' + CAST(@NextVal AS VARCHAR(10)), 4);

                    INSERT INTO dbo.AdditionalServices (
                        AdditionalServicesID, AdditionalServiceName, AdditionalServicesDescription, ServicePrice, CategoryName, AdditionalServiceImage, AdditionalServiceImageMimeType
                    )
                    VALUES (@NewID, @Name, @Desc, @Price, @Category, @Img, @Mime);

                    SELECT @NewID;
                END";

            await using var cmd = new SqlCommand(procSql, conn);
            cmd.Parameters.AddWithValue("@Name", model.ServiceCategoryName.Trim());
            cmd.Parameters.AddWithValue("@Desc", model.ServiceCategoryDescription.Trim());
            cmd.Parameters.AddWithValue("@Price", model.ServicePrice);
            cmd.Parameters.AddWithValue("@Category", model.ServiceCategory.Trim());
            cmd.Parameters.Add("@Img", SqlDbType.VarBinary, -1).Value = (object?)model.ImageBytes ?? DBNull.Value;
            cmd.Parameters.AddWithValue("@Mime", (object?)model.ImageMimeType ?? "image/jpeg");
            cmd.Parameters.AddWithValue("@AdminId", "Admin");

            var resultId = (await cmd.ExecuteScalarAsync(cancellationToken))?.ToString() ?? model.GeneratedId;
            return (true, null, resultId);
        }
        catch (Exception dbEx)
        {
            _logger.LogError(dbEx, "Failed to create additional service in database fallback.");
            return (false, dbEx.Message, null);
        }
    }
}
