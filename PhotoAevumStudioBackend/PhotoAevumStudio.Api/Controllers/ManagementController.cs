using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoAevumStudio.Application.Interfaces;

namespace PhotoAevumStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // All management data requires authentication
public class ManagementController : ControllerBase
{
    private readonly IStudioManagementService _service;

    public ManagementController(IStudioManagementService service)
    {
        _service = service;
    }

    [HttpGet("employees")]
    public async Task<IActionResult> GetEmployees(CancellationToken ct)
    {
        var employees = await _service.GetEmployeesAsync(ct);
        
        // Map to a shape that matches the frontend's EmployeeViewModel
        var result = employees.Select(e => new
        {
            e.Id,
            EmployeeId = e.IdentityUser.StudioId,
            e.IdentityUser.FirstName,
            e.IdentityUser.LastName,
            e.IdentityUser.MiddleName,
            e.IdentityUser.Email,
            Role = "Employee", // Can fetch from UserManager if needed
            e.Specializations,
            e.Status
        });
        
        return Ok(result);
    }

    [HttpGet("specializations")]
    public async Task<IActionResult> GetSpecializations(CancellationToken ct)
    {
        var specs = await _service.GetSpecializationsAsync(ct);
        return Ok(specs);
    }

    [HttpGet("services")]
    public async Task<IActionResult> GetServices(CancellationToken ct)
    {
        var services = await _service.GetServicesAsync(ct);
        return Ok(services);
    }

    [HttpGet("equipment")]
    public async Task<IActionResult> GetEquipment(CancellationToken ct)
    {
        var equipment = await _service.GetEquipmentAsync(ct);
        var result = equipment.Select(eq => new
        {
            eq.Id,
            eq.Name,
            eq.SerialNumber,
            eq.Status,
            AssignedEmployee = eq.AssignedEmployee?.IdentityUser.FullName ?? string.Empty
        });
        return Ok(result);
    }
}
