# Linkly Cancelled Backend Status Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 补全 Linkly 后端状态机的 `Cancelled` 最终状态，让后端能产出客户端已预留处理的取消状态。

**Architecture:** `Cancelled` 是 Linkly backend session 的最终状态，不应继续占用 active session，也不应进入恢复轮询。实现以共享状态常量为合同入口，在后端解析 200 OK 的 Linkly 响应时仅把明确取消结果映射为 `Cancelled`，其他失败仍保持 `Completed + TransactionSuccess=false` 或既有 `Failed` 语义，避免把 declined 误判为取消。

**Tech Stack:** .NET/C#, xUnit, `Hbpos.Contracts.Linkly`, `Hbpos.Api` Linkly async backend service, existing Linkly backend client tests.

---

## File Structure

- Modify: `apps/pos-wpf/src/Hbpos.Contracts/Linkly/LinklyCloudBackendStatusConstants.cs`
  - 新增共享常量 `StatusCancelled = "Cancelled"`，供 API 和客户端统一引用。
- Modify: `apps/pos-wpf/src/Hbpos.Api/Services/LinklyCloudBackendAsyncService.cs`
  - 在 `ApplyCompletedPayload` 内解析 Linkly final response 后决定 `Completed` 或 `Cancelled`。
  - 新增 `IsCancelledResponse(...)` / `IsCancelledResponseCode(...)` / `IsCancelledResponseText(...)` 小函数。
  - 更新 `IsFinalForClientRecovery(...)`，让 `Cancelled` 不再作为可恢复 active session 暴露。
- Modify: `apps/pos-wpf/tests/Hbpos.Api.Tests/LinklyCloudBackendAsyncServiceTests.cs`
  - 新增后端状态映射测试，覆盖 `RecoverAsync` 或 `GetStatusAsync` 收到取消响应时产出 `Cancelled`、`IsActive=false`。
  - 新增恢复过滤测试，覆盖 `Cancelled` session 不会被 `GetResumableSessionAsync` 返回。
- Optional Modify: `apps/pos-wpf/src/Hbpos.Client.Wpf/Services/LinklyBackendTerminalClient.cs`
  - 不需要行为改动；客户端已有 `IsCancelledStatus("Cancelled"|"Canceled")`。
  - 如执行者愿意顺手收敛魔法字符串，可把客户端 `"Cancelled"` 替换为共享常量，但这不是本修复的必要条件。

## Current Evidence

- 客户端已把 `Cancelled` / `Canceled` 当最终状态处理：

```csharp
private static bool IsFinal(LinklyCloudBackendSessionResponse status)
{
    return string.Equals(status.Status, StatusCompleted, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status.Status, StatusFailed, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status.Status, StatusNotSubmitted, StringComparison.OrdinalIgnoreCase) ||
        IsCancelledStatus(status.Status);
}
```

- 后端目前 `ApplyCompletedPayload` 一进入 HTTP 200 就先写 `StatusCompleted`，因此不会产出 `Cancelled`：

```csharp
private static void ApplyCompletedPayload(
    LinklyCloudBackendSessionRecord session,
    string? payloadJson,
    bool updateTxnRef = true,
    bool updateOutcomeEvidence = true)
{
    session.Status = StatusCompleted;
    session.IsActive = false;
    session.RecoveryAction = null;
    if (string.IsNullOrWhiteSpace(payloadJson))
    {
        return;
    }

    using var document = JsonDocument.Parse(payloadJson);
    var response = ReadResponse(document.RootElement);
    session.TransactionSuccess = ReadBool(response, "Success");
    ...
}
```

- 后端恢复过滤目前也没有 `Cancelled`：

```csharp
private static bool IsFinalForClientRecovery(LinklyCloudBackendSessionRecord session)
{
    return string.Equals(session.Status, "Completed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(session.Status, "Failed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(session.Status, "NotSubmitted", StringComparison.OrdinalIgnoreCase);
}
```

---

### Task 1: Add Shared Cancelled Status Constant

**Files:**
- Modify: `apps/pos-wpf/src/Hbpos.Contracts/Linkly/LinklyCloudBackendStatusConstants.cs`

- [ ] **Step 1: Add the shared constant**

