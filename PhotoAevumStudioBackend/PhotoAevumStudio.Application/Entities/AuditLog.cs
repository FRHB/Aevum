namespace PhotoAevumStudio.Application.Entities;

public class AuditLog
{
    public int Id { get; set; }
    
    public string UserId { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    
    public string Action { get; set; } = string.Empty;     // e.g. "Create Booking"
    public string Resource { get; set; } = string.Empty;   // e.g. "Booking", "Employee"
    
    /// <summary>
    /// ID of the modified resource, if applicable
    /// </summary>
    public string? ResourceId { get; set; }
    
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Payload or summary of changes
    /// </summary>
    public string? Details { get; set; }
}
