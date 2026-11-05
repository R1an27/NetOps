using NetOps.Agent;

Console.WriteLine("Agent infor: ");
Console.WriteLine($"Hostname : {SystemInfoService.GetHostName()}");
Console.WriteLine($"OS       : {SystemInfoService.GetOsDescription()}");
Console.WriteLine($"Local IP : {SystemInfoService.GetLocalIpAddress()}");