using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace Blocks.Genesis;

/// <summary>
/// Resolves the request's Blocks-Version (header, else the tenant's pin, else the latest), stores it in
/// HttpContext.Items, echoes it on the response and answers GET .../meta/version (decision 0003, I11-WP5).
/// Sits directly after TenantValidationMiddleware, so the tenant is known.
/// </summary>
public class BlocksVersionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ITenants _tenants;
    private readonly TimeProvider _time;

    public BlocksVersionMiddleware(RequestDelegate next, ITenants tenants)
        : this(next, tenants, TimeProvider.System)
    {
    }

    internal BlocksVersionMiddleware(RequestDelegate next, ITenants tenants, TimeProvider time)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _time = time;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var tenantId = BlocksContext.GetContext()?.TenantId;
        var tenant = string.IsNullOrWhiteSpace(tenantId) ? null : _tenants.GetTenantByID(tenantId);
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);

        var header = context.Request.Headers[BlocksApiVersions.HeaderName].FirstOrDefault();
        var resolution = BlocksApiVersions.Resolve(header, tenant?.BlocksVersion, tenant is not null, today);

        if (resolution.Error is not null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new BaseResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string>
                {
                    { "Message", resolution.Error },
                    { "SupportedVersions", string.Join(",", BlocksApiVersions.Supported.Select(v => v.Version)) }
                }
            })).ConfigureAwait(false);
            return;
        }

        context.Items[BlocksApiVersions.ItemKey] = resolution.Version;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[BlocksApiVersions.HeaderName] = resolution.Version;
            if (resolution.Entry?.Deprecated is not null)
            {
                context.Response.Headers["Deprecation"] = "true";
            }
            if (resolution.Entry?.Sunset is { } sunset)
            {
                context.Response.Headers["Sunset"] = BlocksApiVersions.SunsetHeader(sunset);
            }
            return Task.CompletedTask;
        });

        if (HttpMethods.IsGet(context.Request.Method) && (context.Request.Path.Value ?? string.Empty).TrimEnd('/').EndsWith("/meta/version", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                path = "v4",
                latest = BlocksApiVersions.Latest,
                baseline = BlocksApiVersions.Baseline,
                supported = BlocksApiVersions.Supported.Select(v => v.Version),
                deprecated = BlocksApiVersions.Supported.Where(v => v.Deprecated is not null)
                    .Select(v => new { version = v.Version, sunset = v.Sunset?.ToString("yyyy-MM-dd") }),
                project = tenant is null ? null : resolution.Version
            })).ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
    }
}
