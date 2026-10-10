using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

/// <summary>
/// In-memory mock implementation of <see cref="IUserService"/>.
/// Replace the backing lists with EF Core / API calls when ready.
/// </summary>
public sealed class UserService : IUserService
{
    // ── Seed data ────────────────────────────────────────────────────────────

    private readonly List<Admin> _admins = new()
    {
        new Admin { Id = "SADM-001", Name = "Root Administrator", Email = "sadm@aevumstudio.com", Role = "Super Admin" },
        new Admin { Id = "ADM-001",  Name = "Maria Santos",       Email = "msantos@aevumstudio.com", Role = "Admin" },
        new Admin { Id = "ADM-002",  Name = "Jose Reyes",         Email = "jreyes@aevumstudio.com",  Role = "Admin" },
    };

    private readonly List<Employee> _employees = new()
    {
        new Employee { Id = "EMP-001", Name = "Ana Cruz",    Email = "acruz@aevumstudio.com",  Position = "Photographer",       Department = "Production" },
        new Employee { Id = "EMP-002", Name = "Ben Torres",  Email = "btorres@aevumstudio.com", Position = "Photo Editor",      Department = "Post-Production" },
        new Employee { Id = "EMP-003", Name = "Clara Lim",   Email = "clim@aevumstudio.com",    Position = "Studio Coordinator", Department = "Operations" },
    };

    private readonly List<Customer> _customers = new()
    {
        new Customer { Id = "CUST-0001", Name = "David Kim",    Email = "dkim@email.com",    Phone = "+63 912-345-6789", JoinDate = new DateTime(2025, 3, 10) },
        new Customer { Id = "CUST-0002", Name = "Elena Gomez",  Email = "egomez@email.com",  Phone = "+63 917-654-3210", JoinDate = new DateTime(2025, 6, 22) },
        new Customer { Id = "CUST-0003", Name = "Frank Uy",     Email = "fuy@email.com",     Phone = "+63 919-111-2222", JoinDate = new DateTime(2026, 1,  5)  },
    };

    // ── Admins ───────────────────────────────────────────────────────────────

    public IReadOnlyList<Admin> GetAdmins() => _admins.AsReadOnly();

    public Admin? GetAdminById(string id) =>
        _admins.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public void AddAdmin(Admin admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        // ID validity is enforced inside Admin.Id setter; duplicate check here.
        if (_admins.Any(a => a.Id.Equals(admin.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"An admin with ID '{admin.Id}' already exists.");

        _admins.Add(admin);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Business rules enforced:
    /// <list type="number">
    ///   <item>SADM-### is the root Super Admin and can NEVER be deleted.</item>
    ///   <item>ADM-### can only be deleted by an account whose ID starts with "SADM-".</item>
    /// </list>
    /// </remarks>
    public void DeleteAdmin(string targetId, string requestingUserId)
    {
        // ── Rule 1: SADM accounts are immutable ──────────────────────────────
        if (targetId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The Super Admin account (SADM-###) is the root administrator and can never be deleted or modified.");

        // ── Rule 2: Only an SADM can delete a regular ADM ────────────────────
        if (!requestingUserId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Insufficient privileges. Only the Super Admin (SADM-###) can delete a regular Admin account.");

        var target = GetAdminById(targetId)
            ?? throw new KeyNotFoundException($"Admin with ID '{targetId}' was not found.");

        _admins.Remove(target);
    }

    // ── Employees ────────────────────────────────────────────────────────────

    public IReadOnlyList<Employee> GetEmployees() => _employees.AsReadOnly();

    public Employee? GetEmployeeById(string id) =>
        _employees.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public void AddEmployee(Employee employee)
    {
        ArgumentNullException.ThrowIfNull(employee);

        if (_employees.Any(e => e.Id.Equals(employee.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"An employee with ID '{employee.Id}' already exists.");

        _employees.Add(employee);
    }

    public bool DeleteEmployee(string id)
    {
        var employee = GetEmployeeById(id);
        if (employee is null) return false;
        _employees.Remove(employee);
        return true;
    }

    // ── Customers ────────────────────────────────────────────────────────────

    public IReadOnlyList<Customer> GetCustomers() => _customers.AsReadOnly();

    public Customer? GetCustomerById(string id) =>
        _customers.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public void AddCustomer(Customer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);

        if (_customers.Any(c => c.Id.Equals(customer.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A customer with ID '{customer.Id}' already exists.");

        _customers.Add(customer);
    }

    public bool DeleteCustomer(string id)
    {
        var customer = GetCustomerById(id);
        if (customer is null) return false;
        _customers.Remove(customer);
        return true;
    }
}
