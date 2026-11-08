// [6.1] Dùng bộ nhớ chung đã có trên Server
using Microsoft.Extensions.Caching.Memory;

using Microsoft.AspNetCore.SignalR;
using NetOps.Server.Services;
using NetOps.Shared;

// Dòng này đặt class NetOpsHub vào nhóm tên NetOps.Server.Hubs. Tên đầy đủ của class ấy là: NetOps.Server.Hubs.NetOpsHub
namespace NetOps.Server.Hubs;

public class NetOpsHub : Hub
{
    private readonly AgentConnectionManager _connections;
    private readonly IMemoryCache _cache;

    public NetOpsHub(AgentConnectionManager connections, IMemoryCache cache)
    {
        _connections = connections;
        _cache = cache;
    }

    // [6.2] Khi kết nối thành công, ghi nhận người sở hữu
    // OnConnectedAsync() là phương thức có sẵn của lớp Hub.
    // SignalR tự gọi nó khi một client kết nối tới Hub.
    // Override để bổ sung việc ghi nhớ chủ sở hữu.
    // https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs?view=aspnetcore-10.0#handle-events-for-a-connection
    public override Task OnConnectedAsync()
    {
        // Lấy định danh người dùng mà SignalR đã xác định từ danh tính được xác thực.
        var userId = Context.UserIdentifier;

        // xử lý trường hợp không lấy được định danh: . Abort() cũng là API có sẵn của SignalR.
        if (string.IsNullOrWhiteSpace(userId))
        {
            // Context.Abort() dừng kết nối, rồi phương thức kết thúc
            Context.Abort();
            // gọi phần xử lý của lớp Hub gốc và trả về Task của nó.
            return Task.CompletedTask;
        }

        // Lưu vào bộ nhớ
        _cache.Set($"desktop:{Context.ConnectionId}", userId);

        return base.OnConnectedAsync();
    }

    // [6.3] Khi ngắt kết nối, xóa mục đã lưu
    // OnDisconnectedAsync() cũng là phương thức của Microsoft SignalR, được gọi khi client (desktop) ngắt kết nối.
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        // xóa thông tin của kết nối vừa đóng.
        _cache.Remove($"desktop:{Context.ConnectionId}");

        return base.OnDisconnectedAsync(exception);
    }

    public List<AgentSummary> GetAgents()
    {
        return _connections.GetAgents();
    }
}