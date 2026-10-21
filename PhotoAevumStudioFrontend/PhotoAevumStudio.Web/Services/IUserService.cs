global using EquipmentRow = PhotoAevumStudio.Web.Services.EquipmentRowDto;
global using EquipmentTypeRow = PhotoAevumStudio.Web.Services.EquipmentTypeRowDto;
global using BookingRow = PhotoAevumStudio.Web.Services.BookingRowDto;
global using PhotoRow = PhotoAevumStudio.Web.Services.PhotoRowDto;
global using AuditLogRow = PhotoAevumStudio.Web.Services.AuditLogRowDto;

using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public sealed record EquipmentRowDto(
    string Id,
    string Name,
    string TypeId,
    string TypeName,
    string SerialNumber,
    string Status,
    string? Image)
{
    public string Category => TypeName;
    public int Quantity => 1;
}

public sealed record EquipmentTypeRowDto(
    string Id,
    string Name,
    int TotalUnits,
    int AvailableUnits)
{
    public string Description => $"{AvailableUnits} of {TotalUnits} available";
}

public sealed class BookingRowDto
{
    public BookingRowDto(
        string Id,
        string EventName,
        string EventDescription,
        string CustomerId,
        string CustomerName,
        DateTime EventDate,
        TimeSpan EventTime,
        string Status,
        DateTime CreatedAt)
    {
        this.Id = Id;
        this.EventName = EventName;
        this.EventDescription = EventDescription;
        this.CustomerId = CustomerId;
        this.CustomerName = CustomerName;
        this.EventDate = EventDate;
        this.EventTime = EventTime;
        this.Status = Status;
        this.CreatedAt = CreatedAt;
    }

    public string Id { get; set; }
    public string EventName { get; set; }
    public string EventDescription { get; set; }
    public string CustomerId { get; set; }
    public string CustomerName { get; set; }
    public DateTime EventDate { get; set; }
    public TimeSpan EventTime { get; set; }
    public string Status { get; set; }
    public DateTime CreatedAt { get; set; }

    public string BookingId => Id;
    public string ClientName => CustomerName;
    public string ServiceTitle => EventName;
    public DateTime EventDateTime => EventDate.Date + EventTime;
    public decimal TotalPrice => 0m;
}

public sealed record PhotoRowDto(
    int Id,
    string BookingId,
    string FileName,
    string StoragePath,
    string UploadedById,
    DateTime UploadedAt,
    bool IsWatermarked,
    bool IsFinalDelivery)
{
    public string PhotoId => Id.ToString();
    public string Url => StoragePath;
}

public sealed record AuditLogRowDto(
    long Id,
    string Action,
    string EntityType,
    string? EntityId,
    string? UserId,
    string? IpAddress,
    DateTime Timestamp)
{
    public string EntityName => EntityType;
}

/// <summary>
/// Contract for the SQL Server database user and studio data service.
/// </summary>
public interface IUserService
{
    // ── Admins ──────────────────────────────────────────────────────────────
    IReadOnlyList<Admin> GetAdmins();
    Admin? GetAdminById(string id);
    void AddAdmin(Admin admin);
    void DeleteAdmin(string targetId, string requestingUserId);

    // ── Employees ───────────────────────────────────────────────────────────
    IReadOnlyList<Employee> GetEmployees();
    Employee? GetEmployeeById(string id);
    void AddEmployee(Employee employee);
    bool DeleteEmployee(string id);

    // ── Customers ───────────────────────────────────────────────────────────
    IReadOnlyList<Customer> GetCustomers();
    Customer? GetCustomerById(string id);
    void AddCustomer(Customer customer);
    bool DeleteCustomer(string id);

    // ── Specializations ─────────────────────────────────────────────────────
    IReadOnlyList<SpecializationLookupItem> GetSpecializationLookups();
    IReadOnlyList<EmployeeSpecializationDto> GetEmployeeSpecializations();
    SpecializationLookupItem AddSpecialization(SpecializationModel model);

    // ── Studio Services & Packages ──────────────────────────────────────────
    IReadOnlyList<MainServiceItem> GetMainServices();
    MainServiceItem AddMainService(MainServiceModel model);
    IReadOnlyList<PackageServiceItem> GetPackageServices();
    PackageServiceItem AddPackageService(PackageServiceModel model);
    IReadOnlyList<AddOnServiceItem> GetAdditionalServices();
    AddOnServiceItem AddAdditionalService(AddOnServiceModel model);

    // ── Equipment ───────────────────────────────────────────────────────────
    List<EquipmentRowDto> GetEquipmentRows();
    List<EquipmentTypeRowDto> GetEquipmentTypes();
    EquipmentRowDto AddEquipment(string name, string typeName, string serialNo, string status = "Available");
    EquipmentRowDto AddEquipment(string name, string typeName, int quantity, string status = "Available") =>
        AddEquipment(name, typeName, $"SN-{Guid.NewGuid():N}"[..9].ToUpperInvariant(), status);

    // ── Bookings, Photos & Audit Logs ───────────────────────────────────────
    List<BookingRowDto> GetBookings(string? customerId = null);
    void UpdateBookingStatus(string eventAppointmentId, string newStatus);
    List<PhotoRowDto> GetPhotos(string? bookingId = null);
    List<AuditLogRowDto> GetAuditLogs(string? userId = null);
}
