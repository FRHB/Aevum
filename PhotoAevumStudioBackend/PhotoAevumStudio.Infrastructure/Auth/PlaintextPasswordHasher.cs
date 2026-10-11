using Microsoft.AspNetCore.Identity;
using PhotoAevumStudio.Application.Entities;

namespace PhotoAevumStudio.Infrastructure.Auth;

public class PlaintextPasswordHasher : IPasswordHasher<ApplicationUser>
{
    public string HashPassword(ApplicationUser user, string password)
    {
        return password; // No hashing
    }

    public PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword, string providedPassword)
    {
        if (hashedPassword == providedPassword)
        {
            return PasswordVerificationResult.Success;
        }
        return PasswordVerificationResult.Failed;
    }
}
