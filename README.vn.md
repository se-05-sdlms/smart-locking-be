<p align="center">
  <img src="./docs/images/readme-header.png" alt="Backend tủ khóa thông minh Boxora" width="100%" />
</p>

<p align="center">
  <img
    src="./docs/images/boxora-header.gif"
    alt="Boxora"
    width="560"
  />
</p>

<p align="center">
  <strong>Ngôn ngữ:</strong>
  <a href="./README.vn.md">🇻🇳 Tiếng Việt</a>
  &nbsp;|&nbsp;
  <a href="./README.md">🇬🇧 English</a>
</p>

<h3 align="center">
  ⚙️ Backend API của SDLMS
</h3>

<p align="center">
  Nền tảng quản lý giao nhận bưu kiện thông minh, kết nối thời gian thực và tích hợp IoT.
</p>

<p align="center">
  <img
    src="https://img.shields.io/badge/Frontend-React_18-149ECA?style=flat-square&logo=react&logoColor=white"
    alt="Frontend React 18"
  />
  <img
    src="https://img.shields.io/badge/Backend-.NET_8-512BD4?style=flat-square&logo=dotnet&logoColor=white"
    alt="Backend .NET 8"
  />
  <img
    src="https://img.shields.io/badge/Database-PostgreSQL-4169E1?style=flat-square&logo=postgresql&logoColor=white"
    alt="Database PostgreSQL"
  />
  <img
    src="https://img.shields.io/badge/Realtime-SignalR-512BD4?style=flat-square"
    alt="Realtime SignalR"
  />
  <img
    src="https://img.shields.io/badge/MQTT-EMQX-00B173?style=flat-square"
    alt="MQTT EMQX"
  />
  <img
    src="https://img.shields.io/badge/Hardware-ESP32-E7352C?style=flat-square&logo=espressif&logoColor=white"
    alt="Hardware ESP32"
  />
</p>

<p align="center">
  <a href="#quick-start"><img src="https://img.shields.io/badge/B%E1%BA%AFt_%C4%91%E1%BA%A7u_nhanh-Xem-2ea44f?style=for-the-badge" alt="Bắt đầu nhanh" /></a>
  <a href="#tech-stack"><img src="https://img.shields.io/badge/C%C3%B4ng_ngh%E1%BB%87-Xem-0969da?style=for-the-badge" alt="Công nghệ" /></a>
  <a href="#architecture"><img src="https://img.shields.io/badge/Ki%E1%BA%BFn_tr%C3%BAc-Xem-8250df?style=for-the-badge" alt="Kiến trúc" /></a>
  <a href="#related-repositories"><img src="https://img.shields.io/badge/Repo_li%C3%AAn_quan-Xem-e85d04?style=for-the-badge" alt="Repo liên quan" /></a>
  <a href="#development-team"><img src="https://img.shields.io/badge/Nh%C3%B3m_ph%C3%A1t_tri%E1%BB%83n-Xem-DB2777?style=for-the-badge" alt="Nhóm phát triển" /></a>
</p>

## Tổng quan

`smart-locking-be` là Backend API cốt lõi của hệ thống Boxora, chịu trách nhiệm xử lý nghiệp vụ, authentication, luồng bưu kiện, cấp phát ngăn tủ, audit hệ thống, giao tiếp thời gian thực và điều phối thiết bị IoT.

### Các module chính

* **Identity & Access** — quản lý authentication, JWT token, role, permission và trạng thái tài khoản.
* **Quản lý cư dân** — quản lý đăng ký, hồ sơ, tùy chọn giao hàng và lịch sử bưu kiện.
* **Luồng Shipper Guest** — hỗ trợ session tạm thời để gửi bưu kiện mà không cần đăng ký tài khoản.
* **Quản lý bưu kiện** — quản lý phê duyệt, lưu trữ, nhận hàng, xử lý quá hạn và lịch sử giao nhận.
* **Quản lý tủ** — quản lý tòa nhà, cụm tủ, ngăn tủ, trạng thái sẵn sàng và cấp phát ngăn động.
* **Vận hành tủ** — hỗ trợ giám sát, xử lý sự cố, thao tác khẩn cấp có audit và yêu cầu bảo trì.
* **Realtime & IoT Integration** — giao tiếp với client qua SignalR và với thiết bị tủ qua MQTT.
* **Quản trị & Audit** — quản lý policy, phân công Locker Operator, báo cáo và audit logs.

