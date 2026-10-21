namespace AevumStudio.Models;

public enum BookingStatus { Pending, Confirmed, Completed, Cancelled }

public record Booking(
    int Id,
    string Title,
    string Service,
    DateTime Start,
    string Location,
    BookingStatus Status);

public record GalleryAlbum(
    int Id,
    string Title,
    int FileCount,
    DateTime DeliveredOn,
    string Tone);

public class CustomerProfile
{
    public string FirstName { get; set; } = "Sarah";
    public string MiddleName { get; set; } = "L.";
    public string LastName { get; set; } = "Jenkins";

    public string FullName
    {
        get
        {
            var parts = new[] { FirstName, MiddleName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s));
            return string.Join(" ", parts);
        }
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                FirstName = parts[0];
                MiddleName = "";
                LastName = "";
            }
            else if (parts.Length == 2)
            {
                FirstName = parts[0];
                MiddleName = "";
                LastName = parts[1];
            }
            else if (parts.Length >= 3)
            {
                FirstName = parts[0];
                MiddleName = string.Join(" ", parts.Skip(1).Take(parts.Length - 2));
                LastName = parts[^1];
            }
        }
    }

    public string DisplayName => $"{FirstName} {LastName}".Trim();

    public string Email { get; set; } = "sarah.jenkins@example.com";
    public string Phone { get; set; } = "+1 (555) 123-4567";
    public string UserId { get; set; } = "CUST-88392";
    public string Role { get; set; } = "Client";
    public string NewPassword { get; set; } = "";
    public string ConfirmPassword { get; set; } = "";
    public bool EmailReminders { get; set; } = true;
}
