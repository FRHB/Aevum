using System.Data;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Data.SqlClient;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

/// <summary>
/// Service managing synchronized state and data operations for Equipment,
/// Equipment Types, EventEquipment Dispatches, and Maintenance History Logs.
/// Backed directly by SQL Server (dbo.Equipment, dbo.EquipmentType, dbo.EventEquipment, dbo.HistoryLog)
/// and Cloudflare R2 object storage for equipment imagery.
/// </summary>
public sealed class EquipmentService : IEquipmentService
{
    private readonly object _lock = new();

    private readonly List<EquipmentTypeModel> _types = new();
    private readonly List<EquipmentItemModel> _equipments = new();
    private readonly List<EventAppointmentModel> _events = new();
    private readonly List<DispatchItemModel> _dispatches = new();
    private readonly List<MaintenanceLogModel> _maintenanceLogs = new();

    private int _nextEventEquipmentId = 1;
    private long _nextHistoryLogId = 1;

    public event Action? OnChange;

    private readonly string? _connectionString;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly IAmazonS3? _r2Client;
    private readonly string _servicesBucket;
    private readonly bool _r2Configured;

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c) && c != "PLACEHOLDER")
                return c.Trim();
        }
        return null;
    }

    public EquipmentService(IConfiguration? configuration = null, IHttpClientFactory? httpClientFactory = null)
    {
        _httpClientFactory = httpClientFactory;
        _connectionString = FirstNonEmpty(
            configuration?.GetConnectionString("DefaultConnection"),
            configuration?["ConnectionStrings__DefaultConnection"]);

        var accessKey = FirstNonEmpty(
            configuration?["CloudflareR2:AccessKey"],
            configuration?["CloudflareR2__AccessKey"],
            Environment.GetEnvironmentVariable("R2_ACCESS_KEY")) ?? "PLACEHOLDER";

        var secretKey = FirstNonEmpty(
            configuration?["CloudflareR2:SecretKey"],
            configuration?["CloudflareR2__SecretKey"],
            Environment.GetEnvironmentVariable("R2_SECRET_KEY")) ?? "PLACEHOLDER";

        var accountId = FirstNonEmpty(
            configuration?["CloudflareR2:AccountId"],
            configuration?["CloudflareR2__AccountId"],
            Environment.GetEnvironmentVariable("R2_ACCOUNT_ID"));

        var serviceUrl = FirstNonEmpty(
            configuration?["CloudflareR2:ServiceUrl"],
            configuration?["CloudflareR2__ServiceUrl"],
            Environment.GetEnvironmentVariable("R2_SERVICE_URL"));

        if (string.IsNullOrWhiteSpace(serviceUrl))
        {
            serviceUrl = !string.IsNullOrWhiteSpace(accountId)
                ? $"https://{accountId}.r2.cloudflarestorage.com"
                : "https://f6757b400d51c23671cedb8b0efc4b32.r2.cloudflarestorage.com";
        }

        _servicesBucket = FirstNonEmpty(
            configuration?["CloudflareR2:Buckets:Services"],
            configuration?["CloudflareR2__Buckets__Services"],
            Environment.GetEnvironmentVariable("R2_BUCKET_SERVICES")) ?? "services";

        _r2Configured = !string.IsNullOrWhiteSpace(accessKey)
            && !string.IsNullOrWhiteSpace(secretKey)
            && accessKey != "PLACEHOLDER"
            && secretKey != "PLACEHOLDER";

        if (_r2Configured)
        {
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
            catch
            {
            }
        }
    }

    private bool HasDatabase => !string.IsNullOrWhiteSpace(_connectionString);

    public void NotifyStateChanged()
    {
        OnChange?.Invoke();
    }

    private static string ExtractR2ObjectKey(string rawKeyOrUrl, string bucketName)
    {
        var trimmed = rawKeyOrUrl.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            trimmed = Uri.UnescapeDataString(uri.AbsolutePath);
        }

        trimmed = trimmed.TrimStart('/');
        var bucketPrefix = bucketName.Trim('/') + "/";
        while (trimmed.StartsWith(bucketPrefix, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[bucketPrefix.Length..].TrimStart('/');
        }

        return trimmed;
    }

    private string? ResolveEquipmentImageUrl(string? rawImagePath, byte[]? imageBytes = null)
    {
        if (!string.IsNullOrWhiteSpace(rawImagePath))
        {
            var trimmed = rawImagePath.Trim();
            if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return trimmed;

            bool isR2Reference =
                trimmed.StartsWith("equipment/", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("services/", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("r2.dev", StringComparison.OrdinalIgnoreCase);

            if (isR2Reference && _r2Configured && _r2Client != null)
            {
                try
                {
                    var key = ExtractR2ObjectKey(trimmed, _servicesBucket);
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        var req = new GetPreSignedUrlRequest
                        {
                            BucketName = _servicesBucket,
                            Key = key,
                            Expires = DateTime.UtcNow.AddHours(24)
                        };
                        return _r2Client.GetPreSignedURL(req);
                    }
                }
                catch
                {
                }
            }

            if (!trimmed.Contains("r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.Contains("r2.dev", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }
        }

        if (imageBytes != null && imageBytes.Length > 0)
        {
            return $"data:image/jpeg;base64,{Convert.ToBase64String(imageBytes)}";
        }

        return null;
    }

    private async Task<(string? StoredPath, byte[]? ImageBytes)> UploadEquipmentImageToR2Async(
        string? imageInput,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imageInput))
            return (null, null);

        var trimmed = imageInput.Trim();
        if (!trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return (trimmed, null);
        }

        try
        {
            var commaIndex = trimmed.IndexOf(',');
            if (commaIndex < 0)
                return (trimmed, null);

            var header = trimmed[..commaIndex];
            var base64Data = trimmed[(commaIndex + 1)..];
            var imageBytes = Convert.FromBase64String(base64Data);

            var mimeType = "image/jpeg";
            if (header.Contains("image/png", StringComparison.OrdinalIgnoreCase))
                mimeType = "image/png";
            else if (header.Contains("image/webp", StringComparison.OrdinalIgnoreCase))
                mimeType = "image/webp";

            var ext = mimeType switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                _ => ".jpg"
            };

            if (_r2Configured && _r2Client != null)
            {
                var key = $"equipment/{Guid.NewGuid():N}{ext}";
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));

                using var ms = new MemoryStream(imageBytes);
                var putReq = new PutObjectRequest
                {
                    BucketName = _servicesBucket,
                    Key = key,
                    InputStream = ms,
                    ContentType = mimeType,
                    DisablePayloadSigning = true
                };
                await _r2Client.PutObjectAsync(putReq, cts.Token);
                return (key, imageBytes);
            }

            return (trimmed, imageBytes);
        }
        catch
        {
            return (trimmed, null);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. EQUIPMENT CATALOG & TYPES (dbo.Equipment & dbo.EquipmentType)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<EquipmentItemModel>> GetEquipmentsAsync(CancellationToken ct = default)
    {
        if (HasDatabase)
        {
            try
            {
                var dbItems = new List<EquipmentItemModel>();
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                const string sql = @"
                    IF OBJECT_ID(N'dbo.Equipment', N'U') IS NOT NULL
                    BEGIN
                        SELECT 
                            e.EquipmentID,
                            e.EquipmentName,
                            e.EquipmentDescription,
                            e.EquipmentTypeID,
                            ISNULL(et.EquipmentTypeName, e.EquipmentTypeID) AS EquipmentTypeName,
                            e.EquipmentStatus,
                            e.SerialNo,
                            e.ImagePath,
                            e.EquipmentImage,
                            activeEvt.EventAppointmentID,
                            activeEvt.EventName
                        FROM dbo.Equipment e
                        LEFT JOIN dbo.EquipmentType et ON et.EquipmentTypeID = e.EquipmentTypeID
                        OUTER APPLY (
                            SELECT TOP 1 ea.EventAppointmentID, ea.EventName
                            FROM dbo.EventEquipment ee
                            INNER JOIN dbo.EventAppointment ea ON ea.EventAppointmentID = ee.EventAppointmentID
                            WHERE ee.EquipmentID = e.EquipmentID
                              AND ea.EventStatus NOT IN ('Cancelled', 'Delivered', 'Completed')
                            ORDER BY ea.EventDate ASC, ea.EventTime ASC
                        ) activeEvt
                        ORDER BY e.EquipmentID ASC;
                    END;";

                await using var cmd = new SqlCommand(sql, conn);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var rawImagePath = reader.IsDBNull(7) ? null : reader.GetString(7);
                    var imageBytes = reader.IsDBNull(8) ? null : (byte[])reader[8];

                    dbItems.Add(new EquipmentItemModel
                    {
                        EquipmentID = reader.GetString(0),
                        EquipmentName = reader.GetString(1),
                        EquipmentDescription = reader.IsDBNull(2) ? null : reader.GetString(2),
                        EquipmentTypeID = reader.GetString(3),
                        EquipmentTypeName = reader.GetString(4),
                        EquipmentStatus = reader.GetString(5),
                        SerialNo = reader.IsDBNull(6) ? null : reader.GetString(6),
                        ImagePath = ResolveEquipmentImageUrl(rawImagePath, imageBytes),
                        EquipmentImage = imageBytes,
                        CurrentEventId = reader.IsDBNull(9) ? null : reader.GetString(9),
                        CurrentEventName = reader.IsDBNull(10) ? null : reader.GetString(10)
                    });
                }

                lock (_lock)
                {
                    _equipments.Clear();
                    _equipments.AddRange(dbItems);
                    return _equipments.ToList();
                }
            }
            catch
            {
            }
        }

        lock (_lock)
        {
            return _equipments.OrderBy(e => e.EquipmentID).ToList();
        }
    }

    public async Task<EquipmentItemModel?> GetEquipmentByIdAsync(string equipmentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(equipmentId))
            return null;

        await GetEquipmentsAsync(ct);
        lock (_lock)
        {
            return _equipments.FirstOrDefault(e => e.EquipmentID.Equals(equipmentId.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }

    public async Task<IReadOnlyList<EquipmentTypeModel>> GetEquipmentTypesAsync(CancellationToken ct = default)
    {
        if (HasDatabase)
        {
            try
            {
                var dbTypes = new List<EquipmentTypeModel>();
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                const string sql = @"
                    IF OBJECT_ID(N'dbo.EquipmentType', N'U') IS NOT NULL
                    BEGIN
                        SELECT 
                            et.EquipmentTypeID,
                            ISNULL(et.EquipmentTypeName, et.EquipmentTypeID) AS EquipmentTypeName,
                            COUNT(e.EquipmentID) AS TotalCount,
                            SUM(CASE WHEN e.EquipmentStatus = 'Available' THEN 1 ELSE 0 END) AS AvailableCount,
                            SUM(CASE WHEN e.EquipmentStatus = 'Maintenance' THEN 1 ELSE 0 END) AS MaintenanceCount
                        FROM dbo.EquipmentType et
                        LEFT JOIN dbo.Equipment e ON e.EquipmentTypeID = et.EquipmentTypeID
                        GROUP BY et.EquipmentTypeID, et.EquipmentTypeName
                        ORDER BY et.EquipmentTypeName ASC;
                    END;";

                await using var cmd = new SqlCommand(sql, conn);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var typeId = reader.GetString(0);
                    string existingColor = "Blue";
                    lock (_lock)
                    {
                        existingColor = _types.FirstOrDefault(t => t.EquipmentTypeID.Equals(typeId, StringComparison.OrdinalIgnoreCase))?.ColorTag ?? "Blue";
                    }

                    dbTypes.Add(new EquipmentTypeModel
                    {
                        EquipmentTypeID = typeId,
                        EquipmentTypeName = reader.GetString(1),
                        AssociatedItemsCount = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                        AvailableCount = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                        MaintenanceCount = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                        Status = "Active",
                        ColorTag = existingColor
                    });
                }

                lock (_lock)
                {
                    _types.Clear();
                    _types.AddRange(dbTypes);
                    return _types.ToList();
                }
            }
            catch
            {
            }
        }

        lock (_lock)
        {
            foreach (var t in _types)
            {
                var itemsForType = _equipments.Where(e => e.EquipmentTypeID.Equals(t.EquipmentTypeID, StringComparison.OrdinalIgnoreCase)).ToList();
                t.AssociatedItemsCount = itemsForType.Count;
                t.AvailableCount = itemsForType.Count(e => e.IsAvailable);
                t.MaintenanceCount = itemsForType.Count(e => e.IsMaintenance);
            }

            return _types.OrderBy(t => t.EquipmentTypeName).ToList();
        }
    }

    public async Task<string> GetNextEquipmentIdPreviewAsync(CancellationToken ct = default)
    {
        if (HasDatabase)
        {
            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                await using var cmd = new SqlCommand("dbo.sp_GetNextEquipmentIdPreview", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };
                var res = await cmd.ExecuteScalarAsync(ct);
                if (res != null && res != DBNull.Value && !string.IsNullOrWhiteSpace(res.ToString()))
                {
                    return res.ToString()!;
                }
            }
            catch
            {
            }
        }

        lock (_lock)
        {
            int maxNum = 0;
            foreach (var eq in _equipments)
            {
                var match = Regex.Match(eq.EquipmentID, @"\d+");
                if (match.Success && int.TryParse(match.Value, out int n) && n > maxNum)
                    maxNum = n;
            }
            return $"EQP-{(maxNum + 1):D4}";
        }
    }

    public async Task<(bool Success, string? Error, EquipmentItemModel? CreatedItem)> CreateEquipmentAsync(
        string name,
        string? description,
        string typeId,
        string? serialNo,
        string? imagePath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (false, "Equipment name cannot be empty.", null);

        if (string.IsNullOrWhiteSpace(typeId))
            return (false, "Equipment category type is required.", null);

        var (storedImagePath, imageBytes) = await UploadEquipmentImageToR2Async(imagePath, ct);

        if (HasDatabase)
        {
            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                await using var cmd = new SqlCommand("dbo.sp_CreateEquipmentWithCustomId", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.AddWithValue("@EquipmentName", name.Trim());
                cmd.Parameters.AddWithValue("@EquipmentDescription", (object?)description?.Trim() ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@EquipmentTypeID", typeId.Trim());
                cmd.Parameters.AddWithValue("@SerialNo", string.IsNullOrWhiteSpace(serialNo) ? DBNull.Value : serialNo.Trim());
                cmd.Parameters.AddWithValue("@ImagePath", (object?)storedImagePath ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CreatedByAdminID", "ADM-001");

                var outputIdParam = new SqlParameter("@AssignedEquipmentID", SqlDbType.NVarChar, 30)
                {
                    Direction = ParameterDirection.Output
                };
                cmd.Parameters.Add(outputIdParam);

                EquipmentItemModel? createdItem = null;
                await using (var reader = await cmd.ExecuteReaderAsync(ct))
                {
                    if (await reader.ReadAsync(ct))
                    {
                        var createdId = reader.GetString(0);
                        createdItem = new EquipmentItemModel
                        {
                            EquipmentID = createdId,
                            EquipmentName = reader.GetString(1),
                            EquipmentDescription = reader.IsDBNull(2) ? null : reader.GetString(2),
                            EquipmentTypeID = reader.GetString(3),
                            EquipmentTypeName = reader.GetString(4),
                            EquipmentStatus = reader.GetString(5),
                            SerialNo = reader.IsDBNull(6) ? null : reader.GetString(6),
                            ImagePath = ResolveEquipmentImageUrl(reader.IsDBNull(7) ? storedImagePath : reader.GetString(7), imageBytes),
                            EquipmentImage = imageBytes
                        };
                    }
                }

                var assignedId = createdItem?.EquipmentID ?? outputIdParam.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(assignedId) && imageBytes != null && imageBytes.Length > 0)
                {
                    await using var imgCmd = new SqlCommand(
                        "UPDATE dbo.Equipment SET EquipmentImage = @Img WHERE EquipmentID = @Id;", conn);
                    imgCmd.Parameters.Add("@Img", SqlDbType.VarBinary, -1).Value = imageBytes;
                    imgCmd.Parameters.AddWithValue("@Id", assignedId);
                    await imgCmd.ExecuteNonQueryAsync(ct);
                }

                if (createdItem == null && !string.IsNullOrWhiteSpace(assignedId))
                {
                    createdItem = new EquipmentItemModel
                    {
                        EquipmentID = assignedId,
                        EquipmentName = name.Trim(),
                        EquipmentDescription = description?.Trim(),
                        EquipmentTypeID = typeId.Trim(),
                        EquipmentTypeName = typeId.Trim(),
                        EquipmentStatus = "Available",
                        SerialNo = string.IsNullOrWhiteSpace(serialNo) ? null : serialNo.Trim(),
                        ImagePath = ResolveEquipmentImageUrl(storedImagePath, imageBytes),
                        EquipmentImage = imageBytes
                    };
                }

                await GetEquipmentsAsync(ct);
                NotifyStateChanged();
                return (true, null, createdItem);
            }
            catch (SqlException ex)
            {
                return (false, ex.Message, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, null);
            }
        }

        lock (_lock)
        {
            var type = _types.FirstOrDefault(t => t.EquipmentTypeID.Equals(typeId, StringComparison.OrdinalIgnoreCase));
            if (type == null)
            {
                type = new EquipmentTypeModel
                {
                    EquipmentTypeID = typeId.Trim(),
                    EquipmentTypeName = typeId.Trim(),
                    ColorTag = "Blue",
                    Status = "Active"
                };
                _types.Add(type);
            }

            if (!string.IsNullOrWhiteSpace(serialNo) && _equipments.Any(e => !string.IsNullOrEmpty(e.SerialNo) && e.SerialNo.Equals(serialNo.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return (false, $"An equipment item with Serial Number '{serialNo}' already exists in the registry.", null);
            }

            int maxNum = 0;
            foreach (var eq in _equipments)
            {
                var match = Regex.Match(eq.EquipmentID, @"\d+");
                if (match.Success && int.TryParse(match.Value, out int n) && n > maxNum)
                    maxNum = n;
            }
            var generatedId = $"EQP-{(maxNum + 1):D4}";

            var newItem = new EquipmentItemModel
            {
                EquipmentID = generatedId,
                EquipmentName = name.Trim(),
                EquipmentDescription = description?.Trim(),
                EquipmentTypeID = type.EquipmentTypeID,
                EquipmentTypeName = type.EquipmentTypeName,
                EquipmentStatus = "Available",
                SerialNo = string.IsNullOrWhiteSpace(serialNo) ? null : serialNo.Trim(),
                ImagePath = ResolveEquipmentImageUrl(storedImagePath, imageBytes),
                EquipmentImage = imageBytes
            };

            _equipments.Insert(0, newItem);
            NotifyStateChanged();
            return (true, null, newItem);
        }
    }

    public async Task<(bool Success, string? Error)> UpdateEquipmentAsync(
        string id,
        string name,
        string? description,
        string typeId,
        string? serialNo,
        string? imagePath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return (false, "Equipment ID is required.");
        if (string.IsNullOrWhiteSpace(name))
            return (false, "Equipment name cannot be empty.");

        string? storedImagePath = imagePath;
        byte[]? imageBytes = null;
        if (!string.IsNullOrWhiteSpace(imagePath) && imagePath.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            (storedImagePath, imageBytes) = await UploadEquipmentImageToR2Async(imagePath, ct);
        }

        if (HasDatabase)
        {
            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                const string sql = @"
                    UPDATE dbo.Equipment
                    SET EquipmentName = @Name,
                        EquipmentDescription = @Desc,
                        EquipmentTypeID = @TypeId,
                        SerialNo = @SerialNo,
                        ImagePath = CASE 
                            WHEN @ImgPath IS NOT NULL AND @ImgPath NOT LIKE 'https://%.r2.cloudflarestorage.com/%' THEN @ImgPath 
                            ELSE ImagePath 
                        END,
                        EquipmentImage = COALESCE(@ImgBytes, EquipmentImage)
                    WHERE EquipmentID = @Id;

                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ActionDetails, ChangedByType, ChangedByAdminID)
                    VALUES ('UPDATE', 'Equipment', @Id, @Details, 'Admin', 'ADM-001');";

                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", id.Trim());
                cmd.Parameters.AddWithValue("@Name", name.Trim());
                cmd.Parameters.AddWithValue("@Desc", (object?)description?.Trim() ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@TypeId", typeId.Trim());
                cmd.Parameters.AddWithValue("@SerialNo", string.IsNullOrWhiteSpace(serialNo) ? DBNull.Value : serialNo.Trim());
                cmd.Parameters.AddWithValue("@ImgPath", (object?)storedImagePath ?? DBNull.Value);
                cmd.Parameters.Add("@ImgBytes", SqlDbType.VarBinary, -1).Value = (object?)imageBytes ?? DBNull.Value;
                cmd.Parameters.AddWithValue("@Details", $"Equipment details updated for ID {id.Trim()}.");

                await cmd.ExecuteNonQueryAsync(ct);
                await GetEquipmentsAsync(ct);
                NotifyStateChanged();
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        lock (_lock)
        {
            var eq = _equipments.FirstOrDefault(e => e.EquipmentID.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
            if (eq == null)
                return (false, $"Equipment '{id}' not found.");

            var type = _types.FirstOrDefault(t => t.EquipmentTypeID.Equals(typeId, StringComparison.OrdinalIgnoreCase) || t.EquipmentTypeName.Equals(typeId, StringComparison.OrdinalIgnoreCase));
            if (type != null)
            {
                eq.EquipmentTypeID = type.EquipmentTypeID;
                eq.EquipmentTypeName = type.EquipmentTypeName;
            }

            eq.EquipmentName = name.Trim();
            eq.EquipmentDescription = description?.Trim();
            eq.SerialNo = string.IsNullOrWhiteSpace(serialNo) ? null : serialNo.Trim();
            if (!string.IsNullOrWhiteSpace(storedImagePath))
            {
                eq.ImagePath = ResolveEquipmentImageUrl(storedImagePath, imageBytes);
            }

            NotifyStateChanged();
            return (true, null);
        }
    }

    public async Task<(bool Success, string? Error)> UpdateEquipmentTypeAsync(
        string id,
        string name,
        string colorTag = "Blue",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return (false, "Equipment Type ID is required.");
        if (string.IsNullOrWhiteSpace(name))
            return (false, "Equipment Type name cannot be empty.");

        if (HasDatabase)
        {
            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                const string sql = @"
                    UPDATE dbo.EquipmentType
                    SET EquipmentTypeName = @Name
                    WHERE EquipmentTypeID = @Id;

                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ActionDetails, ChangedByType, ChangedByAdminID)
                    VALUES ('UPDATE', 'EquipmentType', @Id, @Details, 'Admin', 'ADM-001');";

                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", id.Trim());
                cmd.Parameters.AddWithValue("@Name", name.Trim());
                cmd.Parameters.AddWithValue("@Details", $"Equipment Type '{name.Trim()}' updated in catalog.");
                await cmd.ExecuteNonQueryAsync(ct);

                await GetEquipmentTypesAsync(ct);
                NotifyStateChanged();
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        lock (_lock)
        {
            var type = _types.FirstOrDefault(t => t.EquipmentTypeID.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
            if (type == null)
                return (false, $"Equipment Type '{id}' not found.");

            type.EquipmentTypeName = name.Trim();
            type.ColorTag = string.IsNullOrWhiteSpace(colorTag) ? "Blue" : colorTag.Trim();

            foreach (var eq in _equipments.Where(e => e.EquipmentTypeID.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                eq.EquipmentTypeName = type.EquipmentTypeName;
            }

            NotifyStateChanged();
            return (true, null);
        }
    }

    public async Task<(bool Success, string? Error)> AddEquipmentAsync(
        string name,
        string typeId,
        string? serialNo,
        string? equipmentId = null,
        CancellationToken ct = default)
    {
        var (success, error, _) = await CreateEquipmentAsync(name, null, typeId, serialNo, null, ct);
        return (success, error);
    }

    public async Task<string> GetNextEquipmentTypeIdAsync(CancellationToken ct = default)
    {
        if (HasDatabase)
        {
            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                const string sql = @"
                    DECLARE @MaxTableVal INT = 0;
                    IF OBJECT_ID(N'dbo.EquipmentType', N'U') IS NOT NULL
                    BEGIN
                        SELECT @MaxTableVal = ISNULL(MAX(TRY_CAST(SUBSTRING(EquipmentTypeID, 6, 10) AS INT)), 0)
                        FROM dbo.EquipmentType
                        WHERE EquipmentTypeID LIKE 'ETYP-%';
                    END;
                    SELECT 'ETYP-' + RIGHT('0000' + CAST((@MaxTableVal + 1) AS VARCHAR(10)), 4);";

                await using var cmd = new SqlCommand(sql, conn);
                var res = await cmd.ExecuteScalarAsync(ct);
                if (res != null && res != DBNull.Value && !string.IsNullOrWhiteSpace(res.ToString()))
                {
                    return res.ToString()!;
                }
            }
            catch
            {
            }
        }

        lock (_lock)
        {
            int maxId = 0;
            foreach (var t in _types)
            {
                if (t.EquipmentTypeID.StartsWith("ETYP-", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(t.EquipmentTypeID[5..], out var parsed) && parsed > maxId)
                {
                    maxId = parsed;
                }
            }
            return $"ETYP-{(maxId + 1):D4}";
        }
    }

    public async Task<(bool Success, string? Error)> AddEquipmentTypeAsync(
        string typeId,
        string typeName,
        string colorTag = "Blue",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(typeId) || string.IsNullOrWhiteSpace(typeName))
            return (false, "Category ID and Name are required.");

        var cleanId = typeId.Trim().ToUpperInvariant();
        var cleanName = typeName.Trim();

        if (HasDatabase)
        {
            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                await using var cmd = new SqlCommand("dbo.sp_CreateEquipmentType", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };
                cmd.Parameters.AddWithValue("@EquipmentTypeName", cleanName);
                cmd.Parameters.AddWithValue("@EquipmentTypeID", cleanId);
                cmd.Parameters.AddWithValue("@CreatedByAdminID", "ADM-001");
                var outParam = new SqlParameter("@GeneratedID", SqlDbType.NVarChar, 30)
                {
                    Direction = ParameterDirection.Output
                };
                cmd.Parameters.Add(outParam);

                await cmd.ExecuteNonQueryAsync(ct);
                await GetEquipmentTypesAsync(ct);
                NotifyStateChanged();
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        lock (_lock)
        {
            if (_types.Any(t => t.EquipmentTypeID.Equals(cleanId, StringComparison.OrdinalIgnoreCase)))
                return (false, $"Equipment Type '{cleanId}' already exists.");

            _types.Add(new EquipmentTypeModel
            {
                EquipmentTypeID = cleanId,
                EquipmentTypeName = cleanName,
                ColorTag = colorTag,
                Status = "Active"
            });

            NotifyStateChanged();
            return (true, null);
        }
    }

    public async Task<(bool Success, string? Error)> UpdateEquipmentStatusAsync(
        string equipmentId,
        string newStatus,
        string details,
        string actorId,
        string actorType = "Admin",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(equipmentId))
            return (false, "Equipment ID is required.");

        if (HasDatabase)
        {
            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                const string sql = @"
                    DECLARE @OldStatus NVARCHAR(20);
                    SELECT @OldStatus = EquipmentStatus FROM dbo.Equipment WHERE EquipmentID = @Id;
                    IF @OldStatus IS NULL
                        THROW 50001, 'Equipment was not found.', 1;

                    UPDATE dbo.Equipment
                    SET EquipmentStatus = @NewStatus
                    WHERE EquipmentID = @Id;

                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ActionDetails, ChangedByType, ChangedByAdminID, ChangedByEmployeeID)
                    VALUES ('STATUS_CHANGE', 'Equipment', @Id, @Details, @ActorType, @AdminId, @EmpId);";

                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", equipmentId.Trim());
                cmd.Parameters.AddWithValue("@NewStatus", newStatus.Trim());
                cmd.Parameters.AddWithValue("@Details", $"Status changed to {newStatus}. Details: {details}");
                cmd.Parameters.AddWithValue("@ActorType", actorType);
                cmd.Parameters.AddWithValue("@AdminId", actorType == "Admin" ? (object)actorId : DBNull.Value);
                cmd.Parameters.AddWithValue("@EmpId", actorType == "Employee" ? (object)actorId : DBNull.Value);

                await cmd.ExecuteNonQueryAsync(ct);
                await GetEquipmentsAsync(ct);
                NotifyStateChanged();
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        lock (_lock)
        {
            var eq = _equipments.FirstOrDefault(e => e.EquipmentID.Equals(equipmentId, StringComparison.OrdinalIgnoreCase));
            if (eq == null)
                return (false, $"Equipment '{equipmentId}' was not found.");

            eq.EquipmentStatus = newStatus;
            NotifyStateChanged();
            return (true, null);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. DISPATCH & CHECKOUT (dbo.EventEquipment & dbo.EventAppointment)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<DispatchItemModel>> GetDispatchesAsync(CancellationToken ct = default)
    {
        if (HasDatabase)
        {
            try
            {
                var dbDispatches = new List<DispatchItemModel>();
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                const string sql = @"
                    IF OBJECT_ID(N'dbo.EventEquipment', N'U') IS NOT NULL
                    BEGIN
                        SELECT 
                            ee.EventEquipmentID,
                            ee.EventAppointmentID,
                            ISNULL(ea.EventName, ee.EventAppointmentID) AS EventName,
                            ISNULL(ea.EventDate, CAST(SYSUTCDATETIME() AS DATE)) AS EventDate,
                            ISNULL(ea.EventTime, CAST('09:00:00' AS TIME)) AS EventTime,
                            ISNULL(ea.CustomerID, '') AS CustomerID,
                            ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(c.CustomerFirstName, '') + ' ' + ISNULL(c.CustomerLastName, ''))), ''), ISNULL(ea.CustomerID, '')) AS CustomerName,
                            ee.EquipmentID,
                            ISNULL(e.EquipmentName, ee.EquipmentID) AS EquipmentName,
                            e.SerialNo,
                            ISNULL(et.EquipmentTypeName, ISNULL(e.EquipmentTypeID, '')) AS EquipmentTypeName,
                            ISNULL(ea.EventStatus, 'Confirmed') AS EventStatus
                        FROM dbo.EventEquipment ee
                        LEFT JOIN dbo.EventAppointment ea ON ea.EventAppointmentID = ee.EventAppointmentID
                        LEFT JOIN dbo.Customer c ON c.CustomerID = ea.CustomerID
                        LEFT JOIN dbo.Equipment e ON e.EquipmentID = ee.EquipmentID
                        LEFT JOIN dbo.EquipmentType et ON et.EquipmentTypeID = e.EquipmentTypeID
                        ORDER BY ee.EventEquipmentID DESC;
                    END;";

                await using var cmd = new SqlCommand(sql, conn);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var eventDate = reader.GetDateTime(3);
                    var eventStatus = reader.GetString(11);
                    string dispatchStatus = eventStatus.Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
                                            eventStatus.Equals("Delivered", StringComparison.OrdinalIgnoreCase)
                        ? "Returned"
                        : (eventDate.Date <= DateTime.Today ? "Dispatched" : "Scheduled");

                    dbDispatches.Add(new DispatchItemModel
                    {
                        EventEquipmentID = reader.GetInt32(0),
                        EventAppointmentID = reader.GetString(1),
                        EventName = reader.GetString(2),
                        EventDate = eventDate,
                        EventTime = reader.GetTimeSpan(4),
                        CustomerID = reader.GetString(5),
                        CustomerName = reader.GetString(6),
                        EquipmentID = reader.GetString(7),
                        EquipmentName = reader.GetString(8),
                        SerialNo = reader.IsDBNull(9) ? null : reader.GetString(9),
                        EquipmentTypeName = reader.GetString(10),
                        DispatchStatus = dispatchStatus,
                        DispatchedAt = eventDate
                    });
                }

                lock (_lock)
                {
                    _dispatches.Clear();
                    _dispatches.AddRange(dbDispatches);
                    return _dispatches.ToList();
                }
            }
            catch
            {
            }
        }

        lock (_lock)
        {
            return _dispatches.OrderByDescending(d => d.DispatchedAt).ToList();
        }
    }

    public async Task<IReadOnlyList<EventAppointmentModel>> GetUpcomingEventsAsync(CancellationToken ct = default)
    {
        if (HasDatabase)
        {
            try
            {
                var dbEvents = new List<EventAppointmentModel>();
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                const string sql = @"
                    IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
                    BEGIN
                        SELECT 
                            ea.EventAppointmentID,
                            ea.EventName,
                            ISNULL(ea.EventDescription, '') AS EventDescription,
                            ea.CustomerID,
                            ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(c.CustomerFirstName, '') + ' ' + ISNULL(c.CustomerLastName, ''))), ''), ea.CustomerID) AS CustomerName,
                            ea.EventDate,
                            ea.EventTime,
                            ea.EventStatus
                        FROM dbo.EventAppointment ea
                        LEFT JOIN dbo.Customer c ON c.CustomerID = ea.CustomerID
                        WHERE ea.EventStatus NOT IN ('Cancelled')
                        ORDER BY ea.EventDate ASC, ea.EventTime ASC;
                    END;";

                await using var cmd = new SqlCommand(sql, conn);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    dbEvents.Add(new EventAppointmentModel
                    {
                        EventAppointmentID = reader.GetString(0),
                        EventName = reader.GetString(1),
                        EventDescription = reader.GetString(2),
                        CustomerID = reader.GetString(3),
                        CustomerName = reader.GetString(4),
                        EventDate = reader.GetDateTime(5),
                        EventTime = reader.GetTimeSpan(6),
                        EventStatus = reader.GetString(7)
                    });
                }

                lock (_lock)
                {
                    _events.Clear();
                    _events.AddRange(dbEvents);
                    return _events.ToList();
                }
            }
            catch
            {
            }
        }

        lock (_lock)
        {
            return _events.OrderBy(e => e.EventDate).ThenBy(e => e.EventTime).ToList();
        }
    }

    public async Task<(bool Success, string? Error)> AssignEquipmentToEventAsync(
        string eventId,
        string equipmentId,
        string actorId = "ADM-001",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(equipmentId))
            return (false, "Event and Equipment IDs are required.");

        if (HasDatabase)
        {
            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                await using var checkCmd = new SqlCommand(
                    "SELECT EquipmentName, EquipmentStatus FROM dbo.Equipment WHERE EquipmentID = @EqId;", conn);
                checkCmd.Parameters.AddWithValue("@EqId", equipmentId.Trim());
                await using (var r = await checkCmd.ExecuteReaderAsync(ct))
                {
                    if (!await r.ReadAsync(ct))
                        return (false, $"Equipment '{equipmentId}' not found.");

                    var eqName = r.GetString(0);
                    var eqStatus = r.GetString(1);
                    if (eqStatus.Equals("Maintenance", StringComparison.OrdinalIgnoreCase))
                        return (false, $"Cannot dispatch '{eqName}' ({equipmentId}). It is currently flagged in MAINTENANCE.");
                    if (eqStatus.Equals("Retired", StringComparison.OrdinalIgnoreCase))
                        return (false, $"Cannot dispatch '{eqName}'. It has been retired from active studio inventory.");
                }

                await using var cmd = new SqlCommand("dbo.sp_AssignEquipmentToEvent", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };
                cmd.Parameters.AddWithValue("@EventAppointmentID", eventId.Trim());
                cmd.Parameters.AddWithValue("@EquipmentID", equipmentId.Trim());
                await cmd.ExecuteNonQueryAsync(ct);

                await using var logCmd = new SqlCommand(@"
                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ActionDetails, ChangedByType, ChangedByAdminID)
                    VALUES ('UPDATE', 'Equipment', @EqId, @Details, 'Admin', @ActorId);", conn);
                logCmd.Parameters.AddWithValue("@EqId", equipmentId.Trim());
                logCmd.Parameters.AddWithValue("@Details", $"Dispatched gear for event appointment {eventId.Trim()}.");
                logCmd.Parameters.AddWithValue("@ActorId", actorId);
                await logCmd.ExecuteNonQueryAsync(ct);

                await GetDispatchesAsync(ct);
                await GetEquipmentsAsync(ct);
                NotifyStateChanged();
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        lock (_lock)
        {
            var evt = _events.FirstOrDefault(e => e.EventAppointmentID.Equals(eventId, StringComparison.OrdinalIgnoreCase));
            if (evt == null)
                return (false, $"Event appointment '{eventId}' not found.");

            var eq = _equipments.FirstOrDefault(e => e.EquipmentID.Equals(equipmentId, StringComparison.OrdinalIgnoreCase));
            if (eq == null)
                return (false, $"Equipment '{equipmentId}' not found.");

            if (eq.IsMaintenance)
                return (false, $"Cannot dispatch '{eq.EquipmentName}' ({eq.EquipmentID}). It is currently flagged in MAINTENANCE.");

            if (eq.IsRetired)
                return (false, $"Cannot dispatch '{eq.EquipmentName}'. It has been retired from the active studio inventory.");

            var dispatch = new DispatchItemModel
            {
                EventEquipmentID = _nextEventEquipmentId++,
                EventAppointmentID = evt.EventAppointmentID,
                EventName = evt.EventName,
                EventDate = evt.EventDate,
                EventTime = evt.EventTime,
                CustomerID = evt.CustomerID,
                CustomerName = evt.CustomerName,
                EquipmentID = eq.EquipmentID,
                EquipmentName = eq.EquipmentName,
                SerialNo = eq.SerialNo,
                EquipmentTypeName = eq.EquipmentTypeName,
                DispatchStatus = evt.EventDate.Date <= DateTime.Today ? "Dispatched" : "Scheduled",
                DispatchedAt = DateTime.UtcNow
            };
            _dispatches.Insert(0, dispatch);
            NotifyStateChanged();
            return (true, null);
        }
    }

    public async Task<(bool Success, string? Error)> AssignMultipleEquipmentToEventAsync(
        string eventId,
        IEnumerable<string> equipmentIds,
        string actorId = "ADM-001",
        CancellationToken ct = default)
    {
        foreach (var id in equipmentIds)
        {
            var (success, error) = await AssignEquipmentToEventAsync(eventId, id, actorId, ct);
            if (!success) return (false, error);
        }
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> VerifyReturnAsync(
        int eventEquipmentId,
        string notes,
        string actorId = "ADM-001",
        bool flagMaintenance = false,
        CancellationToken ct = default)
    {
        if (HasDatabase)
        {
            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                const string sql = @"
                    DECLARE @EqId NVARCHAR(30);
                    DECLARE @EvtId NVARCHAR(30);
                    SELECT @EqId = EquipmentID, @EvtId = EventAppointmentID
                    FROM dbo.EventEquipment
                    WHERE EventEquipmentID = @EeId;

                    IF @EqId IS NULL
                        THROW 50001, 'Dispatch assignment not found.', 1;

                    DELETE FROM dbo.EventEquipment WHERE EventEquipmentID = @EeId;

                    UPDATE dbo.Equipment
                    SET EquipmentStatus = @NewStatus
                    WHERE EquipmentID = @EqId;

                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ActionDetails, ChangedByType, ChangedByAdminID)
                    VALUES (@ActionType, 'Equipment', @EqId, @Details, 'Admin', @ActorId);";

                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@EeId", eventEquipmentId);
                cmd.Parameters.AddWithValue("@NewStatus", flagMaintenance ? "Maintenance" : "Available");
                cmd.Parameters.AddWithValue("@ActionType", flagMaintenance ? "STATUS_CHANGE" : "UPDATE");
                cmd.Parameters.AddWithValue("@Details", flagMaintenance
                    ? $"FLAGGED FOR MAINTENANCE upon return. Notes: {notes}"
                    : $"Gear return verified by {actorId}. Notes: {notes}");
                cmd.Parameters.AddWithValue("@ActorId", actorId);

                await cmd.ExecuteNonQueryAsync(ct);
                await GetDispatchesAsync(ct);
                await GetEquipmentsAsync(ct);
                NotifyStateChanged();
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        lock (_lock)
        {
            var dispatch = _dispatches.FirstOrDefault(d => d.EventEquipmentID == eventEquipmentId);
            if (dispatch == null)
                return (false, $"Dispatch assignment ID #{eventEquipmentId} not found.");

            dispatch.DispatchStatus = "Returned";
            dispatch.ReturnedAt = DateTime.UtcNow;
            dispatch.VerifiedBy = actorId;
            if (!string.IsNullOrWhiteSpace(notes))
                dispatch.Notes = (dispatch.Notes + " | Return: " + notes).Trim();

            var eq = _equipments.FirstOrDefault(e => e.EquipmentID.Equals(dispatch.EquipmentID, StringComparison.OrdinalIgnoreCase));
            if (eq != null)
            {
                eq.CurrentEventId = null;
                eq.CurrentEventName = null;
                eq.EquipmentStatus = flagMaintenance ? "Maintenance" : "Available";
            }

            NotifyStateChanged();
            return (true, null);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. MAINTENANCE & SERVICING (dbo.HistoryLog & dbo.Equipment)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<MaintenanceLogModel>> GetMaintenanceLogsAsync(CancellationToken ct = default)
    {
        if (HasDatabase)
        {
            try
            {
                var dbLogs = new List<MaintenanceLogModel>();
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                const string sql = @"
                    IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
                    BEGIN
                        SELECT 
                            h.HistoryLogID,
                            h.ActionType,
                            h.EntityName,
                            h.EntityID,
                            ISNULL(e.EquipmentName, h.EntityID) AS EquipmentName,
                            e.SerialNo,
                            ISNULL(e.EquipmentStatus, 'Available') AS EquipmentStatus,
                            ISNULL(h.ActionDetails, '') AS ActionDetails,
                            h.ChangedByType,
                            h.ChangedByAdminID,
                            h.ChangedByEmployeeID,
                            h.LoggedAt
                        FROM dbo.HistoryLog h
                        LEFT JOIN dbo.Equipment e ON e.EquipmentID = h.EntityID
                        WHERE h.EntityName IN ('Equipment', 'EquipmentType', 'EventEquipment')
                        ORDER BY h.LoggedAt DESC, h.HistoryLogID DESC;
                    END;";

                await using var cmd = new SqlCommand(sql, conn);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    dbLogs.Add(new MaintenanceLogModel
                    {
                        HistoryLogID = reader.GetInt64(0),
                        ActionType = reader.GetString(1),
                        EntityName = reader.GetString(2),
                        EntityID = reader.GetString(3),
                        EquipmentName = reader.GetString(4),
                        SerialNo = reader.IsDBNull(5) ? null : reader.GetString(5),
                        EquipmentStatus = reader.GetString(6),
                        ActionDetails = reader.GetString(7),
                        ChangedByType = reader.GetString(8),
                        ChangedByAdminID = reader.IsDBNull(9) ? null : reader.GetString(9),
                        ChangedByEmployeeID = reader.IsDBNull(10) ? null : reader.GetString(10),
                        LoggedAt = reader.GetDateTime(11)
                    });
                }

                lock (_lock)
                {
                    _maintenanceLogs.Clear();
                    _maintenanceLogs.AddRange(dbLogs);
                    return _maintenanceLogs.ToList();
                }
            }
            catch
            {
            }
        }

        lock (_lock)
        {
            return _maintenanceLogs.OrderByDescending(m => m.LoggedAt).ToList();
        }
    }

    public async Task<(bool Success, string? Error)> LogMaintenanceAsync(
        string equipmentId,
        string serviceType,
        string issueDetails,
        string actorId,
        bool setUnderMaintenance = true,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(equipmentId))
            return (false, "Equipment ID is required.");

        if (HasDatabase)
        {
            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                const string sql = @"
                    IF NOT EXISTS (SELECT 1 FROM dbo.Equipment WHERE EquipmentID = @Id)
                        THROW 50001, 'Equipment not found.', 1;

                    IF @SetMaint = 1
                    BEGIN
                        UPDATE dbo.Equipment SET EquipmentStatus = 'Maintenance' WHERE EquipmentID = @Id;
                    END

                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ActionDetails, ChangedByType, ChangedByAdminID)
                    VALUES (@ActionType, 'Equipment', @Id, @Details, 'Admin', @ActorId);";

                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", equipmentId.Trim());
                cmd.Parameters.AddWithValue("@SetMaint", setUnderMaintenance ? 1 : 0);
                cmd.Parameters.AddWithValue("@ActionType", setUnderMaintenance ? "STATUS_CHANGE" : "UPDATE");
                cmd.Parameters.AddWithValue("@Details", $"[{serviceType}] {issueDetails.Trim()}");
                cmd.Parameters.AddWithValue("@ActorId", string.IsNullOrWhiteSpace(actorId) ? "ADM-001" : actorId.Trim());

                await cmd.ExecuteNonQueryAsync(ct);
                await GetEquipmentsAsync(ct);
                await GetMaintenanceLogsAsync(ct);
                NotifyStateChanged();
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        lock (_lock)
        {
            var eq = _equipments.FirstOrDefault(e => e.EquipmentID.Equals(equipmentId, StringComparison.OrdinalIgnoreCase));
            if (eq == null)
                return (false, $"Equipment '{equipmentId}' not found.");

            if (setUnderMaintenance)
            {
                eq.EquipmentStatus = "Maintenance";
                eq.CurrentEventId = null;
                eq.CurrentEventName = null;
            }

            _maintenanceLogs.Insert(0, new MaintenanceLogModel
            {
                HistoryLogID = _nextHistoryLogId++,
                ActionType = setUnderMaintenance ? "STATUS_CHANGE" : "UPDATE",
                EntityName = "Equipment",
                EntityID = eq.EquipmentID,
                EquipmentName = eq.EquipmentName,
                SerialNo = eq.SerialNo,
                EquipmentStatus = eq.EquipmentStatus,
                ActionDetails = $"[{serviceType}] {issueDetails.Trim()}",
                ChangedByType = "Admin",
                ChangedByAdminID = actorId,
                LoggedAt = DateTime.UtcNow
            });

            NotifyStateChanged();
            return (true, null);
        }
    }

    public async Task<(bool Success, string? Error)> ResolveMaintenanceAsync(
        string equipmentId,
        string resolutionNotes,
        string actorId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(equipmentId))
            return (false, "Equipment ID is required.");

        if (HasDatabase)
        {
            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                const string sql = @"
                    IF NOT EXISTS (SELECT 1 FROM dbo.Equipment WHERE EquipmentID = @Id)
                        THROW 50001, 'Equipment not found.', 1;

                    UPDATE dbo.Equipment SET EquipmentStatus = 'Available' WHERE EquipmentID = @Id;

                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ActionDetails, ChangedByType, ChangedByAdminID)
                    VALUES ('STATUS_CHANGE', 'Equipment', @Id, @Details, 'Admin', @ActorId);";

                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", equipmentId.Trim());
                cmd.Parameters.AddWithValue("@Details", $"MAINTENANCE RESOLVED: Equipment tested and returned to active service. Resolution notes: {resolutionNotes.Trim()}");
                cmd.Parameters.AddWithValue("@ActorId", string.IsNullOrWhiteSpace(actorId) ? "ADM-001" : actorId.Trim());

                await cmd.ExecuteNonQueryAsync(ct);
                await GetEquipmentsAsync(ct);
                await GetMaintenanceLogsAsync(ct);
                NotifyStateChanged();
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        lock (_lock)
        {
            var eq = _equipments.FirstOrDefault(e => e.EquipmentID.Equals(equipmentId, StringComparison.OrdinalIgnoreCase));
            if (eq == null)
                return (false, $"Equipment '{equipmentId}' not found.");

            eq.EquipmentStatus = "Available";

            _maintenanceLogs.Insert(0, new MaintenanceLogModel
            {
                HistoryLogID = _nextHistoryLogId++,
                ActionType = "STATUS_CHANGE",
                EntityName = "Equipment",
                EntityID = eq.EquipmentID,
                EquipmentName = eq.EquipmentName,
                SerialNo = eq.SerialNo,
                EquipmentStatus = "Available",
                ActionDetails = $"MAINTENANCE RESOLVED: Equipment tested and returned to active service. Resolution notes: {resolutionNotes.Trim()}",
                ChangedByType = "Admin",
                ChangedByAdminID = actorId,
                LoggedAt = DateTime.UtcNow
            });

            NotifyStateChanged();
            return (true, null);
        }
    }
}
