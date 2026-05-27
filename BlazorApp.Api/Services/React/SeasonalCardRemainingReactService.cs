using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class SeasonalCardRemainingReactService : ISeasonalCardRemainingReactService
    {
        private readonly ISqlSugarClient _db;
        private readonly ICurrentUserService _currentUserService;
        private readonly ICurrentUserManageableStoreScopeService _scopeService;
        private readonly ILogger<SeasonalCardRemainingReactService> _logger;

        public SeasonalCardRemainingReactService(
            SqlSugarContext context,
            ICurrentUserService currentUserService,
            ICurrentUserManageableStoreScopeService scopeService,
            ILogger<SeasonalCardRemainingReactService> logger
        )
        {
            _db = context.Db;
            _currentUserService = currentUserService;
            _scopeService = scopeService;
            _logger = logger;
        }

        public async Task<ApiResponse<List<SeasonalCardCatalogDto>>> GetCatalogAsync()
        {
            var rows = await _db.Queryable<SeasonalCardCatalog>()
                .Where(item => !item.IsDeleted && item.IsEnabled)
                .OrderBy(item => item.SortOrder)
                .ToListAsync();

            return ApiResponse<List<SeasonalCardCatalogDto>>.OK(
                rows.Select(ToCatalogDto).ToList()
            );
        }

        public async Task<ApiResponse<SeasonalCardRemainingSubmissionDto>> CreateSubmissionAsync(
            CreateSeasonalCardRemainingSubmissionDto request
        )
        {
            var validation = ValidateCreateRequest(request);
            if (!validation.Success)
            {
                return ApiResponse<SeasonalCardRemainingSubmissionDto>.Error(
                    validation.Message,
                    validation.ErrorCode
                );
            }

            var storeCode = request.StoreCode.Trim();
            var access = await ResolveManagedStoreAccessAsync(storeCode);
            if (!access.Success)
            {
                return ApiResponse<SeasonalCardRemainingSubmissionDto>.Error(
                    access.Message,
                    access.ErrorCode
                );
            }

            var store = await _db.Queryable<Store>()
                .FirstAsync(item => !item.IsDeleted && item.StoreCode == storeCode);
            if (store == null)
            {
                return ApiResponse<SeasonalCardRemainingSubmissionDto>.Error(
                    "分店不存在",
                    "STORE_NOT_FOUND"
                );
            }

            var catalog = await _db.Queryable<SeasonalCardCatalog>()
                .FirstAsync(item =>
                    !item.IsDeleted && item.CatalogGuid == request.CatalogGuid.Trim()
                );
            if (catalog == null)
            {
                return ApiResponse<SeasonalCardRemainingSubmissionDto>.Error(
                    "季节卡目录不存在",
                    "CATALOG_NOT_FOUND"
                );
            }

            if (!catalog.IsEnabled)
            {
                return ApiResponse<SeasonalCardRemainingSubmissionDto>.Error(
                    "季节卡目录未启用",
                    "CATALOG_DISABLED"
                );
            }

            var unitPriceResult = ResolveUnitPrice(catalog, request.CustomUnitPrice);
            if (!unitPriceResult.Success)
            {
                return ApiResponse<SeasonalCardRemainingSubmissionDto>.Error(
                    unitPriceResult.Message,
                    unitPriceResult.ErrorCode
                );
            }

            var now = DateTime.UtcNow;
            var submission = new SeasonalCardRemainingSubmission
            {
                SubmissionGuid = Guid.NewGuid().ToString(),
                StoreCode = storeCode,
                CatalogGuid = catalog.CatalogGuid,
                CatalogCode = catalog.CatalogCode,
                CardType = catalog.CardType,
                PriceOption = catalog.PriceOption,
                PriceLabel = catalog.PriceLabel,
                UnitPrice = unitPriceResult.UnitPrice,
                SeasonYear = request.SeasonYear,
                RemainingQuantity = request.RemainingQuantity,
                Remark = NormalizeRemark(request.Remark),
                SubmittedAt = now,
                SubmittedByUserGuid = _currentUserService.GetCurrentUserGuid(),
                SubmittedByName = _currentUserService.GetCurrentUsername(),
                CreatedAt = now,
                CreatedBy = _currentUserService.GetCurrentUsername(),
                UpdatedAt = now,
                UpdatedBy = _currentUserService.GetCurrentUsername(),
            };

            await _db.Insertable(submission).ExecuteCommandAsync();
            _logger.LogInformation(
                "季节卡剩余已提交: StoreCode={StoreCode}, CatalogGuid={CatalogGuid}, SeasonYear={SeasonYear}",
                submission.StoreCode,
                submission.CatalogGuid,
                submission.SeasonYear
            );

            return ApiResponse<SeasonalCardRemainingSubmissionDto>.OK(
                ToSubmissionDto(submission, store.StoreName),
                "季节卡剩余已提交"
            );
        }

        public async Task<ApiResponse<PagedResult<SeasonalCardRemainingSubmissionDto>>> GetSubmissionsAsync(
            SeasonalCardRemainingSubmissionQueryDto query
        )
        {
            var access = await ResolveManagedStoreAccessAsync(query.StoreCode);
            if (!access.Success)
            {
                return ApiResponse<PagedResult<SeasonalCardRemainingSubmissionDto>>.Error(
                    access.Message,
                    access.ErrorCode
                );
            }

            var pageNumber = query.PageNumber <= 0 ? 1 : query.PageNumber;
            var pageSize = query.PageSize <= 0 ? 20 : Math.Min(query.PageSize, 200);
            var storeCode = query.StoreCode?.Trim();

            var submissions = _db.Queryable<SeasonalCardRemainingSubmission>()
                .Where(item => !item.IsDeleted)
                .WhereIF(!string.IsNullOrWhiteSpace(storeCode), item => item.StoreCode == storeCode!)
                .WhereIF(
                    string.IsNullOrWhiteSpace(storeCode) && access.StoreCodes.Count > 0,
                    item => access.StoreCodes.Contains(item.StoreCode)
                )
                .WhereIF(query.CardType.HasValue, item => item.CardType == query.CardType!.Value)
                .WhereIF(query.SeasonYear.HasValue, item => item.SeasonYear == query.SeasonYear!.Value);

            var total = await submissions.CountAsync();
            var rows = await submissions
                .OrderByDescending(item => item.SubmittedAt)
                .ToPageListAsync(pageNumber, pageSize);

            var storeNameByCode = await GetStoreNameByCodeAsync(rows.Select(item => item.StoreCode));

            return ApiResponse<PagedResult<SeasonalCardRemainingSubmissionDto>>.OK(
                new PagedResult<SeasonalCardRemainingSubmissionDto>
                {
                    Items = rows
                        .Select(item =>
                            ToSubmissionDto(
                                item,
                                storeNameByCode.GetValueOrDefault(item.StoreCode)
                            )
                        )
                        .ToList(),
                    Total = total,
                    Page = pageNumber,
                    PageSize = pageSize,
                }
            );
        }

        public async Task<ApiResponse<SeasonalCardRemainingSubmissionDto>> GetSubmissionByGuidAsync(
            string submissionGuid
        )
        {
            if (string.IsNullOrWhiteSpace(submissionGuid))
            {
                return ApiResponse<SeasonalCardRemainingSubmissionDto>.Error(
                    "提交编号不能为空",
                    "SUBMISSION_GUID_REQUIRED"
                );
            }

            var submission = await _db.Queryable<SeasonalCardRemainingSubmission>()
                .FirstAsync(item =>
                    !item.IsDeleted && item.SubmissionGuid == submissionGuid.Trim()
                );
            if (submission == null)
            {
                return ApiResponse<SeasonalCardRemainingSubmissionDto>.Error(
                    "季节卡剩余提交不存在",
                    "NOT_FOUND"
                );
            }

            var access = await ResolveManagedStoreAccessAsync(submission.StoreCode);
            if (!access.Success)
            {
                return ApiResponse<SeasonalCardRemainingSubmissionDto>.Error(
                    access.Message,
                    access.ErrorCode
                );
            }

            var storeName = await _db.Queryable<Store>()
                .Where(item => !item.IsDeleted && item.StoreCode == submission.StoreCode)
                .Select(item => item.StoreName)
                .FirstAsync();

            return ApiResponse<SeasonalCardRemainingSubmissionDto>.OK(
                ToSubmissionDto(submission, storeName)
            );
        }

        private async Task<Dictionary<string, string>> GetStoreNameByCodeAsync(
            IEnumerable<string> storeCodes
        )
        {
            var codes = storeCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!codes.Any())
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var stores = await _db.Queryable<Store>()
                .Where(item => !item.IsDeleted && codes.Contains(item.StoreCode))
                .Select(item => new { item.StoreCode, item.StoreName })
                .ToListAsync();

            return stores.ToDictionary(
                item => item.StoreCode,
                item => item.StoreName,
                StringComparer.OrdinalIgnoreCase
            );
        }

        private static ValidationResult ValidateCreateRequest(
            CreateSeasonalCardRemainingSubmissionDto request
        )
        {
            if (string.IsNullOrWhiteSpace(request.StoreCode))
            {
                return ValidationResult.Error("分店代码不能为空", "STORE_CODE_REQUIRED");
            }

            if (string.IsNullOrWhiteSpace(request.CatalogGuid))
            {
                return ValidationResult.Error("目录编号不能为空", "CATALOG_GUID_REQUIRED");
            }

            if (request.SeasonYear <= 0)
            {
                return ValidationResult.Error("季节年份必须大于 0", "INVALID_SEASON_YEAR");
            }

            if (request.RemainingQuantity < 0)
            {
                return ValidationResult.Error("剩余数量不能为负数", "INVALID_QUANTITY");
            }

            return ValidationResult.OK();
        }

        private static UnitPriceResult ResolveUnitPrice(
            SeasonalCardCatalog catalog,
            decimal? customUnitPrice
        )
        {
            if (!catalog.AllowsCustomUnitPrice)
            {
                if (customUnitPrice.HasValue)
                {
                    return UnitPriceResult.Error(
                        "固定价格目录项不接受覆盖价格",
                        "FIXED_PRICE_OVERRIDE_NOT_ALLOWED"
                    );
                }

                if (!catalog.FixedUnitPrice.HasValue || catalog.FixedUnitPrice.Value <= 0)
                {
                    return UnitPriceResult.Error("固定目录价格无效", "INVALID_FIXED_PRICE");
                }

                return UnitPriceResult.OK(catalog.FixedUnitPrice.Value);
            }

            if (!customUnitPrice.HasValue)
            {
                return UnitPriceResult.Error(
                    "其他价格目录必须提交大于 0 的实际售价",
                    "CUSTOM_PRICE_REQUIRED"
                );
            }

            var roundedUnitPrice = decimal.Round(customUnitPrice.Value, 2);
            if (roundedUnitPrice <= 0)
            {
                return UnitPriceResult.Error(
                    "其他价格目录必须提交大于 0 的实际售价",
                    "CUSTOM_PRICE_REQUIRED"
                );
            }

            return UnitPriceResult.OK(roundedUnitPrice);
        }

        private async Task<StoreAccessResult> ResolveManagedStoreAccessAsync(string? requestedStoreCode)
        {
            var scope = await _scopeService.GetScopeAsync();
            if (!scope.IsAllowed)
            {
                return StoreAccessResult.Forbidden(scope.Message);
            }

            var requested = requestedStoreCode?.Trim();
            if (!string.IsNullOrWhiteSpace(requested) && !scope.CanAccessStoreCode(requested))
            {
                return StoreAccessResult.Forbidden("没有权限访问该分店", "FORBIDDEN_STORE");
            }

            return StoreAccessResult.Allowed(
                string.IsNullOrWhiteSpace(requested)
                    ? scope.StoreCodes.ToList()
                    : new List<string> { requested }
            );
        }

        private static string? NormalizeRemark(string? remark)
        {
            return string.IsNullOrWhiteSpace(remark) ? null : remark.Trim();
        }

        private static SeasonalCardCatalogDto ToCatalogDto(SeasonalCardCatalog item) => new()
        {
            CatalogGuid = item.CatalogGuid,
            CatalogCode = item.CatalogCode,
            CardType = item.CardType,
            CardTypeName = SeasonalCardCatalogSeedData.GetCardTypeName(item.CardType),
            PriceOption = item.PriceOption,
            PriceOptionName = SeasonalCardCatalogSeedData.GetPriceOptionName(item.PriceOption),
            PriceLabel = item.PriceLabel,
            FixedUnitPrice = item.FixedUnitPrice,
            AllowsCustomUnitPrice = item.AllowsCustomUnitPrice,
            IsEnabled = item.IsEnabled,
            SortOrder = item.SortOrder,
        };

        private static SeasonalCardRemainingSubmissionDto ToSubmissionDto(
            SeasonalCardRemainingSubmission item,
            string? storeName
        ) => new()
        {
            SubmissionGuid = item.SubmissionGuid,
            StoreCode = item.StoreCode,
            StoreName = storeName,
            CatalogGuid = item.CatalogGuid,
            CatalogCode = item.CatalogCode,
            CardType = item.CardType,
            CardTypeName = SeasonalCardCatalogSeedData.GetCardTypeName(item.CardType),
            PriceOption = item.PriceOption,
            PriceOptionName = SeasonalCardCatalogSeedData.GetPriceOptionName(item.PriceOption),
            PriceLabel = item.PriceLabel,
            SeasonYear = item.SeasonYear,
            RemainingQuantity = item.RemainingQuantity,
            UnitPrice = item.UnitPrice,
            Remark = item.Remark,
            SubmittedByUserGuid = item.SubmittedByUserGuid,
            SubmittedByName = item.SubmittedByName,
            SubmittedAt = item.SubmittedAt,
        };

        private sealed class StoreAccessResult
        {
            public bool Success { get; private init; }
            public string Message { get; private init; } = string.Empty;
            public string? ErrorCode { get; private init; }
            public List<string> StoreCodes { get; private init; } = new();

            public static StoreAccessResult Allowed(List<string> storeCodes) => new()
            {
                Success = true,
                StoreCodes = storeCodes,
            };

            public static StoreAccessResult Forbidden(
                string message,
                string errorCode = "FORBIDDEN"
            ) => new()
            {
                Success = false,
                Message = message,
                ErrorCode = errorCode,
            };
        }

        private sealed class ValidationResult
        {
            public bool Success { get; private init; }
            public string Message { get; private init; } = string.Empty;
            public string? ErrorCode { get; private init; }

            public static ValidationResult OK() => new() { Success = true };

            public static ValidationResult Error(string message, string errorCode) => new()
            {
                Success = false,
                Message = message,
                ErrorCode = errorCode,
            };
        }

        private sealed class UnitPriceResult
        {
            public bool Success { get; private init; }
            public string Message { get; private init; } = string.Empty;
            public string? ErrorCode { get; private init; }
            public decimal UnitPrice { get; private init; }

            public static UnitPriceResult OK(decimal unitPrice) => new()
            {
                Success = true,
                UnitPrice = unitPrice,
            };

            public static UnitPriceResult Error(string message, string errorCode) => new()
            {
                Success = false,
                Message = message,
                ErrorCode = errorCode,
            };
        }
    }
}
