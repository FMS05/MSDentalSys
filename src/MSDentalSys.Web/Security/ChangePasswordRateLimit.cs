using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MSDentalSys.Web.Security;

public sealed class ChangePasswordRateLimit : IRateLimiterPolicy<string>
{
    public const string PolicyName = "ChangePassword";

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        // La autorización se ejecuta antes; nunca se utiliza un ID del formulario o de la URL.
        return string.IsNullOrEmpty(userId)
            ? RateLimitPartition.GetNoLimiter("unauthenticated")
            : RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask> OnRejected => async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }
        await context.HttpContext.Response.WriteAsync(
            "Has realizado demasiados intentos de cambio de contraseña. Espera un minuto y vuelve a intentarlo.",
            cancellationToken);
    };
}
