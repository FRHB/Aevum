using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.Entities;

namespace PhotoAevumStudio.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Photo> Photos { get; set; }
}