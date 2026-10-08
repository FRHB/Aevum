namespace PhotoAevumStudio.Application.Entities;

public class Photo
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public int? BookingId { get; set; }
    public Booking? Booking { get; set; }
}