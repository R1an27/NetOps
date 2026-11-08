using Duende.IdentityModel.OidcClient.Browser;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Security.Cryptography;

namespace NetOps.Desktop;

// IBrowser là interface của Duende.
// Interface này quy định: lớp muốn làm nhiệm vụ trình duyệt cho OIDC phải có phương thức InvokeAsync.
public class SystemBrowser : IBrowser
{
    public async Task<BrowserResult> InvokeAsync(
        BrowserOptions options,
        CancellationToken cancellationToken = default)
    {
        // Làm sao phần đang chờ biết rằng callback đã đến?
        // Um hãy rán mà tưởng tượng khái niệm siêu trừu tượng này: callback là một phiếu hẹn nhận chuỗi kết quả.
        // Được dùng ở 2 nơi [1], [2].
        var callback = new TaskCompletionSource<string>(
            // RunContinuationsAsynchronously cho phép phần đang chờ được tiếp tục bất đồng bộ,
            // tránh chạy ngay phần tiếp theo bên trong lời gọi TrySetResult.
            // Đây là API của .NET, được mô tả tại TaskCompletionSource<T>.
            // https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource-1?view=net-10.0
            TaskCreationOptions.RunContinuationsAsynchronously);

        // builder: đối tượng dùng để chuẩn bị cấu hình.
        // UseUrls(options.EndUrl): đặt địa chỉ sẽ lắng nghe, ở đây là http://127.0.0.1:7890/.
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(options.EndUrl);

        // Build(): tạo đối tượng app từ cấu hình vừa chuẩn bị.
        // await using: bảo đảm giải phóng tài nguyên của app khi phương thức kết thúc.
        await using var app = builder.Build();

        // Khi trình duyệt gửi yêu cầu GET tới đường dẫn /, hãy chạy phần code trong dấu { }
        // Với cấu hình hiện tại, đó là yêu cầu tới: http://127.0.0.1:7890/
        // context là đối tượng chứa thông tin yêu cầu HTTP đang đến và phản hồi mà chúng ta sẽ gửi lại.
        // Cách khai báo đường dẫn bằng MapGet thuộc Minimal APIs của ASP.NET Core.
        app.MapGet("/", async (HttpContext context) =>
        {
            await context.Response.WriteAsync(
                "Login response received. You can return to NetOps.");

            // [2] Nơi cung cấp kết quả
            // TrySetResult(...) đưa chuỗi (bên dưới á) vào phiếu hẹn.
            // Lúc ấy, dòng await [1] đang chờ được tiếp tục, và biến response nhận chuỗi vừa cung cấp.
            callback.TrySetResult(
                // Ví dụ trình duyệt truy cập: http://127.0.0.1:7890/?code=abc123&state=xyz
                // Thì: context.Request.QueryString.Value có giá trị ?code=abc123&state=xyz
                // ?? "" nghĩa là: nếu giá trị bên trái là null, dùng chuỗi rỗng.
                context.Request.QueryString.Value ?? "");
        });

        // khởi động bộ nhận trước:
        await app.StartAsync(cancellationToken);

        try
        {
            // Rồi mới mở trình duyệt:
            // Process và ProcessStartInfo thuộc System.Diagnostics của .NET.
            // https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.useshellexecute?view=net-10.0
            Process.Start(new ProcessStartInfo(options.StartUrl)
            {
                // UseShellExecute = true yêu cầu Windows mở URL bằng ứng dụng đã đăng ký cho nó,
                // thông thường là trình duyệt mặc định của người dùng.
                UseShellExecute = true
            });

            // [1] Nơi chờ kết quả:
            // Desktop chờ tới khi:
            // - Nhận được callback.
            // - Hết thời gian cho phép.
            // - Hoạt động bị hủy qua cancellationToken.
            var response = await callback.Task.WaitAsync(
                // Timeout ở đây là thời gian chờ phản hồi đăng nhập từ trình duyệt.
                options.Timeout, cancellationToken);

            //
            return new BrowserResult
            {
                // Ở đây, Success có nghĩa: phần trình duyệt đã nhận được phản hồi và trả nó cho thư viện OIDC.
                // Chưa thể kết luận người dùng đăng nhập thành công tại dòng này.
                // Callback có thể chứa mã đăng nhập hoặc thông tin lỗi.
                // Thư viện OIDC sẽ xử lý tiếp, kiểm tra phản hồi và trao đổi mã đăng nhập lấy token.
                // Vì vậy, thông báo trên trình duyệt chỉ ghi: Login response received. You can return to NetOps.
                ResultType = BrowserResultType.Success,
                Response = response
            };
        }
        catch (TimeoutException)
        {
            return new BrowserResult
            {
                ResultType = BrowserResultType.Timeout
            };
        }
        catch (OperationCanceledException)
        {
            return new BrowserResult
            {
                ResultType = BrowserResultType.UserCancel
            };
        }
        // Phần này dừng bộ nhận và giải phóng cổng 7890 sau khi hoàn tất, hết giờ hoặc gặp lỗi.
        // Lưu ý: finally vẫn chạy khi bên trong try đã return.
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }
}