Replace the constants block with:

```csharp
public static class LinklyCloudBackendStatusConstants
{
    public const string StatusPending = "Pending";
    public const string StatusCompleted = "Completed";
    public const string StatusCancelled = "Cancelled";
    public const string StatusFailed = "Failed";
    public const string StatusNotSubmitted = "NotSubmitted";
    public const string StatusTokenRefreshRequired = "TokenRefreshRequired";

    public const string RecoveryRetry = "Retry";
    public const string RecoveryRefreshToken = "RefreshToken";
}
```

- [ ] **Step 2: Build contracts through dependent API test project**

Run:

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Api.Tests/Hbpos.Api.Tests.csproj --filter "FullyQualifiedName~LinklyCloudBackendAsyncServiceTests" --artifacts-path artifacts/linkly-cancelled-contract-check
```

Expected: compile succeeds; current tests may pass because the new constant is unused.

---

### Task 2: Write Failing API Test For Cancelled Mapping

**Files:**
- Modify: `apps/pos-wpf/tests/Hbpos.Api.Tests/LinklyCloudBackendAsyncServiceTests.cs`

- [ ] **Step 1: Add a red test for cancel response mapping**

Add this test near the existing `RecoverAsync` status mapping tests:

```csharp
[Fact]
public async Task RecoverAsync_maps_cancelled_linkly_result_to_cancelled_status()
{
    var repository = new InMemoryLinklyCloudBackendAsyncRepository();
    var transport = new StubTransport
    {
        RecoverResponse = new LinklyCloudBackendTransportResponse(
            HttpStatusCode.OK,
            """
            {
                "Response": {
                    "Success": false,
                    "TxnRef": "TXN-CANCELLED",
                    "ResponseCode": "TC",
                    "ResponseText": "CANCELLED"
                }
            }
            """)
    };
    var service = CreateService(repository, transport);
    await repository.UpsertSessionAsync(new LinklyCloudBackendSessionRecord
    {
        Environment = "Sandbox",
        StoreCode = StoreCode,
        DeviceCode = DeviceCode,
        SessionId = "cancelled-session",
        TxnRef = "TXN-CANCELLED",
        Status = "Pending",
        IsActive = true,
        UpdatedAt = DateTimeOffset.UtcNow
    }, CancellationToken.None);

    var response = await service.RecoverAsync(
        StoreCode,
        DeviceCode,
        "cancelled-session",
        new LinklyCloudBackendRecoverRequest("Sandbox"),
        CancellationToken.None);

    Assert.Equal(LinklyCloudBackendStatusConstants.StatusCancelled, response.Status);
    Assert.False(response.TransactionSuccess);
    Assert.Equal("TC", response.ResponseCode);
    Assert.Equal("CANCELLED", response.ResponseText);
    Assert.False(response.IsActive);

    var persisted = await repository.GetSessionAsync(
        "Sandbox",
        StoreCode,
        DeviceCode,
        "cancelled-session",
        CancellationToken.None);
    Assert.Equal(LinklyCloudBackendStatusConstants.StatusCancelled, persisted?.Status);
    Assert.False(persisted?.IsActive);
}
```

- [ ] **Step 2: Run test and verify it fails**

Run:

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Api.Tests/Hbpos.Api.Tests.csproj --filter "FullyQualifiedName~LinklyCloudBackendAsyncServiceTests.RecoverAsync_maps_cancelled_linkly_result_to_cancelled_status" --artifacts-path artifacts/linkly-cancelled-red
```

Expected: FAIL because actual status is currently `Completed`.

---

### Task 3: Implement Backend Cancelled Mapping

**Files:**
- Modify: `apps/pos-wpf/src/Hbpos.Api/Services/LinklyCloudBackendAsyncService.cs`

- [ ] **Step 1: Run impact analysis before editing**

Run CodeGraph impact for the symbol that will change:

```text
impact target: ApplyCompletedPayload, direction upstream
```

Report direct callers and risk. If HIGH or CRITICAL, pause and warn before editing.

- [ ] **Step 2: Replace status assignment in `ApplyCompletedPayload`**

Replace the body of `ApplyCompletedPayload` with this shape:

