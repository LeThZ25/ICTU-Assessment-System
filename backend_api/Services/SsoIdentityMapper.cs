using System.Security.Claims;
using backend_api.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;

namespace backend_api.Services;

/// <summary>Maps a validated external identity to roles provisioned in the local database.</summary>
public static class SsoIdentityMapper
{
    public static async Task ValidateAndMapAsync(TokenValidatedContext context, string provider, string domain)
    {
        var principal = context.Principal;
        var email = principal?.FindFirst("email")?.Value ?? principal?.FindFirst(ClaimTypes.Email)?.Value;
        if (provider == "Microsoft" && string.IsNullOrWhiteSpace(email))
            email = principal?.FindFirst("preferred_username")?.Value;

        email = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !email.EndsWith(domain, StringComparison.OrdinalIgnoreCase) ||
            email.Length <= domain.Length)
        {
            context.Fail("Account is outside the permitted organization domain.");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
        var emailKey = email.ToUpperInvariant();
        var matches = await db.Users.AsNoTracking()
            .Where(u => u.Email.Trim().ToUpper() == emailKey)
            .Take(2).ToListAsync();
        // Reject ambiguous identities rather than letting casing choose a provisioned account.
        if (matches.Count != 1)
        {
            context.Fail("Account identity is missing or ambiguous.");
            return;
        }
        var user = matches[0];
        if (!string.Equals(user.AuthProvider, provider, StringComparison.OrdinalIgnoreCase) ||
            (user.Role != "Lecturer" && user.Role != "Student") ||
            (user.Role == "Lecturer" && provider != "Google"))
        {
            context.Fail("Account has not been provisioned for this provider and role.");
            return;
        }

        if (user.Role == "Student" && (string.IsNullOrWhiteSpace(user.StudentCode) ||
            !await db.Students.AnyAsync(s => s.StudentCode.Trim().ToUpper() == user.StudentCode.Trim().ToUpper())))
        {
            context.Fail("Student record has not been provisioned.");
            return;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.Role),
            new("AuthProvider", provider)
        };
        if (user.Role == "Student") claims.Add(new("StudentCode", user.StudentCode!.Trim().ToUpperInvariant()));
        context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