---

<a id="quick-start"></a>

<details open>
<summary><strong>🚀 Bắt đầu nhanh</strong></summary>

### Yêu cầu

Cài các công cụ sau trước khi chạy project:

* .NET SDK 8.x. Solution đang target `net8.0`; thống nhất giữ package .NET/EF Core ở version 8.
* PostgreSQL 15+ trên máy local, hoặc database PostgreSQL trên Supabase.
* EF Core CLI 8.0.11 để tạo và apply migration.
* Docker: không bắt buộc, chỉ cần nếu muốn chạy hạ tầng bằng container.

```bash
dotnet --version
dotnet tool update --global dotnet-ef --version 8.0.11
dotnet ef --version
```

Các version NuGet hiện đang dùng trong repository:

| Project | Package | Version |
| ------- | ------- | ------- |
| `smart-locking-be.API` | `Microsoft.AspNetCore.Authentication.JwtBearer` | `8.0.28` |
| `smart-locking-be.API` | `Serilog.AspNetCore` | `8.0.3` |
| `smart-locking-be.API` | `Serilog.Settings.Configuration` | `8.0.4` |
| `smart-locking-be.API` | `Serilog.Sinks.Console` | `5.0.1` |
| `smart-locking-be.API` | `Serilog.Sinks.File` | `5.0.0` |
| `smart-locking-be.API` | `Swashbuckle.AspNetCore` | `6.9.0` |
| `smart-locking-be.Application` | `Microsoft.Extensions.DependencyInjection.Abstractions` | `8.0.2` |
| `smart-locking-be.Infrastructure` | `Microsoft.EntityFrameworkCore.Design` | `8.0.11` |
| `smart-locking-be.Infrastructure` | `Microsoft.Extensions.Configuration.Abstractions` | `8.0.0` |
| `smart-locking-be.Infrastructure` | `Microsoft.Extensions.DependencyInjection.Abstractions` | `8.0.2` |
| `smart-locking-be.Infrastructure` | `Npgsql.EntityFrameworkCore.PostgreSQL` | `8.0.11` |
| `smart-locking-be.Tests` | `coverlet.collector` | `6.0.0` |
| `smart-locking-be.Tests` | `Microsoft.NET.Test.Sdk` | `17.8.0` |
| `smart-locking-be.Tests` | `xunit` | `2.5.3` |
| `smart-locking-be.Tests` | `xunit.runner.visualstudio` | `2.5.3` |

Các version này tương thích với project `net8.0` hiện tại. Không nâng EF Core packages hoặc `dotnet-ef` lên 9.x trừ khi cả solution được migrate có chủ đích lên .NET 9.

### Cài đặt

```bash
git clone https://github.com/se-05-sdlms/smart-locking-be
cd smart-locking-be
dotnet restore
```

### Cấu hình

Tạo file cấu hình development trên máy local. File này đã được ignore bởi Git, nên mỗi thành viên trong team cần tự tạo ở máy mình.

```bash
# macOS/Linux/Git Bash
cp smart-locking-be.API/appsettings.json smart-locking-be.API/appsettings.Development.json

# Windows PowerShell
Copy-Item smart-locking-be.API/appsettings.json smart-locking-be.API/appsettings.Development.json
```

Mở `smart-locking-be.API/appsettings.Development.json` và cập nhật tối thiểu các giá trị sau:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=smart_locking_dev;Username=postgres;Password=postgres"
  },
  "Jwt": {
    "Key": "development-only-secret-key-please-change-32-bytes",
    "Issuer": "smart-locking-be",
    "Audience": "smart-locking-clients"
  }
}
```

Bạn cũng có thể override cấu hình bằng biến môi trường hoặc .NET User Secrets:

```env
ConnectionStrings__DefaultConnection=
Jwt__Key=
Jwt__Issuer=
Jwt__Audience=
```

### Chạy môi trường phát triển

```bash
# Restore dependencies
dotnet restore

# Chạy API bằng http launch profile
dotnet run --project smart-locking-be.API --launch-profile http
```

* URL API local: `http://localhost:5005`
* Swagger URL: `http://localhost:5005/swagger`
* Endpoint mẫu: `http://localhost:5005/weatherforecast`

Chạy bằng HTTPS profile:

```bash
dotnet run --project smart-locking-be.API --launch-profile https
```

* HTTPS URL: `https://localhost:7001`
* HTTP URL: `http://localhost:5005`

