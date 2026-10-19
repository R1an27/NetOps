namespace ServerManager.Shared.Contracts.Models;

/// <summary>
/// Kết quả thống kê của thao tác phát sóng (broadcast) một thông điệp tới danh sách các Agent trực tuyến.
/// </summary>
/// <param name="TotalTargeted">Tổng số phiên kết nối Agent mục tiêu được nhắm tới tại thời điểm phát sóng.</param>
/// <param name="SuccessCount">Số lượng Agent đã nhận thông điệp thành công.</param>
/// <param name="FailedCount">Số lượng Agent gặp sự cố truyền tải mạng hoặc ngắt kết nối trong lúc phát sóng.</param>
public record BroadcastResult(int TotalTargeted, int SuccessCount, int FailedCount)
{
    /// <summary>
    /// Cho biết liệu thao tác phát sóng có thành công tuyệt đối trên tất cả các Agent mục tiêu hay không (ít nhất 1 Agent và không có lỗi).
    /// </summary>
    public bool AllSucceeded => TotalTargeted > 0 && FailedCount == 0;

    /// <summary>
    /// Cho biết liệu có bất kỳ Agent mục tiêu nào bị thất bại trong quá trình phát sóng hay không.
    /// </summary>
    public bool HasFailures => FailedCount > 0;
}
