using System.Diagnostics.Metrics;
using NetOps.Shared;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace NetOps.Server.Services;

/// <summary>
/// Xuất định kỳ CPU, RAM và Disk của các Agent đang kết nối sang Prometheus qua OTLP nội bộ.
/// </summary>
public sealed class AgentMetricsExporter : IHostedService, IDisposable
{
    private static readonly TimeSpan HeartbeatStaleAfter = TimeSpan.FromSeconds(30);

    private readonly AgentConnectionManager _connectionManager;
    private readonly Meter _meter = new("NetOps.Agent", "1.0.0");
    private readonly MeterProvider _meterProvider;
    private int _disposed;

    public AgentMetricsExporter(
        AgentConnectionManager connectionManager,
        IConfiguration configuration,
        ILogger<AgentMetricsExporter> logger)
    {
        _connectionManager = connectionManager;

        _meter.CreateObservableGauge<double>(
            name: "system_cpu_utilization_percent",
            observeValues: () => ObserveMetrics(hb => hb.CpuUsagePercent),
            unit: "%",
            description: "Ty le su dung CPU hien tai");

        _meter.CreateObservableGauge<long>(
            name: "system_memory_used_bytes",
            observeValues: () => ObserveMetrics(hb => hb.MemoryUsedBytes),
            unit: "By",
            description: "Dung luong RAM he thong dang su dung");

        _meter.CreateObservableGauge<long>(
            name: "system_disk_free_bytes",
            observeValues: () => ObserveMetrics(hb => hb.DiskFreeBytes),
            unit: "By",
            description: "Dung luong o dia he dieu hanh con trong");

        var otlpEndpoint = BuildPrometheusOtlpEndpoint(configuration);
        _meterProvider = Sdk.CreateMeterProviderBuilder()
            .ConfigureResource(r => r.AddService("NetOps.Server.AgentMetrics"))
            .AddMeter(_meter.Name)
            .AddOtlpExporter((options, readerOptions) =>
            {
                options.Endpoint = otlpEndpoint;
                options.Protocol = OtlpExportProtocol.HttpProtobuf;
                readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 5000;
            })
            .Build();

        logger.LogInformation("Da bat OTLP exporter cho Agent metrics toi {Endpoint}.", otlpEndpoint);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _meterProvider.Dispose();
        _meter.Dispose();
    }

    public static string ResolvePrometheusBaseUrl(IConfiguration configuration)
    {
        var url = configuration["Prometheus:BaseUrl"];
        return string.IsNullOrWhiteSpace(url) ? "http://localhost:9090" : url.Trim();
    }

    private static Uri BuildPrometheusOtlpEndpoint(IConfiguration configuration)
    {
        var baseUrl = ResolvePrometheusBaseUrl(configuration).TrimEnd('/') + "/";
        return new Uri(new Uri(baseUrl), "api/v1/otlp/v1/metrics");
    }

    private IEnumerable<Measurement<T>> ObserveMetrics<T>(Func<HeartbeatMessage, T> selector)
        where T : struct
    {
        var cutoff = DateTime.UtcNow - HeartbeatStaleAfter;
        var results = new List<Measurement<T>>();

        foreach (var conn in _connectionManager.GetActiveConnections())
        {
            if (conn.LatestHeartbeat is { } hb && conn.LastHeartbeatUtc >= cutoff)
            {
                results.Add(new Measurement<T>(
                    selector(hb),
                    new KeyValuePair<string, object?>("host_id", conn.AgentId)));
            }
        }

        return results;
    }
}
