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
            formData.Add(new StringContent(string.IsNullOrWhiteSpace(model.RateType) ? "per hour" : model.RateType.Trim()), "RateType");

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

    // ═════════════════════════════════════════════════════════════════════
    //  Featured services + public Portfolio mapping
    //  Direct SQL on purpose: the public Home page is anonymous, so it
    //  cannot use the "AevumApi.Authorized" HttpClient.
    // ═════════════════════════════════════════════════════════════════════

    private static readonly SemaphoreSlim FeaturedSchemaLock = new(1, 1);
    private static bool _featuredSchemaReady;

    /// <summary>
    /// Creates the three helper tables if they do not exist yet. If your DB login has no DDL
    /// rights, run this script once by hand instead (same SQL as below).
    /// </summary>
    private async Task EnsureFeaturedSchemaAsync(SqlConnection conn, CancellationToken ct)
    {
        if (_featuredSchemaReady) return;

        await FeaturedSchemaLock.WaitAsync(ct);
        try
        {
            if (_featuredSchemaReady) return;

            const string ddl = @"
                IF OBJECT_ID(N'dbo.FeaturedService', N'U') IS NULL
                    CREATE TABLE dbo.FeaturedService (
                        EntityType NVARCHAR(20)  NOT NULL,  -- Main | Package | AddOn
                        EntityCode NVARCHAR(50)  NOT NULL,  -- SRV-0001 / package code / ADD-0001
                        FeaturedAt DATETIME2     NOT NULL CONSTRAINT DF_FeaturedService_At DEFAULT SYSUTCDATETIME(),
                        CONSTRAINT PK_FeaturedService PRIMARY KEY (EntityType, EntityCode)
                    );

                IF OBJECT_ID(N'dbo.PortfolioCategory', N'U') IS NULL
                    CREATE TABLE dbo.PortfolioCategory (
                        CategoryID    NVARCHAR(30)  NOT NULL CONSTRAINT PK_PortfolioCategory PRIMARY KEY,
                        CategoryName  NVARCHAR(120) NOT NULL,
                        Description   NVARCHAR(400) NULL,
                        ParentSection NVARCHAR(30)  NOT NULL,  -- Photography | Videography
                        ColorTag      NVARCHAR(20)  NOT NULL CONSTRAINT DF_PortfolioCategory_Color DEFAULT 'Blue',
                        IsActive      BIT           NOT NULL CONSTRAINT DF_PortfolioCategory_Active DEFAULT 1,
                        UpdatedAt     DATETIME2     NOT NULL CONSTRAINT DF_PortfolioCategory_At DEFAULT SYSUTCDATETIME()
                    );

                IF OBJECT_ID(N'dbo.PortfolioCategoryService', N'U') IS NULL
                    CREATE TABLE dbo.PortfolioCategoryService (
                        CategoryID   NVARCHAR(30)  NOT NULL,
                        ServiceName  NVARCHAR(120) NOT NULL,
                        CONSTRAINT PK_PortfolioCategoryService PRIMARY KEY (CategoryID, ServiceName),
                        CONSTRAINT FK_PortfolioCategoryService_Category FOREIGN KEY (CategoryID)
                            REFERENCES dbo.PortfolioCategory (CategoryID) ON DELETE CASCADE
                    );";

            await using var cmd = new SqlCommand(ddl, conn);
            await cmd.ExecuteNonQueryAsync(ct);
            _featuredSchemaReady = true;
        }
        finally
        {
            FeaturedSchemaLock.Release();
        }
    }

    public async Task<IReadOnlySet<string>> GetFeaturedCodesAsync(string entityType, CancellationToken cancellationToken = default)
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            await EnsureFeaturedSchemaAsync(conn, cancellationToken);

            await using var cmd = new SqlCommand(
                "SELECT EntityCode FROM dbo.FeaturedService WHERE EntityType = @Type;", conn);
            cmd.Parameters.AddWithValue("@Type", entityType);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                codes.Add(reader.GetString(0));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load featured codes for {EntityType}.", entityType);
        }
        return codes;
    }

    public async Task<bool> SetFeaturedAsync(string entityType, string code, bool isFeatured, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(code))
            return false;

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            await EnsureFeaturedSchemaAsync(conn, cancellationToken);

            const string sql = @"
                IF @IsFeatured = 1
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM dbo.FeaturedService WHERE EntityType = @Type AND EntityCode = @Code)
                        INSERT INTO dbo.FeaturedService (EntityType, EntityCode) VALUES (@Type, @Code);
                END
                ELSE
                BEGIN
                    DELETE FROM dbo.FeaturedService WHERE EntityType = @Type AND EntityCode = @Code;
                END";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Type", entityType);
            cmd.Parameters.AddWithValue("@Code", code.Trim());
            cmd.Parameters.AddWithValue("@IsFeatured", isFeatured ? 1 : 0);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set featured={IsFeatured} for {EntityType} {Code}.", isFeatured, entityType, code);
            return false;
        }
    }

    /// <summary>Returns every category an admin has published to the portfolio (active or not).</summary>
    public async Task<IReadOnlyList<PortfolioCategoryDto>> GetPortfolioCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var map = new Dictionary<string, PortfolioCategoryDto>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            await EnsureFeaturedSchemaAsync(conn, cancellationToken);

            await using (var cmd = new SqlCommand(@"
                SELECT CategoryID, CategoryName, Description, ParentSection, ColorTag, IsActive
                FROM dbo.PortfolioCategory
                ORDER BY ParentSection, CategoryName;", conn))
            await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var dto = new PortfolioCategoryDto
                    {
                        Code = reader.GetString(0),
                        Name = reader.GetString(1),
                        Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                        ParentSection = reader.GetString(3),
                        ColorTag = reader.GetString(4),
                        IsActive = reader.GetBoolean(5),
                        ShowInPortfolio = true
                    };
                    map[dto.Code] = dto;
                }
            }

            await using (var cmd = new SqlCommand(
                "SELECT CategoryID, ServiceName FROM dbo.PortfolioCategoryService;", conn))
            await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (map.TryGetValue(reader.GetString(0), out var dto))
                        dto.ServiceNames.Add(reader.GetString(1));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load portfolio categories.");
        }
        return map.Values.ToList();
    }

    /// <summary>
    /// Upserts the category into the portfolio (or removes it when <c>ShowInPortfolio</c> is false)
    /// and replaces its mapped service names.
    /// </summary>
    public async Task<bool> SavePortfolioCategoryAsync(PortfolioCategoryDto category, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);
        if (string.IsNullOrWhiteSpace(category.Code)) return false;

        var section = PortfolioSections.All.FirstOrDefault(
            x => x.Equals(category.ParentSection, StringComparison.OrdinalIgnoreCase)) ?? PortfolioSections.Photography;

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            await EnsureFeaturedSchemaAsync(conn, cancellationToken);
            await using var trans = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);

            try
            {
                if (!category.ShowInPortfolio)
                {
                    // Child rows are removed by ON DELETE CASCADE.
                    await using var del = new SqlCommand(
                        "DELETE FROM dbo.PortfolioCategory WHERE CategoryID = @Id;", conn, trans);
                    del.Parameters.AddWithValue("@Id", category.Code.Trim());
                    await del.ExecuteNonQueryAsync(cancellationToken);
                }
                else
                {
                    const string upsert = @"
                        MERGE dbo.PortfolioCategory WITH (HOLDLOCK) AS t
                        USING (SELECT @Id AS CategoryID) AS s ON t.CategoryID = s.CategoryID
                        WHEN MATCHED THEN UPDATE SET
                            CategoryName = @Name, Description = @Desc, ParentSection = @Section,
                            ColorTag = @Color, IsActive = @Active, UpdatedAt = SYSUTCDATETIME()
                        WHEN NOT MATCHED THEN INSERT (CategoryID, CategoryName, Description, ParentSection, ColorTag, IsActive)
                            VALUES (@Id, @Name, @Desc, @Section, @Color, @Active);";

                    await using var up = new SqlCommand(upsert, conn, trans);
                    up.Parameters.AddWithValue("@Id", category.Code.Trim());
                    up.Parameters.AddWithValue("@Name", category.Name.Trim());
                    up.Parameters.AddWithValue("@Desc", (object?)category.Description?.Trim() ?? DBNull.Value);
                    up.Parameters.AddWithValue("@Section", section);
                    up.Parameters.AddWithValue("@Color", string.IsNullOrWhiteSpace(category.ColorTag) ? "Blue" : category.ColorTag);
                    up.Parameters.AddWithValue("@Active", category.IsActive);
                    await up.ExecuteNonQueryAsync(cancellationToken);

                    await using (var clear = new SqlCommand(
                        "DELETE FROM dbo.PortfolioCategoryService WHERE CategoryID = @Id;", conn, trans))
                    {
                        clear.Parameters.AddWithValue("@Id", category.Code.Trim());
                        await clear.ExecuteNonQueryAsync(cancellationToken);
                    }

                    foreach (var name in category.ServiceNames
                                 .Where(n => !string.IsNullOrWhiteSpace(n))
                                 .Select(n => n.Trim())
                                 .Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        await using var ins = new SqlCommand(
                            "INSERT INTO dbo.PortfolioCategoryService (CategoryID, ServiceName) VALUES (@Id, @Name);", conn, trans);
                        ins.Parameters.AddWithValue("@Id", category.Code.Trim());
                        ins.Parameters.AddWithValue("@Name", name);
                        await ins.ExecuteNonQueryAsync(cancellationToken);
                    }
                }

                await trans.CommitAsync(cancellationToken);
                return true;
            }
            catch
            {
                await trans.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save portfolio category {Code}.", category.Code);
            return false;
        }
    }
}
