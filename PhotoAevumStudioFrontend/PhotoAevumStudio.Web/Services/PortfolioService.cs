using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public class PortfolioService : IPortfolioService
{
    private readonly string _connectionString;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<PortfolioService>? _logger;
    private readonly AmazonS3Client? _r2Client;
    private readonly string _r2BucketName;
    private readonly string _r2PublicBaseUrl;
    private readonly bool _r2Configured;

    public PortfolioService(IConfiguration? configuration, IWebHostEnvironment env, ILogger<PortfolioService>? logger = null)
    {
        _env = env;
        _logger = logger;
        _connectionString = configuration?.GetConnectionString("DefaultConnection")
            ?? "Data Source=localhost\\SQLEXPRESS;Initial Catalog=PhotoAevumStudio;Integrated Security=True;Persist Security Info=False;Pooling=False;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=True;Application Name=\"SQL Server Management Studio\";Command Timeout=0";

        var accessKey = configuration?["CloudflareR2:AccessKey"] ?? "PLACEHOLDER";
        var secretKey = configuration?["CloudflareR2:SecretKey"] ?? "PLACEHOLDER";
        var accountId = configuration?["CloudflareR2:AccountId"];
        var serviceUrl = configuration?["CloudflareR2:ServiceUrl"];
        if (string.IsNullOrWhiteSpace(serviceUrl))
        {
            serviceUrl = !string.IsNullOrWhiteSpace(accountId)
                ? $"https://{accountId}.r2.cloudflarestorage.com"
                : "https://f6757b400d51c23671cedb8b0efc4b32.r2.cloudflarestorage.com";
        }

        _r2BucketName = configuration?["CloudflareR2:Buckets:Portfolio"]
                        ?? configuration?["CloudflareR2:BucketName"]
                        ?? "portfolio";
        _r2PublicBaseUrl = configuration?["CloudflareR2:PublicUrl"] ?? "https://pub-photoaevum.r2.dev";

        _r2Configured = !string.IsNullOrWhiteSpace(accessKey) 
                        && !string.IsNullOrWhiteSpace(secretKey) 
                        && accessKey != "PLACEHOLDER" 
                        && secretKey != "PLACEHOLDER";

        try
        {
            var credentials = new BasicAWSCredentials(accessKey, secretKey);
            var s3Config = new AmazonS3Config
            {
                ServiceURL = serviceUrl,
                AuthenticationRegion = "auto",
                ForcePathStyle = true
            };
            _r2Client = new AmazonS3Client(credentials, s3Config);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to initialize AmazonS3Client for Cloudflare R2.");
        }
    }

    private async Task<string> UploadToR2Async(string uniqueFileName, byte[] data, string contentType, CancellationToken ct)
    {
        var key = $"portfolio/{uniqueFileName}";
        if (_r2Configured && _r2Client != null)
        {
            try
            {
                using var ms = new MemoryStream(data);
                var putReq = new PutObjectRequest
                {
                    BucketName = _r2BucketName,
                    Key = key,
                    InputStream = ms,
                    ContentType = string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType,
                    DisablePayloadSigning = true
                };
                await _r2Client.PutObjectAsync(putReq, ct);
                return $"{_r2PublicBaseUrl.TrimEnd('/')}/{key}";
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Cloudflare R2 upload error for {Key}", key);
            }
        }
        return $"{_r2PublicBaseUrl.TrimEnd('/')}/{key}";
    }

    private async Task DeleteFromR2Async(string fileName, CancellationToken ct)
    {
        if (!_r2Configured || _r2Client == null || string.IsNullOrWhiteSpace(fileName)) return;
        try
        {
            var key = fileName;
            if (key.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || key.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(key);
                key = uri.AbsolutePath.TrimStart('/');
            }
            if (!key.StartsWith("portfolio/")) key = $"portfolio/{key}";

            var delReq = new DeleteObjectRequest
            {
                BucketName = _r2BucketName,
                Key = key
            };
            await _r2Client.DeleteObjectAsync(delReq, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to delete R2 object {FileName}", fileName);
        }
    }

    private SqlConnection CreateConnection() => new SqlConnection(_connectionString);

    public async Task<List<PortfolioCategoryModel>> GetCategoriesAsync(CancellationToken ct = default)
    {
        var list = new List<PortfolioCategoryModel>();

        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(ct);

            const string sql = @"
                SELECT pc.CategoryID,
                       pc.CategoryName,
                       pc.Description,
                       pc.ParentSection,
                       pc.ColorTag,
                       pc.IsActive,
                       pc.CreatedAt,
                       (SELECT COUNT(*) FROM dbo.Portfolios p WHERE p.CategoryID = pc.CategoryID) AS PortfolioCount
                FROM dbo.PortfolioCategory pc
                ORDER BY pc.CategoryName;";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                var catId = reader.GetString(0);
                list.Add(new PortfolioCategoryModel
                {
                    CategoryID = catId,
                    CategoryName = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    ParentSection = reader.IsDBNull(3) ? "Photography" : reader.GetString(3),
                    ColorTag = reader.IsDBNull(4) ? "Blue" : reader.GetString(4),
                    IsActive = reader.GetBoolean(5),
                    CreatedAt = reader.GetDateTime(6),
                    PortfolioCount = reader.GetInt32(7)
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load portfolio categories.");
        }

        return list;
    }

    public async Task<PortfolioCategoryModel?> CreateCategoryAsync(string name, string? description, string parentSection, string colorTag, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(ct);

            string nextId;
            const string idSql = @"
                DECLARE @NextID nvarchar(30);
                IF OBJECT_ID(N'dbo.sp_GetNextPortfolioCategoryID', N'P') IS NOT NULL
                BEGIN
                    EXEC dbo.sp_GetNextPortfolioCategoryID @NextCategoryID = @NextID OUTPUT;
                END
                ELSE
                BEGIN
                    DECLARE @MaxNum INT = 0;
                    SELECT @MaxNum = ISNULL(MAX(TRY_CAST(SUBSTRING(CategoryID, CHARINDEX('-', CategoryID) + 1, 20) AS INT)), 0)
                    FROM dbo.PortfolioCategory
                    WHERE CategoryID LIKE 'PCAT-%';
                    SET @NextID = 'PCAT-' + RIGHT('0000' + CAST((@MaxNum + 1) AS VARCHAR(10)), 4);
                END
                SELECT @NextID;";

            using (var idCmd = new SqlCommand(idSql, conn))
            {
                var val = await idCmd.ExecuteScalarAsync(ct);
                nextId = val?.ToString() ?? ("PCAT-" + DateTime.UtcNow.Ticks.ToString()[^4..]);
            }

            const string insertSql = @"
                INSERT INTO dbo.PortfolioCategory 
                    (CategoryID, CategoryName, Description, ParentSection, ColorTag, IsActive, CreatedAt, UpdatedAt)
                VALUES 
                    (@Id, @Name, @Desc, @Section, @Color, 1, SYSUTCDATETIME(), SYSUTCDATETIME());";

            using (var cmd = new SqlCommand(insertSql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", nextId);
                cmd.Parameters.AddWithValue("@Name", name.Trim());
                cmd.Parameters.AddWithValue("@Desc", ((object?)description?.Trim()) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Section", string.IsNullOrWhiteSpace(parentSection) ? "Photography" : parentSection.Trim());
                cmd.Parameters.AddWithValue("@Color", string.IsNullOrWhiteSpace(colorTag) ? "Blue" : colorTag.Trim());
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await LogHistorySafeAsync(conn, "CREATE", "PortfolioCategory", nextId, $"Created portfolio category '{name.Trim()}' ({nextId})", ct);

            return new PortfolioCategoryModel
            {
                CategoryID = nextId,
                CategoryName = name.Trim(),
                Description = description?.Trim(),
                ParentSection = parentSection,
                ColorTag = colorTag,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                PortfolioCount = 0
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create portfolio category '{CategoryName}'.", name);
            return null;
        }
    }

    public async Task<bool> DeleteCategoryAsync(string categoryId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(categoryId)) return false;

        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(ct);

            const string delSql = "DELETE FROM dbo.PortfolioCategory WHERE CategoryID = @Id;";
            using var cmd = new SqlCommand(delSql, conn);
            cmd.Parameters.AddWithValue("@Id", categoryId.Trim());
            var affected = await cmd.ExecuteNonQueryAsync(ct);

            if (affected > 0)
            {
                await LogHistorySafeAsync(conn, "DELETE", "PortfolioCategory", categoryId.Trim(), $"Deleted portfolio category {categoryId.Trim()}", ct);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<PortfolioFolderModel>> GetPortfoliosAsync(string? category = null, bool? featuredOnly = null, CancellationToken ct = default)
    {
        var list = new List<PortfolioFolderModel>();

        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(ct);

            string sql = @"
                SELECT p.PortfolioId,
                       p.PortfolioName,
                       p.PortfolioDescription,
                       p.CategoryID,
                       COALESCE(pc.CategoryName, p.CategoryID) AS CategoryName,
                       p.IsFeatured,
                       p.HighlightImageFileName,
                       p.CreatedAt
                FROM dbo.Portfolios p
                LEFT JOIN dbo.PortfolioCategory pc ON pc.CategoryID = p.CategoryID
                WHERE 1 = 1";

            if (!string.IsNullOrWhiteSpace(category) && !category.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                sql += " AND (UPPER(p.CategoryID) = UPPER(@Category) OR UPPER(pc.CategoryName) = UPPER(@Category))";
            }

            if (featuredOnly == true)
            {
                sql += " AND p.IsFeatured = 1";
            }

            sql += " ORDER BY p.CreatedAt DESC;";

            using (var cmd = new SqlCommand(sql, conn))
            {
                if (!string.IsNullOrWhiteSpace(category) && !category.Equals("All", StringComparison.OrdinalIgnoreCase))
                {
                    cmd.Parameters.AddWithValue("@Category", category.Trim());
                }

                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var id = reader.GetString(0);
                    var name = reader.GetString(1);
                    var desc = reader.IsDBNull(2) ? null : reader.GetString(2);
                    var catId = reader.GetString(3);
                    var catName = reader.GetString(4);
                    var isFeatured = reader.GetBoolean(5);
                    var highlight = reader.IsDBNull(6) ? null : reader.GetString(6);
                    var createdAt = reader.GetDateTime(7);

                    list.Add(new PortfolioFolderModel
                    {
                        PortfolioId = id,
                        PortfolioName = name,
                        PortfolioDescription = desc,
                        CategoryID = catId,
                        CategoryName = catName,
                        IsFeatured = isFeatured,
                        HighlightImageFileName = highlight,
                        HighlightImageUrl = ResolveImageUrl(highlight),
                        CreatedAt = createdAt
                    });
                }
            }

            // Populate images for each portfolio
            if (list.Count > 0)
            {
                const string imgSql = @"
                    SELECT pi.Id, pi.PortfolioId, pi.FileName, pi.UploadedAt
                    FROM dbo.PortfolioImages pi
                    ORDER BY pi.UploadedAt ASC;";

                using var imgCmd = new SqlCommand(imgSql, conn);
                using var imgReader = await imgCmd.ExecuteReaderAsync(ct);
                var dict = list.ToDictionary(p => p.PortfolioId, StringComparer.OrdinalIgnoreCase);

                while (await imgReader.ReadAsync(ct))
                {
                    var imgId = imgReader.GetInt32(0);
                    var pId = imgReader.GetString(1);
                    var fName = imgReader.GetString(2);
                    var upAt = imgReader.GetDateTime(3);

                    if (dict.TryGetValue(pId, out var folder))
                    {
                        folder.Images.Add(new PortfolioImageModel
                        {
                            Id = imgId,
                            PortfolioId = pId,
                            FileName = fName,
                            Url = ResolveImageUrl(fName),
                            UploadedAt = upAt
                        });

                        // Fallback highlight image if not set
                        if (string.IsNullOrEmpty(folder.HighlightImageUrl) && !string.IsNullOrEmpty(fName))
                        {
                            folder.HighlightImageUrl = ResolveImageUrl(fName);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load portfolio categories.");
        }

        return list;
    }

    public async Task<List<PortfolioFolderModel>> GetFeaturedPortfoliosAsync(CancellationToken ct = default)
    {
        return await GetPortfoliosAsync(category: null, featuredOnly: true, ct: ct);
    }

    public async Task<PortfolioFolderModel?> CreatePortfolioAsync(
        string name,
        string? description,
        string categoryId,
        bool isFeatured,
        List<(string FileName, string ContentType, byte[] Data)> images,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var savedImageFileNames = new List<string>();

        // Ensure local uploads directory exists
        var uploadRoot = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads", "portfolio");
        try { Directory.CreateDirectory(uploadRoot); } catch { }

        // Upload image files to Cloudflare R2 and persist locally as reliable fallback
        foreach (var img in images)
        {
            if (img.Data == null || img.Data.Length == 0) continue;

            var ext = Path.GetExtension(img.FileName);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";
            var uniqueFileName = $"{Guid.NewGuid():N}{ext}";

            try
            {
                var localFilePath = Path.Combine(uploadRoot, uniqueFileName);
                await File.WriteAllBytesAsync(localFilePath, img.Data, ct);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to persist image locally to {UploadRoot}", uploadRoot);
            }

            await UploadToR2Async(uniqueFileName, img.Data, img.ContentType, ct);
            savedImageFileNames.Add(uniqueFileName);
        }

        var highlightFileName = savedImageFileNames.FirstOrDefault();

        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(ct);

            string portfolioId;
            const string idSql = @"
                DECLARE @NextID nvarchar(30);
                IF OBJECT_ID(N'dbo.sp_GetNextPortfolioID', N'P') IS NOT NULL
                BEGIN
                    EXEC dbo.sp_GetNextPortfolioID @NextPortfolioID = @NextID OUTPUT;
                END
                ELSE
                BEGIN
                    DECLARE @MaxNum INT = 0;
                    SELECT @MaxNum = ISNULL(MAX(TRY_CAST(SUBSTRING(PortfolioId, CHARINDEX('-', PortfolioId) + 1, 20) AS INT)), 0)
                    FROM dbo.Portfolios
                    WHERE PortfolioId LIKE 'PRF-%';
                    SET @NextID = 'PRF-' + RIGHT('0000' + CAST((@MaxNum + 1) AS VARCHAR(10)), 4);
                END
                SELECT @NextID;";

            using (var idCmd = new SqlCommand(idSql, conn))
            {
                var val = await idCmd.ExecuteScalarAsync(ct);
                portfolioId = val?.ToString() ?? ("PRF-" + DateTime.UtcNow.Ticks.ToString()[^4..]);
            }

            const string insertSql = @"
                INSERT INTO dbo.Portfolios 
                    (PortfolioId, PortfolioName, PortfolioDescription, CategoryID, IsFeatured, HighlightImageFileName, CreatedAt)
                VALUES 
                    (@Id, @Name, @Desc, @CatId, @Featured, @Highlight, SYSUTCDATETIME());";

            using (var cmd = new SqlCommand(insertSql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", portfolioId);
                cmd.Parameters.AddWithValue("@Name", name.Trim());
                cmd.Parameters.AddWithValue("@Desc", ((object?)description?.Trim()) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CatId", categoryId.Trim());
                cmd.Parameters.AddWithValue("@Featured", isFeatured);
                cmd.Parameters.AddWithValue("@Highlight", ((object?)highlightFileName) ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // Insert images
            foreach (var fn in savedImageFileNames)
            {
                const string imgSql = @"
                    INSERT INTO dbo.PortfolioImages (PortfolioId, FileName, UploadedAt)
                    VALUES (@PId, @FileName, SYSUTCDATETIME());";

                using var imgCmd = new SqlCommand(imgSql, conn);
                imgCmd.Parameters.AddWithValue("@PId", portfolioId);
                imgCmd.Parameters.AddWithValue("@FileName", fn);
                await imgCmd.ExecuteNonQueryAsync(ct);
            }

            await LogHistorySafeAsync(conn, "CREATE", "Portfolio", portfolioId, $"Created portfolio album '{name.Trim()}' ({portfolioId}) with {savedImageFileNames.Count} image(s)", ct);

            return new PortfolioFolderModel
            {
                PortfolioId = portfolioId,
                PortfolioName = name.Trim(),
                PortfolioDescription = description?.Trim(),
                CategoryID = categoryId.Trim(),
                IsFeatured = isFeatured,
                HighlightImageFileName = highlightFileName,
                HighlightImageUrl = ResolveImageUrl(highlightFileName),
                CreatedAt = DateTime.UtcNow,
                Images = savedImageFileNames.Select((f, idx) => new PortfolioImageModel
                {
                    Id = idx + 1,
                    PortfolioId = portfolioId,
                    FileName = f,
                    Url = ResolveImageUrl(f),
                    UploadedAt = DateTime.UtcNow
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create portfolio category '{CategoryName}'.", name);
            return null;
        }
    }

    public async Task<bool> UpdatePortfolioAsync(
        string portfolioId,
        string name,
        string? description,
        string categoryId,
        List<(string FileName, string ContentType, byte[] Data)>? newImages = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(portfolioId) || string.IsNullOrWhiteSpace(name)) return false;

        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(ct);

            const string updateSql = @"
                UPDATE dbo.Portfolios 
                SET PortfolioName = @Name,
                    PortfolioDescription = @Desc,
                    CategoryID = @CatId
                WHERE PortfolioId = @Id;";

            using (var cmd = new SqlCommand(updateSql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", portfolioId.Trim());
                cmd.Parameters.AddWithValue("@Name", name.Trim());
                cmd.Parameters.AddWithValue("@Desc", ((object?)description?.Trim()) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CatId", categoryId.Trim());
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // Upload and insert any new images directly to Cloudflare R2 and local storage
            if (newImages != null && newImages.Count > 0)
            {
                var uploadRoot = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads", "portfolio");
                try { Directory.CreateDirectory(uploadRoot); } catch { }

                foreach (var img in newImages)
                {
                    if (img.Data == null || img.Data.Length == 0) continue;

                    var ext = Path.GetExtension(img.FileName);
                    if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                    var uniqueFileName = $"{Guid.NewGuid():N}{ext}";

                    try
                    {
                        var localFilePath = Path.Combine(uploadRoot, uniqueFileName);
                        await File.WriteAllBytesAsync(localFilePath, img.Data, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to persist new image locally to {UploadRoot}", uploadRoot);
                    }

                    await UploadToR2Async(uniqueFileName, img.Data, img.ContentType, ct);

                    const string imgSql = @"
                        INSERT INTO dbo.PortfolioImages (PortfolioId, FileName, UploadedAt)
                        VALUES (@PId, @FileName, SYSUTCDATETIME());";

                    using var imgCmd = new SqlCommand(imgSql, conn);
                    imgCmd.Parameters.AddWithValue("@PId", portfolioId.Trim());
                    imgCmd.Parameters.AddWithValue("@FileName", uniqueFileName);
                    await imgCmd.ExecuteNonQueryAsync(ct);
                }
            }

            await LogHistorySafeAsync(conn, "UPDATE", "Portfolio", portfolioId.Trim(), $"Updated portfolio '{name.Trim()}' ({portfolioId.Trim()})", ct);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ToggleFeatureAsync(string portfolioId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(portfolioId)) return false;

        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(ct);

            const string toggleSql = @"
                UPDATE dbo.Portfolios 
                SET IsFeatured = CASE WHEN IsFeatured = 1 THEN 0 ELSE 1 END
                WHERE PortfolioId = @Id;

                SELECT IsFeatured, PortfolioName FROM dbo.Portfolios WHERE PortfolioId = @Id;";

            bool newFeaturedState = false;
            string portName = portfolioId;

            using (var cmd = new SqlCommand(toggleSql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", portfolioId.Trim());
                using var reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    newFeaturedState = reader.GetBoolean(0);
                    portName = reader.GetString(1);
                }
                else
                {
                    return false;
                }
            }

            await LogHistorySafeAsync(conn, "UPDATE", "Portfolio", portfolioId.Trim(), $"Toggled featured status of portfolio '{portName}' to {newFeaturedState}", ct);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeletePortfolioAsync(string portfolioId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(portfolioId)) return false;

        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(ct);

            // Fetch images to delete from disk
            var imagesToDelete = new List<string>();
            const string fetchImgSql = "SELECT FileName FROM dbo.PortfolioImages WHERE PortfolioId = @Id;";
            using (var fetchCmd = new SqlCommand(fetchImgSql, conn))
            {
                fetchCmd.Parameters.AddWithValue("@Id", portfolioId.Trim());
                using var reader = await fetchCmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    imagesToDelete.Add(reader.GetString(0));
                }
            }

            const string delSql = @"
                DELETE FROM dbo.PortfolioImages WHERE PortfolioId = @Id;
                DELETE FROM dbo.Portfolios WHERE PortfolioId = @Id;";

            using (var cmd = new SqlCommand(delSql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", portfolioId.Trim());
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // Remove files from Cloudflare R2
            foreach (var fn in imagesToDelete)
            {
                await DeleteFromR2Async(fn, ct);
            }

            await LogHistorySafeAsync(conn, "DELETE", "Portfolio", portfolioId.Trim(), $"Deleted portfolio {portfolioId.Trim()}", ct);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task LogHistorySafeAsync(SqlConnection conn, string actionType, string entityName, string entityId, string details, CancellationToken ct)
    {
        try
        {
            const string logSql = @"
                IF OBJECT_ID(N'dbo.sp_AddHistoryLog', N'P') IS NOT NULL
                BEGIN
                    EXEC dbo.sp_AddHistoryLog
                        @ActionType = @Action,
                        @EntityName = @Entity,
                        @EntityID = @Id,
                        @ActionDetails = @Details,
                        @ChangedByType = 'Admin',
                        @ActorID = 'Admin';
                END
                ELSE IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedByType, ChangedByAdminID, ActionDetails, LoggedAt)
                    VALUES (@Action, @Entity, @Id, 'Admin', 'Admin', @Details, SYSUTCDATETIME());
                END";

            using var logCmd = new SqlCommand(logSql, conn);
            logCmd.Parameters.AddWithValue("@Action", actionType);
            logCmd.Parameters.AddWithValue("@Entity", entityName);
            logCmd.Parameters.AddWithValue("@Id", entityId);
            logCmd.Parameters.AddWithValue("@Details", details);
            await logCmd.ExecuteNonQueryAsync(ct);
        }
        catch
        {
            // Do not fail the main operation if logging encounters an issue
        }
    }

    private string ResolveImageUrl(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "images/AevumLogo.jpg";
        if (fileName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
        {
            return fileName;
        }

        var clean = fileName.TrimStart('/');
        if (clean.StartsWith("uploads/portfolio/"))
            clean = clean.Substring("uploads/portfolio/".Length);
        if (clean.StartsWith("portfolio/"))
            clean = clean.Substring("portfolio/".Length);

        // Hybrid check:
        // 1. If Cloudflare R2 is configured with a valid public domain, serve from R2
        if (_r2Configured && !string.IsNullOrWhiteSpace(_r2PublicBaseUrl) && !_r2PublicBaseUrl.Contains("pub-photoaevum.r2.dev", StringComparison.OrdinalIgnoreCase))
        {
            return $"{_r2PublicBaseUrl.TrimEnd('/')}/portfolio/{clean}";
        }

        // 2. Fallback to local uploads folder
        var webRoot = _env?.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var localDiskPath = Path.Combine(webRoot, "uploads", "portfolio", clean);
        if (File.Exists(localDiskPath))
        {
            return $"/uploads/portfolio/{clean}";
        }

        // 3. Fallback to R2 client presigned URL if client is available
        if (_r2Configured && _r2Client != null)
        {
            try
            {
                var presigned = _r2Client.GetPreSignedURL(new Amazon.S3.Model.GetPreSignedUrlRequest
                {
                    BucketName = _r2BucketName,
                    Key = $"portfolio/{clean}",
                    Expires = DateTime.UtcNow.AddHours(2)
                });
                if (!string.IsNullOrWhiteSpace(presigned)) return presigned;
            }
            catch
            {
            }
        }

        return $"/uploads/portfolio/{clean}";
    }
}
