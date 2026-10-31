using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ProofLab.Api;

/// <summary>Production is served by the authenticated loopback reverse proxy.
/// Never trust client-supplied forwarding headers to grant access.</summary>
public sealed class ApiRequestAccess
{
    private readonly Uri? publicOrigin;
    public bool IsProduction => publicOrigin is not null;
    public ApiRequestAccess(IConfiguration configuration)
    {
        var value=configuration["Deployment:PublicOrigin"];
        if(string.IsNullOrWhiteSpace(value))return;
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri) || uri.Scheme!="https" ||
            uri.AbsolutePath!="/" || uri.Query.Length!=0 || uri.Fragment.Length!=0 || uri.UserInfo.Length!=0)
            throw new InvalidOperationException("Deployment:PublicOrigin must be an exact HTTPS origin.");
        publicOrigin=uri;
    }
    public bool TrustedHost(HttpContext context)
    {
        if(context.Connection.RemoteIpAddress is not IPAddress ip || !IPAddress.IsLoopback(ip))return false;
        if(publicOrigin is null)return context.Request.Host.Host is "localhost" or "127.0.0.1" or "::1";
        return string.Equals(context.Request.Host.Host,publicOrigin.Host,StringComparison.OrdinalIgnoreCase) &&
            (context.Request.Host.Port ?? 443)==publicOrigin.Port;
    }
    public bool AllowedOrigin(string origin,bool adaptive=false)
    {
        if(publicOrigin is not null)return string.Equals(origin,publicOrigin.GetLeftPart(UriPartial.Authority),StringComparison.OrdinalIgnoreCase);
        return origin is "http://localhost:4300" or "http://127.0.0.1:4300" ||
            (adaptive && (origin.Length==0 || origin is "http://localhost:4327" or "http://127.0.0.1:4327"));
    }
    public static bool Host(HttpContext c)=>c.RequestServices.GetRequiredService<ApiRequestAccess>().TrustedHost(c);
    public static bool Origin(HttpContext c,bool adaptive=false)=>
        c.RequestServices.GetRequiredService<ApiRequestAccess>().AllowedOrigin(c.Request.Headers.Origin.ToString(),adaptive);
    public static bool Manual(HttpContext c,string action)=>Host(c) && Origin(c) &&
        c.Request.Headers["X-ProofLab-Manual-Action"]==action;
}
