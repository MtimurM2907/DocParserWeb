using System.Security.Claims;
using DocParseLab.Server.Models;

namespace DocParseLab.Server.Extensions;

public static class ClaimsPrincipalExtensions
{
    public const string RoleClaimType = "role";

    public static bool TryGetUserId(this ClaimsPrincipal? user, out int userId)
    {
        userId = 0;
        if (user?.Identity is not { IsAuthenticated: true })
        {
            return false;
        }

        var idValue = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idValue, out userId);
    }

    public static string? GetUserRole(this ClaimsPrincipal? user)
    {
        return user?.FindFirstValue(RoleClaimType);
    }

    public static bool IsInRole(this ClaimsPrincipal? user, string role)
    {
        return string.Equals(user?.GetUserRole(), role, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAdmin(this ClaimsPrincipal? user) => user.IsInRole(UserRoles.Admin);

    public static bool IsManagerOrAdmin(this ClaimsPrincipal? user) =>
        user.IsInRole(UserRoles.Admin) || user.IsInRole(UserRoles.Manager);
}
