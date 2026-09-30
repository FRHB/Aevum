namespace YourNamespace.ViewModels
{
    public class AdminDashboardViewModel
    {
        public IEnumerable<ScheduleViewModel> Schedules { get; set; } = new List<ScheduleViewModel>();
        public IEnumerable<EmployeeViewModel> Employees { get; set; } = new List<EmployeeViewModel>();
        public IEnumerable<ServiceViewModel> Services { get; set; } = new List<ServiceViewModel>();
        public IEnumerable<EquipmentViewModel> EquipmentList { get; set; } = new List<EquipmentViewModel>();
    }

    public class ScheduleViewModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
    }

    public class EmployeeViewModel
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Initials { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    public class ServiceViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Duration { get; set; }
    }

    public class EquipmentViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string AssignedEmployee { get; set; } = string.Empty;
        public string Status { get; set; } = "Available";
    }
}