Apply EF Core migrations sau khi đã có entity và migration:

```bash
dotnet ef database update \
  --project smart-locking-be.Infrastructure \
  --startup-project smart-locking-be.API
```

</details>

<a id="tech-stack"></a>

<details open>
<summary><strong>🧰 Công nghệ</strong></summary>

| Nhóm                | Công nghệ                                |
| ------------------- | ---------------------------------------- |
| Framework           | ASP.NET Core Web API, .NET 8 LTS         |
| Architecture        | Clean Architecture                       |
| ORM                 | Entity Framework Core 8.0.11             |
| Database            | PostgreSQL 15+, Supabase                 |
| Authentication      | JWT Bearer Token                         |
| Tài liệu API        | Swashbuckle.AspNetCore 6.9.0, Swagger UI |
| Logging             | Serilog.AspNetCore 8.0.3                 |
| Testing             | xUnit 2.5.3                              |
| Code coverage       | coverlet.collector 6.0.0                 |
| Containerization    | Docker: không bắt buộc                   |

</details>

<a id="architecture"></a>

<details open>
<summary><strong>🏗️ Kiến trúc</strong></summary>

```mermaid
flowchart TD
    subgraph Clients["🖥️ Tầng Client"]
        direction LR
        A["📱 Mobile App / Web App<br/>dành cho cư dân"]
        B["🚚 Mobile Web App<br/>dành cho Shipper"]
        C["🏪 Web App<br/>dành cho Locker Kiosk"]
        H["🧑‍💼 Dashboard dành cho<br/>Admin / Locker Operator"]
    end

    subgraph Backend["⚙️ ASP.NET Core Backend"]
        P["Presentation Layer<br/>REST API · Swagger · SignalR Hub"]
        AP["Application Layer<br/>Use Cases · Interfaces · Validation"]
        D["Domain Layer<br/>Entities · Value Objects · Business Rules"]
        I["Infrastructure Layer<br/>EF Core · External Services"]
    end

    DB[("🗄️ PostgreSQL Database<br/>Supabase · Audit Logs")]
    E["📡 EMQX MQTT Broker<br/>MQTT v5.0"]
    F["🔌 ESP32 Locker Controller<br/>ESP32 WROOM 32D"]
    G["🔒 Phần cứng tủ<br/>Khóa điện tử · Relay · Cảm biến cửa"]

    A -->|"(1) HTTPS / REST API / JWT<br/>SignalR thời gian thực"| P
    B -->|"(1) HTTPS / REST API / JWT<br/>SignalR thời gian thực"| P
    C -->|"(1) HTTPS / REST API / JWT<br/>SignalR thời gian thực"| P
    H -->|"(1) HTTPS / REST API / JWT<br/>SignalR thời gian thực"| P

    P --> AP
    AP --> D
    AP --> I

    I <-->|"(2) Entity Framework Core"| DB
    I <-->|"(3) MQTT Publish / Subscribe<br/>Server-side"| E
    E <-->|"(4) MQTT qua Wi-Fi / Internet"| F
    F -->|"(5) Điều khiển và đọc trạng thái"| G

    style P fill:#dbeafe,stroke:#2563eb,stroke-width:2px
    style AP fill:#ede9fe,stroke:#7c3aed,stroke-width:2px
    style D fill:#fef3c7,stroke:#d97706,stroke-width:2px
    style I fill:#dcfce7,stroke:#16a34a,stroke-width:2px
    style E fill:#ccfbf1,stroke:#0f766e,stroke-width:2px
    style F fill:#ffedd5,stroke:#ea580c,stroke-width:2px
    style G fill:#fee2e2,stroke:#dc2626,stroke-width:2px
    style DB fill:#f3e8ff,stroke:#9333ea,stroke-width:2px
    style A fill:#eff6ff,stroke:#3b82f6
    style B fill:#eff6ff,stroke:#3b82f6
    style C fill:#eff6ff,stroke:#3b82f6
    style H fill:#eff6ff,stroke:#3b82f6
```

Backend là tầng ứng dụng duy nhất giao tiếp trực tiếp với database và MQTT Broker. Frontend, Mobile và Kiosk giao tiếp với Backend thông qua REST API và SignalR.

</details>

<details open>
<summary><strong>🔐 Biến môi trường</strong></summary>

Cấu hình các giá trị nhạy cảm bằng `smart-locking-be.API/appsettings.Development.json`, biến môi trường hoặc .NET User Secrets.

