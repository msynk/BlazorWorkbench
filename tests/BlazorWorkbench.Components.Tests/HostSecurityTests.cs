using System.Net;
using BlazorWorkbench.Hosting;
using Microsoft.AspNetCore.Http;

namespace BlazorWorkbench.Components.Tests;

public class HostSecurityTests
{
    [Theory]
    [InlineData("localhost", true)]
    [InlineData("localhost:5000", true)]
    [InlineData("LOCALHOST:5000", true)]
    [InlineData("localhost.", true)]
    [InlineData("app.localhost:8080", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.0.0.1:5000", true)]
    [InlineData("127.1.2.3:80", true)]
    [InlineData("[::1]", true)]
    [InlineData("[::1]:5000", true)]
    [InlineData("", false)]
    [InlineData("evil.example", false)]
    [InlineData("evil.example:5000", false)]
    [InlineData("localhost.evil.example", false)]
    [InlineData("notlocalhost", false)]
    [InlineData("192.168.1.10:5000", false)]
    [InlineData("[fe80::1]:5000", false)]
    [InlineData("0.0.0.0", false)]
    public void Only_local_host_names_are_accepted(string host, bool expected) =>
        Assert.Equal(expected, WorkbenchSecurityMiddleware.IsLocalHost(new HostString(host)));

    private static async Task<(HttpContext Context, bool Called)> InvokeAsync(string host, IPAddress? remote, bool allowRemote = false)
    {
        var called = false;
        var middleware = new WorkbenchSecurityMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, new WorkbenchHostOptions { AllowRemoteAccess = allowRemote, AllowAnonymousRemoteAccess = allowRemote });
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        context.Connection.RemoteIpAddress = remote;
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context);
        return (context, called);
    }

    [Fact]
    public async Task Loopback_requests_for_a_foreign_host_name_are_rejected()
    {
        var (context, called) = await InvokeAsync("rebind.evil.example:5000", IPAddress.Loopback);
        Assert.False(called);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Local_requests_reach_the_app()
    {
        // WebApplicationFactory / TestServer requests use Host: localhost and no remote address.
        var (_, testServer) = await InvokeAsync("localhost", null);
        Assert.True(testServer);
        var (_, kestrel) = await InvokeAsync("127.0.0.1:5123", IPAddress.Loopback);
        Assert.True(kestrel);
        var (_, ipv6) = await InvokeAsync("[::1]:5123", IPAddress.IPv6Loopback);
        Assert.True(ipv6);
    }

    [Fact]
    public async Task Remote_clients_are_rejected_even_with_a_local_host_name()
    {
        var (context, called) = await InvokeAsync("localhost", IPAddress.Parse("192.168.1.10"));
        Assert.False(called);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Remote_access_skips_the_host_check()
    {
        var (_, called) = await InvokeAsync("workbench.example.com", IPAddress.Parse("10.0.0.5"), allowRemote: true);
        Assert.True(called);
    }

    [Fact]
    public void Default_headers_never_replace_headers_set_by_the_host()
    {
        IHeaderDictionary headers = new HeaderDictionary { ["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none'" };
        WorkbenchSecurityMiddleware.AddDefaultHeaders(headers);
        Assert.Equal("default-src 'self'; frame-ancestors 'none'", headers.ContentSecurityPolicy.ToString());
        Assert.Equal("nosniff", headers.XContentTypeOptions.ToString());
        Assert.Equal("no-referrer", headers["Referrer-Policy"].ToString());

        IHeaderDictionary empty = new HeaderDictionary();
        WorkbenchSecurityMiddleware.AddDefaultHeaders(empty);
        Assert.Equal(WorkbenchSecurityMiddleware.DefaultContentSecurityPolicy, empty.ContentSecurityPolicy.ToString());
    }
}
