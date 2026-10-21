namespace AevumStudio.Dashboard.Components.Pages;

public partial class EmployeeDashboard
{
    private bool isClockedIn;
    private string clockInTime = "";
    private string toastMessage = "";
    private bool showToast;

    // Expandable dropdown navigation state logic
    private readonly HashSet<string> openDropdowns = new(StringComparer.OrdinalIgnoreCase);

    private bool IsMenuExpanded(string menuKey) => openDropdowns.Contains(menuKey);

    private void ToggleDropdown(string menuKey)
    {
        if (!openDropdowns.Add(menuKey))
        {
            openDropdowns.Remove(menuKey);
        }
    }

    private bool isScheduleExpanded => IsMenuExpanded("Schedule");
    private bool isBookingsExpanded => IsMenuExpanded("Bookings");
    private bool isEquipmentExpanded => IsMenuExpanded("Equipment");

    private readonly List<ChecklistItem> checklistItems = new()
    {
        new ChecklistItem { Id = 1, Description = "Charge camera batteries", Time = "9:00 AM", IsCompleted = true },
        new ChecklistItem { Id = 2, Description = "Set up Studio A lighting for Sarah Jenkins", Time = "1:30 PM", IsCompleted = false },
        new ChecklistItem { Id = 3, Description = "Confirm Mark Uy’s session details", Time = "3:00 PM", IsCompleted = false },
        new ChecklistItem { Id = 4, Description = "Return equipment and log damage", Time = "5:45 PM", IsCompleted = false }
    };

    private void ToggleClock()
    {
        isClockedIn = !isClockedIn;

        if (isClockedIn)
        {
            clockInTime = DateTime.Now.ToString("h:mm tt");
            _ = ShowToast("Clocked in");
        }
        else
        {
            _ = ShowToast("Clocked out");
        }
    }

    private async Task ShowToast(string message)
    {
        toastMessage = message;
        showToast = true;
        StateHasChanged();

        await Task.Delay(1800);

        showToast = false;
        await InvokeAsync(StateHasChanged);
    }

    private sealed class ChecklistItem
    {
        public int Id { get; set; }
        public string Description { get; set; } = "";
        public string Time { get; set; } = "";
        public bool IsCompleted { get; set; }
    }
}
