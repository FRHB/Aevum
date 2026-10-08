namespace PhotoAevumStudio.Web.Models;

public record AuthResult(bool Success, string? Token = null, string? Error = null);

public record LoginRequest(string UserId, string Password);

public record LoginResponse(string Token, string Role, string FullName, DateTime ExpiresAt);
