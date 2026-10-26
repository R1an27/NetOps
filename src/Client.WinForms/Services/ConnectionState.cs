// =========================================================================================
// Giai đoạn T18.1: Chuẩn hóa kiến trúc hợp đồng kết nối (Architecture Contract Refactoring)
// Hợp đồng chính thức hiện tại được định nghĩa tập trung tại:
//   src/Shared.Contracts/Models/ConnectionState.cs (ServerManager.Shared.Contracts.Models)
// File này cung cấp global using để giữ trọn vẹn 100% tương thích ngược trong Client.WinForms.
// =========================================================================================

global using ConnectionState = ServerManager.Shared.Contracts.Models.ConnectionState;
global using ConnectionStateChangedEventArgs = ServerManager.Shared.Contracts.Models.ConnectionStateChangedEventArgs;