```env
ConnectionStrings__DefaultConnection=
Jwt__Key=
Jwt__Issuer=
Jwt__Audience=
```

Không lưu secret thật trong source code hoặc commit file cấu hình production lên Git.

</details>

<details>
<summary><strong>🧪 Build, test và format</strong></summary>

```bash
# Restore dependency
dotnet restore

# Build solution ở Debug
dotnet build smart-locking-be.sln

# Build solution ở Release
dotnet build smart-locking-be.sln --configuration Release

# Chạy toàn bộ test
dotnet test smart-locking-be.sln

# Kiểm tra format
dotnet format --verify-no-changes

# Xem version package đang dùng
dotnet list smart-locking-be.sln package

# Tạo database migration sau khi thêm hoặc sửa entity
dotnet ef migrations add InitialCreate \
  --project smart-locking-be.Infrastructure \
  --startup-project smart-locking-be.API \
  --output-dir Persistence/Migrations

# Áp dụng database migration
dotnet ef database update \
  --project smart-locking-be.Infrastructure \
  --startup-project smart-locking-be.API
```

</details>

<details>
<summary><strong>📁 Cấu trúc dự án</strong></summary>

```text
smart-locking-be/
├── docs/
│   └── images/
├── smart-locking-be.API/
│   ├── Constants/
│   ├── Controllers/
│   ├── Extensions/
│   ├── Options/
│   ├── Properties/
│   │   └── launchSettings.json
│   ├── appsettings.json
│   └── Program.cs
├── smart-locking-be.Application/
│   └── DependencyInjection.cs
├── smart-locking-be.Domain/
│   └── smart-locking-be.Domain.csproj
├── smart-locking-be.Infrastructure/
│   ├── Persistence/
│   │   └── ApplicationDbContext.cs
│   └── DependencyInjection.cs
├── smart-locking-be.Tests/
│   └── Test.cs
├── README.md
├── README.vn.md
└── smart-locking-be.sln
```

</details>

<a id="related-repositories"></a>

<details open>
<summary><strong>🔗 Repo và tài liệu liên quan</strong></summary>

| Thành phần           | Liên kết                                                                                             |
| -------------------- | ---------------------------------------------------------------------------------------------------- |
| GitHub Organization  | [se-05-sdlms](https://github.com/se-05-sdlms)                                                        |
| Frontend             | [smart-locking-fe](https://github.com/se-05-sdlms/smart-locking-fe)                                  |
| Mobile               | [smart-locking-mobile](https://github.com/se-05-sdlms/smart-locking-mobile)                          |
| Tài liệu dự án       | [Google Drive](https://drive.google.com/drive/folders/1M3OPsm2NxAi7WnAfsKgV4MQEMRy5rOsa?usp=sharing) |

</details>

<a id="development-team"></a>

<details open>
<summary><strong>👥 Nhóm phát triển</strong></summary>

* **Mã dự án:** `SDLMS`
* **Nhóm:** `SE_05`

### Giảng viên hướng dẫn

| Họ và tên            | Vai trò              | Email                                       |
| -------------------- | -------------------- | ------------------------------------------- |
| ThS. Lê Thị Bích Tra | Giảng viên hướng dẫn | [traltb@fe.edu.vn](mailto:traltb@fe.edu.vn) |

### Thành viên

| MSSV     | Họ và tên            | Vai trò     | Email                                                             |
| -------- | -------------------- | ----------- | ----------------------------------------------------------------- |
| DE180519 | Nguyễn Phan Huy      | Trưởng nhóm | [huynpde180519@fpt.edu.vn](mailto:huynpde180519@fpt.edu.vn)       |
| DE180405 | Phan Thành Vương     | Thành viên  | [vuongptde180405@fpt.edu.vn](mailto:vuongptde180405@fpt.edu.vn)   |
| DE180313 | Võ Văn Hài           | Thành viên  | [haivvde180313@fpt.edu.vn](mailto:haivvde180313@fpt.edu.vn)       |
| DE180393 | Trần Minh Cường      | Thành viên  | [cuongtmde180393@fpt.edu.vn](mailto:cuongtmde180393@fpt.edu.vn)   |
| DE181072 | Trương Hà Thùy Trang | Thành viên  | [trangthtde181072@fpt.edu.vn](mailto:trangthtde181072@fpt.edu.vn) |

</details>
