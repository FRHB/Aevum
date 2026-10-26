using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public class PortfolioService : IPortfolioService
{
    private readonly string _connectionString;
    private readonly IWebHostEnvironment _env;

    public PortfolioService(IConfiguration? configuration, IWebHostEnvironment env)
    {
        _env = env;
        _connectionString = configuration?.GetConnectionString("DefaultConnection")
            ?? "Data Source=localhost\\SQLEXPRESS;Initial Catalog=PhotoAevumStudio;Integrated Security=True;Persist Security Info=False;Pooling=False;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=True;Application Name=\"SQL Server Management Studio\";Command Timeout=0";
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
                SELECT pc.CategoryCode,
                       pc.CategoryName,
                       pc.Description,
                       pc.ParentSection,
                       pc.ColorTag,
                       pc.IsActive,
                       pc.CreatedAt,
                       (SELECT COUNT(*) FROM dbo.Portfolios p WHERE p.CategoryCode = pc.CategoryCode) AS PortfolioCount
                FROM dbo.PortfolioCategory pc
                ORDER BY pc.CategoryName;";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                list.Add(new PortfolioCategoryModel
                {
                    CategoryCode = reader.GetString(0),
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
        catch
        {
        }

        return list;
    }

    public async Task<PortfolioCategoryModel?> CreateCategoryAsync(string name, string? description, string parentSection, string colorTag, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var code = name.Trim().ToUpperInvariant().Replace(" ", "_");
        if (code.Length > 50) code = code[..50];

        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(ct);

            const string checkSql = "SELECT COUNT(*) FROM dbo.PortfolioCategory WHERE CategoryCode = @Code;";
            using (var checkCmd = new SqlCommand(checkSql, conn))
            {
                checkCmd.Parameters.AddWithValue("@Code", code);
                var exists = (int)(await checkCmd.ExecuteScalarAsync(ct) ?? 0) > 0;
                if (exists)
                {
                    code = $"{code}_{Guid.NewGuid():N}"[..Math.Min(code.Length + 5, 50)];
                }
            }

            const string insertSql = @"
                INSERT INTO dbo.PortfolioCategory 
                    (CategoryCode, CategoryName, Description, ParentSection, ColorTag, IsActive, CreatedAt, UpdatedAt)
                VALUES 
                    (@Code, @Name, @Desc, @Section, @Color, 1, SYSUTCDATETIME(), SYSUTCDATETIME());";

            using (var cmd = new SqlCommand(insertSql, conn))
            {
                cmd.Parameters.AddWithValue("@Code", code);
                cmd.Parameters.AddWithValue("@Name", name.Trim());
                cmd.Parameters.AddWithValue("@Desc", ((object)description?.Trim()) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Section", string.IsNullOrWhiteSpace(parentSection) ? "Photography" : parentSection.Trim());
                cmd.Parameters.AddWithValue("@Color", string.IsNullOrWhiteSpace(colorTag) ? "Blue" : colorTag.Trim());
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // Log activity
            const string logSql = @"
                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedBy, ActionDetails, LoggedAt)
                    VALUES ('Create', 'PortfolioCategory', @Code, 'Admin', @Details, SYSUTCDATETIME());
                END";

            using (var logCmd = new SqlCommand(logSql, conn))
            {
                logCmd.Parameters.AddWithValue("@Code", code);
                logCmd.Parameters.AddWithValue("@Details", $"Created portfolio category '{name.Trim()}' ({code})");
                await logCmd.ExecuteNonQueryAsync(ct);
            }

            return new PortfolioCategoryModel
            {
                CategoryCode = code,
                CategoryName = name.Trim(),
                Description = description?.Trim(),
                ParentSection = parentSection,
                ColorTag = colorTag,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                PortfolioCount = 0
            };
        }
        catch
        {
            return null;
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
                       p.CategoryCode,
                       COALESCE(pc.CategoryName, p.CategoryCode) AS CategoryName,
                       p.IsFeatured,
                       p.HighlightImageFileName,
                       p.CreatedAt
                FROM dbo.Portfolios p
                LEFT JOIN dbo.PortfolioCategory pc ON pc.CategoryCode = p.CategoryCode
                WHERE 1 = 1";

            if (!string.IsNullOrWhiteSpace(category) && !category.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                sql += " AND (UPPER(p.CategoryCode) = UPPER(@Category) OR UPPER(pc.CategoryName) = UPPER(@Category))";
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
                    var catCode = reader.GetString(3);
                    var catName = reader.GetString(4);
                    var isFeatured = reader.GetBoolean(5);
                    var highlight = reader.IsDBNull(6) ? null : reader.GetString(6);
                    var createdAt = reader.GetDateTime(7);

                    list.Add(new PortfolioFolderModel
                    {
                        PortfolioId = id,
                        PortfolioName = name,
                        PortfolioDescription = desc,
                        CategoryCode = catCode,
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
        catch
        {
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
        string categoryCode,
        bool isFeatured,
        List<(string FileName, string ContentType, byte[] Data)> images,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var portfolioId = "PORT-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var savedImageFileNames = new List<string>();

        // Ensure upload directory exists
        var uploadRoot = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads", "portfolio");
        try
        {
            Directory.CreateDirectory(uploadRoot);
        }
        catch
        {
        }

        // Save image files
        foreach (var img in images)
        {
            if (img.Data == null || img.Data.Length == 0) continue;

            var ext = Path.GetExtension(img.FileName);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";
            var uniqueFileName = $"{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(uploadRoot, uniqueFileName);

            try
            {
                await File.WriteAllBytesAsync(filePath, img.Data, ct);
                savedImageFileNames.Add(uniqueFileName);
            }
            catch
            {
                // Fallback store base64 string
                savedImageFileNames.Add($"data:{img.ContentType};base64,{Convert.ToBase64String(img.Data)}");
            }
        }

        var highlightFileName = savedImageFileNames.FirstOrDefault();

        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(ct);

            const string insertSql = @"
                INSERT INTO dbo.Portfolios 
                    (PortfolioId, PortfolioName, PortfolioDescription, CategoryCode, IsFeatured, HighlightImageFileName, CreatedAt)
                VALUES 
                    (@Id, @Name, @Desc, @CatCode, @Featured, @Highlight, SYSUTCDATETIME());";

            using (var cmd = new SqlCommand(insertSql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", portfolioId);
                cmd.Parameters.AddWithValue("@Name", name.Trim());
                cmd.Parameters.AddWithValue("@Desc", ((object)description?.Trim()) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CatCode", categoryCode.Trim());
                cmd.Parameters.AddWithValue("@Featured", isFeatured);
                cmd.Parameters.AddWithValue("@Highlight", ((object)highlightFileName) ?? DBNull.Value);
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

            // Log activity
            const string logSql = @"
                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedBy, ActionDetails, LoggedAt)
                    VALUES ('Create', 'Portfolio', @Id, 'Admin', @Details, SYSUTCDATETIME());
                END";

            using (var logCmd = new SqlCommand(logSql, conn))
            {
                logCmd.Parameters.AddWithValue("@Id", portfolioId);
                logCmd.Parameters.AddWithValue("@Details", $"Created portfolio album '{name.Trim()}' with {savedImageFileNames.Count} image(s)");
                await logCmd.ExecuteNonQueryAsync(ct);
            }

            return new PortfolioFolderModel
            {
                PortfolioId = portfolioId,
                PortfolioName = name.Trim(),
                PortfolioDescription = description?.Trim(),
                CategoryCode = categoryCode.Trim(),
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
        catch
        {
            return null;
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

            // Log activity
            const string logSql = @"
                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedBy, ActionDetails, LoggedAt)
                    VALUES ('Update', 'Portfolio', @Id, 'Admin', @Details, SYSUTCDATETIME());
                END";

            using (var logCmd = new SqlCommand(logSql, conn))
            {
                logCmd.Parameters.AddWithValue("@Id", portfolioId.Trim());
                logCmd.Parameters.AddWithValue("@Details", $"Toggled featured status of portfolio '{portName}' to {newFeaturedState}");
                await logCmd.ExecuteNonQueryAsync(ct);
            }

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

            const string deleteSql = @"
                DELETE FROM dbo.PortfolioImages WHERE PortfolioId = @Id;
                DELETE FROM dbo.Portfolios WHERE PortfolioId = @Id;";

            using (var cmd = new SqlCommand(deleteSql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", portfolioId.Trim());
                var count = await cmd.ExecuteNonQueryAsync(ct);
                if (count == 0) return false;
            }

            const string logSql = @"
                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedBy, ActionDetails, LoggedAt)
                    VALUES ('Delete', 'Portfolio', @Id, 'Admin', @Details, SYSUTCDATETIME());
                END";

            using var logCmd = new SqlCommand(logSql, conn);
            logCmd.Parameters.AddWithValue("@Id", portfolioId.Trim());
            logCmd.Parameters.AddWithValue("@Details", $"Deleted portfolio {portfolioId}");
            await logCmd.ExecuteNonQueryAsync(ct);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveImageUrl(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "images/studio-placeholder.jpg";

        if (fileName.StartsWith("http://") || fileName.StartsWith("https://") || fileName.StartsWith("data:image/"))
            return fileName;

        var clean = fileName.TrimStart('/');
        if (clean.StartsWith("uploads/"))
            return "/" + clean;

        return "/uploads/portfolio/" + clean;
    }
}
