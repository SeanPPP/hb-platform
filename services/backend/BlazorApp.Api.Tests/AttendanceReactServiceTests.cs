using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Models;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Api.Services.Attendance;
using BlazorApp.Api.Security;
using AttendanceQrKeyDataProtection = BlazorApp.Api.Security.AttendanceQrKeyDataProtection;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public sealed class AttendanceReactServiceTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _sqliteConnection;
        private readonly SqlSugarClient _db;
        private readonly byte[] _attendanceKey = RandomNumberGenerator.GetBytes(32);
        private readonly IDataProtectionProvider _attendanceDataProtectionProvider =
            new EphemeralDataProtectionProvider();
        private readonly AttendanceQrKeyProtector _attendanceProtector;
        private readonly AttendancePunchAuthorizationProtector _punchAuthorizationProtector;
        private readonly MutableTimeProvider _timeProvider = new(DateTimeOffset.UtcNow);

        public AttendanceReactServiceTests()
        {
            _attendanceProtector = AttendanceQrKeyDataProtection.CreateProtector(
                _attendanceDataProtectionProvider);
            _punchAuthorizationProtector = AttendancePunchAuthorizationDataProtection.CreateProtector(
                _attendanceDataProtectionProvider);
            _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
            _sqliteConnection = new SqliteConnection($"Data Source={_dbPath}");
            _sqliteConnection.Open();

            _db = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = _sqliteConnection.ConnectionString,
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            });

            _db.CodeFirst.InitTables(
                typeof(User),
                typeof(Store),
                typeof(UserStore),
                typeof(AttendanceSchedule),
                typeof(AttendanceAvailability),
                typeof(AttendancePunch),
                typeof(AttendanceLocationSample),
                typeof(AttendanceApproval),
                typeof(AttendanceStoreHoliday),
                typeof(AttendanceLeaveRequest),
                typeof(AttendanceSettings),
                typeof(EmployeeProfile),
                typeof(AttendancePosQrKey)
            );
            SeedAttendanceSigningKeyAsync().GetAwaiter().GetResult();
        }

        [Fact]
        public async Task PunchAsync_WhenQrTokenMissing_ReturnsQrRequired()
        {
            var service = CreateService("staff-user", "staff", "StoreStaff");

            var result = await service.PunchAsync(new AttendancePunchRequestDto());

            Assert.False(result.Success);
            Assert.Equal("QR_REQUIRED", result.ErrorCode);
        }

        [Fact]
        public async Task ResolveAttendanceQrAsync_ValidToken_ReturnsTrustedDeviceAndExpiry()
        {
            await SeedStoreScopeAsync();
            var issuedAt = new DateTime(
                DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond,
                DateTimeKind.Utc);
            _timeProvider.SetUtcNow(issuedAt.AddSeconds(10));
            var service = CreateService("staff-user", "staff", "StoreStaff");

            var result = await service.ResolveAttendanceQrAsync(new AttendanceQrResolveRequestDto
            {
                QrToken = CreateQrToken(issuedAt),
            });

            Assert.True(result.Success);
            Assert.Equal("BRI", result.Data!.StoreCode);
            Assert.Equal("POS-001", result.Data.DeviceCode);
            Assert.Equal("Brisbane", result.Data.StoreName);
            Assert.Equal(issuedAt.AddSeconds(15), result.Data.ExpiresAtUtc);
            Assert.False(string.IsNullOrWhiteSpace(result.Data.PunchAuthorizationToken));
            Assert.Equal(
                _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(2),
                result.Data.PunchAuthorizationExpiresAtUtc);
        }

        [Fact]
        public async Task PunchAsync_WithValidAuthorization_AllowsResolvedQrAfterQrExpiry()
        {
            await SeedStoreScopeAsync();
            var issuedAt = TruncateToMilliseconds(DateTime.UtcNow);
            var tokenId = Guid.NewGuid();
            var request = CreateQrPunchRequest(issuedAt, tokenId);
            var service = CreateService("staff-user", "staff", "StoreStaff");
            _timeProvider.SetUtcNow(issuedAt.AddSeconds(10));
            var resolved = await service.ResolveAttendanceQrAsync(new AttendanceQrResolveRequestDto
            {
                QrToken = request.QrToken,
            });

            _timeProvider.SetUtcNow(issuedAt.AddSeconds(45));
            request.LocationCapturedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            request.PunchAuthorizationToken = resolved.Data!.PunchAuthorizationToken;
            var result = await service.PunchAsync(request);

            Assert.True(result.Success);
            Assert.Equal(tokenId.ToString(), result.Data!.QrTokenId);
        }

        [Fact]
        public async Task PunchAsync_WithExpiredAuthorization_ReturnsAuthorizationExpired()
        {
            await SeedStoreScopeAsync();
            var issuedAt = TruncateToMilliseconds(DateTime.UtcNow);
            var request = CreateQrPunchRequest(issuedAt, Guid.NewGuid());
            var service = CreateService("staff-user", "staff", "StoreStaff");
            _timeProvider.SetUtcNow(issuedAt.AddSeconds(10));
            var resolved = await service.ResolveAttendanceQrAsync(new AttendanceQrResolveRequestDto
            {
                QrToken = request.QrToken,
            });

            _timeProvider.SetUtcNow(resolved.Data!.PunchAuthorizationExpiresAtUtc);
            request.LocationCapturedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            request.PunchAuthorizationToken = resolved.Data.PunchAuthorizationToken;
            var result = await service.PunchAsync(request);

            Assert.False(result.Success);
            Assert.Equal("ATTENDANCE_PUNCH_AUTHORIZATION_EXPIRED", result.ErrorCode);
        }

        [Fact]
        public async Task PunchAsync_WithTamperedOrChangedBindingAuthorization_ReturnsAuthorizationInvalid()
        {
            await SeedStoreScopeAsync();
            await _db.Insertable(new User
            {
                UserGUID = "other-user",
                Username = "other",
                Email = "other@example.com",
                PasswordHash = "hash",
                CreatedAt = DateTime.UtcNow,
            }).ExecuteCommandAsync();
            await _db.Insertable(new UserStore
            {
                UserStoreGUID = "other-store-bri",
                UserGUID = "other-user",
                StoreGUID = "store-bri",
                CreatedAt = DateTime.UtcNow,
            }).ExecuteCommandAsync();
            var issuedAt = TruncateToMilliseconds(DateTime.UtcNow);
            var originalRequest = CreateQrPunchRequest(issuedAt, Guid.NewGuid());
            var service = CreateService("staff-user", "staff", "StoreStaff");
            _timeProvider.SetUtcNow(issuedAt.AddSeconds(5));
            var resolved = await service.ResolveAttendanceQrAsync(new AttendanceQrResolveRequestDto
            {
                QrToken = originalRequest.QrToken,
            });

            var tamperedRequest = CreateQrPunchRequest(issuedAt, Guid.NewGuid());
            tamperedRequest.QrToken = originalRequest.QrToken;
            tamperedRequest.PunchAuthorizationToken = MutateProtectedToken(
                resolved.Data!.PunchAuthorizationToken);
            var tampered = await service.PunchAsync(tamperedRequest);

            var changedQrRequest = CreateQrPunchRequest(issuedAt, Guid.NewGuid());
            changedQrRequest.PunchAuthorizationToken = resolved.Data.PunchAuthorizationToken;
            var changedQr = await service.PunchAsync(changedQrRequest);

            originalRequest.PunchAuthorizationToken = resolved.Data.PunchAuthorizationToken;
            var changedUser = await CreateService("other-user", "other", "StoreStaff")
                .PunchAsync(originalRequest);

            Assert.Equal("ATTENDANCE_PUNCH_AUTHORIZATION_INVALID", tampered.ErrorCode);
            Assert.Equal("ATTENDANCE_PUNCH_AUTHORIZATION_INVALID", changedQr.ErrorCode);
            Assert.Equal("ATTENDANCE_PUNCH_AUTHORIZATION_INVALID", changedUser.ErrorCode);
        }

        [Fact]
        public async Task PunchAsync_ExpiredAuthorizationForExistingPunch_RemainsIdempotent()
        {
            await SeedStoreScopeAsync();
            var issuedAt = TruncateToMilliseconds(DateTime.UtcNow);
            var request = CreateQrPunchRequest(issuedAt, Guid.NewGuid());
            var service = CreateService("staff-user", "staff", "StoreStaff");
            _timeProvider.SetUtcNow(issuedAt.AddSeconds(5));
            var resolved = await service.ResolveAttendanceQrAsync(new AttendanceQrResolveRequestDto
            {
                QrToken = request.QrToken,
            });
            request.PunchAuthorizationToken = resolved.Data!.PunchAuthorizationToken;
            var first = await service.PunchAsync(request);

            _timeProvider.SetUtcNow(resolved.Data.PunchAuthorizationExpiresAtUtc);
            var duplicate = await service.PunchAsync(request);

            Assert.True(first.Success);
            Assert.True(duplicate.Success);
            Assert.Equal(first.Data!.PunchGuid, duplicate.Data!.PunchGuid);
        }

        [Theory]
        [InlineData("OTHER", "POS-001")]
        [InlineData("BRI", "POS-002")]
        public void AttendancePunchAuthorization_ChangedStoreOrDevice_IsInvalid(
            string storeCode,
            string deviceCode)
        {
            var now = TruncateToMilliseconds(DateTime.UtcNow);
            var token = CreateQrToken(now);
            Assert.True(AttendanceQrTokenCodec.TryDecryptIdentity(
                token, _attendanceKey, out var payload, out var kid, out _));
            var authorization = _punchAuthorizationProtector.Issue(
                "staff-user", token, kid, payload!, now);
            var changedPayload = new AttendanceQrTokenPayload
            {
                TokenId = payload!.TokenId,
                StoreCode = storeCode,
                DeviceCode = deviceCode,
                IssuedAtUtc = payload.IssuedAtUtc,
            };

            var validation = _punchAuthorizationProtector.Validate(
                authorization.Token,
                "staff-user",
                token,
                kid,
                changedPayload,
                now.AddSeconds(30));
            var changedKeyValidation = _punchAuthorizationProtector.Validate(
                authorization.Token,
                "staff-user",
                token,
                "K2",
                payload,
                now.AddSeconds(30));

            Assert.Equal(AttendancePunchAuthorizationValidationResult.Invalid, validation);
            Assert.Equal(
                AttendancePunchAuthorizationValidationResult.Invalid,
                changedKeyValidation);
        }

        [Fact]
        public async Task ResolveAttendanceQrAsync_WhenActiveKeyAlgorithmDrifts_RejectsBeforeDecrypt()
        {
            await SeedStoreScopeAsync();
            await _db.Updateable<AttendancePosQrKey>()
                .SetColumns(item => item.Algorithm == "ES256")
                .SetColumns(item => item.ProtectedKey == "not-protected-data")
                .Where(item => item.Kid == "K1")
                .ExecuteCommandAsync();

            var result = await CreateService("staff-user", "staff", "StoreStaff")
                .ResolveAttendanceQrAsync(new AttendanceQrResolveRequestDto
                {
                    QrToken = CreateQrToken(DateTime.UtcNow),
                });

            Assert.False(result.Success);
            Assert.Equal("ATTENDANCE_QR_KEY_INVALID", result.ErrorCode);
        }

        [Theory]
        [InlineData(false, "POS_DEVICE_DISABLED")]
        [InlineData(true, "FORBIDDEN_STORE")]
        public async Task ResolveAttendanceQrAsync_DisabledPosOrStoreForbidden_Rejects(
            bool posActive,
            string expectedError)
        {
            await SeedStoreScopeAsync();
            var userGuid = posActive ? "outside-user" : "staff-user";
            var result = await CreateService(userGuid, "user", posActive, "StoreStaff")
                .ResolveAttendanceQrAsync(new AttendanceQrResolveRequestDto
                {
                    QrToken = CreateQrToken(DateTime.UtcNow),
                });

            Assert.False(result.Success);
            Assert.Equal(expectedError, result.ErrorCode);
        }

        [Fact]
        public async Task PunchAsync_WithValidQrToken_AutomaticallyClocksInThenOutAndCompletesDay()
        {
            await SeedStoreScopeAsync();
            var now = DateTime.UtcNow;
            var service = CreateService("staff-user", "staff", "StoreStaff");

            var clockInRequest = CreateQrPunchRequest(now, Guid.NewGuid());
            // Expo 严格白名单不发送客户端权限状态，服务端只验证真实坐标与采集时间。
            clockInRequest.LocationPermissionStatus = null;
            var clockIn = await service.PunchAsync(clockInRequest);
            var duplicate = await service.PunchAsync(clockInRequest);
            _timeProvider.SetUtcNow(now.AddSeconds(31));
            var expiredDuplicate = await service.PunchAsync(clockInRequest);
            var forgedRequest = CreateQrPunchRequest(now, Guid.NewGuid());
            var signatureSeparator = clockInRequest.QrToken!.LastIndexOf('.');
            var signatureOffset = signatureSeparator + 1;
            forgedRequest.QrToken = clockInRequest.QrToken[..signatureOffset]
                + (clockInRequest.QrToken[signatureOffset] == 'A' ? 'B' : 'A')
                + clockInRequest.QrToken[(signatureOffset + 1)..];
            _timeProvider.SetUtcNow(now.AddSeconds(31));
            var forged = await service.PunchAsync(forgedRequest);
            var clockOut = await service.PunchAsync(CreateQrPunchRequest(now, Guid.NewGuid()));
            var complete = await service.PunchAsync(CreateQrPunchRequest(now, Guid.NewGuid()));

            Assert.True(clockIn.Success);
            Assert.Equal("ClockIn", clockIn.Data!.PunchType);
            Assert.Equal("PosQr", clockIn.Data.Source);
            Assert.Equal("POS-001", clockIn.Data.PosDeviceCode);
            Assert.Equal("staff", clockIn.Data.EmployeeName);
            Assert.Equal("Brisbane", clockIn.Data.StoreName);
            Assert.Equal(clockIn.Data.PunchTimeUtc, clockIn.Data.ServerTimeUtc);
            Assert.Equal(clockIn.Data.PunchGuid, duplicate.Data!.PunchGuid);
            Assert.Equal(clockIn.Data.PunchGuid, expiredDuplicate.Data!.PunchGuid);
            Assert.False(forged.Success);
            Assert.Equal("ATTENDANCE_QR_AUTH_INVALID", forged.ErrorCode);
            Assert.True(clockOut.Success);
            Assert.Equal("ClockOut", clockOut.Data!.PunchType);
            Assert.False(complete.Success);
            Assert.Equal("DAY_COMPLETE", complete.ErrorCode);
        }

        [Fact]
        public async Task PunchAsync_WithRevokedOrUnknownKey_Rejects()
        {
            await SeedStoreScopeAsync();
            var now = DateTime.UtcNow;
            var service = CreateService("staff-user", "staff", "StoreStaff");
            await _db.Updateable<AttendancePosQrKey>()
                .SetColumns(item => item.Status == "Revoked")
                .Where(item => item.Kid == "K1")
                .ExecuteCommandAsync();

            var revoked = await service.PunchAsync(CreateQrPunchRequest(now, Guid.NewGuid()));
            var unknownRequest = CreateQrPunchRequest(now, Guid.NewGuid());
            unknownRequest.QrToken = AttendanceQrTokenCodec.Encrypt(new AttendanceQrTokenPayload
            {
                TokenId = Guid.NewGuid(), StoreCode = "BRI", DeviceCode = "POS-001", IssuedAtUtc = now,
            }, "K2", RandomNumberGenerator.GetBytes(32));
            var unknown = await service.PunchAsync(unknownRequest);

            Assert.Equal("ATTENDANCE_QR_KEY_REVOKED", revoked.ErrorCode);
            Assert.Equal("ATTENDANCE_QR_KEY_UNKNOWN", unknown.ErrorCode);
        }

        [Fact]
        public async Task PunchAsync_WhenProtectedKeyCannotBeDecrypted_ReturnsSecurityError()
        {
            await SeedStoreScopeAsync();
            await _db.Updateable<AttendancePosQrKey>()
                .SetColumns(item => item.ProtectedKey == "not-protected-data")
                .Where(item => item.Kid == "K1")
                .ExecuteCommandAsync();

            var result = await CreateService("staff-user", "staff", "StoreStaff")
                .PunchAsync(CreateQrPunchRequest(DateTime.UtcNow, Guid.NewGuid()));

            Assert.False(result.Success);
            Assert.Equal("ATTENDANCE_QR_KEY_DECRYPT_FAILED", result.ErrorCode);
            Assert.DoesNotContain("not-protected-data", result.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task PunchAsync_WhenActiveKeyAlgorithmDrifts_RejectsBeforeDecrypt()
        {
            await SeedStoreScopeAsync();
            await _db.Updateable<AttendancePosQrKey>()
                .SetColumns(item => item.Algorithm == string.Empty)
                .SetColumns(item => item.ProtectedKey == "not-protected-data")
                .Where(item => item.Kid == "K1")
                .ExecuteCommandAsync();

            var result = await CreateService("staff-user", "staff", "StoreStaff")
                .PunchAsync(CreateQrPunchRequest(DateTime.UtcNow, Guid.NewGuid()));

            Assert.False(result.Success);
            Assert.Equal("ATTENDANCE_QR_KEY_INVALID", result.ErrorCode);
        }

        [Fact]
        public async Task PunchAsync_WithExpiredNotActiveOrMismatchedToken_Rejects()
        {
            await SeedStoreScopeAsync();
            var issuedAt = DateTime.UtcNow;
            var service = CreateService("staff-user", "staff", "StoreStaff");

            var expiredRequest = CreateQrPunchRequest(issuedAt, Guid.NewGuid());
            _timeProvider.SetUtcNow(issuedAt.AddSeconds(15));
            var expired = await service.PunchAsync(expiredRequest);
            var notActiveRequest = CreateQrPunchRequest(issuedAt, Guid.NewGuid());
            _timeProvider.SetUtcNow(issuedAt.AddSeconds(-1));
            var notActive = await service.PunchAsync(notActiveRequest);
            var mismatchRequest = CreateQrPunchRequest(issuedAt, Guid.NewGuid());
            mismatchRequest.QrToken = CreateQrToken(issuedAt, deviceCode: "POS-002");
            _timeProvider.SetUtcNow(issuedAt);
            var mismatch = await service.PunchAsync(mismatchRequest);
            var storeMismatchRequest = CreateQrPunchRequest(issuedAt, Guid.NewGuid());
            storeMismatchRequest.QrToken = CreateQrToken(issuedAt, storeCode: "OTHER");
            var storeMismatch = await service.PunchAsync(storeMismatchRequest);

            Assert.Equal("ATTENDANCE_QR_EXPIRED", expired.ErrorCode);
            Assert.Equal("ATTENDANCE_QR_NOT_ACTIVE", notActive.ErrorCode);
            Assert.Equal("ATTENDANCE_QR_DEVICE_MISMATCH", mismatch.ErrorCode);
            Assert.Equal("ATTENDANCE_QR_DEVICE_MISMATCH", storeMismatch.ErrorCode);
        }

        [Fact]
        public async Task PunchAsync_WhenPosDisabledOrEmployeeHasNoStoreAccess_Rejects()
        {
            await SeedStoreScopeAsync();
            var now = DateTime.UtcNow;
            var disabled = await CreateService("staff-user", "staff", false, "StoreStaff")
                .PunchAsync(CreateQrPunchRequest(now, Guid.NewGuid()));
            var forbidden = await CreateService("outside-user", "outside", "StoreStaff")
                .PunchAsync(CreateQrPunchRequest(now, Guid.NewGuid()));

            Assert.Equal("POS_DEVICE_DISABLED", disabled.ErrorCode);
            Assert.Equal("FORBIDDEN_STORE", forbidden.ErrorCode);
        }

        [Fact]
        public async Task PunchAsync_TwoEmployeesCanUseSameQrToken()
        {
            await SeedStoreScopeAsync();
            await _db.Insertable(new User
            {
                UserGUID = "second-user",
                Username = "second",
                Email = "second@example.com",
                PasswordHash = "hash",
                CreatedAt = DateTime.UtcNow,
            }).ExecuteCommandAsync();
            await _db.Insertable(new UserStore
            {
                UserStoreGUID = "second-store-bri",
                UserGUID = "second-user",
                StoreGUID = "store-bri",
                CreatedAt = DateTime.UtcNow,
            }).ExecuteCommandAsync();
            var now = DateTime.UtcNow;
            var request = CreateQrPunchRequest(now, Guid.NewGuid());

            var first = await CreateService("staff-user", "staff", "StoreStaff").PunchAsync(request);
            var second = await CreateService("second-user", "second", "StoreStaff").PunchAsync(request);

            Assert.True(first.Success);
            Assert.True(second.Success);
            Assert.NotEqual(first.Data!.PunchGuid, second.Data!.PunchGuid);
        }

        [Theory]
        [InlineData(2601)]
        [InlineData(2627)]
        public void AttendancePunchPersistenceException_DetectsDirectAndWrappedUniqueSqlErrors(int number)
        {
            var sqlException = CreateSqlException(number);
            var sugarWrapper = new SqlSugarException("SqlSugar insert failed");
            typeof(Exception).GetField("_innerException", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(sugarWrapper, sqlException);

            Assert.True(AttendancePunchPersistenceException.IsUniqueConstraintViolation(sqlException));
            Assert.True(AttendancePunchPersistenceException.IsUniqueConstraintViolation(sugarWrapper));
        }

        [Fact]
        public void AttendancePunchPersistenceException_DoesNotHideOtherDatabaseErrors()
        {
            Assert.False(AttendancePunchPersistenceException.IsUniqueConstraintViolation(CreateSqlException(50000)));
            Assert.False(AttendancePunchPersistenceException.IsUniqueConstraintViolation(
                new SqlSugarException("connection failed")));
        }

        [Fact]
        public async Task PunchAsync_PersistsPunchAndApprovalInsideOneTransaction()
        {
            var source = await File.ReadAllTextAsync(Path.Combine(
                FindRepoRoot(),
                "services/backend/BlazorApp.Api/Services/React/AttendanceReactService.cs"));

            Assert.Contains("BeginTranAsync", source);
            Assert.Contains("CommitTranAsync", source);
            Assert.Contains("RollbackTranAsync", source);
        }

        [Fact]
        public async Task GetSchedulesAsync_WhenStoreManagerRequestsUnmanagedStore_ReturnsForbidden()
        {
            await SeedStoreScopeAsync();
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.GetSchedulesAsync(new AttendanceScheduleQueryDto
            {
                StoreCode = "OTHER",
                WeekStartDate = new DateTime(2026, 5, 18),
            });

            Assert.False(result.Success);
            Assert.Equal("FORBIDDEN_STORE", result.ErrorCode);
        }

        [Fact]
        public async Task CreateMyAvailabilityAsync_AllowsMultipleSegmentsWithinWeek()
        {
            await SeedStoreScopeAsync();
            var service = CreateService("staff-user", "staff", "StoreStaff");

            var result = await service.CreateMyAvailabilityAsync(
                new CreateAttendanceAvailabilityDto
                {
                    StoreCode = "BRI",
                    WeekStartDate = new DateTime(2026, 5, 18),
                    Segments = new List<AttendanceAvailabilitySegmentDto>
                    {
                        new()
                        {
                            AvailableDate = new DateTime(2026, 5, 18),
                            StartTime = new TimeSpan(9, 0, 0),
                            EndTime = new TimeSpan(12, 0, 0),
                        },
                        new()
                        {
                            AvailableDate = new DateTime(2026, 5, 20),
                            StartTime = new TimeSpan(14, 0, 0),
                            EndTime = new TimeSpan(18, 0, 0),
                        },
                    },
                }
            );

            Assert.True(result.Success);
            Assert.Equal(2, result.Data!.Count);
        }

        [Fact]
        public async Task CreateScheduleAsync_WhenSameStoreUserDateOverlaps_ReturnsConflict()
        {
            await SeedStoreScopeAsync();
            var service = CreateService("manager-user", "manager", "StoreManager");

            var first = await service.CreateScheduleAsync(new CreateAttendanceScheduleDto
            {
                StoreCode = "BRI",
                UserGuid = "staff-user",
                WorkDate = new DateTime(2026, 5, 18),
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(13, 0, 0),
            });
            var second = await service.CreateScheduleAsync(new CreateAttendanceScheduleDto
            {
                StoreCode = "BRI",
                UserGuid = "staff-user",
                WorkDate = new DateTime(2026, 5, 18),
                StartTime = new TimeSpan(12, 0, 0),
                EndTime = new TimeSpan(16, 0, 0),
            });

            Assert.True(first.Success);
            Assert.False(second.Success);
            Assert.Equal("SCHEDULE_OVERLAP", second.ErrorCode);
        }

        [Fact]
        public async Task CreateScheduleAsync_WhenStatusIsMissing_SavesDraft()
        {
            await SeedStoreScopeAsync();
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.CreateScheduleAsync(new CreateAttendanceScheduleDto
            {
                StoreCode = "BRI",
                UserGuid = "staff-user",
                WorkDate = new DateTime(2026, 5, 18),
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(17, 0, 0),
            });

            Assert.True(result.Success);
            Assert.Equal("Draft", result.Data!.Status);
            Assert.Equal("Draft", await _db.Queryable<AttendanceSchedule>()
                .Where(item => item.ScheduleGuid == result.Data.ScheduleGuid)
                .Select(item => item.Status)
                .FirstAsync());
        }

        [Fact]
        public async Task CreateScheduleAsync_WhenStatusIsActive_SavesDraft()
        {
            await SeedStoreScopeAsync();
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.CreateScheduleAsync(new CreateAttendanceScheduleDto
            {
                StoreCode = "BRI",
                UserGuid = "staff-user",
                WorkDate = new DateTime(2026, 5, 18),
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(17, 0, 0),
                Status = "Active",
            });

            Assert.True(result.Success);
            Assert.Equal("Draft", result.Data!.Status);
            Assert.Equal("Draft", await _db.Queryable<AttendanceSchedule>()
                .Where(item => item.ScheduleGuid == result.Data.ScheduleGuid)
                .Select(item => item.Status)
                .FirstAsync());
        }

        [Fact]
        public async Task PublishWeekAsync_WhenStoreManagerPublishesManagedStore_ActivatesOnlyDraftSchedulesInWeek()
        {
            await SeedStoreScopeAsync();
            await SeedScheduleAsync("draft-in-week", "BRI", "staff-user", new DateTime(2026, 5, 18), "Draft");
            await SeedScheduleAsync("active-in-week", "BRI", "staff-user", new DateTime(2026, 5, 19), "Active");
            await SeedScheduleAsync("cancelled-in-week", "BRI", "staff-user", new DateTime(2026, 5, 20), "Cancelled");
            await SeedScheduleAsync("draft-outside-week", "BRI", "staff-user", new DateTime(2026, 5, 25), "Draft");
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.PublishWeekAsync(new PublishAttendanceWeekDto
            {
                StoreCode = "BRI",
                WeekStartDate = new DateTime(2026, 5, 18),
            });

            Assert.True(result.Success);
            Assert.Equal(1, result.Data);
            Assert.Equal("Active", await GetScheduleStatusAsync("draft-in-week"));
            Assert.Equal("Active", await GetScheduleStatusAsync("active-in-week"));
            Assert.Equal("Cancelled", await GetScheduleStatusAsync("cancelled-in-week"));
            Assert.Equal("Draft", await GetScheduleStatusAsync("draft-outside-week"));
        }

        [Fact]
        public async Task PublishWeekAsync_WhenStoreManagerRequestsUnmanagedStore_ReturnsForbidden()
        {
            await SeedStoreScopeAsync();
            await SeedScheduleAsync("other-draft", "OTHER", "staff-user", new DateTime(2026, 5, 18), "Draft");
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.PublishWeekAsync(new PublishAttendanceWeekDto
            {
                StoreCode = "OTHER",
                WeekStartDate = new DateTime(2026, 5, 18),
            });

            Assert.False(result.Success);
            Assert.Equal("FORBIDDEN_STORE", result.ErrorCode);
            Assert.Equal("Draft", await GetScheduleStatusAsync("other-draft"));
        }

        [Fact]
        public async Task GetMyWeekAsync_ReturnsRelatedStoreActiveSchedules()
        {
            await SeedStoreScopeAsync();
            await SeedScheduleAsync("my-active", "BRI", "staff-user", new DateTime(2026, 5, 18), "Active");
            await SeedScheduleAsync("my-draft", "BRI", "staff-user", new DateTime(2026, 5, 19), "Draft");
            await SeedScheduleAsync("manager-active", "BRI", "manager-user", new DateTime(2026, 5, 20), "Active");
            var service = CreateService("staff-user", "staff", "StoreStaff");

            var result = await service.GetMyWeekAsync(new DateTime(2026, 5, 18), "BRI");

            Assert.True(result.Success);
            Assert.Collection(
                result.Data!,
                schedule =>
                {
                    Assert.Equal("my-active", schedule.ScheduleGuid);
                    Assert.True(schedule.IsMine);
                    Assert.Equal("Active", schedule.Status);
                },
                schedule =>
                {
                    Assert.Equal("manager-active", schedule.ScheduleGuid);
                    Assert.False(schedule.IsMine);
                    Assert.Equal("manager", schedule.EmployeeName);
                }
            );
        }

        [Fact]
        public async Task GetMyTodayAsync_WhenWorkDateProvided_ReturnsSelectedDateData()
        {
            await SeedStoreScopeAsync();
            await SeedScheduleAsync("selected-day", "BRI", "staff-user", new DateTime(2026, 5, 19), "Active");
            await SeedScheduleAsync("other-day", "BRI", "staff-user", new DateTime(2026, 5, 20), "Active");
            await _db.Insertable(new AttendancePunch
            {
                PunchGuid = "selected-punch",
                StoreCode = "BRI",
                UserGuid = "staff-user",
                WorkDate = new DateTime(2026, 5, 19),
                PunchType = "ClockIn",
                PunchTimeUtc = DateTime.Parse("2026-05-18T23:00:00Z").ToUniversalTime(),
                PunchTimeLocal = new DateTime(2026, 5, 19, 9, 0, 0),
                Status = "Normal",
                CreatedAt = DateTime.UtcNow,
            }).ExecuteCommandAsync();
            await _db.Insertable(new AttendanceStoreHoliday
            {
                HolidayGuid = "selected-holiday",
                StoreCode = "BRI",
                HolidayDate = new DateTime(2026, 5, 19),
                HolidayName = "Selected holiday",
                BusinessStatus = "Open",
                CreatedAt = DateTime.UtcNow,
            }).ExecuteCommandAsync();
            var service = CreateService("staff-user", "staff", "StoreStaff");

            var result = await service.GetMyTodayAsync(new DateTime(2026, 5, 19), "BRI");

            Assert.True(result.Success);
            Assert.Equal("2026-05-19", result.Data!.WorkDate.ToString("yyyy-MM-dd"));
            Assert.Equal("selected-day", Assert.Single(result.Data.Schedules).ScheduleGuid);
            Assert.Equal("selected-punch", Assert.Single(result.Data.Punches).PunchGuid);
            Assert.Equal("selected-holiday", Assert.Single(result.Data.Holidays).HolidayGuid);
        }

        [Fact]
        public async Task PunchAsync_WhenOnlyDraftScheduleExists_TreatsPunchAsNoSchedule()
        {
            await SeedStoreScopeAsync();
            await SeedScheduleAsync("draft-schedule", "BRI", "staff-user", new DateTime(2026, 5, 18), "Draft");
            var service = CreateService("staff-user", "staff", "StoreStaff");

            var result = await service.PunchAsync(CreatePunchRequest(
                "ClockIn",
                "2026-05-17T23:00:00Z"
            ));

            Assert.True(result.Success);
            Assert.Equal("NoSchedule", result.Data!.Status);
            Assert.Null(result.Data.ScheduleGuid);
        }

        [Theory]
        [InlineData("Australia/Brisbane", "2026-05-20T14:30:00Z", "2026-05-21")]
        [InlineData("Australia/Melbourne", "2026-05-20T14:30:00Z", "2026-05-21")]
        [InlineData("Unexpected/Zone", "2026-05-20T14:30:00Z", "2026-05-21")]
        public async Task PunchAsync_StoresLocalTimeAndWorkDateByResolvedStoreTimeZone(
            string storeTimeZone,
            string punchTimeUtc,
            string expectedWorkDate
        )
        {
            await SeedStoreScopeAsync();
            var service = CreateService("staff-user", "staff", "StoreStaff");

            var result = await service.PunchAsync(CreatePunchRequest(
                "ClockIn",
                punchTimeUtc,
                storeTimeZone
            ));

            Assert.True(result.Success);
            Assert.Equal(expectedWorkDate, result.Data!.WorkDate.ToString("yyyy-MM-dd"));
            Assert.Equal("Australia/Brisbane", result.Data.StoreTimeZone);
        }

        [Fact]
        public async Task PunchAsync_WhenBrisbaneStoreTimeZoneMissing_UsesBrisbaneTimeZone()
        {
            await SeedStoreScopeAsync();
            var service = CreateService("staff-user", "staff", "StoreStaff");
            var now = DateTime.Parse("2026-01-01T13:30:00Z").ToUniversalTime();
            _timeProvider.SetUtcNow(now);

            var result = await service.PunchAsync(new AttendancePunchRequestDto
            {
                QrToken = CreateQrToken(now),
                StoreCode = "BRI",
                PunchType = "ClockIn",
                PunchTimeUtc = now,
                LocationLatitude = -27.4698,
                LocationLongitude = 153.0251,
                LocationAccuracy = 12.5,
                LocationPermissionStatus = "granted",
                LocationCapturedAtUtc = DateTime.UtcNow,
            });

            Assert.True(result.Success, $"{result.ErrorCode}: {result.Message}");
            Assert.Equal("Australia/Brisbane", result.Data!.StoreTimeZone);
            Assert.Equal("2026-01-01", result.Data.WorkDate.ToString("yyyy-MM-dd"));
        }

        [Theory]
        [InlineData("ClockIn", "2026-05-18T00:20:00Z", "Late")]
        [InlineData("ClockOut", "2026-05-18T06:40:00Z", "EarlyLeave")]
        [InlineData("ClockIn", "2026-05-19T00:00:00Z", "NoSchedule")]
        public async Task PunchAsync_WhenExceptionalStatus_CreatesPendingApproval(
            string punchType,
            string punchTimeUtc,
            string expectedStatus
        )
        {
            await SeedStoreScopeAsync();
            var service = CreateService("staff-user", "staff", "StoreStaff");
            await SeedScheduleAsync();
            var request = CreatePunchRequest(punchType, punchTimeUtc);
            if (punchType == "ClockOut")
            {
                await _db.Insertable(new AttendancePunch
                {
                    PunchGuid = Guid.NewGuid().ToString(),
                    StoreCode = "BRI",
                    UserGuid = "staff-user",
                    WorkDate = DateTime.Parse(punchTimeUtc).ToUniversalTime().AddHours(10).Date,
                    StoreTimeZone = "Australia/Brisbane",
                    PunchType = "ClockIn",
                    PunchTimeUtc = DateTime.Parse(punchTimeUtc).ToUniversalTime().AddHours(-8),
                    PunchTimeLocal = DateTime.Parse(punchTimeUtc).ToUniversalTime().AddHours(2),
                    Status = "Normal",
                    CreatedAt = DateTime.UtcNow,
                }).ExecuteCommandAsync();
            }

            var result = await service.PunchAsync(request);

            Assert.True(result.Success);
            Assert.Equal(expectedStatus, result.Data!.Status);
            Assert.Equal(1, await _db.Queryable<AttendanceApproval>().CountAsync());
        }

        [Fact]
        public async Task PunchAsync_WhenLocationMissing_ReturnsLocationRequired()
        {
            await SeedStoreScopeAsync();
            var service = CreateService("staff-user", "staff", "StoreStaff");
            var now = DateTime.UtcNow;
            _timeProvider.SetUtcNow(now);

            var result = await service.PunchAsync(new AttendancePunchRequestDto
            {
                QrToken = CreateQrToken(now),
                StoreCode = "BRI",
                StoreTimeZone = "Australia/Brisbane",
                PunchType = "ClockIn",
                PunchTimeUtc = DateTime.Parse("2026-05-18T00:00:00Z").ToUniversalTime(),
            });

            Assert.False(result.Success);
            Assert.Equal("LOCATION_REQUIRED", result.ErrorCode);
        }

        [Fact]
        public async Task PunchAsync_WhenLocationCapturedAtIsStale_ReturnsLocationRequired()
        {
            await SeedStoreScopeAsync();
            var service = CreateService("staff-user", "staff", "StoreStaff");
            var now = DateTime.UtcNow;
            _timeProvider.SetUtcNow(now);

            var result = await service.PunchAsync(new AttendancePunchRequestDto
            {
                QrToken = CreateQrToken(now),
                StoreCode = "BRI",
                StoreTimeZone = "Australia/Brisbane",
                PunchType = "ClockIn",
                PunchTimeUtc = now,
                LocationLatitude = -27.4698,
                LocationLongitude = 153.0251,
                LocationAccuracy = 12.5,
                LocationPermissionStatus = "granted",
                LocationCapturedAtUtc = now.AddMinutes(-10),
            });

            Assert.False(result.Success);
            Assert.Equal("LOCATION_REQUIRED", result.ErrorCode);
            Assert.Equal("定位采集时间无效", result.Message);
        }

        [Fact]
        public async Task PunchAsync_WhenLocationProvided_StoresLocationAuditFields()
        {
            await SeedStoreScopeAsync();
            var service = CreateService("staff-user", "staff", "StoreStaff");
            var capturedAt = DateTime.UtcNow;
            _timeProvider.SetUtcNow(capturedAt);

            var result = await service.PunchAsync(new AttendancePunchRequestDto
            {
                QrToken = CreateQrToken(capturedAt),
                StoreCode = "BRI",
                StoreTimeZone = "Australia/Brisbane",
                PunchType = "ClockIn",
                PunchTimeUtc = DateTime.Parse("2026-05-18T00:00:00Z").ToUniversalTime(),
                DeviceId = "hbmobile-test-device",
                LocationLatitude = -27.4698,
                LocationLongitude = 153.0251,
                LocationAccuracy = 12.5,
                LocationPermissionStatus = "granted",
                LocationCapturedAtUtc = capturedAt,
            });

            Assert.True(result.Success);
            Assert.Equal(-27.4698, result.Data!.LocationLatitude);
            Assert.Equal(153.0251, result.Data.LocationLongitude);
            Assert.Equal(12.5, result.Data.LocationAccuracy);
            Assert.Equal("granted", result.Data.LocationPermissionStatus);
            Assert.Equal(capturedAt, result.Data.LocationCapturedAtUtc);

            var stored = await _db.Queryable<AttendancePunch>()
                .FirstAsync(item => item.PunchGuid == result.Data.PunchGuid);
            Assert.NotNull(stored);
            Assert.Equal("hbmobile-test-device", stored.DeviceId);
            Assert.Equal(-27.4698, stored.LocationLatitude);
            Assert.Equal(153.0251, stored.LocationLongitude);
            Assert.Equal(12.5, stored.LocationAccuracyMeters);
            Assert.Equal(capturedAt, stored.LocationCapturedAtUtc);
        }

        [Fact]
        public async Task CreateLocationSampleAsync_WhenStoreAllowed_SavesShiftSample()
        {
            await SeedStoreScopeAsync();
            var service = CreateService("staff-user", "staff", "StoreStaff");
            var capturedAt = DateTime.UtcNow;

            var result = await service.CreateLocationSampleAsync(new AttendanceLocationSampleRequestDto
            {
                StoreCode = "BRI",
                HardwareId = "hbmobile-test-device",
                SystemDeviceNumber = "SYS-001",
                DeviceSystem = "iOS",
                EventType = "ShiftSample",
                LocationLatitude = -27.4705,
                LocationLongitude = 153.0260,
                LocationAccuracy = 18.2,
                LocationPermissionStatus = "granted",
                LocationCapturedAtUtc = capturedAt,
            });

            Assert.True(result.Success);
            Assert.Equal("staff-user", result.Data!.UserGuid);
            Assert.Equal("BRI", result.Data.StoreCode);
            Assert.Equal("SYS-001", result.Data.SystemDeviceNumber);
            Assert.Equal("ShiftSample", result.Data.EventType);
            Assert.Equal(-27.4705, result.Data.LocationLatitude);
            Assert.Equal(153.0260, result.Data.LocationLongitude);
            Assert.Equal(18.2, result.Data.LocationAccuracy);
            Assert.Equal(capturedAt, result.Data.LocationCapturedAtUtc);

            var stored = await _db.Queryable<AttendanceLocationSample>()
                .FirstAsync(item => item.SampleGuid == result.Data.SampleGuid);
            Assert.NotNull(stored);
            Assert.Equal("hbmobile-test-device", stored.HardwareId);
            Assert.Equal("iOS", stored.DeviceSystem);
        }

        [Fact]
        public async Task GetLocationSamplesAsync_WhenDateIsStoreLocalDate_ReturnsBrisbaneEarlySample()
        {
            await SeedStoreScopeAsync();
            var service = CreateService("manager-user", "manager", "StoreManager");
            var capturedAtUtc = DateTime.Parse("2026-01-01T13:20:00Z").ToUniversalTime();

            await _db.Insertable(new AttendanceLocationSample
            {
                SampleGuid = "brisbane-early-sample",
                UserGuid = "staff-user",
                StoreCode = "BRI",
                EventType = "ShiftSample",
                LocationLatitude = -27.4705,
                LocationLongitude = 153.0260,
                LocationCapturedAtUtc = capturedAtUtc,
                CreatedAt = capturedAtUtc,
            }).ExecuteCommandAsync();

            var result = await service.GetLocationSamplesAsync(new AttendanceLocationSampleQueryDto
            {
                StoreCode = "BRI",
                UserGuid = "staff-user",
                FromDate = new DateTime(2026, 1, 1),
                ToDate = new DateTime(2026, 1, 1),
            });

            Assert.True(result.Success);
            var sample = Assert.Single(result.Data!);
            Assert.Equal("brisbane-early-sample", sample.SampleGuid);
            Assert.Equal(capturedAtUtc, sample.LocationCapturedAtUtc);
        }

        [Fact]
        public async Task CreateManagedLeaveRequestAsync_WhenValidAnnualLeave_CreatesPendingLeaveAndApproval()
        {
            await SeedStoreScopeAsync();
            await SeedEmployeeProfileAsync("staff-user", EmployeeType.FullTime);
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.CreateManagedLeaveRequestAsync(new CreateManagedAttendanceLeaveRequestDto
            {
                StoreCode = "BRI",
                UserGuid = "staff-user",
                LeaveType = "AnnualLeave",
                StartDate = new DateTime(2026, 5, 18),
                EndDate = new DateTime(2026, 5, 19),
                Reason = "Family trip",
            });

            Assert.True(result.Success);
            Assert.Equal("Pending", result.Data!.Status);
            Assert.Equal("staff-user", result.Data.UserGuid);

            var leave = await _db.Queryable<AttendanceLeaveRequest>()
                .FirstAsync(item => item.LeaveGuid == result.Data.LeaveGuid);
            Assert.NotNull(leave);
            Assert.Equal("Pending", leave.Status);

            var approval = await _db.Queryable<AttendanceApproval>()
                .FirstAsync(item => item.SourceType == "Leave" && item.SourceGuid == result.Data.LeaveGuid);
            Assert.NotNull(approval);
            Assert.Equal("Pending", approval.ReviewStatus);
            Assert.Equal("staff-user", approval.ApplicantUserGuid);
        }

        [Fact]
        public async Task CreateManagedLeaveRequestAsync_WhenEmployeeEmploymentTypeIsCasual_ReturnsValidationError()
        {
            await SeedStoreScopeAsync();
            await SeedEmployeeProfileAsync("staff-user", EmployeeType.Temporary);
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.CreateManagedLeaveRequestAsync(new CreateManagedAttendanceLeaveRequestDto
            {
                StoreCode = "BRI",
                UserGuid = "staff-user",
                LeaveType = "AnnualLeave",
                StartDate = new DateTime(2026, 5, 18),
                EndDate = new DateTime(2026, 5, 18),
            });

            Assert.False(result.Success);
            Assert.Equal("INVALID_EMPLOYMENT_TYPE", result.ErrorCode);
        }

        [Fact]
        public async Task CreateManagedLeaveRequestAsync_WhenSickLeaveHasNoAttachment_ReturnsValidationError()
        {
            await SeedStoreScopeAsync();
            await SeedEmployeeProfileAsync("staff-user", EmployeeType.PartTime);
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.CreateManagedLeaveRequestAsync(new CreateManagedAttendanceLeaveRequestDto
            {
                StoreCode = "BRI",
                UserGuid = "staff-user",
                LeaveType = "SickLeave",
                StartDate = new DateTime(2026, 5, 18),
                EndDate = new DateTime(2026, 5, 18),
            });

            Assert.False(result.Success);
            Assert.Equal("ATTACHMENT_REQUIRED", result.ErrorCode);
        }

        [Fact]
        public async Task CreateManagedLeaveRequestAsync_WhenEmployeeNotInRequestedStore_ReturnsForbidden()
        {
            await SeedStoreScopeAsync();
            await SeedEmployeeProfileAsync("staff-user", EmployeeType.FullTime);
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.CreateManagedLeaveRequestAsync(new CreateManagedAttendanceLeaveRequestDto
            {
                StoreCode = "OTHER",
                UserGuid = "staff-user",
                LeaveType = "AnnualLeave",
                StartDate = new DateTime(2026, 5, 18),
                EndDate = new DateTime(2026, 5, 18),
            });

            Assert.False(result.Success);
            Assert.Equal("FORBIDDEN_STORE", result.ErrorCode);
        }

        [Fact]
        public async Task GetLeaveAttachmentUploadSignatureAsync_WhenFileNameProvided_ReturnsAttendanceObjectKey()
        {
            await SeedStoreScopeAsync();
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.GetLeaveAttachmentUploadSignatureAsync(new DirectUploadRequest
            {
                FileName = "medical certificate (May).pdf",
                ContentType = "application/pdf",
                FileSize = 2048,
            });

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.StartsWith("attendance/leave-attachments/", result.Data!.ObjectKey);
            Assert.EndsWith("-medical-certificate-May.pdf", result.Data.ObjectKey);
            Assert.Equal("application/pdf", result.Data.Headers["Content-Type"]);
            Assert.Contains(result.Data.ObjectKey, result.Data.Url);
            var signTimeMatch = Regex.Match(Uri.UnescapeDataString(result.Data.Url), @"q-sign-time=(\d+);(\d+)");
            Assert.True(signTimeMatch.Success);
            Assert.True(
                long.Parse(signTimeMatch.Groups[2].Value) - long.Parse(signTimeMatch.Groups[1].Value) <= 3600
            );
        }

        [Fact]
        public async Task CreateManagedLeaveRequestAsync_WhenSickLeaveAttachmentIsExternal_ReturnsValidationError()
        {
            await SeedStoreScopeAsync();
            await SeedEmployeeProfileAsync("staff-user", EmployeeType.PartTime);
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.CreateManagedLeaveRequestAsync(new CreateManagedAttendanceLeaveRequestDto
            {
                StoreCode = "BRI",
                UserGuid = "staff-user",
                LeaveType = "SickLeave",
                StartDate = new DateTime(2026, 5, 18),
                EndDate = new DateTime(2026, 5, 18),
                AttachmentUrl = "https://example.com/medical.jpg",
            });

            Assert.False(result.Success);
            Assert.Equal("INVALID_ATTACHMENT_URL", result.ErrorCode);
        }

        [Fact]
        public async Task CreateScheduleAsync_WhenPartialHolidayExists_DoesNotBlockSchedule()
        {
            await SeedStoreScopeAsync();
            await _db.Insertable(new AttendanceStoreHoliday
            {
                HolidayGuid = "holiday-partial",
                StoreCode = "BRI",
                HolidayDate = new DateTime(2026, 5, 18),
                HolidayName = "Partial trading day",
                BusinessStatus = "Partial",
                OpenTime = new TimeSpan(10, 0, 0),
                CloseTime = new TimeSpan(16, 0, 0),
                CreatedAt = DateTime.UtcNow,
            }).ExecuteCommandAsync();
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.CreateScheduleAsync(new CreateAttendanceScheduleDto
            {
                StoreCode = "BRI",
                UserGuid = "staff-user",
                WorkDate = new DateTime(2026, 5, 18),
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(17, 0, 0),
            });

            Assert.True(result.Success);
        }

        [Fact]
        public async Task BatchUpsertHolidaysAsync_WhenStoresAreManaged_CreatesHolidayForEachStore()
        {
            await SeedStoreScopeAsync();
            await SeedManagerStoreAccessAsync("manager-user", "store-other");
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.BatchUpsertHolidaysAsync(new BatchUpsertAttendanceStoreHolidayDto
            {
                StoreCodes = new List<string> { "BRI", "OTHER" },
                HolidayDate = new DateTime(2026, 12, 25),
                HolidayName = "Christmas Day",
                BusinessStatus = "Closed",
                IsPaidHoliday = true,
                Remark = "Batch created",
            });

            Assert.True(result.Success);
            Assert.Equal(2, result.Data!.CreatedCount);
            Assert.Equal(0, result.Data.UpdatedCount);
            Assert.Equal(2, result.Data.Items.Count);
            Assert.Equal(2, await CountActiveHolidaysAsync(new DateTime(2026, 12, 25)));
        }

        [Fact]
        public async Task BatchUpsertHolidaysAsync_WhenHolidayAlreadyExists_UpdatesExistingRow()
        {
            await SeedStoreScopeAsync();
            await _db.Insertable(new AttendanceStoreHoliday
            {
                HolidayGuid = "existing-holiday",
                StoreCode = "BRI",
                HolidayDate = new DateTime(2026, 12, 25),
                HolidayName = "Old Christmas",
                BusinessStatus = "Open",
                IsPaidHoliday = false,
                CreatedAt = DateTime.UtcNow,
            }).ExecuteCommandAsync();
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.BatchUpsertHolidaysAsync(new BatchUpsertAttendanceStoreHolidayDto
            {
                StoreCodes = new List<string> { "BRI" },
                HolidayDate = new DateTime(2026, 12, 25),
                HolidayName = "Christmas Day",
                BusinessStatus = "Partial",
                OpenTime = new TimeSpan(10, 0, 0),
                CloseTime = new TimeSpan(16, 0, 0),
                IsPaidHoliday = true,
                Remark = "Updated",
            });

            Assert.True(result.Success);
            Assert.Equal(0, result.Data!.CreatedCount);
            Assert.Equal(1, result.Data.UpdatedCount);
            var row = await _db.Queryable<AttendanceStoreHoliday>()
                .FirstAsync(item => item.HolidayGuid == "existing-holiday");
            Assert.Equal("Christmas Day", row.HolidayName);
            Assert.Equal("Partial", row.BusinessStatus);
            Assert.True(row.IsPaidHoliday);
            Assert.Equal(1, await CountActiveHolidaysAsync(new DateTime(2026, 12, 25)));
        }

        [Fact]
        public async Task BatchUpsertHolidaysAsync_WhenAnyStoreIsForbidden_RollsBackEntireBatch()
        {
            await SeedStoreScopeAsync();
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.BatchUpsertHolidaysAsync(new BatchUpsertAttendanceStoreHolidayDto
            {
                StoreCodes = new List<string> { "BRI", "OTHER" },
                HolidayDate = new DateTime(2026, 12, 25),
                HolidayName = "Christmas Day",
                BusinessStatus = "Closed",
                IsPaidHoliday = true,
            });

            Assert.False(result.Success);
            Assert.Equal("FORBIDDEN_STORE", result.ErrorCode);
            Assert.Equal(0, await CountActiveHolidaysAsync(new DateTime(2026, 12, 25)));
        }

        [Fact]
        public async Task BatchUpsertHolidaysAsync_WhenDuplicateActiveRowsExist_UpdatesOneAndSoftDeletesExtras()
        {
            await SeedStoreScopeAsync();
            await _db.Insertable(new[]
            {
                new AttendanceStoreHoliday
                {
                    HolidayGuid = "duplicate-a",
                    StoreCode = "BRI",
                    HolidayDate = new DateTime(2026, 12, 25),
                    HolidayName = "Duplicate A",
                    BusinessStatus = "Open",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-2),
                },
                new AttendanceStoreHoliday
                {
                    HolidayGuid = "duplicate-b",
                    StoreCode = "BRI",
                    HolidayDate = new DateTime(2026, 12, 25),
                    HolidayName = "Duplicate B",
                    BusinessStatus = "Open",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-1),
                },
            }).ExecuteCommandAsync();
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.BatchUpsertHolidaysAsync(new BatchUpsertAttendanceStoreHolidayDto
            {
                StoreCodes = new List<string> { "BRI" },
                HolidayDate = new DateTime(2026, 12, 25),
                HolidayName = "Christmas Day",
                BusinessStatus = "Closed",
                IsPaidHoliday = true,
            });

            Assert.True(result.Success);
            Assert.Equal(0, result.Data!.CreatedCount);
            Assert.Equal(1, result.Data.UpdatedCount);
            Assert.Equal(1, await CountActiveHolidaysAsync(new DateTime(2026, 12, 25)));
            Assert.Equal(1, await _db.Queryable<AttendanceStoreHoliday>()
                .Where(item =>
                    item.HolidayDate >= new DateTime(2026, 12, 25)
                    && item.HolidayDate < new DateTime(2026, 12, 26)
                    && item.IsDeleted
                )
                .CountAsync());
        }

        public void Dispose()
        {
            _db.Dispose();
            _sqliteConnection.Dispose();

            if (File.Exists(_dbPath))
            {
                SqliteTempFileCleanup.DeleteIfExists(_dbPath);
            }
        }

        private async Task SeedAttendanceSigningKeyAsync()
        {
            await _db.Insertable(new AttendancePosQrKey
            {
                Kid = "K1",
                Algorithm = "A256GCM",
                ProtectedKey = _attendanceProtector.Protect(_attendanceKey),
                StoreCode = "BRI",
                DeviceCode = "POS-001",
                HardwareId = "HW-001",
                Status = "Active",
                RegisteredAtUtc = DateTime.UtcNow,
            }).ExecuteCommandAsync();
        }

        private AttendancePunchRequestDto CreateQrPunchRequest(DateTime now, Guid tokenId)
        {
            _timeProvider.SetUtcNow(now);
            return new AttendancePunchRequestDto
            {
                QrToken = AttendanceQrTokenCodec.Encrypt(new AttendanceQrTokenPayload
                {
                    TokenId = tokenId,
                    StoreCode = "BRI",
                    DeviceCode = "POS-001",
                    IssuedAtUtc = now,
                }, "K1", _attendanceKey),
                LocationLatitude = -27.4698,
                LocationLongitude = 153.0251,
                LocationAccuracy = 12.5,
                LocationPermissionStatus = "granted",
                LocationCapturedAtUtc = now,
            };
        }

        private AttendancePunchRequestDto CreatePunchRequest(
            string punchType,
            string punchTimeUtc,
            string storeTimeZone = "Australia/Brisbane",
            string storeCode = "BRI"
        )
        {
            var punchAtUtc = DateTime.Parse(punchTimeUtc).ToUniversalTime();
            _timeProvider.SetUtcNow(punchAtUtc);
            return new AttendancePunchRequestDto
            {
                QrToken = CreateQrToken(punchAtUtc),
                StoreCode = storeCode,
                StoreTimeZone = storeTimeZone,
                PunchType = punchType,
                PunchTimeUtc = punchAtUtc,
                LocationLatitude = -27.4698,
                LocationLongitude = 153.0251,
                LocationAccuracy = 12.5,
                LocationPermissionStatus = "granted",
                LocationCapturedAtUtc = DateTime.UtcNow,
            };
        }

        private AttendanceReactService CreateService(string userGuid, string username, params string[] roles)
            => CreateService(userGuid, username, true, roles);

        private AttendanceReactService CreateService(
            string userGuid,
            string username,
            bool posDeviceActive,
            params string[] roles)
        {
            var httpContextAccessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(CreateClaims(userGuid, username, roles), "TestAuth")
                    ),
                },
            };
            var currentUserService = new CurrentUserService(httpContextAccessor);
            var context = CreateSqlSugarContext(_db);
            var scopeService = new CurrentUserManageableStoreScopeService(
                context,
                currentUserService,
                httpContextAccessor
            );
            var uploadService = new TencentCloudUploadService(
                Options.Create(new TencentCloudSettings
                {
                    SecretId = "secret-id",
                    SecretKey = "secret-key",
                    BucketName = "test-bucket-123",
                    Region = "ap-guangzhou",
                }),
                NullLogger<TencentCloudUploadService>.Instance,
                new HttpClient()
            );

            return new AttendanceReactService(
                context,
                currentUserService,
                scopeService,
                httpContextAccessor,
                NullLogger<AttendanceReactService>.Instance,
                uploadService,
                new StubAttendancePosDeviceStatusProvider(posDeviceActive),
                _timeProvider,
                _attendanceProtector,
                _punchAuthorizationProtector
            );
        }

        private static DateTime TruncateToMilliseconds(DateTime value) =>
            new(value.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);

        private static string MutateProtectedToken(string token)
        {
            var index = token.Length / 2;
            var replacement = token[index] == 'A' ? 'B' : 'A';
            return token[..index] + replacement + token[(index + 1)..];
        }

        private string CreateQrToken(DateTime now, string storeCode = "BRI", string deviceCode = "POS-001") =>
            AttendanceQrTokenCodec.Encrypt(new AttendanceQrTokenPayload
            {
                TokenId = Guid.NewGuid(),
                StoreCode = storeCode,
                DeviceCode = deviceCode,
                IssuedAtUtc = now,
            }, "K1", _attendanceKey);

        private sealed class StubAttendancePosDeviceStatusProvider(bool isActive)
            : IAttendancePosDeviceStatusProvider
        {
            public Task<bool> IsActiveAsync(
                string deviceCode,
                string storeCode,
                string hardwareId,
                CancellationToken cancellationToken = default) => Task.FromResult(isActive);
        }

        private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
        {
            private DateTimeOffset _utcNow = utcNow;
            public override DateTimeOffset GetUtcNow() => _utcNow;
            public void SetUtcNow(DateTime utcNow) => _utcNow = new DateTimeOffset(utcNow.ToUniversalTime());
        }

        private static SqlException CreateSqlException(int number)
        {
            var error = (SqlError)RuntimeHelpers.GetUninitializedObject(typeof(SqlError));
            typeof(SqlError).GetField("_number", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(error, number);
            var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
            typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(errors, new object[] { error });
            var exception = (SqlException)RuntimeHelpers.GetUninitializedObject(typeof(SqlException));
            typeof(SqlException).GetField("_errors", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(exception, errors);
            return exception;
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "services")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new DirectoryNotFoundException("找不到仓库根目录");
        }

        private async Task SeedStoreScopeAsync()
        {
            await _db.Insertable(
                new[]
                {
                    new Store
                    {
                        StoreGUID = "store-bri",
                        StoreCode = "BRI",
                        StoreName = "Brisbane",
                        CreatedAt = DateTime.UtcNow,
                    },
                    new Store
                    {
                        StoreGUID = "store-other",
                        StoreCode = "OTHER",
                        StoreName = "Other",
                        CreatedAt = DateTime.UtcNow,
                    },
                }
            ).ExecuteCommandAsync();

            await _db.Insertable(
                new[]
                {
                    new User
                    {
                        UserGUID = "manager-user",
                        Username = "manager",
                        Email = "manager@example.com",
                        PasswordHash = "hash",
                        CreatedAt = DateTime.UtcNow,
                    },
                    new User
                    {
                        UserGUID = "staff-user",
                        Username = "staff",
                        Email = "staff@example.com",
                        PasswordHash = "hash",
                        CreatedAt = DateTime.UtcNow,
                    },
                }
            ).ExecuteCommandAsync();

            await _db.Insertable(
                new[]
                {
                    new UserStore
                    {
                        UserStoreGUID = "manager-store-bri",
                        UserGUID = "manager-user",
                        StoreGUID = "store-bri",
                        IsPrimary = true,
                        CreatedAt = DateTime.UtcNow,
                    },
                    new UserStore
                    {
                        UserStoreGUID = "staff-store-bri",
                        UserGUID = "staff-user",
                        StoreGUID = "store-bri",
                        CreatedAt = DateTime.UtcNow,
                    },
                }
            ).ExecuteCommandAsync();
        }

        private async Task SeedManagerStoreAccessAsync(string userGuid, string storeGuid)
        {
            await _db.Insertable(new UserStore
            {
                UserStoreGUID = $"{userGuid}-{storeGuid}",
                UserGUID = userGuid,
                StoreGUID = storeGuid,
                IsPrimary = true,
                CreatedAt = DateTime.UtcNow,
            }).ExecuteCommandAsync();
        }

        private async Task SeedEmployeeProfileAsync(string userGuid, EmployeeType? employeeType)
        {
            await _db.Insertable(new EmployeeProfile
            {
                UserGUID = userGuid,
                EmployeeType = employeeType,
                CreatedAt = DateTime.UtcNow,
            }).ExecuteCommandAsync();
        }

        private async Task<int> CountActiveHolidaysAsync(DateTime holidayDate)
        {
            return await _db.Queryable<AttendanceStoreHoliday>()
                .Where(item =>
                    item.HolidayDate >= holidayDate.Date
                    && item.HolidayDate < holidayDate.Date.AddDays(1)
                    && !item.IsDeleted
                )
                .CountAsync();
        }

        private async Task SeedScheduleAsync()
        {
            await SeedScheduleAsync(
                "schedule-1",
                "BRI",
                "staff-user",
                new DateTime(2026, 5, 18),
                "Active"
            );
        }

        private async Task SeedScheduleAsync(
            string scheduleGuid,
            string storeCode,
            string userGuid,
            DateTime workDate,
            string status
        )
        {
            await _db.Insertable(new AttendanceSchedule
            {
                ScheduleGuid = scheduleGuid,
                StoreCode = storeCode,
                UserGuid = userGuid,
                WorkDate = workDate,
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(17, 0, 0),
                Status = status,
                CreatedAt = DateTime.UtcNow,
            }).ExecuteCommandAsync();
        }

        private async Task<string> GetScheduleStatusAsync(string scheduleGuid)
        {
            return await _db.Queryable<AttendanceSchedule>()
                .Where(item => item.ScheduleGuid == scheduleGuid)
                .Select(item => item.Status)
                .FirstAsync();
        }

        private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
        {
            var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(
                typeof(SqlSugarContext)
            );

            var dbField = typeof(SqlSugarContext).GetField(
                "_db",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            dbField!.SetValue(context, db);

            return context;
        }

        private static IEnumerable<Claim> CreateClaims(
            string userGuid,
            string username,
            IEnumerable<string> roles
        )
        {
            yield return new Claim("userGuid", userGuid);
            yield return new Claim("userId", userGuid);
            yield return new Claim(ClaimTypes.NameIdentifier, userGuid);
            yield return new Claim(ClaimTypes.Name, username);

            foreach (var role in roles)
            {
                yield return new Claim(ClaimTypes.Role, role);
            }
        }
    }
}
