# Subscription Management, Overdue Handling, and PayOS Integration Implementation Plan

> **For agentic workers:**
> - **Rapid Code Navigation (Codegraph):** Use `codegraph` tools (symbol lookup, jump to definition, find references, call hierarchy) to inspect and understand existing code quickly instead of reading full files unnecessarily.
> - **Minimalist Coding Skill (Ponytail):** Channel the `ponytail` skill at all times: write the laziest/simplest solution that actually works, shortest path, zero boilerplate, no speculative abstractions, no unrequested interfaces/factories, reuse existing codebase helpers/types, and follow YAGNI (You Aren't Gonna Need It).
> - **Execution Mode:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Implement the Resident Subscription System (SubscriptionPlan & ResidentSubscription) integrated with PayOS payment gateway for smart locker usage, enhance the 7-day overdue parcel lifecycle (block remote unlock after 7 days, 24h clearance warnings, overdue fee capping, and operator clearance list), and submit a pull request to `dev` using the repo's PR template.

**Architecture:** Clean Architecture with Domain-Driven Design principles in .NET 8. POCO entities in `smart-locking-be.Domain`, EF Core Code First configurations with PostgreSQL partial indexes in `smart-locking-be.Infrastructure`, DTOs and business contracts in `smart-locking-be.Application`, and Controllers & PeriodicTimer BackgroundServices in `smart-locking-be.API`.

**Tech Stack:** .NET 8, C# 12, Entity Framework Core 8, Npgsql PostgreSQL, payOS SDK, xUnit, Moq/Mocking.

**Spec:** `docs/superpowers/specs/2026-09-12-sdlms-code-first-design.md`, `BE-HUY.md`, and `database_description.md`.

---

## Global Constraints & Philosophy

- **Ponytail Rule:** Write the cleanest, most concise, and minimal code. Do not add abstractions "for the future". Favor 1 clean line over 20 lines of ceremonial code.
- **Codegraph Navigation:** Use codegraph to locate definitions and callers before modifying code to prevent regression across sibling callers.
- Domain POCOs must not reference Entity Framework Core or database drivers.
- All relationships must configure `DeleteBehavior.Restrict`.
- All monetary/rate fields use `decimal(18,2)`.
- All timestamps use `DateTimeOffset` in UTC semantics.
- Enum properties are stored as string values in PostgreSQL.
- Existing tests in `smart-locking-be.Tests` (`ModelContract`, `RelationalModelContract`, `PersistenceModelTests`) must remain 100% green without regressions.
- Every task must be verified with `dotnet build` and `dotnet test`.

---

## Review Focus

1. **Subscription Stacking:** If a resident already has an `Active` subscription and purchases another plan, upon successful payment activation `new.StartDate` must start from `current.EndDate`, not `now`.
2. **PayOS Webhook Verification & Idempotency:** Webhook requests from PayOS must verify checksum signature. Activation must be idempotent: duplicate webhook events for the same `orderCode` must not duplicate active periods or throw unhandled exceptions.
3. **Pending Payment Lifecycle:** Subscribing creates a `PendingPayment` record with PayOS `checkoutUrl`. The subscription is only transitioned to `Active` after PayOS webhook confirmation or verified checkout sync.
4. **7-Day Overdue Pickup Blocking:** Attempting to call `UnlockPickup` when `now >= parcel.MaxStorageUntil` must be rejected with 409 Conflict (`InvalidOperationException`), directing resident to the collection point (`RecoveryAddress`).
5. **Overdue Fee Cap:** `OverdueChargeService` must not calculate compounding fees past `parcel.MaxStorageUntil` (fee calculation caps at 7 days).
6. **Subscription Expiration Safety:** `SubscriptionExpirationWorker` must transition `Active` subscriptions with `EndDate < now` to `Expired` without race conditions.
7. **Clearance Queue Isolation:** Operator/Admin querying `ParcelListView.ClearancePending` must only receive parcels where `Status in (Stored, Overdue)` AND `MaxStorageUntil <= now`.

---

## File Structure

```
smart-locking-be.Domain/
├── Entities/
│   ├── SubscriptionPlan.cs              [Create: Plan specifications]
│   ├── ResidentSubscription.cs          [Create: Resident subscription instance]
│   └── ResidentProfile.cs               [Modify: Add Subscriptions collection]
└── Enums/
    └── SubscriptionEnums.cs             [Create: SubscriptionStatus]

smart-locking-be.Infrastructure/
├── Options/
│   └── PayOSOptions.cs                  [Create: PayOS configuration model]
├── Persistence/
│   ├── ApplicationDbContext.cs          [Modify: Add DbSets]
│   └── Configurations/
│       ├── SubscriptionPlanConfiguration.cs     [Create: EF mapping & indexes]
│       └── ResidentSubscriptionConfiguration.cs [Create: EF mapping & indexes]
└── Services/
    ├── SubscriptionPlanService.cs               [Create: Admin CRUD & queries]
    ├── ResidentSubscriptionService.cs           [Create: Subscribe, stacking & activation]
    ├── PayOSPaymentGatewayService.cs            [Create: PayOS SDK wrapper]
    ├── ParcelPickupService.cs                   [Modify: Block unlock past 7d]
    ├── OverdueChargeService.cs                  [Modify: Cap fees at MaxStorageUntil]
    └── ParcelService.cs                         [Modify: Support ClearancePending view]

smart-locking-be.Application/
├── DTOs/Subscriptions/
│   ├── SubscriptionPlanDtos.cs                  [Create: Plan request/response DTOs]
│   └── ResidentSubscriptionDtos.cs              [Create: Sub request/response DTOs]
├── DTOs/Payments/
│   └── PaymentGatewayDtos.cs                    [Create: PayOS checkout & webhook DTOs]
├── DTOs/Parcels/
│   └── ParcelListView.cs                        [Modify: Add ClearancePending]
└── Interfaces/Services/
    ├── ISubscriptionPlanService.cs              [Create: Interface]
    ├── IResidentSubscriptionService.cs          [Create: Interface]
    └── IPaymentGatewayService.cs                [Create: PayOS payment interface]

smart-locking-be.API/
├── Controllers/
│   ├── SubscriptionPlansController.cs           [Create: Admin & public endpoints]
│   ├── ResidentSubscriptionsController.cs       [Create: Resident endpoints]
│   └── PaymentWebhooksController.cs             [Create: PayOS webhook endpoint]
└── Services/
    └── SubscriptionExpirationWorker.cs          [Create: Hourly expiration job]

smart-locking-be.Tests/
├── ModelContract.cs                             [Modify: Add new entities to contract]
├── RelationalModelContract.cs                   [Modify: Add relational expectations]
├── Subscriptions/
│   ├── SubscriptionPlanServiceTests.cs          [Create: CRUD tests]
│   └── ResidentSubscriptionServiceTests.cs      [Create: Stacking, pending & status tests]
├── Payments/
│   ├── PayOSPaymentGatewayServiceTests.cs       [Create: Checkout link & verification tests]
│   └── PaymentWebhooksControllerTests.cs        [Create: Webhook processing tests]
└── Parcels/
    ├── ParcelPickupServiceTests.cs              [Modify: Verify 7-day pickup block]
    └── OverdueChargeServiceTests.cs             [Modify: Verify fee capping]
```

---

### Task 1: Domain Entities & Enums for Subscriptions

**Files:**
- Create: `smart-locking-be.Domain/Enums/SubscriptionEnums.cs`
- Create: `smart-locking-be.Domain/Entities/SubscriptionPlan.cs`
- Create: `smart-locking-be.Domain/Entities/ResidentSubscription.cs`
- Modify: `smart-locking-be.Domain/Entities/ResidentProfile.cs`

**Interfaces:**
- Produces: `SubscriptionStatus`, `SubscriptionPlan`, `ResidentSubscription`

- [x] **Step 1: Create `SubscriptionEnums.cs`**
  Define `SubscriptionStatus` with values `PendingPayment`, `Active`, `Expired`, `Cancelled`.

- [x] **Step 2: Create `SubscriptionPlan.cs`**
  Properties: `Id` (Guid), `Code` (string), `Name` (string), `Description` (string?), `Price` (decimal), `DurationDays` (int), `MaxActiveParcels` (int), `IsActive` (bool), `CreatedAt` (DateTimeOffset), `UpdatedAt` (DateTimeOffset), navigation `ResidentSubscriptions`.

- [x] **Step 3: Create `ResidentSubscription.cs`**
  Properties: `Id` (Guid), `ResidentProfileId` (Guid), `SubscriptionPlanId` (Guid), `StartDate` (DateTimeOffset), `EndDate` (DateTimeOffset), `Status` (SubscriptionStatus), `PriceSnapshot` (decimal), `ExternalOrderCode` (string?), `ExternalTransactionId` (string?), `PaidAt` (DateTimeOffset?), `CreatedAt` (DateTimeOffset), `UpdatedAt` (DateTimeOffset), navigation `ResidentProfile`, `SubscriptionPlan`.

- [x] **Step 4: Update `ResidentProfile.cs`**
  Add `public ICollection<ResidentSubscription> Subscriptions { get; set; } = new List<ResidentSubscription>();`.

- [x] **Step 5: Verify build**
  Run: `dotnet build smart-locking-be.Domain`
  Expected: Build succeeds with 0 errors.

---

### Task 2: EF Core Configurations, Model Contracts, and Migration

**Files:**
- Create: `smart-locking-be.Infrastructure/Persistence/Configurations/SubscriptionPlanConfiguration.cs`
- Create: `smart-locking-be.Infrastructure/Persistence/Configurations/ResidentSubscriptionConfiguration.cs`
- Modify: `smart-locking-be.Infrastructure/Persistence/ApplicationDbContext.cs`
- Modify: `smart-locking-be.Tests/ModelContract.cs`
- Modify: `smart-locking-be.Tests/RelationalModelContract.cs`

**Interfaces:**
- Consumes: `SubscriptionPlan`, `ResidentSubscription`, `SubscriptionStatus`
- Produces: `DbSet<SubscriptionPlan>`, `DbSet<ResidentSubscription>`

- [x] **Step 1: Create `SubscriptionPlanConfiguration.cs`**
  Inherit `BaseConfiguration<SubscriptionPlan>`. Unique index on `Code`. Check constraint `Price >= 0` and `DurationDays > 0`.

- [x] **Step 2: Create `ResidentSubscriptionConfiguration.cs`**
  Inherit `BaseConfiguration<ResidentSubscription>`. Store `Status` as string. Foreign keys to `ResidentProfile` and `SubscriptionPlan` with `DeleteBehavior.Restrict`. Index on `ResidentProfileId`. Unique/sparse index on `ExternalOrderCode` where not null for fast PayOS lookup.

- [x] **Step 3: Register DbSets in `ApplicationDbContext.cs`**
  Add `public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();` and `public DbSet<ResidentSubscription> ResidentSubscriptions => Set<ResidentSubscription>();`.

- [x] **Step 4: Update `ModelContract.cs` and `RelationalModelContract.cs`**
  Add `"SubscriptionPlan"` and `"ResidentSubscription"` to `EntityNames` and `Properties` dictionary with their respective properties so `PersistenceModelTests` pass.

- [x] **Step 5: Run persistence model tests**
  Run: `dotnet test --filter "FullyQualifiedName~PersistenceModelTests"`
  Expected: All model contract tests PASS.

- [x] **Step 6: Add EF Core Migration**
  Run: `dotnet ef migrations add AddSubscriptionSystem --project smart-locking-be.Infrastructure --startup-project smart-locking-be.API`
  Expected: Migration generated cleanly.

---

### Task 3: Subscription & Payment Gateway Application DTOs and Interfaces

**Files:**
- Create: `smart-locking-be.Application/DTOs/Subscriptions/SubscriptionPlanDtos.cs`
- Create: `smart-locking-be.Application/DTOs/Subscriptions/ResidentSubscriptionDtos.cs`
- Create: `smart-locking-be.Application/DTOs/Payments/PaymentGatewayDtos.cs`
- Create: `smart-locking-be.Application/Interfaces/Services/ISubscriptionPlanService.cs`
- Create: `smart-locking-be.Application/Interfaces/Services/IResidentSubscriptionService.cs`
- Create: `smart-locking-be.Application/Interfaces/Services/IPaymentGatewayService.cs`

**Interfaces:**
- Produces:
  - `SubscriptionPlanResponse(Guid Id, string Code, string Name, string? Description, decimal Price, int DurationDays, int MaxActiveParcels, bool IsActive)`
  - `CreateSubscriptionPlanRequest(string Code, string Name, string? Description, decimal Price, int DurationDays, int MaxActiveParcels)`
  - `UpdateSubscriptionPlanRequest(string Name, string? Description, decimal Price, int DurationDays, int MaxActiveParcels, bool IsActive)`
  - `ResidentSubscriptionResponse(Guid Id, Guid PlanId, string PlanName, DateTimeOffset StartDate, DateTimeOffset EndDate, string Status, decimal Price, string? ExternalOrderCode, DateTimeOffset? PaidAt)`
  - `SubscribePlanRequest(Guid PlanId)`
  - `SubscriptionCheckoutResponse(Guid SubscriptionId, string CheckoutUrl, long OrderCode, decimal Amount)`
  - `CreatePaymentLinkRequest(long OrderCode, int Amount, string Description, string ReturnUrl, string CancelUrl)`
  - `CreatePaymentLinkResult(string CheckoutUrl, string PaymentLinkId, string QrCode)`
  - `PayOSWebhookData(long OrderCode, int Amount, string Description, string Reference, string TransactionDateTime, string Code, string Desc)`
  - `PayOSWebhookPayload(string Code, string Desc, PayOSWebhookData? Data, string Signature)`

- [x] **Step 1: Create minimal DTO records using C# positional record syntax**
- [x] **Step 2: Define `ISubscriptionPlanService.cs`**
- [x] **Step 3: Define `IPaymentGatewayService.cs`**
- [x] **Step 4: Define `IResidentSubscriptionService.cs`**
- [x] **Step 5: Verify build**
  Run: `dotnet build smart-locking-be.Application`
  Expected: Build succeeds with 0 errors.

---

### Task 4: Subscription Plan Management Service & Tests

**Files:**
- Create: `smart-locking-be.Infrastructure/Services/SubscriptionPlanService.cs`
- Create: `smart-locking-be.Tests/Subscriptions/SubscriptionPlanServiceTests.cs`

**Interfaces:**
- Consumes: `ISubscriptionPlanService`, `ApplicationDbContext`

- [x] **Step 1: Write failing tests in `SubscriptionPlanServiceTests.cs`**
  Test `CreateAsync_ValidRequest_CreatesPlan`, `CreateAsync_DuplicateCode_ThrowsArgumentException`, `GetPlansAsync_ActiveOnly_FiltersCorrectly`.
- [x] **Step 2: Run test to verify failure**
  Run: `dotnet test --filter "FullyQualifiedName~SubscriptionPlanServiceTests"`
  Expected: FAIL with missing implementation.
- [x] **Step 3: Implement `SubscriptionPlanService.cs`**
  Use minimal code: standard EF Core queries (`AsNoTracking()`), validation guards, and return DTO mappings directly.
- [x] **Step 4: Run test to verify pass**
  Run: `dotnet test --filter "FullyQualifiedName~SubscriptionPlanServiceTests"`
  Expected: PASS.

---

### Task 5: PayOS Payment Gateway Service Integration & Tests

**Files:**
- Modify: `smart-locking-be.Infrastructure/smart-locking-be.Infrastructure.csproj` (add `payOS` package)
- Create: `smart-locking-be.Infrastructure/Options/PayOSOptions.cs`
- Create: `smart-locking-be.Infrastructure/Services/PayOSPaymentGatewayService.cs`
- Create: `smart-locking-be.Tests/Payments/PayOSPaymentGatewayServiceTests.cs`

**Interfaces:**
- Consumes: `IPaymentGatewayService`, `IOptions<PayOSOptions>`

- [x] **Step 1: Add `payOS` package to Infrastructure**
  Add `<PackageReference Include="payOS" Version="1.0.9" />` to `smart-locking-be.Infrastructure.csproj`.
- [x] **Step 2: Create `PayOSOptions.cs`**
  Properties: `ClientId`, `ApiKey`, `ChecksumKey`, `ReturnUrl`, `CancelUrl`.
- [x] **Step 3: Write tests in `PayOSPaymentGatewayServiceTests.cs`**
  Verify payment link generation and webhook signature verification logic.
- [x] **Step 4: Implement `PayOSPaymentGatewayService.cs`**
  Wrap PayOS SDK minimally without extra boilerplate layers.
- [x] **Step 5: Run tests to verify pass**
  Run: `dotnet test --filter "FullyQualifiedName~PayOSPaymentGatewayServiceTests"`
  Expected: PASS.

---

### Task 6: Resident Subscription Service & PayOS Checkout Flow Tests

**Files:**
- Create: `smart-locking-be.Infrastructure/Services/ResidentSubscriptionService.cs`
- Create: `smart-locking-be.Tests/Subscriptions/ResidentSubscriptionServiceTests.cs`

**Interfaces:**
- Consumes: `IResidentSubscriptionService`, `IPaymentGatewayService`, `ApplicationDbContext`, `TimeProvider`

- [x] **Step 1: Write failing tests in `ResidentSubscriptionServiceTests.cs`**
  Test `SubscribeAsync_CreatesPendingSubscriptionAndReturnsCheckoutUrl`,
  `ActivateSubscriptionAsync_NoPriorSubscription_StartsNowAndSetsActive`,
  `ActivateSubscriptionAsync_HasActiveSubscription_StacksFromPriorEndDate`,
  `ActivateSubscriptionAsync_AlreadyActive_IsIdempotent`,
  `ExpireOutdatedSubscriptionsAsync_ExpiredSubscriptions_MarksExpired`.
- [x] **Step 2: Run test to verify failure**
  Run: `dotnet test --filter "FullyQualifiedName~ResidentSubscriptionServiceTests"`
  Expected: FAIL.
- [x] **Step 3: Implement `ResidentSubscriptionService.cs`**
  - **SubscribeAsync:**
    1. Resolve resident profile for `userId`.
    2. Retrieve plan; validate `plan.IsActive`.
    3. Generate positive numeric `orderCode` using timestamp ms.
    4. Call `paymentGatewayService.CreatePaymentLinkAsync(...)`.
    5. Save `ResidentSubscription` entity (`Status = PendingPayment`, `ExternalOrderCode = orderCode.ToString()`).
    6. Return `SubscriptionCheckoutResponse`.
  - **ActivateSubscriptionAsync:**
    1. Look up subscription by `ExternalOrderCode`.
    2. If already `Active`, return early (idempotent).
    3. Determine existing active subscription for resident to calculate stacked `StartDate` and `EndDate`.
    4. Set `Status = Active`, `PaidAt = now`, `ExternalTransactionId = externalTransactionId`.
    5. Save changes.
  - **ExpireOutdatedSubscriptionsAsync:**
    Mark all `Active` subscriptions with `EndDate < now` as `Expired`.
- [x] **Step 4: Run tests to verify pass**
  Run: `dotnet test --filter "FullyQualifiedName~ResidentSubscriptionServiceTests"`
  Expected: PASS.

---

### Task 7: Overdue Parcel Enhancement — Block Remote Pickup After 7 Days

**Files:**
- Modify: `smart-locking-be.Infrastructure/Services/ParcelPickupService.cs:40-60`
- Modify: `smart-locking-be.Tests/Parcels/ParcelPickupServiceTests.cs`

**Interfaces:**
- Consumes: `ParcelPickupService.UnlockAsync`

- [x] **Step 1: Write failing test in `ParcelPickupServiceTests.cs`**
  Write `UnlockAsync_ParcelPastMaxStorageUntil_ThrowsInvalidOperationException`: Seed parcel with `MaxStorageUntil = Now.AddMinutes(-5)`. Assert `UnlockAsync` throws `InvalidOperationException`.
- [x] **Step 2: Run test to verify failure**
  Run: `dotnet test --filter "FullyQualifiedName~UnlockAsync_ParcelPastMaxStorageUntil"`
  Expected: FAIL.
- [x] **Step 3: Update `ParcelPickupService.UnlockAsync`**
  Add guard clause:
  ```csharp
  if (timeProvider.GetUtcNow() >= parcel.MaxStorageUntil)
  {
      throw new InvalidOperationException("Kiện hàng đã vượt quá hạn mức lưu trữ tối đa (7 ngày) và đã được chuyển sang diện thu hồi về kho tập kết.");
  }
  ```
- [x] **Step 4: Run test to verify pass**
  Run: `dotnet test --filter "FullyQualifiedName~ParcelPickupServiceTests"`
  Expected: PASS.

---

### Task 8: Overdue Parcel Enhancement — Clearance Warning & Fee Cap

**Files:**
- Modify: `smart-locking-be.Infrastructure/Services/ParcelReminderService.cs:40-55`
- Modify: `smart-locking-be.Infrastructure/Services/OverdueChargeService.cs:50-77`
- Modify: `smart-locking-be.Tests/Parcels/OverdueChargeServiceTests.cs`
- Modify: `smart-locking-be.Tests/Parcels/ParcelReminderServiceTests.cs`

**Interfaces:**
- Consumes: `OverdueChargeService.CalculateAsync`, `ParcelReminderService.SendDailyRemindersAsync`

- [x] **Step 1: Write failing test for fee capping in `OverdueChargeServiceTests.cs`**
  `CalculateAsync_ParcelPastMaxStorage_CapsCalculatedHoursAtMaxStorageUntil`: Seed parcel where `now` is 3 days past `MaxStorageUntil`. Verify calculated billable hours do not exceed `(MaxStorageUntil - PickupDueAt).TotalHours`.
- [x] **Step 2: Update `OverdueChargeService.cs`**
  Cap the calculation timestamp: `DateTimeOffset calculationCutoff = now > parcel.MaxStorageUntil ? parcel.MaxStorageUntil : now;` and calculate `billableHours = (int)Math.Ceiling((calculationCutoff - parcel.PickupDueAt).TotalHours);`.
- [x] **Step 3: Update `ParcelReminderService.cs`**
  If `(parcel.MaxStorageUntil - now).TotalHours <= 24`, send urgent final 24h clearance notification.
- [x] **Step 4: Run tests to verify pass**
  Run: `dotnet test --filter "FullyQualifiedName~OverdueChargeServiceTests"`
  Expected: PASS.

---

### Task 9: Clearance Pending Filter for Operators & ParcelService

**Files:**
- Modify: `smart-locking-be.Application/DTOs/Parcels/ParcelListView.cs`
- Modify: `smart-locking-be.Infrastructure/Services/ParcelService.cs`
- Modify: `smart-locking-be.Tests/Parcels/ParcelServiceTests.cs`

**Interfaces:**
- Consumes: `IParcelService.GetParcelsAsync`

- [x] **Step 1: Add `ClearancePending` to `ParcelListView` enum**
- [x] **Step 2: Update `ParcelService.GetParcelsAsync`**
  When `view == ParcelListView.ClearancePending`: filter parcels where `(parcel.Status == ParcelStatus.Stored || parcel.Status == ParcelStatus.Overdue) && parcel.MaxStorageUntil <= now`.
- [x] **Step 3: Write test in `ParcelServiceTests.cs`**
  Verify operator can list parcels awaiting 7-day clearance.
- [x] **Step 4: Run tests**
  Run: `dotnet test --filter "FullyQualifiedName~ParcelServiceTests"`
  Expected: PASS.

---

### Task 10: API Controllers, PayOS Webhook & Worker Registration

**Files:**
- Create: `smart-locking-be.API/Controllers/SubscriptionPlansController.cs`
- Create: `smart-locking-be.API/Controllers/ResidentSubscriptionsController.cs`
- Create: `smart-locking-be.API/Controllers/PaymentWebhooksController.cs`
- Create: `smart-locking-be.API/Services/SubscriptionExpirationWorker.cs`
- Create: `smart-locking-be.Tests/Payments/PaymentWebhooksControllerTests.cs`
- Modify: `smart-locking-be.API/DependencyInjection.cs`
- Modify: `smart-locking-be.Infrastructure/DependencyInjection.cs`

**Interfaces:**
- Consumes: `ISubscriptionPlanService`, `IResidentSubscriptionService`, `IPaymentGatewayService`

- [x] **Step 1: Create `SubscriptionPlansController.cs`**
  CRUD endpoints for plans (`GET /api/subscription-plans`, admin endpoints under `/api/admin/subscription-plans`).
- [x] **Step 2: Create `ResidentSubscriptionsController.cs`**
  Resident subscription endpoints (`current`, `history`, `subscribe`).
- [x] **Step 3: Create `PaymentWebhooksController.cs`**
  `POST /api/webhooks/payos` verifying payload and calling `ActivateSubscriptionAsync`.
- [x] **Step 4: Write tests in `PaymentWebhooksControllerTests.cs`**
  Test valid webhook activates subscription; invalid checksum returns 400 Bad Request.
- [x] **Step 5: Create `SubscriptionExpirationWorker.cs`**
  Background worker running hourly to expire outdated subscriptions.
- [x] **Step 6: Register services in `DependencyInjection.cs`**
  Register PayOS, subscription services, and hosted service.
- [x] **Step 7: Run full solution build and tests**
  Run: `dotnet test`
  Expected: 0 errors, 100% tests pass.

---

### Task 11: Final Verification & Create Pull Request to `dev`

**Files:**
- Reference: `.github/PULL_REQUEST_TEMPLATE.md`

- [x] **Step 1: Final sanity verification**
  Run `dotnet build --no-incremental` and `dotnet test`. Ensure 0 errors and all test suites pass.
- [ ] **Step 2: Push changes to remote branch**
  Run: `git push origin 26-payment`
- [ ] **Step 3: Create Pull Request targeting branch `dev`**
  Use GitHub CLI (`gh pr create --base dev`) or web with PR body formatted strictly adhering to `.github/PULL_REQUEST_TEMPLATE.md`:

```markdown
# 🔐 SMART LOCKING — BACKEND
## 📌 Mô tả PR

```text
Triển khai hệ thống gói cước cư dân (Subscription Plan & Resident Subscription) tích hợp cổng thanh toán PayOS (tạo checkout link và xử lý webhook tự động kích hoạt gói có cộng dồn thời hạn). Đồng thời nâng cấp vòng đời kiện hàng quá hạn 7 ngày: chặn mở tủ từ xa khi quá hạn, gửi cảnh báo dọn tủ 24h, chốt trần tính phí phạt và hỗ trợ màn hình danh sách chờ thu hồi (Clearance Pending) cho nhân viên vận hành.
```

## 🔗 Issue liên quan

```text
Closes #26
```

## 📝 Thay đổi chính

```text
- Domain: Thêm entities SubscriptionPlan, ResidentSubscription và enum SubscriptionStatus.
- Infrastructure: Mapping EF Core, migration AddSubscriptionSystem, cài đặt SDK payOS và PayOSPaymentGatewayService.
- Application: Định nghĩa DTOs và interfaces cho Subscription và Payment Gateway.
- Overdue Handling: Chặn mở tủ sau 7 ngày trong ParcelPickupService, chốt trần phí phạt trong OverdueChargeService, cảnh báo 24h trong ParcelReminderService, thêm view ClearancePending trong ParcelService.
- API: Cung cấp SubscriptionPlansController, ResidentSubscriptionsController, PaymentWebhooksController và SubscriptionExpirationWorker.
- Tests: Bổ sung đầy đủ unit tests cho toàn bộ luồng đăng ký gói, thanh toán PayOS, webhook callback và kiểm tra quá hạn 7 ngày.
```

## 📸 Evidence

```text
Tất cả các bài test trong solution smart-locking-be.Tests đều PASS 100% (dotnet test hoàn tất thành công).
```

## ✅ Checklist

- [x] Đã test và tất cả test đều PASS.
- [x] Đã format/lint code.
- [x] Đã kiểm tra API và xử lý lỗi.
- [x] Đã kiểm tra Database/Migration nếu có thay đổi.
- [x] Đã cập nhật Documentation nếu cần.
- [x] Đã tự review code.
- [x] Không có code/file không liên quan.
```

## Resumed execution notes (2026-10-13)

- Tasks 1–2 reused existing commits `8e39966` and `b7ce086`; baseline 171 tests passed.
- Tasks 3–10 completed on `26-payment`. See `docs/subscription-and-overdue.md` for actual contracts, configuration, and integration limits.
- Signed webhook data preserves the complete provider payload. Activation checks amount/currency and serializes per resident using PostgreSQL row locks. Pending interval captures purchased duration; pending orders persist before network dispatch.
- Random positive 52-bit order codes replace timestamp-only codes to avoid same-millisecond collisions across instances.
- Deadline behavior uses persisted `MaxStorageUntil`; no policy or historical deadline changes.
- PayOS checkout uses SDK1.0.9. Offline tests verify actual SDK signatures and checkout validation, not a live payment link. Real PostgreSQL concurrency requires an integration environment; Docker was unavailable.
- Final review fixed callback registration probes: verified unknown orders return200 without mutation. No unresolved important findings. Multi-worker notification dedup remains a known inherited limitation.
- PR references Related #26 instead of Closes #26 because that issue also requires overdue-fee payment/history/reconciliation endpoints outside this subscription plan.
