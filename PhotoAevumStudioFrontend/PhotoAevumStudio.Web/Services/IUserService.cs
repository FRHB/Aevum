using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

/// <summary>
/// Contract for the mock user-management data service.
/// </summary>
public interface IUserService
{
    // ── Admins ──────────────────────────────────────────────────────────────
    IReadOnlyList<Admin> GetAdmins();
    Admin? GetAdminById(string id);

    /// <summary>Adds a new admin. Validates ID format before adding.</summary>
    void AddAdmin(Admin admin);

    /// <summary>
    /// Deletes an admin. Enforces the following rules:
    /// <list type="number">
    ///   <item>SADM-### accounts can NEVER be deleted.</item>
    ///   <item>ADM-### accounts can only be deleted by the root SADM.</item>
    /// </list>
    /// </summary>
    /// <param name="targetId">ID of the admin to delete.</param>
    /// <param name="requestingUserId">ID of the user performing the delete.</param>
    /// <exception cref="InvalidOperationException">
    ///   Thrown when business rules prevent deletion.
    /// </exception>
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
}
