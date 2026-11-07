using Microsoft.AspNetCore.Identity;

namespace PhotoAevumStudio.Infrastructure.Auth;

public class CustomIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DuplicateEmail(string email)
    {
        return new IdentityError
        {
            Code = nameof(DuplicateEmail),
            Description = "This email address is already registered under another account role. Please use a different email."
        };
    }
}
