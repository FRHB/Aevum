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

    private async Task<(string Key, string Url, bool Uploaded)> UploadToR2Async(string uniqueFileName, byte[] data, string contentType, CancellationToken ct)
    {
        var safeName = string.IsNullOrWhiteSpace(uniqueFileName) ? $"{Guid.NewGuid():N}.jpg" : uniqueFileName;
        var key = safeName.StartsWith("portfolio/", StringComparison.OrdinalIgnoreCase)
            ? safeName
            : $"portfolio/{safeName}";
        var resolvedMime = string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType;
        var fallbackUrl = $"{(_r2PublicBaseUrl ?? string.Empty).TrimEnd('/')}/{key}";

        if (_r2Configured && _r2Client != null && data is { Length: > 0 })
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(8));

                using var ms = new MemoryStream(data);
                var putReq = new PutObjectRequest
                {
                    BucketName = _r2BucketName,
                    Key = key,
                    InputStream = ms,
                    ContentType = resolvedMime,
                    DisablePayloadSigning = true
                };
                await _r2Client.PutObjectAsync(putReq, cts.Token);

                var presignedReq = new GetPreSignedUrlRequest
                {
                    BucketName = _r2BucketName,
                    Key = key,
                    Expires = DateTime.UtcNow.AddDays(7)
                };
                var presignedUrl = _r2Client.GetPreSignedURL(presignedReq);
                return (key, string.IsNullOrWhiteSpace(presignedUrl) ? fallbackUrl : presignedUrl, true);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Cloudflare R2 upload error for {Key} in bucket {Bucket}", key, _r2BucketName);
            }
        }

        return (key, fallbackUrl, false);
    }

    private static async Task EnsurePortfolioSchemaAsync(SqlConnection conn, CancellationToken ct)
    {
        const string sql = @"
            IF OBJECT_ID(N'dbo.PortfolioImages', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.PortfolioImages', N'ImageUrl') IS NULL
                    ALTER TABLE dbo.PortfolioImages ADD ImageUrl nvarchar(500) NULL;
                IF COL_LENGTH(N'dbo.PortfolioImages', N'ContentType') IS NULL
                    ALTER TABLE dbo.PortfolioImages ADD ContentType nvarchar(100) NULL CONSTRAINT DF_PortfolioImages_ContentType DEFAULT 'image/jpeg';
                IF COL_LENGTH(N'dbo.PortfolioImages', N'ImageData') IS NULL
                    ALTER TABLE dbo.PortfolioImages ADD ImageData varbinary(max) NULL;
            END;";
        using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
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

    public async Task<bool> UpdateCategoryAsync(string categoryId, string name, string? description, string parentSection, string colorTag, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(categoryId) || string.IsNullOrWhiteSpace(name))
            return false;

        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(ct);

            const string updateSql = @"
                UPDATE dbo.PortfolioCategory
                SET CategoryName = @Name,
                    Description = @Desc,
                    ParentSection = @Section,
                    ColorTag = @Color,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE CategoryID = @Id;";

            using var cmd = new SqlCommand(updateSql, conn);
            cmd.Parameters.AddWithValue("@Id", categoryId.Trim());
            cmd.Parameters.AddWithValue("@Name", name.Trim());
            cmd.Parameters.AddWithValue("@Desc", ((object?)description?.Trim()) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Section", string.IsNullOrWhiteSpace(parentSection) ? "Photography" : parentSection.Trim());
            cmd.Parameters.AddWithValue("@Color", string.IsNullOrWhiteSpace(colorTag) ? "Blue" : colorTag.Trim());

            var affected = await cmd.ExecuteNonQueryAsync(ct);
            if (affected > 0)
            {
                await LogHistorySafeAsync(conn, "UPDATE", "PortfolioCategory", categoryId.Trim(), $"Updated portfolio category '{name.Trim()}' ({categoryId.Trim()})", ct);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to update portfolio category '{CategoryID}'.", categoryId);
            return false;
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
            await EnsurePortfolioSchemaAsync(conn, ct);

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
                    var id = ReadString(reader, 0) ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    var name = ReadString(reader, 1) ?? "Untitled Portfolio";
                    var desc = ReadString(reader, 2);
                    var catId = ReadString(reader, 3) ?? string.Empty;
                    var catName = ReadString(reader, 4) ?? catId;
                    var isFeatured = !reader.IsDBNull(5) && reader.GetBoolean(5);
                    var highlight = ReadString(reader, 6);
                    var createdAt = reader.IsDBNull(7) ? DateTime.UtcNow : reader.GetDateTime(7);

                    list.Add(new PortfolioFolderModel
                    {
                        PortfolioId = id,
                        PortfolioName = name,
                        PortfolioDescription = desc,
                        CategoryID = catId,
                        CategoryName = catName,
                        IsFeatured = isFeatured,
                        HighlightImageFileName = highlight,
                        HighlightImageUrl = !string.IsNullOrWhiteSpace(highlight) ? ResolveImageUrl(highlight) : string.Empty,
                        CreatedAt = createdAt,
                        Images = new List<PortfolioImageModel>()
                    });
                }
            }

            // Populate images for each portfolio from SQL Server and Cloudflare R2
            if (list.Count > 0)
            {
                // Skip ImageData when R2 is live so gallery reloads do not freeze the Blazor circuit.
                var imgSql = _r2Configured
                    ? @"
                    SELECT pi.Id, pi.PortfolioId, pi.FileName, pi.UploadedAt, pi.ImageUrl, pi.ContentType, CAST(NULL AS varbinary(max)) AS ImageData
                    FROM dbo.PortfolioImages pi
                    ORDER BY pi.UploadedAt ASC;"
                    : @"
                    SELECT pi.Id, pi.PortfolioId, pi.FileName, pi.UploadedAt, pi.ImageUrl, pi.ContentType, pi.ImageData
                    FROM dbo.PortfolioImages pi
                    ORDER BY pi.UploadedAt ASC;";

                using var imgCmd = new SqlCommand(imgSql, conn);
                using var imgReader = await imgCmd.ExecuteReaderAsync(ct);
                var dict = list.GroupBy(p => p.PortfolioId, StringComparer.OrdinalIgnoreCase)
                               .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                while (await imgReader.ReadAsync(ct))
                {
                    var imgId = imgReader.IsDBNull(0) ? 0 : imgReader.GetInt32(0);
                    var pId = ReadString(imgReader, 1);
                    if (string.IsNullOrWhiteSpace(pId)) continue;

                    var fName = ReadString(imgReader, 2) ?? string.Empty;
                    var upAt = imgReader.IsDBNull(3) ? DateTime.UtcNow : imgReader.GetDateTime(3);
                    var imgUrl = imgReader.FieldCount > 4 ? ReadString(imgReader, 4) : null;
                    var contentType = imgReader.FieldCount > 5 ? ReadString(imgReader, 5) : "image/jpeg";
                    byte[]? imgData = null;
                    if (imgReader.FieldCount > 6 && !imgReader.IsDBNull(6))
                    {
                        imgData = imgReader[6] as byte[];
                    }

                    if (dict.TryGetValue(pId, out var folder))
                    {
                        folder.Images ??= new List<PortfolioImageModel>();
                        var resolvedUrl = ResolveImageUrl(fName, imgUrl, contentType, imgData);
                        folder.Images.Add(new PortfolioImageModel
                        {
                            Id = imgId,
                            PortfolioId = pId,
                            FileName = fName,
                            ImageUrl = imgUrl ?? ResolveR2Url(fName),
                            ContentType = contentType ?? "image/jpeg",
                            Url = resolvedUrl,
                            UploadedAt = upAt
                        });

                        if (string.IsNullOrEmpty(folder.HighlightImageUrl) ||
                            string.Equals(folder.HighlightImageFileName, fName, StringComparison.OrdinalIgnoreCase))
                        {
                            folder.HighlightImageUrl = resolvedUrl;
                        }
                    }
                }

                foreach (var folder in list)
                {
                    folder.Images ??= new List<PortfolioImageModel>();
                    if (string.IsNullOrWhiteSpace(folder.HighlightImageUrl))
                    {
                        folder.HighlightImageUrl = folder.Images.FirstOrDefault()?.Url ?? "images/AevumLogo.jpg";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load portfolios.");
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

        var uploadedImages = new List<(string Key, string R2Url, string ContentType, byte[] Data)>();
        var sourceImages = images ?? new List<(string FileName, string ContentType, byte[] Data)>();

        try
        {
            foreach (var img in sourceImages)
            {
                if (img.Data == null || img.Data.Length == 0) continue;

                var ext = Path.GetExtension(img.FileName ?? string.Empty);
                if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                var uniqueFileName = $"{Guid.NewGuid():N}{ext}";
                var resolvedMime = string.IsNullOrWhiteSpace(img.ContentType) ? "image/jpeg" : img.ContentType;

                var (r2Key, r2Url, _) = await UploadToR2Async(uniqueFileName, img.Data, resolvedMime, ct);
                uploadedImages.Add((r2Key, r2Url, resolvedMime, img.Data));
            }

            var highlightFileName = uploadedImages.Count > 0 ? uploadedImages[0].Key : null;
            var resolvedCatId = string.IsNullOrWhiteSpace(categoryId) ? "PCAT-0001" : categoryId.Trim();
            using var conn = CreateConnection();
            await conn.OpenAsync(ct);
            await EnsurePortfolioSchemaAsync(conn, ct);

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
                cmd.Parameters.AddWithValue("@CatId", resolvedCatId);
                cmd.Parameters.AddWithValue("@Featured", isFeatured);
                cmd.Parameters.AddWithValue("@Highlight", ((object?)highlightFileName) ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // Insert images into SQL Server with R2 key/URL and binary ImageData
            foreach (var item in uploadedImages)
            {
                const string imgSql = @"
                    INSERT INTO dbo.PortfolioImages (PortfolioId, FileName, ImageUrl, ContentType, ImageData, UploadedAt)
                    VALUES (@PId, @FileName, @ImageUrl, @ContentType, @ImageData, SYSUTCDATETIME());";

                using var imgCmd = new SqlCommand(imgSql, conn);
                imgCmd.Parameters.AddWithValue("@PId", portfolioId);
                imgCmd.Parameters.AddWithValue("@FileName", item.Key ?? string.Empty);
                imgCmd.Parameters.AddWithValue("@ImageUrl", string.IsNullOrWhiteSpace(item.R2Url) ? DBNull.Value : item.R2Url);
                imgCmd.Parameters.AddWithValue("@ContentType", string.IsNullOrWhiteSpace(item.ContentType) ? "image/jpeg" : item.ContentType);
                imgCmd.Parameters.Add("@ImageData", SqlDbType.VarBinary, -1).Value = item.Data;
                await imgCmd.ExecuteNonQueryAsync(ct);
            }

            await LogHistorySafeAsync(conn, "CREATE", "Portfolio", portfolioId, $"Created portfolio album '{name.Trim()}' ({portfolioId}) with {uploadedImages.Count} image(s)", ct);

            var highlightUrl = uploadedImages.Count > 0
                ? ResolveImageUrl(uploadedImages[0].Key, uploadedImages[0].R2Url, uploadedImages[0].ContentType, uploadedImages[0].Data)
                : "images/AevumLogo.jpg";

            return new PortfolioFolderModel
            {
                PortfolioId = portfolioId,
                PortfolioName = name.Trim(),
                PortfolioDescription = description?.Trim(),
                CategoryID = resolvedCatId,
                CategoryName = resolvedCatId,
                IsFeatured = isFeatured,
                HighlightImageFileName = highlightFileName,
                HighlightImageUrl = highlightUrl,
                CreatedAt = DateTime.UtcNow,
                Images = uploadedImages.Select((item, idx) => new PortfolioImageModel
                {
                    Id = idx + 1,
                    PortfolioId = portfolioId,
                    FileName = item.Key,
                    ImageUrl = item.R2Url,
                    ContentType = item.ContentType,
                    Url = ResolveImageUrl(item.Key, item.R2Url, item.ContentType, item.Data),
                    UploadedAt = DateTime.UtcNow
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create portfolio '{PortfolioName}'.", name);
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
            await EnsurePortfolioSchemaAsync(conn, ct);

            var resolvedCatId = string.IsNullOrWhiteSpace(categoryId) ? "PCAT-0001" : categoryId.Trim();
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
                cmd.Parameters.AddWithValue("@CatId", resolvedCatId);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            string? firstNewKey = null;
            if (newImages != null && newImages.Count > 0)
            {
                foreach (var img in newImages)
                {
                    if (img.Data == null || img.Data.Length == 0) continue;

                    var ext = Path.GetExtension(img.FileName ?? string.Empty);
                    if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                    var uniqueFileName = $"{Guid.NewGuid():N}{ext}";
                    var resolvedMime = string.IsNullOrWhiteSpace(img.ContentType) ? "image/jpeg" : img.ContentType;

                    var (r2Key, r2Url, _) = await UploadToR2Async(uniqueFileName, img.Data, resolvedMime, ct);
                    firstNewKey ??= r2Key;

                    const string imgSql = @"
                        INSERT INTO dbo.PortfolioImages (PortfolioId, FileName, ImageUrl, ContentType, ImageData, UploadedAt)
                        VALUES (@PId, @FileName, @ImageUrl, @ContentType, @ImageData, SYSUTCDATETIME());";

                    using var imgCmd = new SqlCommand(imgSql, conn);
                    imgCmd.Parameters.AddWithValue("@PId", portfolioId.Trim());
                    imgCmd.Parameters.AddWithValue("@FileName", r2Key ?? string.Empty);
                    imgCmd.Parameters.AddWithValue("@ImageUrl", string.IsNullOrWhiteSpace(r2Url) ? DBNull.Value : r2Url);
                    imgCmd.Parameters.AddWithValue("@ContentType", resolvedMime);
                    imgCmd.Parameters.Add("@ImageData", SqlDbType.VarBinary, -1).Value = img.Data;
                    await imgCmd.ExecuteNonQueryAsync(ct);
                }

                if (!string.IsNullOrWhiteSpace(firstNewKey))
                {
                    const string highlightSql = @"
                        UPDATE dbo.Portfolios
                        SET HighlightImageFileName = COALESCE(NULLIF(HighlightImageFileName, ''), @Highlight)
                        WHERE PortfolioId = @Id;";
                    using var hlCmd = new SqlCommand(highlightSql, conn);
                    hlCmd.Parameters.AddWithValue("@Id", portfolioId.Trim());
                    hlCmd.Parameters.AddWithValue("@Highlight", firstNewKey);
                    await hlCmd.ExecuteNonQueryAsync(ct);
                }
            }

            await LogHistorySafeAsync(conn, "UPDATE", "Portfolio", portfolioId.Trim(), $"Updated portfolio '{name.Trim()}' ({portfolioId.Trim()})", ct);

            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to update portfolio '{PortfolioId}'.", portfolioId);
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
                    newFeaturedState = !reader.IsDBNull(0) && reader.GetBoolean(0);
                    portName = ReadString(reader, 1) ?? portfolioId;
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
                    var fn = ReadString(reader, 0);
                    if (!string.IsNullOrWhiteSpace(fn))
                        imagesToDelete.Add(fn);
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

    private static string? ReadString(SqlDataReader reader, int ordinal)
    {
        if (ordinal < 0 || ordinal >= reader.FieldCount || reader.IsDBNull(ordinal))
            return null;
        return reader.GetString(ordinal);
    }

    private string ResolveR2Url(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
        if (fileName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return fileName;
        }

        var clean = fileName.TrimStart('/');
        if (clean.StartsWith("portfolio/", StringComparison.OrdinalIgnoreCase))
            clean = clean.Substring("portfolio/".Length);

        var key = $"portfolio/{clean}";

        if (_r2Configured && _r2Client != null)
        {
            try
            {
                var presigned = _r2Client.GetPreSignedURL(new GetPreSignedUrlRequest
                {
                    BucketName = _r2BucketName,
                    Key = key,
                    Expires = DateTime.UtcNow.AddHours(2)
                });
                if (!string.IsNullOrWhiteSpace(presigned)) return presigned;
            }
            catch
            {
            }
        }

        return $"{_r2PublicBaseUrl.TrimEnd('/')}/{key}";
    }

    private string ResolveImageUrl(string? fileName, string? r2ImageUrl = null, string? contentType = null, byte[]? imageData = null)
    {
        if (imageData is { Length: > 0 } && !_r2Configured)
        {
            var mime = string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType;
            return $"data:{mime};base64,{Convert.ToBase64String(imageData)}";
        }

        if (!string.IsNullOrWhiteSpace(r2ImageUrl) &&
            (r2ImageUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             r2ImageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
             r2ImageUrl.StartsWith("data:image", StringComparison.OrdinalIgnoreCase)))
        {
            return r2ImageUrl;
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            if (imageData is { Length: > 0 })
            {
                var mime = string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType;
                return $"data:{mime};base64,{Convert.ToBase64String(imageData)}";
            }
            return "images/AevumLogo.jpg";
        }

        if (fileName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
        {
            return fileName;
        }

        var r2 = ResolveR2Url(fileName);
        if (!string.IsNullOrWhiteSpace(r2)) return r2;

        if (imageData is { Length: > 0 })
        {
            var mime = string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType;
            return $"data:{mime};base64,{Convert.ToBase64String(imageData)}";
        }

        return "images/AevumLogo.jpg";
    }
}
