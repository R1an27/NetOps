# Giai đoạn 1: Dùng SDK .NET 10 để khôi phục thư viện và biên dịch
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy các file cấu hình & .csproj trước để tận dụng Layer Cache cho NuGet
COPY Directory.Build.props ./
COPY src/NetOps.Server/NetOps.Server.csproj src/NetOps.Server/
COPY src/NetOps.ServiceDefaults/NetOps.ServiceDefaults.csproj src/NetOps.ServiceDefaults/
COPY src/NetOps.Shared/NetOps.Shared.csproj src/NetOps.Shared/
# Đọc danh sách NuGet packages ghi trong 3 file .csproj rồi tải chúng về disk của Container (tạm thời) đang chạy stage 1
# Docker đóng băng bước này thành 1 Image Layer (làm cache)
RUN dotnet restore src/NetOps.Server/NetOps.Server.csproj

# Copy mã nguồn của 3 project liên quan và publish bản Release
COPY src/NetOps.Server/ src/NetOps.Server/
COPY src/NetOps.ServiceDefaults/ src/NetOps.ServiceDefaults/
COPY src/NetOps.Shared/ src/NetOps.Shared/
RUN dotnet publish src/NetOps.Server/NetOps.Server.csproj -c Release -o /app/publish --no-restore

# Giai đoạn 2: Chỉ dùng ASP.NET 10 Runtime nhẹ nhàng để chạy sản phẩm
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Chạy bằng user không có quyền root (mặc định có sẵn trong image .NET 10)
USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "NetOps.Server.dll"]