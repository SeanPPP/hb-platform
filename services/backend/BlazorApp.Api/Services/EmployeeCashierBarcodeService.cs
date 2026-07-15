using System.Security.Cryptography;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    public sealed class EmployeeCashierBarcodeService
    {
        private readonly SqlSugarContext _context;
        private readonly ICurrentUserService _currentUser;
        private readonly Func<string> _barcodeFactory;

        public EmployeeCashierBarcodeService(
            SqlSugarContext context,
            ICurrentUserService currentUser,
            Func<string>? barcodeFactory = null
        )
        {
            _context = context;
            _currentUser = currentUser;
            _barcodeFactory = barcodeFactory ?? GenerateBarcode;
        }

        public async Task<ApiResponse<EmployeeCashierBarcodeDto>> GetAsync()
        {
            var userGuid = _currentUser.GetCurrentUserGuid();
            if (string.IsNullOrWhiteSpace(userGuid))
            {
                return ApiResponse<EmployeeCashierBarcodeDto>.Error("未找到当前用户", "CURRENT_USER_NOT_FOUND");
            }
            var entity = await _context.Db.Queryable<EmployeeCashierBarcode>()
                .Where(item => item.UserGUID == userGuid && item.Status)
                .OrderBy(item => item.CreatedAt, OrderByType.Desc)
                .FirstAsync();
            return ApiResponse<EmployeeCashierBarcodeDto>.OK(Map(entity));
        }

        public async Task<ApiResponse<EmployeeCashierBarcodeDto>> RefreshAsync()
        {
            var userGuid = _currentUser.GetCurrentUserGuid();
            if (string.IsNullOrWhiteSpace(userGuid))
            {
                return ApiResponse<EmployeeCashierBarcodeDto>.Error("未找到当前用户", "CURRENT_USER_NOT_FOUND");
            }
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    return await RefreshOnceAsync(userGuid);
                }
                catch (Exception ex) when (
                    attempt < 4 && IsUniqueConstraintViolation(ex)
                )
                {
                    // 极低概率的跨用户随机碰撞由数据库唯一约束兜底，回滚后重新生成。
                }
            }
            throw new InvalidOperationException("无法生成唯一员工收银条码");
        }

        private async Task<ApiResponse<EmployeeCashierBarcodeDto>> RefreshOnceAsync(string userGuid)
        {
            var db = _context.Db;
            await db.Ado.BeginTranAsync();
            try
            {
                await CashierBarcodeMutationLock.AcquireAsync(db);

                var barcode = _barcodeFactory();
                var barcodeHguid = Guid.NewGuid().ToString("N");
                // 关键逻辑：独立占用表是全历史唯一性的硬保证，必须先占位再改有效记录。
                await db.Insertable(new CashierBarcodeReservation
                {
                    Barcode = barcode,
                    CreatedAt = DateTime.UtcNow,
                    OwnerType = "employee",
                    OwnerId = barcodeHguid,
                }).ExecuteCommandAsync();
                var actor = _currentUser.GetCurrentUsername();
                var now = DateTime.UtcNow;
                // 关键逻辑：个人条码启用时同步停用同用户 legacy 条码，跨表也只能有一个有效身份。
                await db.Updateable<CashRegisterUser>()
                    .SetColumns(item => new CashRegisterUser
                    {
                        Status = false,
                        LastModifier = actor,
                        LastModifyDate = now,
                    })
                    .Where(item => item.UserGUID == userGuid && item.Status)
                    .ExecuteCommandAsync();
                await db.Updateable<EmployeeCashierBarcode>()
                    .SetColumns(item => new EmployeeCashierBarcode
                    {
                        Status = false,
                        UpdatedBy = actor,
                        UpdatedAt = now,
                    })
                    .Where(item => item.UserGUID == userGuid && item.Status)
                    .ExecuteCommandAsync();

                var entity = new EmployeeCashierBarcode
                {
                    HGUID = barcodeHguid,
                    UserGUID = userGuid,
                    Barcode = barcode,
                    PrintCount = 0,
                    Status = true,
                    CreatedAt = now,
                    UpdatedBy = actor,
                    UpdatedAt = now,
                };
                await db.Insertable(entity).ExecuteCommandAsync();
                await db.Ado.CommitTranAsync();
                return ApiResponse<EmployeeCashierBarcodeDto>.OK(Map(entity), "条码已刷新");
            }
            catch
            {
                await db.Ado.RollbackTranAsync();
                throw;
            }
        }

        public async Task<ApiResponse<EmployeeCashierBarcodeDto>> ConfirmPrintAsync(
            EmployeeCashierBarcodePrintConfirmationRequest request
        )
        {
            var userGuid = _currentUser.GetCurrentUserGuid();
            if (string.IsNullOrWhiteSpace(userGuid))
            {
                return ApiResponse<EmployeeCashierBarcodeDto>.Error("未找到当前用户", "CURRENT_USER_NOT_FOUND");
            }
            if (request.PrintAttemptId == Guid.Empty)
            {
                return ApiResponse<EmployeeCashierBarcodeDto>.Error(
                    "打印尝试编号无效",
                    "INVALID_PRINT_ATTEMPT_ID"
                );
            }
            var confirmedAttempt = await _context.Db.Queryable<EmployeeCashierBarcodePrintAttempt>()
                .FirstAsync(item => item.PrintAttemptId == request.PrintAttemptId);
            if (confirmedAttempt is not null)
            {
                if (confirmedAttempt.UserGUID != userGuid || confirmedAttempt.Barcode != request.Barcode)
                {
                    return ApiResponse<EmployeeCashierBarcodeDto>.Error(
                        "打印尝试编号已被使用",
                        "PRINT_ATTEMPT_CONFLICT"
                    );
                }
                var confirmedBarcode = await _context.Db.Queryable<EmployeeCashierBarcode>()
                    .FirstAsync(item => item.HGUID == confirmedAttempt.BarcodeHGUID);
                if (confirmedBarcode is null || !confirmedBarcode.Status)
                {
                    var currentBarcode = await _context.Db.Queryable<EmployeeCashierBarcode>()
                        .Where(item => item.UserGUID == userGuid && item.Status)
                        .OrderBy(item => item.CreatedAt, OrderByType.Desc)
                        .FirstAsync();
                    return ChangedBarcodeResponse(currentBarcode);
                }
                return ApiResponse<EmployeeCashierBarcodeDto>.OK(Map(confirmedBarcode));
            }
            var entity = await _context.Db.Queryable<EmployeeCashierBarcode>()
                .Where(item =>
                    item.UserGUID == userGuid
                    && item.Barcode == request.Barcode
                    && item.Status
                )
                .OrderBy(item => item.CreatedAt, OrderByType.Desc)
                .FirstAsync();
            if (entity is null)
            {
                var hasCurrent = await _context.Db.Queryable<EmployeeCashierBarcode>()
                    .AnyAsync(item => item.UserGUID == userGuid && item.Status);
                return ApiResponse<EmployeeCashierBarcodeDto>.Error(
                    hasCurrent ? "当前条码已刷新，请重新打印" : "尚未生成收银条码",
                    hasCurrent ? "CASHIER_BARCODE_CHANGED" : "CASHIER_BARCODE_NOT_FOUND"
                );
            }
            var db = _context.Db;
            await db.Ado.BeginTranAsync();
            try
            {
                var existingAttempt = await db.Queryable<EmployeeCashierBarcodePrintAttempt>()
                    .FirstAsync(item => item.PrintAttemptId == request.PrintAttemptId);
                if (existingAttempt is not null)
                {
                    await db.Ado.CommitTranAsync();
                    return ApiResponse<EmployeeCashierBarcodeDto>.OK(Map(entity));
                }
                await db.Insertable(new EmployeeCashierBarcodePrintAttempt
                {
                    PrintAttemptId = request.PrintAttemptId,
                    BarcodeHGUID = entity.HGUID,
                    UserGUID = userGuid,
                    Barcode = request.Barcode,
                    CreatedAt = DateTime.UtcNow,
                }).ExecuteCommandAsync();
                var updatedAt = DateTime.UtcNow;
                var updatedBy = _currentUser.GetCurrentUsername();
                // 关键逻辑：不同打印 attempt 并发时由数据库原子自增，避免读改写覆盖计数。
                var affectedRows = await db.Ado.ExecuteCommandAsync(
                    "UPDATE EmployeeCashierBarcodes SET PrintCount = PrintCount + 1, UpdatedAt = @updatedAt, UpdatedBy = @updatedBy WHERE HGUID = @hguid AND UserGUID = @userGuid AND Barcode = @barcode AND Status = 1",
                    new SugarParameter("@updatedAt", updatedAt),
                    new SugarParameter("@updatedBy", updatedBy),
                    new SugarParameter("@hguid", entity.HGUID),
                    new SugarParameter("@userGuid", userGuid),
                    new SugarParameter("@barcode", request.Barcode)
                );
                if (affectedRows != 1)
                {
                    await db.Ado.RollbackTranAsync();
                    return ApiResponse<EmployeeCashierBarcodeDto>.Error(
                        "当前条码已刷新，请重新打印",
                        "CASHIER_BARCODE_CHANGED"
                    );
                }
                entity = await db.Queryable<EmployeeCashierBarcode>()
                    .FirstAsync(item => item.HGUID == entity.HGUID);
                await db.Ado.CommitTranAsync();
                return ApiResponse<EmployeeCashierBarcodeDto>.OK(Map(entity), "打印次数已记录");
            }
            catch (Exception ex) when (IsUniqueConstraintViolation(ex))
            {
                await db.Ado.RollbackTranAsync();
                var attempt = await db.Queryable<EmployeeCashierBarcodePrintAttempt>()
                    .FirstAsync(item => item.PrintAttemptId == request.PrintAttemptId
                        && item.UserGUID == userGuid && item.Barcode == request.Barcode);
                if (attempt is null)
                {
                    throw;
                }
                var confirmed = await db.Queryable<EmployeeCashierBarcode>()
                    .FirstAsync(item => item.HGUID == attempt.BarcodeHGUID);
                if (confirmed is null || !confirmed.Status)
                {
                    var current = await db.Queryable<EmployeeCashierBarcode>()
                        .Where(item => item.UserGUID == userGuid && item.Status)
                        .OrderBy(item => item.CreatedAt, OrderByType.Desc)
                        .FirstAsync();
                    return ChangedBarcodeResponse(current);
                }
                return ApiResponse<EmployeeCashierBarcodeDto>.OK(Map(confirmed));
            }
            catch
            {
                await db.Ado.RollbackTranAsync();
                throw;
            }
        }

        private static string GenerateBarcode()
        {
            var digits = new char[10];
            for (var index = 0; index < digits.Length; index++)
            {
                digits[index] = (char)('0' + RandomNumberGenerator.GetInt32(10));
            }
            return AppendEan13CheckDigit("29" + new string(digits));
        }

        public static string AppendEan13CheckDigit(string twelveDigits)
        {
            if (twelveDigits.Length != 12 || twelveDigits.Any(character => !char.IsDigit(character)))
            {
                throw new ArgumentException("EAN-13 主体必须为 12 位数字", nameof(twelveDigits));
            }
            var sum = 0;
            for (var index = 0; index < twelveDigits.Length; index++)
            {
                var digit = twelveDigits[index] - '0';
                sum += index % 2 == 0 ? digit : digit * 3;
            }
            return twelveDigits + ((10 - sum % 10) % 10);
        }

        public static bool IsUniqueConstraintViolation(Exception exception)
        {
            for (var current = exception; current is not null; current = current.InnerException)
            {
                var message = current.Message;
                if (
                    message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("2601", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("2627", StringComparison.OrdinalIgnoreCase)
                )
                {
                    return true;
                }
            }
            return false;
        }

        private static EmployeeCashierBarcodeDto Map(EmployeeCashierBarcode? entity) =>
            entity is null
                ? new EmployeeCashierBarcodeDto { Exists = false }
                : new EmployeeCashierBarcodeDto
                {
                    Exists = true,
                    Barcode = entity.Barcode,
                    PrintCount = entity.PrintCount,
                    CreatedAt = entity.CreatedAt,
                    UpdatedAt = entity.UpdatedAt,
                };

        private static ApiResponse<EmployeeCashierBarcodeDto> ChangedBarcodeResponse(
            EmployeeCashierBarcode? current
        ) => new()
        {
            Success = false,
            Message = current is null ? "尚未生成收银条码" : "当前条码已刷新，请重新打印",
            ErrorCode = current is null ? "CASHIER_BARCODE_NOT_FOUND" : "CASHIER_BARCODE_CHANGED",
            Data = Map(current),
        };
    }
}
