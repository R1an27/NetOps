var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.NetOps_Server>("server");

builder.Build().Run();
