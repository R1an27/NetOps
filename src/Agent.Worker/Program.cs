using Agent.Worker;
using Agent.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<TcpAgentClient>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();