using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.Entities;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;

namespace PhotoAevumStudio.Infrastructure.Services;

public class StudioManagementService : IStudioManagementService
{
    private readonly ApplicationDbContext _db;

    public StudioManagementService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Employee>> GetEmployeesAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Employees
            .Include(e => e.IdentityUser)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetSpecializationsAsync(CancellationToken cancellationToken = default)
    {
        // Extract all specializations, flatten them, and return unique ones
        var allSpecs = await _db.Employees
            .AsNoTracking()
            .Select(e => e.Specializations)
            .ToListAsync(cancellationToken);

        return allSpecs.SelectMany(s => s).Distinct().Order().ToList();
    }

    public async Task<IReadOnlyList<StudioService>> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Services
            .AsNoTracking()
            .Where(s => s.IsActive)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Equipment>> GetEquipmentAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Equipment
            .Include(e => e.AssignedEmployee)
            .ThenInclude(emp => emp!.IdentityUser)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
