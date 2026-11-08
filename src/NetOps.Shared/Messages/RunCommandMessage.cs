namespace NetOps.Shared.Messages
{
    /// <summary>
    /// Message contract được gửi vào RabbitMQ để yêu cầu Agent chạy lệnh
    /// </summary>
    public class RunCommandMessage
    {
        public string AgentId { get; set; } = string.Empty;
        public string CommandId { get; set; } = string.Empty;
        public string CommandText { get; set; } = string.Empty;
        
        // Thời gian tối đa để chạy lệnh trước khi bị ngắt (Timeout)
        public int TimeoutSeconds { get; set; } = 60;
    }
}
