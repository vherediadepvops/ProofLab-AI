using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using ProofLab.Api;
using Xunit;
namespace ProofLab.Tests;
public class ApiRequestAccessTests {
    private static ApiRequestAccess Policy(string? origin=null) => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string,string?> {{"Deployment:PublicOrigin",origin}}).Build());
    private static DefaultHttpContext Context(string host,string ip="127.0.0.1") {
        var c=new DefaultHttpContext();c.Request.Host=new HostString(host);c.Connection.RemoteIpAddress=IPAddress.Parse(ip);return c;
    }
    [Fact] public void ProductionRequiresLoopbackProxyAndExactHost() {
        var p=Policy("https://3.149.236.79");
        Assert.True(p.TrustedHost(Context("3.149.236.79")));
        Assert.True(p.TrustedHost(Context("3.149.236.79:443")));
        Assert.False(p.TrustedHost(Context("3.149.236.79","203.0.113.5")));
        Assert.False(p.TrustedHost(Context("localhost")));
        Assert.False(p.TrustedHost(Context("3.149.236.79:8080")));
        var spoof=Context("attacker.test");spoof.Request.Headers["X-Forwarded-Host"]="3.149.236.79";
        Assert.False(p.TrustedHost(spoof));
    }
    [Theory]
    [InlineData("")][InlineData("null")][InlineData("http://3.149.236.79")]
    [InlineData("https://3.149.236.79.attacker.test")][InlineData("http://localhost:4300")]
    public void ProductionRejectsOtherOriginsEvenAdaptive(string origin) {
        var p=Policy("https://3.149.236.79");Assert.False(p.AllowedOrigin(origin,true));
        Assert.True(p.AllowedOrigin("https://3.149.236.79"));
    }
    [Fact] public void LocalWorkflowKeepsItsAllowedPorts() {
        var p=Policy();Assert.False(p.IsProduction);Assert.True(p.TrustedHost(Context("localhost:5391")));
        Assert.True(p.AllowedOrigin("http://localhost:4300"));Assert.True(p.AllowedOrigin("",true));
        Assert.False(p.AllowedOrigin(""));Assert.False(p.AllowedOrigin("https://evil.test"));
    }
    [Theory][InlineData("http://example.test")][InlineData("https://example.test/path")]
    public void InvalidDeploymentConfigurationFails(string origin) => Assert.Throws<InvalidOperationException>(()=>Policy(origin));
}
