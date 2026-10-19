namespace NetOps.Tests.TestHelpers;

/// <summary>
/// Định nghĩa các hằng số chuẩn cho hệ thống phân loại xUnit Trait đa trục (Category và Area).
/// Hỗ trợ lọc kiểm thử linh hoạt qua CLI: dotnet test --filter "Category=Unit", v.v.
/// </summary>
public static class TestTraits
{
    public static class Category
    {
        public const string Name = "Category";

        /// <summary>
        /// Unit tests: Thuần in-memory, kiểm thử logic, DTOs, encoding/decoding, không mở socket thật.
        /// </summary>
        public const string Unit = "Unit";

        /// <summary>
        /// Integration tests: Kiểm thử tích hợp mạng TCP, vòng đời kết nối, handshake, graceful shutdown.
        /// </summary>
        public const string Integration = "Integration";

        /// <summary>
        /// Stress tests: Kiểm thử áp lực cao, nhiều luồng đồng thời ghi qua kênh hoặc tải nhiều client.
        /// </summary>
        public const string Stress = "Stress";

        /// <summary>
        /// Resilience / Chaos tests: Kiểm thử khả năng chịu lỗi mạng, DoS framing (>16MB), cắt cụt gói tin.
        /// </summary>
        public const string Resilience = "Resilience";
    }

    public static class Area
    {
        public const string Name = "Area";

        /// <summary>
        /// Area liên quan đến giao thức: Framing, Codec, NetOpsMessage envelopes, DTO payloads.
        /// </summary>
        public const string Protocol = "Protocol";

        /// <summary>
        /// Area liên quan đến Agent Worker: Worker background service, AgentNetworkClient, AgentMessageControl.
        /// </summary>
        public const string Agent = "Agent";

        /// <summary>
        /// Area liên quan đến Master Server: ConnectionManager, MasterMessageControl, SessionWatchdogService.
        /// </summary>
        public const string Master = "Master";

        /// <summary>
        /// Area liên quan đến Client: NetOpsClientService, request-response correlation, UI controllers.
        /// </summary>
        public const string Client = "Client";
    }
}