```csharp
private static void ApplyCompletedPayload(
    LinklyCloudBackendSessionRecord session,
    string? payloadJson,
    bool updateTxnRef = true,
    bool updateOutcomeEvidence = true)
{
    session.Status = StatusCompleted;
    session.IsActive = false;
    session.RecoveryAction = null;
    if (string.IsNullOrWhiteSpace(payloadJson))
    {
        return;
    }

    using var document = JsonDocument.Parse(payloadJson);
    var response = ReadResponse(document.RootElement);
    var transactionSuccess = ReadBool(response, "Success");
    var responseCode = ReadString(response, "ResponseCode");
    var responseText = ReadString(response, "ResponseText");

    session.TransactionSuccess = transactionSuccess;
    // 明确的收银员/终端取消属于最终状态，不能继续作为 completed 或 active session 恢复。
    if (IsCancelledResponse(transactionSuccess, responseCode, responseText))
    {
        session.Status = StatusCancelled;
    }

    if (updateTxnRef)
    {
        // 官方 GET transaction 的 TxnRef 是恢复证据；notification 仍保留本地创建的保护引用。
        session.TxnRef = ReadString(response, "TxnRef") ?? session.TxnRef;
    }
    if (updateOutcomeEvidence)
    {
        session.ResponseCode = responseCode;
        session.ResponseText = responseText;
    }
}
```

- [ ] **Step 3: Add helper methods below `ApplyCompletedPayload`**

Add these methods immediately after `ApplyCompletedPayload`:

```csharp
private static bool IsCancelledResponse(bool? transactionSuccess, string? responseCode, string? responseText)
{
    // 只在 Linkly 明确返回失败且 code/text 指向取消时映射 Cancelled，避免普通 declined 被误分类。
    return transactionSuccess == false &&
        (IsCancelledResponseCode(responseCode) || IsCancelledResponseText(responseText));
}

private static bool IsCancelledResponseCode(string? responseCode)
{
    var normalized = NormalizeOptional(responseCode);
    return string.Equals(normalized, "TC", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(normalized, "CANCELLED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(normalized, "CANCELED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(normalized, "CANCEL", StringComparison.OrdinalIgnoreCase);
}

private static bool IsCancelledResponseText(string? responseText)
{
    var normalized = NormalizeOptional(responseText);
    return string.Equals(normalized, "CANCELLED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(normalized, "CANCELED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(normalized, "TRANSACTION CANCELLED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(normalized, "TRANSACTION CANCELED", StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Run the red test and verify it passes**

Run:

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Api.Tests/Hbpos.Api.Tests.csproj --filter "FullyQualifiedName~LinklyCloudBackendAsyncServiceTests.RecoverAsync_maps_cancelled_linkly_result_to_cancelled_status" --artifacts-path artifacts/linkly-cancelled-green
```

Expected: PASS.

---

### Task 4: Write Failing API Test For Recovery Filtering

**Files:**
- Modify: `apps/pos-wpf/tests/Hbpos.Api.Tests/LinklyCloudBackendAsyncServiceTests.cs`

- [ ] **Step 1: Add a red test for resumable filtering**

Add this test near existing active/resumable session tests:

```csharp
[Fact]
public async Task GetResumableSessionAsync_does_not_return_cancelled_session()
{
    var repository = new InMemoryLinklyCloudBackendAsyncRepository();
    var service = CreateService(repository);
    await repository.UpsertSessionAsync(new LinklyCloudBackendSessionRecord
    {
        Environment = "Sandbox",
        StoreCode = StoreCode,
        DeviceCode = DeviceCode,
        SessionId = "cancelled-final-session",
        TxnRef = "TXN-CANCELLED",
        Status = LinklyCloudBackendStatusConstants.StatusCancelled,
        IsActive = false,
        TransactionSuccess = false,
        ResponseCode = "TC",
        ResponseText = "CANCELLED",
        UpdatedAt = DateTimeOffset.UtcNow
    }, CancellationToken.None);

    var response = await service.GetResumableSessionAsync(
        StoreCode,
        DeviceCode,
        "Sandbox",
        CancellationToken.None);

    Assert.Null(response);
}
```

