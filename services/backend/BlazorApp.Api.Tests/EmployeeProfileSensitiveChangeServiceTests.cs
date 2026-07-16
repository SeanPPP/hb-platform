using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using BlazorApp.Api.Data;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Models;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class EmployeeProfileSensitiveChangeServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public EmployeeProfileSensitiveChangeServiceTests()
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _connection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _db.CodeFirst.InitTables(
            typeof(User),
            typeof(Store),
            typeof(UserStore),
            typeof(Role),
            typeof(UserRole),
            typeof(EmployeeProfile),
            typeof(EmployeeProfileSensitiveChangeRequest),
            typeof(EmployeeImageUploadTicket)
        );
    }

    [Fact]
    public async Task UpsertSelfAsync_ReplacingPending_PreservesSupersededHistoryAndOnePending()
    {
        await SeedAsync();
        var service = CreateService("user-self", "self_user");

        var first = await service.UpsertSelfAsync(new EmployeeProfileSensitiveChangeUpsertDto
        {
            BankBsb = "111-222",
            BankAccountNumber = "12345678",
        });
        var second = await service.UpsertSelfAsync(new EmployeeProfileSensitiveChangeUpsertDto
        {
            BankBsb = "333-444",
            BankAccountNumber = "87654321",
        });

        Assert.True(first.Success);
        Assert.True(second.Success);
        var rows = await _db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .OrderBy(item => item.RequestId)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(EmployeeProfileSensitiveChangeStatus.Superseded, rows[0].Status);
        Assert.Equal(EmployeeProfileSensitiveChangeStatus.Pending, rows[1].Status);
        Assert.Equal("87654321", rows[1].BankAccountNumber);
        Assert.Equal(1, rows.Count(item => item.Status == EmployeeProfileSensitiveChangeStatus.Pending));
    }

    [Fact]
    public async Task ApproveAsync_与重新提交并发时_共用锁且不会留下过期版本的待审申请()
    {
        await AssertReviewWaitsForResubmitLockAsync(approve: true);
    }

    [Fact]
    public async Task RejectAsync_与重新提交并发时_共用锁且保留唯一最新待审申请()
    {
        await AssertReviewWaitsForResubmitLockAsync(approve: false);
    }

    [Fact]
    public async Task ApproveAsync_WhenRevisionChanged_ReturnsConflictWithoutOverwritingFormalData()
    {
        await SeedAsync();
        var service = CreateService("user-self", "self_user");
        var submitted = await service.UpsertSelfAsync(new EmployeeProfileSensitiveChangeUpsertDto
        {
            BankAccountNumber = "99990000",
        });
        var profile = await _db.Queryable<EmployeeProfile>().FirstAsync();
        profile.BankACC = "admin-new";
        profile.SensitiveRevision++;
        await _db.Updateable(profile).ExecuteCommandAsync();

        var result = await CreateService("admin-user", "admin").ApproveAsync(
            submitted.Data!.RequestId,
            new EmployeeProfileSensitiveReviewDto()
        );

        Assert.False(result.Success);
        Assert.Equal("EMPLOYEE_PROFILE_SENSITIVE_VERSION_CONFLICT", result.ErrorCode);
        Assert.Equal("admin-new", (await _db.Queryable<EmployeeProfile>().FirstAsync()).BankACC);
        Assert.Equal(
            EmployeeProfileSensitiveChangeStatus.Pending,
            (await _db.Queryable<EmployeeProfileSensitiveChangeRequest>().FirstAsync()).Status
        );
    }

    [Fact]
    public async Task ApproveAsync_WhenNormalizedSnapshotIsUnchanged_ApprovesWithoutIncreasingRevision()
    {
        await SeedAsync();
        var submitted = await CreateService("user-self", "self_user").UpsertSelfAsync(new()
        {
            BankAccountNumber = "  formal-old  ",
        });

        var approved = await CreateService("admin-user", "admin").ApproveAsync(
            submitted.Data!.RequestId,
            new EmployeeProfileSensitiveReviewDto()
        );

        Assert.True(approved.Success);
        var profile = await _db.Queryable<EmployeeProfile>().FirstAsync();
        Assert.Equal("formal-old", profile.BankACC);
        Assert.Equal(3, profile.SensitiveRevision);
        Assert.Equal(
            EmployeeProfileSensitiveChangeStatus.Approved,
            (await _db.Queryable<EmployeeProfileSensitiveChangeRequest>().FirstAsync()).Status
        );
    }

    [Fact]
    public async Task AdminListAsync_ReturnsOnlyChangedFieldMetadataButDetailReturnsAuthorizedFullValues()
    {
        await SeedAsync();
        var profile = await _db.Queryable<EmployeeProfile>().FirstAsync();
        profile.BankACC = "11116789";
        await _db.Updateable(profile).ExecuteCommandAsync();
        var service = CreateService("user-self", "self_user");
        var submitted = await service.UpsertSelfAsync(new EmployeeProfileSensitiveChangeUpsertDto
        {
            BankAccountNumber = "22226789",
            SuperannuationAccountNumber = "SUPER98765",
        });
        var selfDetail = await service.GetSelfAsync();

        var admin = CreateService("admin-user", "admin");
        var list = await admin.GetAdminListAsync(new EmployeeProfileSensitiveChangeQueryDto());
        var detail = await admin.GetAdminDetailAsync(submitted.Data!.RequestId);

        var listItems = Assert.IsAssignableFrom<IReadOnlyCollection<EmployeeProfileSensitiveChangeSummaryDto>>(
            list.Data!.Items
        );
        var listItem = listItems.Single();
        var listJson = JsonSerializer.Serialize(listItem);
        Assert.Contains("bankAccountNumber", listItem.ChangedFields);
        Assert.DoesNotContain("22226789", listJson);
        Assert.DoesNotContain("SUPER98765", listJson);
        Assert.DoesNotContain("BankAccountSummary", listJson);
        Assert.DoesNotContain("SuperannuationAccountSummary", listJson);
        Assert.Equal("22226789", detail.Data!.BankAccountNumber);
        Assert.Equal("SUPER98765", detail.Data.SuperannuationAccountNumber);
        Assert.Contains("bankAccountNumber", submitted.Data.ChangedFields);
        Assert.Contains("bankAccountNumber", detail.Data.ChangedFields);
        Assert.True(selfDetail.Success, selfDetail.Message);
        Assert.NotNull(selfDetail.Data);
        Assert.Contains("bankAccountNumber", selfDetail.Data.ChangedFields);
    }

    [Fact]
    public async Task ChangedFieldsSnapshot_AfterApprove_RemainsStableAcrossSelfAdminDetailAndList()
    {
        await SeedAsync();
        var self = CreateService("user-self", "self_user");
        var submitted = await self.UpsertSelfAsync(new()
        {
            BankAccountNumber = "approved-snapshot",
        });
        var approved = await CreateService("admin-user", "admin").ApproveAsync(
            submitted.Data!.RequestId,
            new EmployeeProfileSensitiveReviewDto { Reason = "已核验" }
        );
        Assert.True(approved.Success);

        var profile = await _db.Queryable<EmployeeProfile>().FirstAsync();
        profile.BankACC = "later-admin-value";
        profile.SensitiveRevision++;
        await _db.Updateable(profile).ExecuteCommandAsync();

        var selfDetail = await CreateService("user-self", "self_user").GetSelfAsync();
        var admin = CreateService("admin-user", "admin");
        var adminDetail = await admin.GetAdminDetailAsync(submitted.Data.RequestId);
        var list = await admin.GetAdminListAsync(new EmployeeProfileSensitiveChangeQueryDto());
        var listItem = list.Data!.Items!.Single(item => item.RequestId == submitted.Data.RequestId);

        Assert.NotNull(selfDetail.Data);
        Assert.Contains("bankAccountNumber", selfDetail.Data.ChangedFields);
        Assert.Contains("bankAccountNumber", adminDetail.Data!.ChangedFields);
        Assert.Contains("bankAccountNumber", listItem.ChangedFields);
    }

    [Fact]
    public async Task ChangedFieldsSnapshot_RejectedAndSuperseded_DoNotDriftAfterFormalChanges()
    {
        await SeedAsync();
        var self = CreateService("user-self", "self_user");
        var superseded = await self.UpsertSelfAsync(new()
        {
            BankAccountNumber = "superseded-account",
        });
        var rejected = await self.UpsertSelfAsync(new()
        {
            BankBsb = "999-000",
            BankAccountNumber = "formal-old",
        });
        await CreateService("admin-user", "admin").RejectAsync(
            rejected.Data!.RequestId,
            new EmployeeProfileSensitiveRejectDto { Reason = "无法核验" }
        );

        var profile = await _db.Queryable<EmployeeProfile>().FirstAsync();
        profile.BankACC = "superseded-account";
        profile.BankBSB = "999-000";
        profile.SensitiveRevision++;
        await _db.Updateable(profile).ExecuteCommandAsync();

        var admin = CreateService("admin-user", "admin");
        var supersededDetail = await admin.GetAdminDetailAsync(superseded.Data!.RequestId);
        var rejectedDetail = await admin.GetAdminDetailAsync(rejected.Data.RequestId);
        var rows = (await admin.GetAdminListAsync(new EmployeeProfileSensitiveChangeQueryDto()))
            .Data!.Items!.ToDictionary(item => item.RequestId);

        Assert.Equal(new[] { "bankAccountNumber" }, supersededDetail.Data!.ChangedFields);
        Assert.Equal(new[] { "bankBsb" }, rejectedDetail.Data!.ChangedFields);
        Assert.Equal(new[] { "bankAccountNumber" }, rows[superseded.Data.RequestId].ChangedFields);
        Assert.Equal(new[] { "bankBsb" }, rows[rejected.Data.RequestId].ChangedFields);
    }

    [Fact]
    public async Task ChangedFieldsSnapshot_MalformedOrUnknownValues_NeverLeaksUncontrolledContent()
    {
        await SeedAsync();
        var submitted = await CreateService("user-self", "self_user").UpsertSelfAsync(new()
        {
            BankAccountNumber = "pending-secret-account",
        });
        var row = await _db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .FirstAsync(item => item.RequestId == submitted.Data!.RequestId);
        var snapshotProperty = typeof(EmployeeProfileSensitiveChangeRequest)
            .GetProperty("ChangedFieldsJson");
        Assert.NotNull(snapshotProperty);

        snapshotProperty.SetValue(
            row,
            "[\"bankAccountNumber\",\"pending-secret-account\",\"identityPhotoUrl\"]"
        );
        await _db.Updateable(row).ExecuteCommandAsync();
        var controlled = await CreateService("admin-user", "admin")
            .GetAdminDetailAsync(row.RequestId);
        Assert.Equal(
            new[] { "bankAccountNumber", "identityPhotoUrl" },
            controlled.Data!.ChangedFields
        );
        Assert.DoesNotContain("pending-secret-account", controlled.Data.ChangedFields);

        snapshotProperty.SetValue(row, "{malformed-json");
        await _db.Updateable(row).ExecuteCommandAsync();
        var malformed = await CreateService("admin-user", "admin")
            .GetAdminDetailAsync(row.RequestId);
        Assert.Empty(malformed.Data!.ChangedFields);

        snapshotProperty.SetValue(row, null);
        await _db.Updateable(row).ExecuteCommandAsync();
        var legacyFallback = await CreateService("admin-user", "admin")
            .GetAdminDetailAsync(row.RequestId);
        Assert.Contains("bankAccountNumber", legacyFallback.Data!.ChangedFields);
    }

    [Fact]
    public async Task ApproveAndRejectAsync_ApplyOnlyApprovedSnapshotAndRequireRejectReason()
    {
        await SeedAsync();
        var submitted = await CreateService("user-self", "self_user").UpsertSelfAsync(new()
        {
            BankBsb = "555-666",
            BankAccountNumber = "approved-account",
            IdentityType = "passport",
            IdentityId = "P123",
        });

        var approved = await CreateService("admin-user", "admin").ApproveAsync(
            submitted.Data!.RequestId,
            new EmployeeProfileSensitiveReviewDto { Reason = "资料已核验" }
        );

        Assert.True(approved.Success);
        var profile = await _db.Queryable<EmployeeProfile>().FirstAsync();
        Assert.Equal("approved-account", profile.BankACC);
        Assert.Equal(4, profile.SensitiveRevision);

        var second = await CreateService("user-self", "self_user").UpsertSelfAsync(new()
        {
            BankAccountNumber = "must-not-apply",
        });
        var missingReason = await CreateService("admin-user", "admin").RejectAsync(
            second.Data!.RequestId,
            new EmployeeProfileSensitiveRejectDto { Reason = " " }
        );
        var rejected = await CreateService("admin-user", "admin").RejectAsync(
            second.Data.RequestId,
            new EmployeeProfileSensitiveRejectDto { Reason = "无法核验" }
        );

        Assert.False(missingReason.Success);
        Assert.Equal("VALIDATION_ERROR", missingReason.ErrorCode);
        Assert.True(rejected.Success);
        Assert.Equal("无法核验", rejected.Data!.ReviewReason);
        Assert.Contains("bankAccountNumber", rejected.Data.ChangedFields);
        Assert.Equal("approved-account", (await _db.Queryable<EmployeeProfile>().FirstAsync()).BankACC);
    }

    [Fact]
    public async Task LegacySelfPut_CannotWriteSensitiveFields_AndAdminDirectChangeSupersedesPending()
    {
        await SeedAsync();
        var seededProfile = await _db.Queryable<EmployeeProfile>().FirstAsync();
        seededProfile.IdentityPhotoObjectKey = "employee-profiles/user-self/identity/formal-preserved.png";
        await _db.Updateable(seededProfile).ExecuteCommandAsync();
        var storage = new FakeStorage();
        var selfSensitive = CreateService("user-self", "self_user", storage);
        var selfProfile = CreateProfileService("user-self", "self_user", selfSensitive);

        var selfResult = await selfProfile.UpsertSelfAsync(new EmployeeProfileUpsertDto
        {
            Address = "new address",
            BankAccountNumber = "pending-from-old-app",
            IdentityId = "NEW-ID",
        });

        Assert.True(selfResult.Success);
        var afterSelf = await _db.Queryable<EmployeeProfile>().FirstAsync();
        Assert.Equal("formal-old", afterSelf.BankACC);
        Assert.Equal("new address", afterSelf.Address);
        var pending = await _db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .FirstAsync(item => item.Status == EmployeeProfileSensitiveChangeStatus.Pending);
        Assert.Equal("pending-from-old-app", pending.BankAccountNumber);
        Assert.Equal("employee-profiles/user-self/identity/formal-preserved.png", pending.IdentityPhotoObjectKey);

        var adminSensitive = CreateService("admin-user", "admin", storage);
        var adminResult = await CreateProfileService("admin-user", "admin", adminSensitive)
            .UpsertAdminAsync("user-self", new EmployeeProfileUpsertDto
            {
                Address = "admin address",
                BankAccountNumber = "admin-direct",
                IdentityId = "ADMIN-ID",
                ConfirmSupersedePendingSensitiveChangeRequest = true,
            });

        Assert.True(adminResult.Success);
        var afterAdmin = await _db.Queryable<EmployeeProfile>().FirstAsync();
        Assert.Equal("admin-direct", afterAdmin.BankACC);
        Assert.Equal(4, afterAdmin.SensitiveRevision);
        Assert.Equal(EmployeeProfileSensitiveChangeStatus.Superseded,
            (await _db.Queryable<EmployeeProfileSensitiveChangeRequest>().FirstAsync()).Status);
        Assert.DoesNotContain("employee-profiles/user-self/identity/formal-preserved.png", storage.DeletedKeys);
    }

    [Fact]
    public async Task LegacySelfPut_NoOpSensitiveSnapshot_PreservesRealPendingButSameSuffixDifferenceReplacesIt()
    {
        await SeedAsync();
        var profile = await _db.Queryable<EmployeeProfile>().FirstAsync();
        profile.BankACC = "11113333";
        await _db.Updateable(profile).ExecuteCommandAsync();
        var sensitive = CreateService("user-self", "self_user");
        var realPending = await sensitive.UpsertSelfAsync(new()
        {
            BankAccountNumber = "22223333",
        });
        var profileService = CreateProfileService("user-self", "self_user", sensitive);

        var noOp = await profileService.UpsertSelfAsync(new EmployeeProfileUpsertDto
        {
            Address = "legacy saved address",
            BankAccountNumber = " 11113333 ",
        });

        Assert.True(noOp.Success);
        var afterNoOp = await _db.Queryable<EmployeeProfileSensitiveChangeRequest>().ToListAsync();
        Assert.Single(afterNoOp);
        Assert.Equal(realPending.Data!.RequestId, afterNoOp.Single().RequestId);
        Assert.Equal("22223333", afterNoOp.Single().BankAccountNumber);
        Assert.Equal(EmployeeProfileSensitiveChangeStatus.Pending, afterNoOp.Single().Status);

        var changed = await profileService.UpsertSelfAsync(new EmployeeProfileUpsertDto
        {
            BankAccountNumber = "77773333",
        });
        Assert.True(changed.Success);
        var afterChanged = await _db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .OrderBy(item => item.RequestId)
            .ToListAsync();
        Assert.Equal(2, afterChanged.Count);
        Assert.Equal(EmployeeProfileSensitiveChangeStatus.Superseded, afterChanged[0].Status);
        Assert.Equal("77773333", afterChanged[1].BankAccountNumber);
        Assert.Equal(EmployeeProfileSensitiveChangeStatus.Pending, afterChanged[1].Status);
    }

    [Fact]
    public async Task UpsertSelfSensitive_WhenExpectedRevisionIsStale_WritesNothingAndCurrentRevisionSucceeds()
    {
        await SeedAsync();
        var profile = await _db.Queryable<EmployeeProfile>().FirstAsync();
        profile.BankACC = "admin-v4";
        profile.SensitiveRevision = 4;
        await _db.Updateable(profile).ExecuteCommandAsync();
        var service = CreateService("user-self", "self_user");

        var stale = await service.UpsertSelfAsync(new()
        {
            BankAccountNumber = "employee-from-v3",
            ExpectedSensitiveRevision = 3,
        });
        Assert.False(stale.Success);
        Assert.Equal(EmployeeProfileSensitiveChangeService.VersionConflictCode, stale.ErrorCode);
        Assert.Equal(0, await _db.Queryable<EmployeeProfileSensitiveChangeRequest>().CountAsync());
        Assert.Equal("admin-v4", (await _db.Queryable<EmployeeProfile>().FirstAsync()).BankACC);

        var current = await service.UpsertSelfAsync(new()
        {
            BankAccountNumber = "employee-from-v4",
            ExpectedSensitiveRevision = 4,
        });
        Assert.True(current.Success);
        Assert.Equal(4, current.Data!.BaseSensitiveRevision);
    }

    [Fact]
    public async Task IdentityPhoto_IsPendingUntilApprove_AndReplaceRejectCleanPendingObjects()
    {
        await SeedAsync();
        var profile = await _db.Queryable<EmployeeProfile>().FirstAsync();
        profile.IdentityPhotoObjectKey = "employee-profiles/user-self/identity/formal.png";
        await _db.Updateable(profile).ExecuteCommandAsync();
        var storage = new FakeStorage();
        var sensitive = CreateService("user-self", "self_user", storage);

        var first = await sensitive.ReplacePendingIdentityPhotoAsync(
            "employee-profiles/user-self/identity/pending-one.png"
        );
        var second = await sensitive.ReplacePendingIdentityPhotoAsync(
            "employee-profiles/user-self/identity/pending-two.png"
        );

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.False((await _db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .FirstAsync(item => item.RequestId == second.Data!.RequestId)).RemoveIdentityPhoto);
        Assert.Equal("employee-profiles/user-self/identity/formal.png",
            (await _db.Queryable<EmployeeProfile>().FirstAsync()).IdentityPhotoObjectKey);
        Assert.Contains("employee-profiles/user-self/identity/pending-one.png", storage.DeletedKeys);

        var rejected = await CreateService("admin-user", "admin", storage).RejectAsync(
            second.Data!.RequestId,
            new EmployeeProfileSensitiveRejectDto { Reason = "照片不清晰" }
        );
        Assert.True(rejected.Success);
        Assert.Contains("employee-profiles/user-self/identity/pending-two.png", storage.DeletedKeys);
        Assert.Equal("employee-profiles/user-self/identity/formal.png",
            (await _db.Queryable<EmployeeProfile>().FirstAsync()).IdentityPhotoObjectKey);

        var third = await CreateService("user-self", "self_user", storage).ReplacePendingIdentityPhotoAsync(
            "employee-profiles/user-self/identity/approved.png"
        );
        Assert.Equal("employee-profiles/user-self/identity/approved.png",
            (await _db.Queryable<EmployeeProfileSensitiveChangeRequest>()
                .FirstAsync(item => item.RequestId == third.Data!.RequestId)).IdentityPhotoObjectKey);
        var approved = await CreateService("admin-user", "admin", storage).ApproveAsync(
            third.Data!.RequestId,
            new EmployeeProfileSensitiveReviewDto()
        );
        Assert.True(approved.Success);
        Assert.NotNull(approved.Data!.CurrentSnapshot);
        Assert.Contains(
            "/approved.png",
            approved.Data.CurrentSnapshot!.IdentityPhotoUrl
        );
        Assert.DoesNotContain(
            "/formal.png",
            approved.Data.CurrentSnapshot.IdentityPhotoUrl
        );
        Assert.Equal("employee-profiles/user-self/identity/approved.png",
            (await _db.Queryable<EmployeeProfile>().FirstAsync(item => item.UserGUID == "user-self"))
                .IdentityPhotoObjectKey);
        Assert.Contains("employee-profiles/user-self/identity/formal.png", storage.DeletedKeys);
    }

    [Fact]
    public async Task DeletePendingIdentityPhotoAsync_LegacyUrlOnly_固化删除意图并在批准后清空正式照片()
    {
        await SeedAsync();
        var profile = await _db.Queryable<EmployeeProfile>().FirstAsync();
        profile.IdentityPhotoObjectKey = null;
        profile.IdentityPhotoUrl = "https://legacy.example/identity.jpg";
        await _db.Updateable(profile).ExecuteCommandAsync();

        var submitted = await CreateService("user-self", "self_user")
            .DeletePendingIdentityPhotoAsync();

        Assert.True(submitted.Success);
        Assert.False(submitted.Data!.HasIdentityPhoto);
        Assert.Contains("identityPhotoUrl", submitted.Data.ChangedFields);
        var deleteRow = await _db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .FirstAsync(item => item.RequestId == submitted.Data.RequestId);
        Assert.True(deleteRow.RemoveIdentityPhoto);

        var resubmitted = await CreateService("user-self", "self_user").UpsertSelfAsync(new()
        {
            BankBsb = "456-789",
        });
        var resubmittedRow = await _db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .FirstAsync(item => item.RequestId == resubmitted.Data!.RequestId);
        Assert.True(resubmittedRow.RemoveIdentityPhoto);
        Assert.Contains("identityPhotoUrl", resubmitted.Data!.ChangedFields);

        var approved = await CreateService("admin-user", "admin").ApproveAsync(
            resubmitted.Data.RequestId,
            new EmployeeProfileSensitiveReviewDto()
        );

        Assert.True(approved.Success);
        var after = await _db.Queryable<EmployeeProfile>().FirstAsync();
        Assert.Null(after.IdentityPhotoObjectKey);
        Assert.Null(after.IdentityPhotoUrl);
        Assert.Equal(4, after.SensitiveRevision);
    }

    [Fact]
    public async Task UpsertSelfAsync_LegacyUrlOnly普通字段申请_不得删除正式证件照()
    {
        await SeedAsync();
        var profile = await _db.Queryable<EmployeeProfile>().FirstAsync();
        profile.IdentityPhotoObjectKey = null;
        profile.IdentityPhotoUrl = "https://legacy.example/identity.jpg";
        await _db.Updateable(profile).ExecuteCommandAsync();

        var submitted = await CreateService("user-self", "self_user").UpsertSelfAsync(new()
        {
            BankAccountNumber = "updated-account",
        });
        var row = await _db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .FirstAsync(item => item.RequestId == submitted.Data!.RequestId);
        Assert.False(row.RemoveIdentityPhoto);
        Assert.DoesNotContain("identityPhotoUrl", submitted.Data!.ChangedFields);

        Assert.True((await CreateService("admin-user", "admin").ApproveAsync(
            row.RequestId,
            new EmployeeProfileSensitiveReviewDto()
        )).Success);
        Assert.Equal(
            "https://legacy.example/identity.jpg",
            (await _db.Queryable<EmployeeProfile>().FirstAsync()).IdentityPhotoUrl
        );
    }

    [Fact]
    public async Task CompleteIdentityUpload_AssociatesTicketAndPendingRequestWithoutChangingFormalPhoto()
    {
        await SeedAsync();
        var profile = await _db.Queryable<EmployeeProfile>().FirstAsync();
        profile.IdentityPhotoObjectKey = "employee-profiles/user-self/identity/formal.png";
        await _db.Updateable(profile).ExecuteCommandAsync();
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="
        );
        const string pendingKey = "pending/employee-profiles/user-self/identity/upload.png";
        await _db.Insertable(new EmployeeImageUploadTicket
        {
            PendingObjectKey = pendingKey,
            FinalObjectKey = pendingKey["pending/".Length..],
            UserGUID = "user-self",
            Kind = "identity",
            ContentType = "image/png",
            FileSize = png.Length,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            Status = EmployeeImageUploadStatus.Pending,
        }).ExecuteCommandAsync();
        var storage = new FakeStorage { Bytes = png };
        var sensitive = CreateService("user-self", "self_user", storage);
        var media = CreateMediaService("user-self", "self_user", storage, sensitive);

        var result = await media.CompleteAsync(new EmployeeImageCompleteRequest
        {
            Kind = "identity",
            ObjectKey = pendingKey,
        });

        Assert.True(result.Success);
        var request = await _db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .FirstAsync(item => item.Status == EmployeeProfileSensitiveChangeStatus.Pending);
        var ticket = await _db.Queryable<EmployeeImageUploadTicket>()
            .FirstAsync(item => item.PendingObjectKey == pendingKey);
        Assert.Equal("employee-profiles/user-self/identity/upload.png", request.IdentityPhotoObjectKey);
        Assert.Equal(request.RequestId, ticket.SensitiveChangeRequestId);
        Assert.Equal(EmployeeImageUploadStatus.Completed, ticket.Status);
        Assert.Equal("employee-profiles/user-self/identity/formal.png",
            (await _db.Queryable<EmployeeProfile>().FirstAsync()).IdentityPhotoObjectKey);
    }

    [Fact]
    public async Task FieldOnlyReplacement_MigratesIdentityTicketSoFailedRejectCleanupCanRetry()
    {
        await SeedAsync();
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="
        );
        const string pendingKey = "pending/employee-profiles/user-self/identity/migrated.png";
        const string finalKey = "employee-profiles/user-self/identity/migrated.png";
        await _db.Insertable(new EmployeeImageUploadTicket
        {
            PendingObjectKey = pendingKey,
            FinalObjectKey = finalKey,
            UserGUID = "user-self",
            Kind = "identity",
            ContentType = "image/png",
            FileSize = png.Length,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            Status = EmployeeImageUploadStatus.Pending,
        }).ExecuteCommandAsync();
        var storage = new FakeStorage { Bytes = png };
        var sensitive = CreateService("user-self", "self_user", storage);
        var media = CreateMediaService("user-self", "self_user", storage, sensitive);

        Assert.True((await media.CompleteAsync(new EmployeeImageCompleteRequest
        {
            Kind = "identity",
            ObjectKey = pendingKey,
        })).Success);
        var firstRequest = await _db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .FirstAsync(item => item.Status == EmployeeProfileSensitiveChangeStatus.Pending);
        var replacement = await sensitive.UpsertSelfAsync(new()
        {
            BankAccountNumber = "field-only-change",
        });
        var ticket = await _db.Queryable<EmployeeImageUploadTicket>().FirstAsync();
        Assert.NotEqual(firstRequest.RequestId, replacement.Data!.RequestId);
        Assert.Equal(replacement.Data.RequestId, ticket.SensitiveChangeRequestId);

        storage.DeleteSucceeds = false;
        var rejected = await CreateService("admin-user", "admin", storage).RejectAsync(
            replacement.Data.RequestId,
            new EmployeeProfileSensitiveRejectDto { Reason = "资料无法核验" }
        );
        Assert.True(rejected.Success);
        ticket = await _db.Queryable<EmployeeImageUploadTicket>().FirstAsync();
        Assert.Equal(EmployeeImageObjectCleanupStatus.Pending, ticket.PreviousObjectCleanupStatus);

        storage.DeleteSucceeds = true;
        await EmployeeImageUploadCleanup.CleanupExpiredAsync(
            _db,
            storage,
            NullLogger.Instance,
            DateTime.UtcNow,
            CancellationToken.None
        );

        ticket = await _db.Queryable<EmployeeImageUploadTicket>().FirstAsync();
        Assert.Equal(EmployeeImageObjectCleanupStatus.Completed, ticket.PreviousObjectCleanupStatus);
        Assert.Equal(2, storage.DeletedKeys.Count(key => key == finalKey));
    }

    [Fact]
    public async Task ExpiredCleanup_DoesNotDeleteIdentityObjectReferencedByPendingApproval()
    {
        await SeedAsync();
        const string finalKey = "employee-profiles/user-self/identity/recover.png";
        var submitted = await CreateService("user-self", "self_user")
            .ReplacePendingIdentityPhotoAsync(finalKey);
        var now = DateTime.UtcNow;
        await _db.Insertable(new EmployeeImageUploadTicket
        {
            PendingObjectKey = "pending/" + finalKey,
            FinalObjectKey = finalKey,
            UserGUID = "user-self",
            Kind = "identity",
            ContentType = "image/png",
            FileSize = 10,
            CreatedAt = now.AddHours(-1),
            ExpiresAt = now.AddMinutes(-30),
            Status = EmployeeImageUploadStatus.Promoted,
            StageChangedAt = now.AddMinutes(-30),
            SensitiveChangeRequestId = submitted.Data!.RequestId,
        }).ExecuteCommandAsync();
        var storage = new FakeStorage();

        await EmployeeImageUploadCleanup.CleanupExpiredAsync(
            _db,
            storage,
            NullLogger.Instance,
            now,
            CancellationToken.None
        );

        Assert.DoesNotContain(finalKey, storage.DeletedKeys);
        Assert.Equal(EmployeeImageUploadStatus.Completed,
            (await _db.Queryable<EmployeeImageUploadTicket>().FirstAsync()).Status);
    }

    [Fact]
    public async Task PromotedRecovery_BackfillsRequestIdSoRejectCleanupCanRetry()
    {
        await SeedAsync();
        const string finalKey = "employee-profiles/user-self/identity/recovered-link.png";
        var submitted = await CreateService("user-self", "self_user")
            .ReplacePendingIdentityPhotoAsync(finalKey);
        var now = DateTime.UtcNow;
        await _db.Insertable(new EmployeeImageUploadTicket
        {
            PendingObjectKey = "pending/" + finalKey,
            FinalObjectKey = finalKey,
            UserGUID = "user-self",
            Kind = "identity",
            ContentType = "image/png",
            FileSize = 10,
            CreatedAt = now.AddHours(-1),
            ExpiresAt = now.AddMinutes(-30),
            Status = EmployeeImageUploadStatus.Promoted,
            StageChangedAt = now.AddMinutes(-30),
        }).ExecuteCommandAsync();
        var storage = new FakeStorage();

        await EmployeeImageUploadCleanup.CleanupExpiredAsync(
            _db,
            storage,
            NullLogger.Instance,
            now,
            CancellationToken.None
        );
        var recovered = await _db.Queryable<EmployeeImageUploadTicket>().FirstAsync();
        Assert.Equal(EmployeeImageUploadStatus.Completed, recovered.Status);
        Assert.Equal(submitted.Data!.RequestId, recovered.SensitiveChangeRequestId);

        storage.DeleteSucceeds = false;
        Assert.True((await CreateService("admin-user", "admin", storage).RejectAsync(
            submitted.Data.RequestId,
            new EmployeeProfileSensitiveRejectDto { Reason = "照片无效" }
        )).Success);
        storage.DeleteSucceeds = true;
        await EmployeeImageUploadCleanup.CleanupExpiredAsync(
            _db,
            storage,
            NullLogger.Instance,
            now.AddMinutes(1),
            CancellationToken.None
        );

        recovered = await _db.Queryable<EmployeeImageUploadTicket>().FirstAsync();
        Assert.Equal(EmployeeImageObjectCleanupStatus.Completed, recovered.PreviousObjectCleanupStatus);
        Assert.Equal(2, storage.DeletedKeys.Count(key => key == finalKey));
    }

    [Fact]
    public async Task ApproveIdentityPhotoDeletion_WithoutUploadTicket_PersistsFailedCleanupForRetry()
    {
        await SeedAsync();
        const string formalKey = "employee-profiles/user-self/identity/formal-delete.png";
        var profile = await _db.Queryable<EmployeeProfile>().FirstAsync();
        profile.IdentityPhotoObjectKey = formalKey;
        await _db.Updateable(profile).ExecuteCommandAsync();
        var storage = new FakeStorage { DeleteSucceeds = false };
        var submitted = await CreateService("user-self", "self_user", storage)
            .DeletePendingIdentityPhotoAsync();

        Assert.True((await CreateService("admin-user", "admin", storage).ApproveAsync(
            submitted.Data!.RequestId,
            new EmployeeProfileSensitiveReviewDto()
        )).Success);
        var cleanupTicket = await _db.Queryable<EmployeeImageUploadTicket>().FirstAsync();
        Assert.NotNull(cleanupTicket);
        Assert.Equal(formalKey, cleanupTicket!.PreviousObjectKey);
        Assert.Equal(EmployeeImageObjectCleanupStatus.Pending, cleanupTicket.PreviousObjectCleanupStatus);

        storage.DeleteSucceeds = true;
        await EmployeeImageUploadCleanup.CleanupExpiredAsync(
            _db,
            storage,
            NullLogger.Instance,
            DateTime.UtcNow.AddMinutes(1),
            CancellationToken.None
        );

        cleanupTicket = await _db.Queryable<EmployeeImageUploadTicket>().FirstAsync();
        Assert.Equal(EmployeeImageObjectCleanupStatus.Completed, cleanupTicket.PreviousObjectCleanupStatus);
        Assert.Equal(2, storage.DeletedKeys.Count(key => key == formalKey));
    }

    [Fact]
    public async Task ReviewScope_店长只可审核管理分店普通员工且仓库经理自审高权限目标全部拒绝()
    {
        await SeedReviewScopeAsync();
        var storeManager = CreateService(
            "manager-a",
            "manager_a",
            roles: ["StoreManager"],
            storeGuids: ["store-a"]
        );

        var list = await storeManager.GetReviewListAsync(new()
        {
            Page = 1,
            PageSize = 1,
            Status = "Pending",
        });

        Assert.True(list.Success);
        Assert.Equal(1, list.Data!.Total);
        var summary = Assert.Single(list.Data.Items!);
        Assert.Equal("employee-a", summary.UserGuid);
        Assert.Equal(["A"], summary.StoreCodes);
        Assert.Equal(["门店 A"], summary.StoreNames);
        var serialized = JsonSerializer.Serialize(summary);
        Assert.DoesNotContain("pending-a-account", serialized);
        Assert.DoesNotContain("BankAccountNumber", serialized);

        foreach (var forbiddenUser in new[] { "employee-b", "manager-a", "privileged-manager" })
        {
            var request = await _db.Queryable<EmployeeProfileSensitiveChangeRequest>()
                .FirstAsync(item => item.UserGUID == forbiddenUser);
            var detail = await storeManager.GetReviewDetailAsync(request.RequestId);
            Assert.False(detail.Success);
            Assert.Equal("REQUEST_NOT_FOUND", detail.ErrorCode);
        }

        var storeBRequest = await _db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .FirstAsync(item => item.UserGUID == "employee-b");
        var beforeProfile = await _db.Queryable<EmployeeProfile>()
            .FirstAsync(item => item.UserGUID == "employee-b");
        var beforeRevision = beforeProfile.SensitiveRevision;
        var beforeAccount = beforeProfile.BankACC;
        var forbiddenApprove = await storeManager.ApproveAsync(
            storeBRequest.RequestId,
            new EmployeeProfileSensitiveReviewDto()
        );
        var forbiddenReject = await storeManager.RejectAsync(
            storeBRequest.RequestId,
            new EmployeeProfileSensitiveRejectDto { Reason = "无权审核" }
        );
        Assert.Equal("REQUEST_NOT_FOUND", forbiddenApprove.ErrorCode);
        Assert.Equal("REQUEST_NOT_FOUND", forbiddenReject.ErrorCode);
        Assert.Equal(
            EmployeeProfileSensitiveChangeStatus.Pending,
            (await _db.Queryable<EmployeeProfileSensitiveChangeRequest>()
                .FirstAsync(item => item.RequestId == storeBRequest.RequestId)).Status
        );
        var afterProfile = await _db.Queryable<EmployeeProfile>()
            .FirstAsync(item => item.UserGUID == "employee-b");
        Assert.Equal(beforeRevision, afterProfile.SensitiveRevision);
        Assert.Equal(beforeAccount, afterProfile.BankACC);

        var storeARequest = await _db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .FirstAsync(item => item.UserGUID == "employee-a");
        Assert.True((await storeManager.ApproveAsync(
            storeARequest.RequestId,
            new EmployeeProfileSensitiveReviewDto { Reason = "已核验" }
        )).Success);

        var warehouseManager = CreateService(
            "warehouse-manager",
            "warehouse_manager",
            roles: ["WarehouseManager"],
            storeGuids: ["store-a"],
            scopeIsAdmin: true
        );
        var warehouseList = await warehouseManager.GetReviewListAsync(new());
        Assert.False(warehouseList.Success);
        Assert.Equal(EmployeeProfileSensitiveChangeService.ReviewScopeForbiddenCode, warehouseList.ErrorCode);
    }

    [Fact]
    public async Task ReviewDetail_管理员可查看全部且CurrentSnapshot返回当前正式敏感资料和安全照片地址()
    {
        await SeedReviewScopeAsync();
        var profile = await _db.Queryable<EmployeeProfile>()
            .FirstAsync(item => item.UserGUID == "employee-b");
        profile.BankBSB = "123-456";
        profile.BankACC = "formal-current-account";
        profile.SuperannuationCompanyName = "Current Super";
        profile.SuperannuationCompanyCode = "CURRENT-CODE";
        profile.SuperannuationAccount = "CURRENT-SUPER-ACCOUNT";
        profile.IdentityType = "passport";
        profile.IdentityId = "CURRENT-ID";
        profile.IdentityPhotoObjectKey = "employee-profiles/employee-b/identity/current.png";
        await _db.Updateable(profile).ExecuteCommandAsync();
        var request = await _db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .FirstAsync(item => item.UserGUID == "employee-b");
        var admin = CreateService("admin-user", "admin", new FakeStorage());

        var list = await admin.GetReviewListAsync(new() { Status = "Pending" });
        var detail = await admin.GetReviewDetailAsync(request.RequestId);

        Assert.True(list.Success);
        Assert.Equal(4, list.Data!.Total);
        Assert.True(detail.Success);
        var snapshot = Assert.IsType<EmployeeProfileSensitiveSnapshotDto>(detail.Data!.CurrentSnapshot);
        Assert.Equal("123-456", snapshot.BankBsb);
        Assert.Equal("formal-current-account", snapshot.BankAccountNumber);
        Assert.Equal("Current Super", snapshot.SuperannuationCompanyName);
        Assert.Equal("CURRENT-CODE", snapshot.SuperannuationCompanyCode);
        Assert.Equal("CURRENT-SUPER-ACCOUNT", snapshot.SuperannuationAccountNumber);
        Assert.Equal("passport", snapshot.IdentityType);
        Assert.Equal("CURRENT-ID", snapshot.IdentityId);
        Assert.True(snapshot.HasIdentityPhoto);
        Assert.NotNull(snapshot.IdentityPhotoUrl);
        Assert.StartsWith("https://", snapshot.IdentityPhotoUrl);
        Assert.Contains("q-sign-algorithm", snapshot.IdentityPhotoUrl);

        profile.IdentityPhotoObjectKey = null;
        profile.IdentityPhotoUrl = "https://legacy-untrusted.example/identity.jpg";
        await _db.Updateable(profile).ExecuteCommandAsync();
        var legacyDetail = await admin.GetReviewDetailAsync(request.RequestId);
        Assert.True(legacyDetail.Data!.CurrentSnapshot!.HasIdentityPhoto);
        Assert.Null(legacyDetail.Data.CurrentSnapshot.IdentityPhotoUrl);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("管理员")]
    [InlineData("SuperAdmin")]
    [InlineData("超级管理员")]
    [InlineData("WarehouseManager")]
    [InlineData("仓库经理")]
    [InlineData("StoreManager")]
    [InlineData("店长")]
    [InlineData("经理")]
    public async Task ReviewScope_店长查询所有受保护角色目标统一返回不存在(string protectedRole)
    {
        await SeedReviewScopeAsync();
        var role = await _db.Queryable<Role>().FirstAsync();
        role.RoleName = protectedRole;
        await _db.Updateable(role).ExecuteCommandAsync();
        var manager = CreateService(
            "manager-a",
            "manager_a",
            roles: ["StoreManager"],
            storeGuids: ["store-a"]
        );
        var request = await _db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .FirstAsync(item => item.UserGUID == "privileged-manager");

        var detail = await manager.GetReviewDetailAsync(request.RequestId);

        Assert.False(detail.Success);
        Assert.Equal("REQUEST_NOT_FOUND", detail.ErrorCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReviewMutation_等待媒体锁期间审核范围被撤销_锁内重取范围且数据库不变(bool approve)
    {
        await SeedReviewScopeAsync();
        var request = await _db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .FirstAsync(item => item.UserGUID == "employee-a");
        using var reviewDb = CreateAdditionalDb();
        var requestRead = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        reviewDb.Aop.OnLogExecuted = (sql, _) =>
        {
            if (IsSensitiveRequestSelect(sql))
            {
                requestRead.TrySetResult();
            }
        };
        var currentScope = new CurrentUserManageableStoreScope
        {
            IsAllowed = true,
            IsAuthenticated = true,
            UserGuid = "manager-a",
            ActorLabel = "manager_a",
            StoreGuids = ["store-a"],
        };
        var scope = new Mock<ICurrentUserManageableStoreScopeService>();
        scope.Setup(service => service.GetScopeAsync()).ReturnsAsync(() => currentScope);
        var reviewer = CreateService(
            "manager-a",
            "manager_a",
            db: reviewDb,
            roles: ["StoreManager"],
            scopeService: scope.Object
        );
        var heldLock = await EmployeeProfileMediaLock.AcquireAsync(
            reviewDb,
            request.UserGUID,
            "sensitive-change"
        );
        var reviewTask = Task.Run(() => approve
            ? reviewer.ApproveAsync(request.RequestId, new EmployeeProfileSensitiveReviewDto())
            : reviewer.RejectAsync(
                request.RequestId,
                new EmployeeProfileSensitiveRejectDto { Reason = "资料无法核验" }
            ));
        await requestRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        currentScope = new CurrentUserManageableStoreScope
        {
            IsAuthenticated = true,
            UserGuid = "manager-a",
            ActorLabel = "manager_a",
            Message = "管理分店已撤销",
        };
        await heldLock.DisposeAsync();

        var result = await reviewTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.Success);
        Assert.Equal(EmployeeProfileSensitiveChangeService.ReviewScopeForbiddenCode, result.ErrorCode);
        Assert.Equal(
            EmployeeProfileSensitiveChangeStatus.Pending,
            (await _db.Queryable<EmployeeProfileSensitiveChangeRequest>()
                .FirstAsync(item => item.RequestId == request.RequestId)).Status
        );
        var profile = await _db.Queryable<EmployeeProfile>()
            .FirstAsync(item => item.UserGUID == request.UserGUID);
        Assert.Equal(2, profile.SensitiveRevision);
        Assert.Equal("formal-employee-a", profile.BankACC);
    }

    [Fact]
    public async Task ReviewController_MapsConflictsAndMissingRequests_AndAdminEndpointsRequireRoleAndEditPolicy()
    {
        await SeedAsync();
        var submitted = await CreateService("user-self", "self_user").UpsertSelfAsync(new()
        {
            BankAccountNumber = "stale",
        });
        var profile = await _db.Queryable<EmployeeProfile>().FirstAsync();
        profile.SensitiveRevision++;
        await _db.Updateable(profile).ExecuteCommandAsync();
        var storage = new FakeStorage();
        var sensitive = CreateService("admin-user", "admin", storage, roles: ["Admin"]);
        var current = CreateCurrentUser("admin-user", "admin");
        var context = CreateContext(_db);
        var profileService = new Mock<IEmployeeProfileService>();
        profileService
            .Setup(service => service.UpsertAdminAsync("user-self", It.IsAny<EmployeeProfileUpsertDto>()))
            .ReturnsAsync((string _, EmployeeProfileUpsertDto dto) =>
                ApiResponse<EmployeeProfileDetailDto>.Error(
                    "冲突",
                    dto.ExpectedSensitiveRevision == 99
                        ? EmployeeProfileSensitiveChangeService.VersionConflictCode
                        : EmployeeProfileService.PendingChangeConfirmationRequiredCode
                ));
        var controller = new EmployeeProfilesController(
            profileService.Object,
            NullLogger<EmployeeProfilesController>.Instance,
            new EmployeeProfileMediaService(
                context,
                current,
                storage,
                NullLogger<EmployeeProfileMediaService>.Instance,
                sensitive
            ),
            new EmployeeCashierBarcodeService(context, current),
            sensitive
        );
        var action = await controller.ApproveReviewSensitiveChangeRequest(
            submitted.Data!.RequestId,
            new EmployeeProfileSensitiveReviewDto()
        );

        Assert.IsType<ConflictObjectResult>(action);
        Assert.IsType<ConflictObjectResult>(await controller.UpsertAdmin(
            "user-self",
            new EmployeeProfileUpsertDto { BankAccountNumber = "admin-new" }
        ));
        Assert.IsType<ConflictObjectResult>(await controller.UpsertAdmin(
            "user-self",
            new EmployeeProfileUpsertDto { BankAccountNumber = "admin-new", ExpectedSensitiveRevision = 99 }
        ));
        profile.SensitiveRevision = submitted.Data.BaseSensitiveRevision;
        await _db.Updateable(profile).ExecuteCommandAsync();
        Assert.IsType<OkObjectResult>(await controller.ApproveSensitiveChangeRequest(
            submitted.Data.RequestId,
            new EmployeeProfileSensitiveReviewDto()
        ));
        Assert.IsType<ConflictObjectResult>(await controller.ApproveSensitiveChangeRequest(
            submitted.Data.RequestId,
            new EmployeeProfileSensitiveReviewDto()
        ));
        Assert.IsType<ConflictObjectResult>(await controller.RejectSensitiveChangeRequest(
            submitted.Data.RequestId,
            new EmployeeProfileSensitiveRejectDto { Reason = "重复处理" }
        ));
        Assert.IsType<NotFoundObjectResult>(await controller.ApproveSensitiveChangeRequest(
            999999,
            new EmployeeProfileSensitiveReviewDto()
        ));
        Assert.IsType<NotFoundObjectResult>(await controller.RejectSensitiveChangeRequest(
            999999,
            new EmployeeProfileSensitiveRejectDto { Reason = "不存在" }
        ));
        foreach (var methodName in new[]
        {
            nameof(EmployeeProfilesController.GetAdminSensitiveChangeRequests),
            nameof(EmployeeProfilesController.GetAdminSensitiveChangeRequest),
            nameof(EmployeeProfilesController.ApproveSensitiveChangeRequest),
            nameof(EmployeeProfilesController.RejectSensitiveChangeRequest),
        })
        {
            var attributes = typeof(EmployeeProfilesController).GetMethod(methodName)!
                .GetCustomAttributes<AuthorizeAttribute>()
                .ToList();
            Assert.Contains(attributes, item => item.Roles == "Admin,管理员");
            Assert.Contains(attributes, item => item.Policy == "EmployeeProfiles.Edit");
        }

        foreach (var methodName in new[]
        {
            nameof(EmployeeProfilesController.GetReviewSensitiveChangeRequests),
            nameof(EmployeeProfilesController.GetReviewSensitiveChangeRequest),
            nameof(EmployeeProfilesController.ApproveReviewSensitiveChangeRequest),
            nameof(EmployeeProfilesController.RejectReviewSensitiveChangeRequest),
        })
        {
            var attributes = typeof(EmployeeProfilesController).GetMethod(methodName)!
                .GetCustomAttributes<AuthorizeAttribute>()
                .ToList();
            Assert.Contains(attributes, item => item.Roles == "Admin,管理员,StoreManager,店长,经理");
            Assert.Contains(
                attributes,
                item => item.Policy == Permissions.EmployeeProfiles.ReviewSensitiveManagedStore
            );
        }
        Assert.IsType<NotFoundObjectResult>(await controller.GetReviewSensitiveChangeRequest(999999));

        var forbiddenController = CreateController(
            CreateService("manager-a", "manager_a", roles: ["StoreManager"], storeGuids: ["store-a"]),
            storage
        );
        var forbiddenRequest = await _db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .FirstAsync(item => item.UserGUID == "user-self");
        Assert.IsType<NotFoundObjectResult>(await forbiddenController.GetReviewSensitiveChangeRequest(
            forbiddenRequest.RequestId
        ));

        var noScopeController = CreateController(
            CreateService("manager-a", "manager_a", roles: ["StoreManager"]),
            storage
        );
        var noScopeResult = Assert.IsType<ObjectResult>(
            await noScopeController.GetReviewSensitiveChangeRequest(forbiddenRequest.RequestId)
        );
        Assert.Equal(StatusCodes.Status403Forbidden, noScopeResult.StatusCode);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }

    private async Task SeedAsync()
    {
        var now = DateTime.UtcNow;
        await _db.Insertable(new User
        {
            UserGUID = "user-self",
            Username = "self_user",
            Email = "self@example.com",
            PasswordHash = "hashed",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        }).ExecuteCommandAsync();
        await _db.Insertable(new EmployeeProfile
        {
            UserGUID = "user-self",
            BankACC = "formal-old",
            SensitiveRevision = 3,
            CreatedAt = now,
            UpdatedAt = now,
        }).ExecuteCommandAsync();
    }

    private async Task SeedReviewScopeAsync()
    {
        var now = DateTime.UtcNow;
        foreach (var userGuid in new[]
        {
            "employee-a", "employee-b", "manager-a", "privileged-manager", "warehouse-manager",
        })
        {
            await _db.Insertable(new User
            {
                UserGUID = userGuid,
                Username = userGuid,
                Email = $"{userGuid}@example.com",
                PasswordHash = "hashed",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            }).ExecuteCommandAsync();
            await _db.Insertable(new EmployeeProfile
            {
                UserGUID = userGuid,
                BankACC = $"formal-{userGuid}",
                SensitiveRevision = 2,
                CreatedAt = now,
                UpdatedAt = now,
            }).ExecuteCommandAsync();
        }
        await _db.Insertable(new[]
        {
            new Store { StoreGUID = "store-a", StoreCode = "A", StoreName = "门店 A", CreatedAt = now, UpdatedAt = now },
            new Store { StoreGUID = "store-b", StoreCode = "B", StoreName = "门店 B", CreatedAt = now, UpdatedAt = now },
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            new UserStore { UserGUID = "employee-a", StoreGUID = "store-a", IsPrimary = true, CreatedAt = now, UpdatedAt = now },
            new UserStore { UserGUID = "employee-a", StoreGUID = "store-b", CreatedAt = now, UpdatedAt = now },
            new UserStore { UserGUID = "employee-b", StoreGUID = "store-b", IsPrimary = true, CreatedAt = now, UpdatedAt = now },
            new UserStore { UserGUID = "manager-a", StoreGUID = "store-a", IsPrimary = true, CreatedAt = now, UpdatedAt = now },
            new UserStore { UserGUID = "privileged-manager", StoreGUID = "store-a", IsPrimary = true, CreatedAt = now, UpdatedAt = now },
        }).ExecuteCommandAsync();
        var role = new Role
        {
            RoleGUID = "role-store-manager",
            RoleName = "StoreManager",
            CreatedAt = now,
            UpdatedAt = now,
        };
        await _db.Insertable(role).ExecuteCommandAsync();
        await _db.Insertable(new UserRole
        {
            UserGUID = "privileged-manager",
            RoleGUID = role.RoleGUID,
            CreatedAt = now,
            UpdatedAt = now,
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            CreatePendingRequest("employee-a", "pending-a-account", now),
            CreatePendingRequest("employee-b", "pending-b-account", now.AddMinutes(1)),
            CreatePendingRequest("manager-a", "pending-self-account", now.AddMinutes(2)),
            CreatePendingRequest("privileged-manager", "pending-privileged-account", now.AddMinutes(3)),
        }).ExecuteCommandAsync();
    }

    private static EmployeeProfileSensitiveChangeRequest CreatePendingRequest(
        string userGuid,
        string bankAccountNumber,
        DateTime submittedAt
    ) => new()
    {
        UserGUID = userGuid,
        BankAccountNumber = bankAccountNumber,
        BaseSensitiveRevision = 2,
        Status = EmployeeProfileSensitiveChangeStatus.Pending,
        SubmittedAt = submittedAt,
        SubmittedBy = userGuid,
        ChangedFieldsJson = "[\"bankAccountNumber\"]",
    };

    private EmployeeProfileSensitiveChangeService CreateService(
        string userGuid,
        string username,
        TencentCloudUploadService? storage = null,
        ISqlSugarClient? db = null,
        IReadOnlyList<string>? roles = null,
        IReadOnlyList<string>? storeGuids = null,
        bool scopeIsAdmin = false,
        ICurrentUserManageableStoreScopeService? scopeService = null
    )
    {
        roles ??= username.Equals("admin", StringComparison.OrdinalIgnoreCase) ? ["Admin"] : [];
        var accessor = CreateHttpContextAccessor(userGuid, username, roles);
        var scope = new Mock<ICurrentUserManageableStoreScopeService>();
        scope.Setup(service => service.GetScopeAsync()).ReturnsAsync(new CurrentUserManageableStoreScope
        {
            IsAllowed = roles.Count > 0,
            IsAuthenticated = true,
            IsAdmin = scopeIsAdmin || roles.Any(role => role is "Admin" or "管理员"),
            UserGuid = userGuid,
            ActorLabel = username,
            StoreGuids = storeGuids ?? [],
        });
        return new EmployeeProfileSensitiveChangeService(
            CreateContext(db ?? _db),
            new CurrentUserService(accessor),
            NullLogger<EmployeeProfileSensitiveChangeService>.Instance,
            scopeService ?? scope.Object,
            accessor,
            storage
        );
    }

    private EmployeeProfilesController CreateController(
        EmployeeProfileSensitiveChangeService sensitive,
        TencentCloudUploadService storage
    )
    {
        var current = Mock.Of<ICurrentUserService>(service =>
            service.GetCurrentUserGuid() == "manager-a"
            && service.GetCurrentUsername() == "manager_a"
        );
        var context = CreateContext(_db);
        return new EmployeeProfilesController(
            Mock.Of<IEmployeeProfileService>(),
            NullLogger<EmployeeProfilesController>.Instance,
            new EmployeeProfileMediaService(
                context,
                current,
                storage,
                NullLogger<EmployeeProfileMediaService>.Instance,
                sensitive
            ),
            new EmployeeCashierBarcodeService(context, current),
            sensitive
        );
    }

    private async Task AssertReviewWaitsForResubmitLockAsync(bool approve)
    {
        await SeedAsync();
        var submitted = await CreateService("user-self", "self_user").UpsertSelfAsync(new()
        {
            BankAccountNumber = "first-pending",
        });
        var requestId = submitted.Data!.RequestId;

        using var resubmitDb = CreateAdditionalDb();
        using var reviewDb = CreateAdditionalDb();
        using var releaseResubmit = new ManualResetEventSlim(false);
        var resubmitReachedBarrier = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var reviewReadRequest = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var resubmitBarrierEntered = 0;
        resubmitDb.Aop.OnLogExecuted = (sql, _) =>
        {
            if (IsSensitiveRequestSelect(sql)
                && Interlocked.Exchange(ref resubmitBarrierEntered, 1) == 0)
            {
                // 关键逻辑：让重新提交在读取旧 Pending 后停住，稳定复现审批与覆盖提交的竞态窗口。
                resubmitReachedBarrier.TrySetResult();
                Assert.True(releaseResubmit.Wait(TimeSpan.FromSeconds(10)), "并发测试屏障等待超时");
            }
        };
        reviewDb.Aop.OnLogExecuted = (sql, _) =>
        {
            if (IsSensitiveRequestSelect(sql))
            {
                reviewReadRequest.TrySetResult();
            }
        };

        var resubmitService = CreateService("user-self", "self_user", db: resubmitDb);
        var resubmitTask = Task.Run(() => resubmitService.UpsertSelfAsync(new()
        {
            BankAccountNumber = "latest-pending",
        }));
        await resubmitReachedBarrier.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var reviewer = CreateService("admin-user", "admin", db: reviewDb);
        var reviewTask = Task.Run(() => approve
            ? reviewer.ApproveAsync(requestId, new EmployeeProfileSensitiveReviewDto())
            : reviewer.RejectAsync(
                requestId,
                new EmployeeProfileSensitiveRejectDto { Reason = "资料无法核验" }
            ));
        await reviewReadRequest.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // 若审批入口共用 sensitive-change 锁，此时只完成了锁外 requestId -> userGuid 定位，不能越过重新提交。
        var reviewCompletedBeforeRelease = await Task.WhenAny(
            reviewTask,
            Task.Delay(TimeSpan.FromMilliseconds(500))
        ) == reviewTask;
        releaseResubmit.Set();

        var resubmit = await resubmitTask;
        var review = await reviewTask;
        Assert.False(reviewCompletedBeforeRelease);
        Assert.True(resubmit.Success);
        Assert.False(review.Success);
        Assert.Equal("REQUEST_NOT_PENDING", review.ErrorCode);

        var rows = await _db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .OrderBy(item => item.RequestId)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(EmployeeProfileSensitiveChangeStatus.Superseded, rows[0].Status);
        Assert.Equal(EmployeeProfileSensitiveChangeStatus.Pending, rows[1].Status);
        Assert.Equal("latest-pending", rows[1].BankAccountNumber);
        Assert.Equal(1, rows.Count(item => item.Status == EmployeeProfileSensitiveChangeStatus.Pending));
        Assert.Equal(
            (await _db.Queryable<EmployeeProfile>().FirstAsync()).SensitiveRevision,
            rows[1].BaseSensitiveRevision
        );
    }

    private SqlSugarClient CreateAdditionalDb() => new(new ConnectionConfig
    {
        ConnectionString = _connection.ConnectionString,
        DbType = DbType.Sqlite,
        IsAutoCloseConnection = true,
        InitKeyType = InitKeyType.Attribute,
    });

    private static bool IsSensitiveRequestSelect(string sql) =>
        sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
        && sql.Contains("EmployeeProfileSensitiveChangeRequest", StringComparison.OrdinalIgnoreCase);

    private EmployeeProfileMediaService CreateMediaService(
        string userGuid,
        string username,
        TencentCloudUploadService storage,
        EmployeeProfileSensitiveChangeService sensitive
    ) => new(
        CreateContext(_db),
        CreateCurrentUser(userGuid, username),
        storage,
        NullLogger<EmployeeProfileMediaService>.Instance,
        sensitive
    );

    private static CurrentUserService CreateCurrentUser(string userGuid, string username)
        => new(CreateHttpContextAccessor(
            userGuid,
            username,
            username.Equals("admin", StringComparison.OrdinalIgnoreCase) ? ["Admin"] : []
        ));

    private static HttpContextAccessor CreateHttpContextAccessor(
        string userGuid,
        string username,
        IReadOnlyList<string> roles
    )
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("userGuid", userGuid),
                    new Claim("userId", userGuid),
                    new Claim(ClaimTypes.NameIdentifier, userGuid),
                    new Claim(ClaimTypes.Name, username),
                }.Concat(roles.Select(role => new Claim(ClaimTypes.Role, role))), "TestAuth")),
            },
        };
        return accessor;
    }

    private EmployeeProfileService CreateProfileService(
        string userGuid,
        string username,
        EmployeeProfileSensitiveChangeService sensitive
    )
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("userId", userGuid),
                    new Claim(ClaimTypes.NameIdentifier, userGuid),
                    new Claim(ClaimTypes.Name, username),
                }, "TestAuth")),
            },
        };
        return new EmployeeProfileService(
            CreateContext(_db),
            new CurrentUserService(accessor),
            NullLogger<EmployeeProfileService>.Instance,
            null,
            sensitive
        );
    }

    private static SqlSugarContext CreateContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    private sealed class FakeStorage : TencentCloudUploadService
    {
        public List<string> DeletedKeys { get; } = new();
        public byte[] Bytes { get; set; } = Array.Empty<byte>();
        public bool DeleteSucceeds { get; set; } = true;

        public FakeStorage()
            : base(
                Options.Create(new TencentCloudSettings
                {
                    SecretId = "id",
                    SecretKey = "key",
                    BucketName = "bucket",
                    Region = "region",
                }),
                NullLogger<TencentCloudUploadService>.Instance,
                new HttpClient()
            )
        {
        }

        public override Task<ApiResponse<bool>> DeleteObjectAsync(
            string objectKey,
            CancellationToken cancellationToken = default
        )
        {
            DeletedKeys.Add(objectKey);
            return Task.FromResult(
                DeleteSucceeds
                    ? ApiResponse<bool>.OK(true)
                    : ApiResponse<bool>.Error("模拟对象存储删除失败", "DELETE_FAILED")
            );
        }

        public override Task<ApiResponse<CosObjectMetadata>> GetObjectMetadataAsync(
            string objectKey,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(ApiResponse<CosObjectMetadata>.OK(new()
        {
            ContentLength = Bytes.Length,
            ContentType = "image/png",
            Owner = "user-self",
            Kind = "identity",
            DeclaredContentType = "image/png",
            DeclaredFileSize = Bytes.Length,
        }));

        public override Task<ApiResponse<byte[]>> DownloadObjectBytesAsync(
            string objectKey,
            int maximumBytes,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(ApiResponse<byte[]>.OK(Bytes));

        public override Task<ApiResponse<bool>> PromoteObjectAsync(
            string sourceObjectKey,
            string targetObjectKey,
            string contentType,
            bool isPublic,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(ApiResponse<bool>.OK(true));
    }
}
