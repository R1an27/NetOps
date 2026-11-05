using NetOps.Agent;

var info = SystemInfoService.GetRegistrationInfo();
Console.WriteLine($"Đã đóng gói thành công máy: {info.Hostname} (ID: {info.AgentId})");