using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NetOps.Server.Services;
using NetOps.Shared;
using Xunit;

namespace NetOps.Server.Tests;

public class AgentAuthenticationTests(AgentTestServer server) : IClassFixture<AgentTestServer>
{
    [Fact]
    public async Task AnonymousConnectionRequiresAuthentication()
    {
        using var client = server.CreateClient();
        using var response = await client.PostAsync("/hubs/agents/negotiate", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Token đã ký vẫn phải có cả danh tính thiết bị và quyền NetOpsAgent.
    [Theory]
    [InlineData(null, true)]
    [InlineData("agent-test", false)]
    public async Task MissingDeviceClaimOrRoleIsForbidden(string? clientId, bool hasAgentRole)
    {
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.IssueToken(clientId, hasAgentRole));
        using var response = await client.PostAsync("/hubs/agents/negotiate", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeviceCanRegisterOnlyItsAuthenticatedId()
    {
        const string agentId = "agent-test";
        await using var connection = new HubConnectionBuilder()
            .WithUrl("https://localhost/hubs/agents", options =>
            {
                // Mọi yêu cầu đi tới Server trong bộ nhớ của bài test.
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => server.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(server.IssueToken(agentId, true));
            }).Build();
        await connection.StartAsync();

        var registry = server.Services.GetRequiredService<AgentConnectionManager>();
        // Mô phỏng một máy đã online. Yêu cầu sai ID phải giữ nguyên phiên đang được dùng để nhận lệnh.
        var otherAgent = RegistrationFor("agent-other");
        registry.Register("existing-session", otherAgent);
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync<RegisterAgentResponse>("Register", otherAgent));
        Assert.True(registry.TryGetConnection(otherAgent.AgentId, out var existing));
        Assert.Equal("existing-session", existing!.ConnectionId);

        // Cùng kết nối đó đăng ký bằng ID đã cấp và tiếp tục gửi heartbeat như Agent bình thường.
        var reply = await connection.InvokeAsync<RegisterAgentResponse>("Register", RegistrationFor(agentId));
        Assert.True(reply.Success);
        await connection.InvokeAsync("Heartbeat", new HeartbeatMessage { AgentId = agentId, MemoryUsedBytes = 512 });
        Assert.True(registry.TryGetConnection(agentId, out var registered));
        Assert.Equal(512L, registered!.LatestHeartbeat!.MemoryUsedBytes);
    }

    private static RegisterAgentRequest RegistrationFor(string id) => new()
    {
        AgentId = id, Hostname = "test", OsDescription = "test",
        IpAddress = "127.0.0.1", CpuCores = 1, TotalMemoryBytes = 1024
    };
}

// Giữ nguyên middleware JWT, chính sách Keycloak và hub của ứng dụng. Khóa ký ngẫu nhiên
// chỉ tồn tại trong bài test, nên có thể kiểm tra phân quyền ngay cả khi chạy ngoại tuyến.
public sealed class AgentTestServer : WebApplicationFactory<AgentHubService>
{
    private const string Issuer = "https://keycloak.test/realms/netops";
    private readonly SymmetricSecurityKey _key = new(RandomNumberGenerator.GetBytes(32));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Luồng đăng ký chỉ cần danh bạ trong RAM. Bỏ các dịch vụ nền dùng RabbitMQ và Prometheus.
            services.RemoveAll<IHostedService>();
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
                metadata.SigningKeys.Add(_key);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
                options.TokenValidationParameters.ValidIssuer = Issuer;
            });
        });
    }

    public string IssueToken(string? clientId, bool hasAgentRole)
    {
        var claims = new List<Claim>();
        if (clientId is not null)
            claims.Add(new Claim("client_id", clientId));
        if (hasAgentRole)
            claims.Add(new Claim("resource_access", """{"netops-backend":{"roles":["NetOpsAgent"]}}""", JsonClaimValueTypes.Json));

        var token = new JwtSecurityToken(Issuer, "netops-backend", claims, expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
