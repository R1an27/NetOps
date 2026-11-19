var builder = DistributedApplication.CreateBuilder(args);

// Thêm rabbitMQ container với plugin quản lý
var rabbitmq = builder.AddRabbitMQ("rabbitmq")
                      .WithManagementPlugin();
// Thêm keycloak container (vì nuget package chưa có, nên phải kéo image chính thức của RED HAT về)
// Keycloak 26 dùng biến KC_BOOTSTRAP_ADMIN_* thay cho KEYCLOAK_ADMIN cũ (đồng bộ với compose.yaml)
var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.1")
                      .WithVolume(name: "netops-keycloak-data", target: "/opt/keycloak/data")
                      .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http")
                      .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
                      .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
                      .WithArgs("start-dev");

// Thêm Prometheus container để thu thập và lưu trữ time-series metrics
var prometheus = builder.AddContainer("prometheus", "prom/prometheus", "v3.0.0")
                        .WithHttpEndpoint(port: 9090, targetPort: 9090, name: "http")
                        .WithArgs(
                      // Chỉ định đường dẫn tới file cấu hình của Prometheus bên trong container
                      "--config.file=/etc/prometheus/prometheus.yml",
                      // Chỉ định thư mục lưu trữ cơ sở dữ liệu chuỗi thời gian
                      "--storage.tsdb.path=/prometheus",
                      //  Mặc định Prometheus hoạt động theo cơ thế Pull, nhưng ta có thể config prometheus để 
                      // Bật tính năng tiếp nhận dữ liệu do Agents đẩy trực tiếp theo chuẩn OpenTelemetry
                      "--web.enable-otlp-receiver"
                        );

builder.AddProject<Projects.NetOps_Server>("server")
       // Thư viện AddRabbitMQ tự động tạo ra connection string cho rabbitMQ
       .WithReference(rabbitmq)
       // keycloak đang là 1 conatainer nên cần phải tham chiếu đến endpoint của nó để server có thể kết nối
       .WithReference(keycloak.GetEndpoint("http"))
       // Prometheus container cũng là container nên tham chiếu qua endpoint HTTP cổng 9090
       .WithReference(prometheus.GetEndpoint("http"));

builder.Build().Run();
