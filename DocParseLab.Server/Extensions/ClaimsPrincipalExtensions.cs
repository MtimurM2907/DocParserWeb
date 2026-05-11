using System.Security.Claims;

namespace DocParseLab.Server.Extensions;

public static class ClaimsPrincipalExtensions
{
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
}