- [ ] **Step 2: Run test and verify it fails before filter update**

Run:

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Api.Tests/Hbpos.Api.Tests.csproj --filter "FullyQualifiedName~LinklyCloudBackendAsyncServiceTests.GetResumableSessionAsync_does_not_return_cancelled_session" --artifacts-path artifacts/linkly-cancelled-resumable-red
```

Expected before implementation: FAIL if repository recovery filter treats cancelled as non-final. If the current repository already excludes `IsActive=false`, keep the test as a regression guard and proceed to Task 5.

---

### Task 5: Treat Cancelled As Final In Backend Recovery Logic

**Files:**
- Modify: `apps/pos-wpf/src/Hbpos.Api/Services/LinklyCloudBackendAsyncService.cs`

- [ ] **Step 1: Run impact analysis before editing**

Run CodeGraph impact:

```text
impact target: IsFinalForClientRecovery, direction upstream
```

Report direct callers and risk. If HIGH or CRITICAL, pause and warn before editing.

- [ ] **Step 2: Update `IsFinalForClientRecovery`**

Replace it with:

```csharp
private static bool IsFinalForClientRecovery(LinklyCloudBackendSessionRecord session)
{
    return string.Equals(session.Status, StatusCompleted, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(session.Status, StatusCancelled, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(session.Status, StatusFailed, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(session.Status, StatusNotSubmitted, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 3: Run resumable test**

Run:

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Api.Tests/Hbpos.Api.Tests.csproj --filter "FullyQualifiedName~LinklyCloudBackendAsyncServiceTests.GetResumableSessionAsync_does_not_return_cancelled_session" --artifacts-path artifacts/linkly-cancelled-resumable-green
```

Expected: PASS.

---

### Task 6: Regression Tests And Build

**Files:**
- No new files.

- [ ] **Step 1: Run focused API Linkly tests**

Run:

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Api.Tests/Hbpos.Api.Tests.csproj --filter "FullyQualifiedName~LinklyCloudBackendAsyncServiceTests|FullyQualifiedName~LinklyControllerTests" --artifacts-path artifacts/linkly-cancelled-api-focused
```

Expected: all selected tests pass.

- [ ] **Step 2: Run focused client Linkly tests**

Run:

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Client.Tests/Hbpos.Client.Tests.csproj --filter "FullyQualifiedName~LinklyBackendTerminalClientTests" --artifacts-path artifacts/linkly-cancelled-client-focused
```

Expected: all selected tests pass; existing client handling of `Cancelled` remains green.

- [ ] **Step 3: Build API and WPF client**

Run:

```powershell
dotnet build apps/pos-wpf/src/Hbpos.Api/Hbpos.Api.csproj --artifacts-path artifacts/linkly-cancelled-api-build
dotnet build apps/pos-wpf/src/Hbpos.Client.Wpf/Hbpos.Client.Wpf.csproj --artifacts-path artifacts/linkly-cancelled-client-build
```

Expected: both builds succeed. Existing nullable warnings in shared projects are acceptable only if they predate this change and are unchanged.

- [ ] **Step 4: Check diff hygiene**

Run:

```powershell
git diff --check -- apps/pos-wpf/src/Hbpos.Contracts/Linkly/LinklyCloudBackendStatusConstants.cs apps/pos-wpf/src/Hbpos.Api/Services/LinklyCloudBackendAsyncService.cs apps/pos-wpf/tests/Hbpos.Api.Tests/LinklyCloudBackendAsyncServiceTests.cs
```

Expected: no whitespace errors. Line-ending warnings are acceptable if they match the repository's current Windows behavior.

---

## Self-Review

- Spec coverage: 方案覆盖“后端状态机缺失 Cancelled”以及“客户端已有处理逻辑但后端不产出”的缺口。
- Risk control: 取消映射要求 `Success=false` 且 response code/text 明确取消，不会把普通 declined/failed 全部改成 `Cancelled`。
- Recovery behavior: `Cancelled` 是最终状态，必须不再作为 active/resumable session 返回。
- Test coverage: 包含红绿测试、Linkly API 回归、客户端回归、API/WPF build。
- Scope: 不改 UI、不改支付页业务流程、不改数据库 schema。
