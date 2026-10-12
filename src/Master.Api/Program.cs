var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
// Đăng ký TCP Manager Server chạy nền cùng ASP.NET Core
builder.Services.AddHostedService<Master.Api.Services.TcpManagerServer>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.Run();