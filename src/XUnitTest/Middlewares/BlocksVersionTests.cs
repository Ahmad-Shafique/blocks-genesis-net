using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Moq;

namespace XUnitTest.Middlewares;

public class BlocksVersionTests
{
    private static readonly DateOnly Today = new(2026, 11, 25);

    private static readonly IReadOnlyList<BlocksApiVersion> Two =
    [
        new BlocksApiVersion("2026-01-01", Deprecated: new DateOnly(2026, 6, 1), Sunset: new DateOnly(2027, 6, 1)),
        new BlocksApiVersion("2026-06-01")
    ];

    [Fact]
    public void Resolve_HeaderWins()
    {
        var r = BlocksApiVersions.Resolve("2026-06-01", "2026-01-01", true, Today, Two);
        Assert.Equal("2026-06-01", r.Version);
        Assert.Null(r.Error);
    }

    [Fact]
    public void Resolve_ProjectWithoutPin_GetsBaseline_NotLatest()
    {
        var r = BlocksApiVersions.Resolve(null, null, true, Today, Two);
        Assert.Equal("2026-01-01", r.Version);
    }

    [Fact]
    public void Resolve_ProjectPin_IsUsed()
    {
        var r = BlocksApiVersions.Resolve(null, "2026-06-01", true, Today, Two);
        Assert.Equal("2026-06-01", r.Version);
    }

    [Fact]
    public void Resolve_NoProject_GetsLatest()
    {
        var r = BlocksApiVersions.Resolve(null, null, false, Today, Two);
        Assert.Equal("2026-06-01", r.Version);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("2099-01-01")]
    [InlineData("2026-1-1")]
    public void Resolve_UnknownMalformedOrFuture_IsAnError(string header)
    {
        var r = BlocksApiVersions.Resolve(header, null, true, Today, Two);
        Assert.Null(r.Version);
        Assert.Equal(BlocksApiVersions.UnknownMessage, r.Error);
    }

    [Fact]
    public void Resolve_PastSunset_IsRetired()
    {
        var r = BlocksApiVersions.Resolve("2026-01-01", null, true, new DateOnly(2027, 6, 2), Two);
        Assert.Equal(BlocksApiVersions.RetiredMessage, r.Error);
    }

    [Fact]
    public void Resolve_DeprecatedButSupported_IsAnsweredWithItsEntry()
    {
        var r = BlocksApiVersions.Resolve("2026-01-01", null, true, Today, Two);
        Assert.Null(r.Error);
        Assert.NotNull(r.Entry?.Sunset);
    }

    [Fact]
    public void Baseline_IsTheFirstSupportedValue()
    {
        Assert.Equal(BlocksApiVersions.Supported[0].Version, BlocksApiVersions.Baseline);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", BlocksApiVersions.Baseline);
    }

    private static (BlocksVersionMiddleware Middleware, Func<bool> NextCalled) Create()
    {
        var called = false;
        var tenants = new Mock<ITenants>();
        var middleware = new BlocksVersionMiddleware(_ => { called = true; return Task.CompletedTask; }, tenants.Object);
        return (middleware, () => called);
    }

    [Fact]
    public async Task Middleware_UnknownHeader_Answers400_AndSkipsTheController()
    {
        var (middleware, nextCalled) = Create();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Headers[BlocksApiVersions.HeaderName] = "1999-01-01";

        await middleware.InvokeAsync(context);

        Assert.Equal(400, context.Response.StatusCode);
        Assert.False(nextCalled());
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("Unknown_Blocks_Version", body);
        Assert.Contains(BlocksApiVersions.Baseline, body);
    }

    [Fact]
    public async Task Middleware_NoProject_StoresLatestInItems_AndCallsNext()
    {
        var (middleware, nextCalled) = Create();
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled());
        Assert.Equal(BlocksApiVersions.Latest, context.Items[BlocksApiVersions.ItemKey]);
    }

    [Fact]
    public async Task Middleware_MetaVersionEndpoint_ListsSupportedVersions()
    {
        var (middleware, nextCalled) = Create();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = "GET";
        context.Request.Path = "/api/meta/version";

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled());
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("\"supported\"", body);
        Assert.Contains(BlocksApiVersions.Latest, body);
    }
}
