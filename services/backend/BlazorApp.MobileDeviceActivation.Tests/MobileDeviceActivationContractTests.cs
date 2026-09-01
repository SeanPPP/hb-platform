using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Cryptography;
using BlazorApp.Api.Controllers.Mobile;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.MobileDeviceActivation;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.POSM;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SqlSugar;
using Xunit;

namespace BlazorApp.MobileDeviceActivation.Tests;

public sealed class MobileDeviceActivationContractTests
{
    [Fact]
    public void Permission_IsSeededWithoutExpandingNonAdminRoleTemplates()
    {
        Assert.Equal(
            "DeviceRegistration.MobileActivationCodes.Manage",
            Permissions.DeviceRegistration.MobileActivationCodes.Manage);
        Assert.Contains(
            PermissionSeedData.AllPermissions,
            item => item.Code == Permissions.DeviceRegistration.MobileActivationCodes.Manage);

        foreach (var template in PermissionSeedData.RolePermissionTemplates)
        {
            if (template.RoleName.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Assert.DoesNotContain(
                Permissions.DeviceRegistration.MobileActivationCodes.Manage,
                template.PermissionCodes);
        }
    }

    [Fact]
    public void Models_UseIndependentTablesAndNeverPersistPlaintextSecrets()
    {
        Assert.Equal(
            "POSM_MobileDeviceActivationGrant",
            typeof(MobileDeviceActivationGrant).GetCustomAttribute<SugarTable>()?.TableName);
        Assert.Equal(
            "POSM_MobileDeviceAccountBinding",
            typeof(MobileDeviceAccountBinding).GetCustomAttribute<SugarTable>()?.TableName);

        foreach (var type in new[]
                 {
                     typeof(MobileDeviceActivationGrant),
                     typeof(MobileDeviceAccountBinding),
                 })
        {
            Assert.DoesNotContain(
                type.GetProperties(),
                property => property.Name is "ActivationCode" or "Credential");
        }

        Assert.Equal(
            "binary(32)",
            typeof(MobileDeviceAccountBinding)
                .GetProperty(nameof(MobileDeviceAccountBinding.CredentialVerifier))!
                .GetCustomAttribute<SugarColumn>()?
                .ColumnDataType);
    }

    [Fact]
    public void PublicDtos_DoNotExposeVerifierOrCredential()
    {
        var responseTypes = new[]
        {
            typeof(MobileDeviceActivationGrantDto),
            typeof(MobileDeviceBindingDto),
            typeof(MobileDeviceActivationMutationResponseDto),
            typeof(MobileDeviceSessionExchangeResponseDto),
        };

        foreach (var type in responseTypes)
        {
            Assert.DoesNotContain(
                type.GetProperties(),
                property => property.Name.Contains("Verifier", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("Credential", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Controllers_UseLockedRoutesAndPermission()
    {
        AssertRoute<MobileDeviceActivationCodesController>(
            "api/react/v1/mobile-device-activation-codes");
        AssertRoute<MobileDeviceActivationController>("api/mobile/v1/device-activation");
        AssertRoute<MobileDeviceSessionController>("api/mobile/v1");

        var authorize = Assert.Single(
            typeof(MobileDeviceActivationCodesController)
                .GetCustomAttributes<AuthorizeAttribute>(),
            item => item.Policy != null);
        Assert.Equal(Permissions.DeviceRegistration.MobileActivationCodes.Manage, authorize.Policy);
    }

    [Fact]
    public void AnonymousMobileEndpoints_UseTheDedicatedRateLimitPolicies()
    {
        AssertRateLimitPolicy<MobileDeviceActivationController>(
            nameof(MobileDeviceActivationController.Preview),
            MobileDeviceActivationRateLimits.AnonymousMutationPolicy);
        AssertRateLimitPolicy<MobileDeviceActivationController>(
            nameof(MobileDeviceActivationController.Redeem),
            MobileDeviceActivationRateLimits.AnonymousMutationPolicy);
        AssertRateLimitPolicy<MobileDeviceActivationController>(
            nameof(MobileDeviceActivationController.Rebind),
            MobileDeviceActivationRateLimits.AnonymousMutationPolicy);
        AssertRateLimitPolicy<MobileDeviceSessionController>(
            nameof(MobileDeviceSessionController.Exchange),
            MobileDeviceActivationRateLimits.SessionExchangePolicy);
        Assert.Equal(10, MobileDeviceActivationRateLimits.PermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(10), MobileDeviceActivationRateLimits.Window);
    }

    [Fact]
    public void CredentialCodec_AcceptsOnlyLowerHexSha256AndMatchesInConstantTime()
    {
        const string credential = "mobile-device-secret-value";
        var verifier = MobileDeviceCredentialCodec.HashCredentialToHex(credential);

        Assert.Equal(64, verifier.Length);
        Assert.True(MobileDeviceCredentialCodec.TryParseVerifier(verifier, out var parsed));
        Assert.True(MobileDeviceCredentialCodec.MatchesCredential(parsed, credential));
        Assert.False(MobileDeviceCredentialCodec.TryParseVerifier(verifier.ToUpperInvariant(), out _));
        Assert.False(MobileDeviceCredentialCodec.TryParseVerifier(verifier[..63], out _));
        Assert.False(MobileDeviceCredentialCodec.MatchesCredential(parsed, credential + "x"));
        Assert.False(MobileDeviceCredentialCodec.IsCredentialShapeValid(new string('x', 15)));
        Assert.True(MobileDeviceCredentialCodec.IsCredentialShapeValid(new string('x', 16)));
        Assert.True(MobileDeviceCredentialCodec.IsCredentialShapeValid(new string('x', 256)));
        Assert.False(MobileDeviceCredentialCodec.IsCredentialShapeValid(new string('x', 257)));

        var weakVerifier = Convert.FromHexString(
            MobileDeviceCredentialCodec.HashCredentialToHex(new string('x', 15)));
        Assert.False(MobileDeviceCredentialCodec.MatchesCredential(
            weakVerifier,
            new string('x', 15)));
        CryptographicOperations.ZeroMemory(weakVerifier);
    }

    [Fact]
    public void BoundCredentialBridge_HasReusableServiceContractAndRequiresTheFullLiveBinding()
    {
        var bridge = typeof(IMobileDeviceActivationService).GetMethod(
            nameof(IMobileDeviceActivationService.ValidateBoundDeviceCredentialAsync));
        Assert.NotNull(bridge);
        Assert.Equal(
            typeof(Task<MobileDeviceCredentialValidationResult>),
            bridge.ReturnType);

        var activeButInvalid = new MobileDeviceCredentialValidationResult(true, false);
        Assert.True(activeButInvalid.HasActiveBinding);
        Assert.False(activeButInvalid.IsValid);

        const string credential = "mobile-device-secret-value";
        Assert.True(MobileDeviceCredentialCodec.TryParseVerifier(
            MobileDeviceCredentialCodec.HashCredentialToHex(credential),
            out var verifier));
        var binding = new MobileDeviceAccountBinding
        {
            BindingId = Guid.NewGuid(),
            DeviceRegistrationId = 42,
            HardwareId = "hardware-1",
            DeviceCode = "MOB_S001_1",
            StoreCode = "S001",
            DeviceSystem = "Android",
            TargetUserGuid = "user-1",
            CredentialVerifier = verifier,
            Version = 1,
            BoundAtUtc = DateTime.UtcNow,
        };
        var registration = new MobileDeviceRegistrationState(
            42,
            "hardware-1",
            "MOB_S001_1",
            "S001",
            "Android",
            "Mobile",
            1);
        var account = new MobileDeviceTargetAccountState("user-1", true, false, true);

        Assert.True(MobileDeviceActivationRules.IsBoundCredentialValid(
            binding,
            registration,
            account,
            credential));
        Assert.False(MobileDeviceActivationRules.IsBoundCredentialValid(
            binding,
            registration,
            account,
            credential + "x"));
        Assert.False(MobileDeviceActivationRules.IsBoundCredentialValid(
            binding,
            registration with { StoreCode = "S002" },
            account,
            credential));
        Assert.False(MobileDeviceActivationRules.IsBoundCredentialValid(
            binding,
            registration,
            account with { IsActive = false },
            credential));
        Assert.False(MobileDeviceActivationRules.IsBoundCredentialValid(
            binding,
            registration,
            account with { HasBoundStoreAccess = false },
            credential));

        binding.RevokedAtUtc = DateTime.UtcNow;
        Assert.False(MobileDeviceActivationRules.IsBoundCredentialValid(
            binding,
            registration,
            account,
            credential));
        CryptographicOperations.ZeroMemory(verifier);
    }

    [Fact]
    public void BoundCredentialBridge_AnyBindingHistoryOwnsTheGateAndRequiresOneActiveBinding()
    {
        var revoked = new MobileDeviceAccountBinding
        {
            BindingId = Guid.NewGuid(),
            HardwareId = "hardware-1",
            RevokedAtUtc = DateTime.UtcNow,
            BoundAtUtc = DateTime.UtcNow.AddMinutes(-1),
        };
        var active = new MobileDeviceAccountBinding
        {
            BindingId = Guid.NewGuid(),
            HardwareId = "hardware-1",
            BoundAtUtc = DateTime.UtcNow,
        };

        var neverBound = MobileDeviceActivationRules.SelectBindingCredentialGate([]);
        Assert.False(neverBound.RequiresBoundCredential);
        Assert.Null(neverBound.ActiveBinding);

        var revokedOnly = MobileDeviceActivationRules.SelectBindingCredentialGate([revoked]);
        Assert.True(revokedOnly.RequiresBoundCredential);
        Assert.Null(revokedOnly.ActiveBinding);

        var oneActive = MobileDeviceActivationRules.SelectBindingCredentialGate([revoked, active]);
        Assert.True(oneActive.RequiresBoundCredential);
        Assert.Same(active, oneActive.ActiveBinding);

        var ambiguous = MobileDeviceActivationRules.SelectBindingCredentialGate([
            active,
            new MobileDeviceAccountBinding
            {
                BindingId = Guid.NewGuid(),
                HardwareId = "hardware-1",
                BoundAtUtc = DateTime.UtcNow.AddSeconds(1),
            },
        ]);
        Assert.True(ambiguous.RequiresBoundCredential);
        Assert.Null(ambiguous.ActiveBinding);

        var result = new MobileDeviceCredentialValidationResult(true, false);
        Assert.True(result.RequiresBoundCredential);
        Assert.True(result.HasActiveBinding);
    }

    [Fact]
    public void ServiceContracts_UseBindingHistoryAndLivePreviewIdentity()
    {
        var source = ReadApiSource(
            "Services",
            "MobileDeviceActivation",
            "MobileDeviceActivationService.cs");
        var bridge = ExtractMethod(
            source,
            "public async Task<MobileDeviceCredentialValidationResult> ValidateBoundDeviceCredentialAsync(",
            "private async Task<ApiResponse<MobileDeviceActivationMutationResponseDto>> RecoverRedeemAsync(");
        Assert.Contains("SelectBindingCredentialGate", bridge, StringComparison.Ordinal);
        Assert.Contains("IsCredentialShapeValid", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "item.HardwareId == normalizedHardwareId\n                && item.RevokedAtUtc == null",
            bridge,
            StringComparison.Ordinal);

        var preview = ExtractMethod(
            source,
            "public async Task<ApiResponse<MobileDeviceActivationPreviewResponseDto>> PreviewAsync(",
            "public async Task<ApiResponse<MobileDeviceActivationMutationResponseDto>> RedeemAsync(");
        Assert.Contains("Redact(target.Username)", preview, StringComparison.Ordinal);
        Assert.Contains("RedactNullable(target.FullName)", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("grant.TargetUsernameSnapshot", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("grant.TargetFullNameSnapshot", preview, StringComparison.Ordinal);

        var legacyBridge = ReadApiSource("Services", string.Empty, "DeviceRegistrationService.cs");
        Assert.Contains(
            "if (bindingValidation.RequiresBoundCredential)",
            legacyBridge,
            StringComparison.Ordinal);
        Assert.Contains(
            "return bindingValidation.IsValid;",
            legacyBridge,
            StringComparison.Ordinal);
        Assert.Contains(
            "return (bindingValidation.IsValid, null);",
            legacyBridge,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RebindCredentialRecovery_RequiresLiveRegistrationButNotTheOldAccountState()
    {
        const string credential = "mobile-device-secret-value";
        Assert.True(MobileDeviceCredentialCodec.TryParseVerifier(
            MobileDeviceCredentialCodec.HashCredentialToHex(credential),
            out var verifier));
        var binding = new MobileDeviceAccountBinding
        {
            BindingId = Guid.NewGuid(),
            DeviceRegistrationId = 42,
            HardwareId = "hardware-1",
            DeviceCode = "MOB_S001_1",
            StoreCode = "S001",
            DeviceSystem = "Android",
            TargetUserGuid = "disabled-user",
            CredentialVerifier = verifier,
            Version = 1,
            BoundAtUtc = DateTime.UtcNow,
        };
        var registration = new MobileDeviceRegistrationState(
            42,
            "hardware-1",
            "MOB_S001_1",
            "S001",
            "Android",
            "Mobile",
            1);

        Assert.True(MobileDeviceActivationRules.IsRebindSourceCredentialValid(
            binding,
            registration,
            credential));
        Assert.False(MobileDeviceActivationRules.IsRebindSourceCredentialValid(
            binding,
            registration,
            credential + "x"));
        Assert.False(MobileDeviceActivationRules.IsRebindSourceCredentialValid(
            binding,
            registration with { DeviceStatus = 0 },
            credential));
        Assert.False(MobileDeviceActivationRules.IsRebindSourceCredentialValid(
            binding,
            registration with { DeviceCode = "MOB_S001_CHANGED" },
            credential));

        binding.RevokedAtUtc = DateTime.UtcNow;
        Assert.False(MobileDeviceActivationRules.IsRebindSourceCredentialValid(
            binding,
            registration,
            credential));
        CryptographicOperations.ZeroMemory(verifier);
    }

    [Fact]
    public void PreviewGate_ConcealsUnknownExpiredRevokedAndConsumedGrantsBeforePlatformCheck()
    {
        var material = BlazorApp.Shared.Security.DeviceActivationCodeCodec.Create();
        Assert.True(
            BlazorApp.Shared.Security.DeviceActivationCodeCodec.TryParse(
                material.ActivationCode,
                out var parsed));
        var now = new DateTime(2026, 8, 31, 5, 0, 0, DateTimeKind.Utc);
        var grant = Grant(material.SecretHash, now.AddMinutes(30));

        var allowed = MobileDeviceActivationRules.EvaluatePreview(
            grant,
            parsed.Secret,
            "Android",
            now);
        var platformMismatch = MobileDeviceActivationRules.EvaluatePreview(
            grant,
            parsed.Secret,
            "iOS",
            now);
        var expired = MobileDeviceActivationRules.EvaluatePreview(
            Grant(material.SecretHash, now),
            parsed.Secret,
            "iOS",
            now);
        var consumedGrant = Grant(material.SecretHash, now.AddMinutes(30));
        consumedGrant.ConsumedAtUtc = now;
        var consumed = MobileDeviceActivationRules.EvaluatePreview(
            consumedGrant,
            parsed.Secret,
            "iOS",
            now);

        Assert.True(allowed.IsAllowed);
        Assert.Equal(MobileDeviceActivationReasonCodes.PlatformMismatch, platformMismatch.ReasonCode);
        Assert.Equal(MobileDeviceActivationReasonCodes.NotAvailable, expired.ReasonCode);
        Assert.Equal(MobileDeviceActivationReasonCodes.NotAvailable, consumed.ReasonCode);
        Assert.Equal(MobileDeviceActivationReasonCodes.NotAvailable,
            MobileDeviceActivationRules.EvaluatePreview(null, parsed.Secret, "iOS", now).ReasonCode);
    }

    [Fact]
    public void RedeemGate_RecoveryCannotConsumeAnUnusedGrantAndExactRecoveryIsRequired()
    {
        var now = new DateTime(2026, 8, 31, 5, 0, 0, DateTimeKind.Utc);
        var grant = Grant(new byte[32], now.AddMinutes(30));

        var recoveryOnUnused = MobileDeviceActivationRules.EvaluateRedeem(
            grant,
            "hardware-1",
            "Android",
            recoveryOnly: true,
            now);
        Assert.False(recoveryOnUnused.IsAllowed);
        Assert.Equal(MobileDeviceActivationReasonCodes.NotAvailable, recoveryOnUnused.ReasonCode);

        grant.ConsumedAtUtc = now;
        grant.ConsumedHardwareId = "hardware-1";
        grant.ConsumedDeviceSystem = "Android";
        grant.ConsumedBindingId = Guid.NewGuid();
        var recovery = MobileDeviceActivationRules.EvaluateRedeem(
            grant,
            "hardware-1",
            "Android",
            recoveryOnly: true,
            now);
        var wrongHardware = MobileDeviceActivationRules.EvaluateRedeem(
            grant,
            "hardware-2",
            "Android",
            recoveryOnly: true,
            now);
        var ordinaryRetry = MobileDeviceActivationRules.EvaluateRedeem(
            grant,
            "hardware-1",
            "Android",
            recoveryOnly: false,
            now);

        Assert.True(recovery.IsAllowed);
        Assert.True(recovery.IsRecovery);
        Assert.Equal(MobileDeviceActivationReasonCodes.NotAvailable, wrongHardware.ReasonCode);
        Assert.Equal(MobileDeviceActivationReasonCodes.NotAvailable, ordinaryRetry.ReasonCode);
    }

    [Fact]
    public void Schema_IsAppendOnlyIdempotentAndEnforcesOneActiveBindingPerHardware()
    {
        var sql = MobileDeviceActivationSchema.EnsureSql;

        Assert.Contains("POSM_MobileDeviceActivationGrant", sql, StringComparison.Ordinal);
        Assert.Contains("POSM_MobileDeviceAccountBinding", sql, StringComparison.Ordinal);
        Assert.Contains("CredentialVerifier", sql, StringComparison.Ordinal);
        Assert.Contains("BINARY(32)", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE [RevokedAtUtc] IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("sp_getapplock", sql, StringComparison.Ordinal);
        Assert.Contains("ROWVERSION", sql, StringComparison.Ordinal);
        Assert.Contains("sys.columns", sql, StringComparison.Ordinal);
        Assert.Contains("sys.check_constraints", sql, StringComparison.Ordinal);
        Assert.Contains("actual.[definition]", sql, StringComparison.Ordinal);
        Assert.Contains("is_not_trusted", sql, StringComparison.Ordinal);
        Assert.Contains("has_filter", sql, StringComparison.Ordinal);
        Assert.Contains("filter_definition", sql, StringComparison.Ordinal);
        Assert.Contains("[key_ordinal] > 1", sql, StringComparison.Ordinal);
        Assert.Contains("expected.[TypeName]", sql, StringComparison.Ordinal);
        Assert.Contains("expected.[MaxLength]", sql, StringComparison.Ordinal);
        Assert.Contains("expected.[IsNullable]", sql, StringComparison.Ordinal);
        Assert.Contains("expected.[Scale]", sql, StringComparison.Ordinal);
        Assert.Contains("N'TargetUserGuid', N'varchar', 64, 0", sql, StringComparison.Ordinal);
        Assert.Contains("N'HardwareId', N'varchar', 100, 0", sql, StringComparison.Ordinal);
        Assert.Contains("N'BoundAtUtc', N'datetime2', 8, 0, 7", sql, StringComparison.Ordinal);
        Assert.Contains("normalized.[Definition] NOT IN", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CHARINDEX(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("or1=1", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Existing mobile device activation schema is incompatible", sql, StringComparison.Ordinal);
        Assert.Contains("COL_LENGTH(N'[dbo].[POSM_MobileDeviceActivationGrant]', N'ActivationCode')", sql, StringComparison.Ordinal);
        Assert.Contains("COL_LENGTH(N'[dbo].[POSM_MobileDeviceAccountBinding]', N'Credential')", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("[ActivationCode]", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("[Credential] ", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(MobileDeviceActivationSchema.EnsureSql, MobileDeviceActivationSchemaMigrator.SqlScriptsForTests);

        var verifySql = MobileDeviceActivationSchema.VerifySql;
        Assert.Contains("sys.check_constraints", verifySql, StringComparison.Ordinal);
        Assert.Contains("filter_definition", verifySql, StringComparison.Ordinal);
        Assert.Contains("COL_LENGTH", verifySql, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE TABLE", verifySql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE INDEX", verifySql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sp_getapplock", verifySql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BEGIN TRANSACTION", verifySql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeviceAccountTokenClaims_HaveDedicatedUseAndBindingVersion()
    {
        var claims = MobileDeviceAccountTokenIssuer.BuildClaims(
            new MobileDeviceAccountTokenSubject(
                "user-1",
                "alice",
                "alice@example.test",
                "Alice",
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                42,
                "hardware-1",
                7,
                ["Manager"]));

        Assert.Contains(claims, claim => claim.Type == "token_use" && claim.Value == "mobile_bound_account");
        Assert.Contains(claims, claim => claim.Type == "mobile_binding_id" && claim.Value == "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        Assert.Contains(claims, claim => claim.Type == "mobile_binding_version" && claim.Value == "7");
        Assert.Contains(claims, claim => claim.Type == ClaimTypes.Role && claim.Value == "Manager");
        Assert.DoesNotContain(claims, claim => claim.Type == "permission");
    }

    [Fact]
    public void DeviceAccountTokenClaims_DoNotInventRolesForAnEmptyAccountRoleSet()
    {
        var claims = MobileDeviceAccountTokenIssuer.BuildClaims(
            new MobileDeviceAccountTokenSubject(
                "user-1",
                "admin",
                "admin@example.test",
                "Admin",
                Guid.NewGuid(),
                42,
                "hardware-1",
                1,
                []));

        Assert.DoesNotContain(claims, claim => claim.Type == ClaimTypes.Role);
    }

    [Fact]
    public void BindingContextResolver_RequiresAuthenticatedDedicatedTokenAndEveryBindingClaim()
    {
        var bindingId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var claims = new[]
        {
            new Claim("token_use", MobileDeviceAccountTokenIssuer.TokenUse),
            new Claim(MobileDeviceAccountTokenIssuer.BindingIdClaim, bindingId.ToString("N")),
            new Claim(MobileDeviceAccountTokenIssuer.BindingVersionClaim, "7"),
            new Claim(MobileDeviceAccountTokenIssuer.DeviceRegistrationIdClaim, "42"),
            new Claim(MobileDeviceAccountTokenIssuer.HardwareIdClaim, "hardware-1"),
            new Claim("userGuid", "user-1"),
        };
        var authenticated = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));

        Assert.True(MobileDeviceBindingContextResolver.TryResolve(authenticated, out var resolved));
        Assert.Equal(bindingId, resolved.BindingId);
        Assert.Equal(7, resolved.BindingVersion);
        Assert.Equal(42, resolved.DeviceRegistrationId);
        Assert.Equal("hardware-1", resolved.HardwareId);
        Assert.Equal("user-1", resolved.UserGuid);

        Assert.False(MobileDeviceBindingContextResolver.TryResolve(
            new ClaimsPrincipal(new ClaimsIdentity(claims)),
            out _));
        Assert.False(MobileDeviceBindingContextResolver.TryResolve(
            PrincipalWithReplacedClaim(claims, "token_use", "access"),
            out _));
        Assert.False(MobileDeviceBindingContextResolver.TryResolve(
            PrincipalWithoutClaim(claims, MobileDeviceAccountTokenIssuer.BindingVersionClaim),
            out _));
        Assert.False(MobileDeviceBindingContextResolver.TryResolve(
            PrincipalWithoutClaim(claims, MobileDeviceAccountTokenIssuer.DeviceRegistrationIdClaim),
            out _));
        Assert.False(MobileDeviceBindingContextResolver.TryResolve(
            PrincipalWithoutClaim(claims, MobileDeviceAccountTokenIssuer.HardwareIdClaim),
            out _));
        Assert.False(MobileDeviceBindingContextResolver.TryResolve(
            PrincipalWithoutClaim(claims, "userGuid"),
            out _));
    }

    [Fact]
    public void DynamicTokenValidation_MatchesEveryBindingDeviceAndAccountDimension()
    {
        var binding = new MobileDeviceAccountBinding
        {
            BindingId = Guid.NewGuid(),
            DeviceRegistrationId = 42,
            HardwareId = "hardware-1",
            DeviceCode = "MOB_S001_1",
            StoreCode = "S001",
            DeviceSystem = "Android",
            TargetUserGuid = "user-1",
            CredentialVerifier = new byte[32],
            Version = 7,
            BoundAtUtc = DateTime.UtcNow,
        };
        var context = new MobileDeviceBindingContext(
            binding.BindingId,
            binding.Version,
            binding.DeviceRegistrationId,
            binding.HardwareId,
            binding.TargetUserGuid);
        var registration = new MobileDeviceRegistrationState(
            42,
            "hardware-1",
            "MOB_S001_1",
            "S001",
            "Android",
            "Mobile",
            1);
        var account = new MobileDeviceTargetAccountState("user-1", true, false, true);

        Assert.True(MobileDeviceActivationRules.IsTokenBindingValid(
            binding,
            context,
            registration,
            account));

        Assert.False(MobileDeviceActivationRules.IsTokenBindingValid(
            binding,
            context with { BindingId = Guid.NewGuid() },
            registration,
            account));
        Assert.False(MobileDeviceActivationRules.IsTokenBindingValid(
            binding,
            context with { BindingVersion = 8 },
            registration,
            account));
        Assert.False(MobileDeviceActivationRules.IsTokenBindingValid(
            binding,
            context with { DeviceRegistrationId = 43 },
            registration,
            account));
        Assert.False(MobileDeviceActivationRules.IsTokenBindingValid(
            binding,
            context with { HardwareId = "hardware-2" },
            registration,
            account));
        Assert.False(MobileDeviceActivationRules.IsTokenBindingValid(
            binding,
            context with { UserGuid = "user-2" },
            registration,
            account));
        Assert.False(MobileDeviceActivationRules.IsTokenBindingValid(
            binding,
            context,
            registration with { DeviceRegistrationId = 43 },
            account));
        Assert.False(MobileDeviceActivationRules.IsTokenBindingValid(
            binding,
            context,
            registration with { HardwareId = "hardware-2" },
            account));
        Assert.False(MobileDeviceActivationRules.IsTokenBindingValid(
            binding,
            context,
            registration with { DeviceCode = "MOB_S001_2" },
            account));
        Assert.False(MobileDeviceActivationRules.IsTokenBindingValid(
            binding,
            context,
            registration with { StoreCode = "S002" },
            account));
        Assert.False(MobileDeviceActivationRules.IsTokenBindingValid(
            binding,
            context,
            registration with { DeviceSystem = "iOS" },
            account));
        Assert.False(MobileDeviceActivationRules.IsTokenBindingValid(
            binding,
            context,
            registration with { DeviceType = "POS" },
            account));
        Assert.False(MobileDeviceActivationRules.IsTokenBindingValid(
            binding,
            context,
            registration with { DeviceStatus = 0 },
            account));
        Assert.False(MobileDeviceActivationRules.IsTokenBindingValid(
            binding,
            context,
            registration,
            account with { UserGuid = "user-2" }));
        Assert.False(MobileDeviceActivationRules.IsTokenBindingValid(
            binding,
            context,
            registration,
            account with { IsActive = false }));
        Assert.False(MobileDeviceActivationRules.IsTokenBindingValid(
            binding,
            context,
            registration,
            account with { IsDeleted = true }));
        Assert.False(MobileDeviceActivationRules.IsTokenBindingValid(
            binding,
            context,
            registration,
            account with { HasBoundStoreAccess = false }));

        binding.RevokedAtUtc = DateTime.UtcNow;
        Assert.False(MobileDeviceActivationRules.IsTokenBindingValid(
            binding,
            context,
            registration,
            account));
    }

    private static MobileDeviceActivationGrant Grant(byte[] secretHash, DateTime expiresAtUtc) =>
        new()
        {
            GrantId = Guid.NewGuid(),
            SecretHash = secretHash,
            StoreCode = "S001",
            DeviceSystem = "Android",
            TargetUserGuid = "user-1",
            TargetUsernameSnapshot = "alice",
            CreatedAtUtc = expiresAtUtc.AddMinutes(-30),
            CreatedBy = "admin",
            Reason = "test",
            ExpiresAtUtc = expiresAtUtc,
        };

    private static void AssertRoute<TController>(string expected)
    {
        var route = Assert.Single(typeof(TController).GetCustomAttributes<RouteAttribute>());
        Assert.Equal(expected, route.Template);
    }

    private static void AssertRateLimitPolicy<TController>(
        string methodName,
        string expectedPolicy)
    {
        var method = typeof(TController).GetMethod(methodName)
            ?? throw new InvalidOperationException($"Missing action {methodName}.");
        var attribute = Assert.Single(method.GetCustomAttributes<EnableRateLimitingAttribute>());
        Assert.Equal(expectedPolicy, attribute.PolicyName);
    }

    private static ClaimsPrincipal PrincipalWithoutClaim(
        IReadOnlyCollection<Claim> claims,
        string removedType) =>
        new(new ClaimsIdentity(
            claims.Where(claim => claim.Type != removedType),
            "Bearer"));

    private static ClaimsPrincipal PrincipalWithReplacedClaim(
        IReadOnlyCollection<Claim> claims,
        string replacedType,
        string value) =>
        new(new ClaimsIdentity(
            claims.Where(claim => claim.Type != replacedType)
                .Append(new Claim(replacedType, value)),
            "Bearer"));

    private static string ReadApiSource(
        string firstSegment,
        string secondSegment,
        string fileName,
        [CallerFilePath] string testSourcePath = "")
    {
        var testProject = Path.GetDirectoryName(testSourcePath)
            ?? throw new InvalidOperationException("Test source directory is unavailable.");
        var backendRoot = Directory.GetParent(testProject)?.FullName
            ?? throw new InvalidOperationException("Backend source directory is unavailable.");
        return File.ReadAllText(Path.Combine(
            backendRoot,
            "BlazorApp.Api",
            firstSegment,
            secondSegment,
            fileName));
    }

    private static string ExtractMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing source marker: {startMarker}");
        Assert.True(end > start, $"Missing source marker: {endMarker}");
        return source[start..end];
    }
}
