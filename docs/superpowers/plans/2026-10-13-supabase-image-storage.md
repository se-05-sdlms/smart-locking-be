# Supabase Image Storage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Chuyển đổi cơ chế lưu trữ ảnh từ mock/local storage sang Supabase Storage, đổi tên `UploadsController` thành `ImagesController`, trả về Public URL từ Supabase và đảm bảo 100% các luồng hiện tại (Shipper gửi ảnh kiện hàng, Resident cập nhật Avatar) hoạt động liền mạch.

**Architecture:** Tạo `SupabaseImageStorageService` thuộc tầng `smart-locking-be.Infrastructure` kế thừa `IImageStorageService` từ `smart-locking-be.Application`. Sử dụng REST API của Supabase Storage qua `HttpClient` của .NET 8 (không phụ thuộc SDK ngoài). Đổi tên `UploadsController` thành `ImagesController` với route chính `api/images` và route alias `/api/uploads/images` để tương thích ngược. Controller tự động phát hiện URL tuyệt đối (Supabase) hoặc đường dẫn tương đối (Local fallback).

**Tech Stack:** .NET 8 (ASP.NET Core), C# 12, Supabase Storage REST API, `HttpClient` / `IHttpClientFactory`.

---

## Global Constraints

- Không sửa đổi interface [`IImageStorageService`](file:///E:/CapstoneProject/smart-locking-be/smart-locking-be.Application/Interfaces/Services/IImageStorageService.cs) để đảm bảo tuân thủ Dependency Inversion.
- Giữ nguyên các ràng buộc xác thực ảnh: định dạng cho phép (`.jpg`, `.jpeg`, `.png`, `.webp`), MIME types tương ứng, và kiểm tra chữ ký nhị phân (magic bytes) trước khi đẩy lên cloud.
- Đảm bảo endpoint cũ `POST /api/uploads/images` vẫn hoạt động song song với endpoint mới `POST /api/images` để không làm gián đoạn frontend/mobile client hiện tại.
- Contract response trả về của API upload phải giữ nguyên cấu trúc JSON: `{ "url": "..." }`.
- Không viết thêm test mới theo yêu cầu người dùng, nhưng đảm bảo toàn bộ 210 test hiện tại của solution tiếp tục compile và PASS.

---

## Review Focus

1. **Khả năng tương thích ngược của URL:** `ImagesController` phải kiểm tra `Uri.IsWellFormedUriString(path, UriKind.Absolute)`. Nếu đã là absolute URL (Supabase), không được nối thêm `${Request.Scheme}://{Request.Host}`.
2. **Kích thước và tên file trên Supabase:** File name phải được tạo ngẫu nhiên bằng `Guid.NewGuid():N` kèm đúng phần mở rộng file để chống ghi đè và path traversal.
3. **Xử lý lỗi Supabase:** Bắt các mã lỗi HTTP khi upload lên Supabase (401 Unauthorized, 403 Forbidden, 413 Payload Too Large) và trả về thông báo lỗi thân thiện, không làm sập ứng dụng.
4. **Tham chiếu kiểu trong Test:** Cập nhật tham chiếu `typeof(UploadsController)` thành `typeof(ImagesController)` trong [`ServiceCompositionTests.cs`](file:///E:/CapstoneProject/smart-locking-be/smart-locking-be.Tests/Architecture/ServiceCompositionTests.cs) để bộ test hiện tại compile thành công.
5. **Route Alias:** Cả hai đường dẫn `POST /api/images` và `POST /api/uploads/images` đều phải trỏ về cùng một action upload.

---

## Tasks

### Task 1: Thêm cấu hình Supabase Storage Options

**Files:**
- Create: `smart-locking-be.Infrastructure/Options/SupabaseStorageOptions.cs`
- Modify: `smart-locking-be.API/appsettings.json`

**Interfaces:**
- Consumes: Cấu hình `IConfiguration` từ `appsettings.json`
- Produces: `SupabaseStorageOptions` chứa `Url`, `ApiKey`, `BucketName`, `Folder`

- [x] **Step 1: Tạo model cấu hình `SupabaseStorageOptions.cs`**
  - Tạo file `smart-locking-be.Infrastructure/Options/SupabaseStorageOptions.cs`:
    ```csharp
    namespace smart_locking_be.Infrastructure.Options;

    public sealed class SupabaseStorageOptions
    {
        public const string SectionName = "SupabaseStorage";

        public string Url { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string BucketName { get; set; } = "smart-locking-images";
        public string Folder { get; set; } = "parcels";
    }
    ```

- [x] **Step 2: Thêm section `SupabaseStorage` vào `smart-locking-be.API/appsettings.json`**
  - Thêm cấu hình mẫu cho Supabase Storage:
    ```json
    "SupabaseStorage": {
      "Url": "",
      "ApiKey": "",
      "BucketName": "smart-locking-images",
      "Folder": "parcels"
    }
    ```

---

### Task 2: Cài đặt `SupabaseImageStorageService` trong tầng Infrastructure

**Files:**
- Create: `smart-locking-be.Infrastructure/Services/SupabaseImageStorageService.cs`

**Interfaces:**
- Consumes: [`IImageStorageService`](file:///E:/CapstoneProject/smart-locking-be/smart-locking-be.Application/Interfaces/Services/IImageStorageService.cs), `HttpClient`, `IOptions<SupabaseStorageOptions>`
- Produces: `SupabaseImageStorageService : IImageStorageService`

- [x] **Step 1: Cài đặt logic xác thực định dạng ảnh và magic bytes**
  - Kiểm tra đuôi file: `.jpg`, `.jpeg`, `.png`, `.webp`.
  - Kiểm tra khớp Content-Type: `image/jpeg`, `image/png`, `image/webp`.
  - Đọc 12 bytes đầu tiên từ stream và kiểm tra chữ ký nhị phân (magic bytes) tương tự `LocalImageStorageService`. Ném `ArgumentException` nếu không hợp lệ.

- [x] **Step 2: Cài đặt logic upload qua Supabase Storage REST API**
  - Tạo tên file: `string storedName = $"{Guid.NewGuid():N}{extension}";`
  - Tạo object path: `string objectPath = string.IsNullOrWhiteSpace(options.Folder) ? storedName : $"{options.Folder.Trim('/')}/{storedName}";`
  - Tạo request `POST {options.Url.TrimEnd('/')}/storage/v1/object/{options.BucketName}/{objectPath}`:
    - Headers:
      - `apikey: {options.ApiKey}`
      - `Authorization: Bearer {options.ApiKey}`
    - Content: `StreamContent` chứa đầy đủ header bytes và phần stream còn lại, gán `Content-Type: {contentType}`.
  - Gửi request qua `HttpClient` với `cancellationToken`.
  - Nếu response không thành công: đọc error body và ném `InvalidOperationException` hoặc `ArgumentException` với chi tiết lỗi.
  - Trả về Public URL tuyệt đối:
    ```csharp
    return $"{options.Url.TrimEnd('/')}/storage/v1/object/public/{options.BucketName}/{objectPath}";
    ```

---

### Task 3: Đăng ký Dependency Injection

**Files:**
- Modify: `smart-locking-be.Infrastructure/DependencyInjection.cs`
- Modify: `smart-locking-be.API/DependencyInjection.cs`

**Interfaces:**
- Consumes: `SupabaseStorageOptions`, `SupabaseImageStorageService`
- Produces: `services.AddScoped<IImageStorageService, SupabaseImageStorageService>()` (có cơ chế fallback về `LocalImageStorageService` nếu thiếu cấu hình Supabase URL/Key)

- [x] **Step 1: Cấu hình Options và Typed HttpClient trong `smart-locking-be.Infrastructure/DependencyInjection.cs`**
  - Bind section `SupabaseStorage` vào `SupabaseStorageOptions`.
  - Đăng ký `HttpClient` cho Supabase với timeout thích hợp (ví dụ 30-60 giây).
  - Đăng ký `SupabaseImageStorageService`.

- [x] **Step 2: Cập nhật đăng ký `IImageStorageService` trong `smart-locking-be.API/DependencyInjection.cs`**
  - Kiểm tra nếu `SupabaseStorage:Url` và `SupabaseStorage:ApiKey` được cung cấp thì dùng `SupabaseImageStorageService`, ngược lại giữ fallback `LocalImageStorageService` (hoặc chuyển đổi qua `SupabaseImageStorageService`).

---

### Task 4: Đổi tên và Refactor `UploadsController` thành `ImagesController`

**Files:**
- Delete/Rename: `smart-locking-be.API/Controllers/UploadsController.cs` -> `smart-locking-be.API/Controllers/ImagesController.cs`
- Modify: `smart-locking-be.Tests/Architecture/ServiceCompositionTests.cs`

**Interfaces:**
- Consumes: `IImageStorageService`
- Produces: `ImagesController` với routes `api/images` và alias `/api/uploads/images`

- [x] **Step 1: Tạo `ImagesController.cs` thay thế `UploadsController.cs`**
  - Route attributes:
    ```csharp
    [ApiController]
    [Route("api/images")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.Upload)]
    public sealed class ImagesController(IImageStorageService imageStorageService) : ControllerBase
    ```
  - Action upload hỗ trợ cả route mới lẫn route cũ:
    ```csharp
    [HttpPost]
    [HttpPost("/api/uploads/images")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > 8 * 1024 * 1024)
            return BadRequest(new { message = "Ảnh phải là JPG, PNG hoặc WEBP và không vượt quá 8 MB." });

        try
        {
            await using Stream stream = file.OpenReadStream();
            string path = await imageStorageService.UploadAsync(stream, file.FileName, file.ContentType, cancellationToken);
            string url = Uri.IsWellFormedUriString(path, UriKind.Absolute)
                ? path
                : $"{Request.Scheme}://{Request.Host}{path}";

            return StatusCode(StatusCodes.Status201Created, new { url });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
    ```
  - Xóa file cũ `smart-locking-be.API/Controllers/UploadsController.cs`.

- [x] **Step 2: Cập nhật tham chiếu trong `smart-locking-be.Tests/Architecture/ServiceCompositionTests.cs`**
  - Đổi `typeof(UploadsController)` thành `typeof(ImagesController)` tại các dòng 20, 34, 37.

---

### Task 5: Build và kiểm tra tính toàn vẹn hệ thống

**Files:**
- Verification only

- [x] **Step 1: Build toàn bộ solution**
  - Chạy `dotnet build` để đảm bảo không có lỗi biên dịch.

- [x] **Step 2: Chạy kiểm tra bộ test hiện tại**
  - Chạy `dotnet test` để xác nhận 210 test hiện có vẫn chạy đạt 100% PASS.

---

### Task 6: Commit, Push và Tạo Pull Request vào nhánh `dev`

**Files:**
- Git repository operations (`95-image-supabase-cloud` -> `dev`)

**Interfaces:**
- Consumes: Codebase đã hoàn thành build và test PASS
- Produces: Pull Request được tạo thành công trên GitHub nhắm vào nhánh `dev` với nội dung theo template `.github/PULL_REQUEST_TEMPLATE.md`

- [x] **Step 1: Kiểm tra git status và diff**
  - Kiểm tra `git status` đảm bảo không có file rác, file nháp hoặc file không liên quan.
  - Review lại `git diff` các thay đổi.

- [x] **Step 2: Commit và Push lên remote branch**
  - Thực hiện stage và commit:
    ```bash
    git add .
    git commit -m "feat(storage): integrate supabase image storage and rename ImagesController"
    ```
  - Push lên nhánh hiện tại:
    ```bash
    git push origin 95-image-supabase-cloud
    ```

- [x] **Step 3: Tạo Pull Request vào nhánh `dev` theo đúng template**
  - Sử dụng GitHub CLI (`gh pr create`) với base branch `dev`, điền đầy đủ các mục theo template `.github/PULL_REQUEST_TEMPLATE.md`:
    ```bash
    gh pr create --base dev --head 95-image-supabase-cloud --title "feat(storage): integrate Supabase image storage and rename ImagesController" --body "$(cat << 'EOF'
    # 🔐 SMART LOCKING — BACKEND
    ## 📌 Mô tả PR

    Chuyển đổi cơ chế lưu trữ ảnh từ local mock sang Supabase Storage cloud, đổi tên `UploadsController` thành `ImagesController`, hỗ trợ trả về Public URL từ Supabase và giữ nguyên khả năng tương thích ngược cho mọi luồng nghiệp vụ hiện tại.

    ## 🔗 Issue liên quan

    Closes #95

    ## 📝 Thay đổi chính

    - Thêm model cấu hình `SupabaseStorageOptions` và section cấu hình trong `appsettings.json`.
    - Tạo `SupabaseImageStorageService` trong tầng Infrastructure giao tiếp qua Supabase Storage REST API.
    - Đổi tên `UploadsController` thành `ImagesController` với route chính `POST /api/images` và route alias `POST /api/uploads/images`.
    - Tự động nhận diện URL tuyệt đối (Supabase) để tránh ghép thừa host.
    - Cập nhật DI và tham chiếu trong `ServiceCompositionTests.cs` đảm bảo 210/210 test hiện có đạt PASS.

    ## 📸 Evidence

    - Build: `dotnet build` succeeded with 0 errors.
    - Test: `dotnet test` passed 210/210 tests.

    ## ✅ Checklist

    - [x] Đã test và tất cả test đều PASS.
    - [x] Đã format/lint code.
    - [x] Đã kiểm tra API và xử lý lỗi.
    - [ ] Đã kiểm tra Database/Migration nếu có thay đổi.
    - [x] Đã cập nhật Documentation nếu cần.
    - [x] Đã tự review code.
    - [x] Không có code/file không liên quan.
    EOF
    )"
    ```


## Kết quả triển khai

- Build: 0 warnings, 0 errors.
- Test hiện có: 210/210 PASS; composition test với cấu hình cloud giả: 1/1 PASS.
- Format whitespace và git diff --check: PASS; review độc lập không có lỗi quan trọng.
- PR: https://github.com/se-05-sdlms/smart-locking-be/pull/135 (base dev).
- Chưa xác minh upload trên Supabase thật; xem docs/supabase-image-storage.md để cấu hình.
