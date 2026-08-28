using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using SqlSugar;

namespace BlazorApp.Api.Features.DataSync.Common;

/// <summary>
/// HQ 同步共享的业务键与 Type1 保护规则；不持有数据库状态。
/// </summary>
internal static class DataSyncProductProtectionRules
{
internal sealed record Type1ProtectionSnapshot(
            HashSet<string> SetCodeIds,
            HashSet<string> BusinessKeys,
            HashSet<string> ProductCodes
        );


        internal static string GetSetCodeBusinessKey(ProductSetCode item) =>
            $"{item.ProductCode}\u001F{item.SetProductCode}";


        internal static string GetStoreMultiCodeBusinessKey(StoreMultiCodeProduct item) =>
            BuildNormalizedSetCodeBusinessKey(item.ProductCode, item.MultiCodeProductCode);


        internal static async Task<Type1ProtectionSnapshot> GetAllType1ProtectionAsync(
            ISqlSugarClient db
        )
        {
            var rows = await db.Queryable<ProductSetCode>()
                .Where(item => item.SetType == 1)
                .Select(item => new
                {
                    item.SetCodeId,
                    item.ProductCode,
                    item.SetProductCode,
                })
                .ToListAsync();

            return new Type1ProtectionSnapshot(
                rows.Select(item => NormalizeBusinessCode(item.SetCodeId))
                    .Where(code => code.Length > 0)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                rows.Select(item =>
                        BuildNormalizedSetCodeBusinessKey(
                            item.ProductCode,
                            item.SetProductCode
                        )
                    )
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                rows.Select(item => NormalizeBusinessCode(item.ProductCode))
                    .Where(code => code.Length > 0)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
            );
        }


        internal static bool TryGetProtectedType1Conflict(
            DIC_一品多码表 hqItem,
            Type1ProtectionSnapshot protection,
            out string reason
        )
        {
            var normalizedGuid = NormalizeBusinessCode(hqItem.HGUID);
            if (
                normalizedGuid.Length > 0
                && protection.SetCodeIds.Contains(normalizedGuid)
            )
            {
                reason = "HQ GUID 与本地 Type1 关系冲突";
                return true;
            }

            var businessKey = BuildNormalizedSetCodeBusinessKey(
                hqItem.H商品编码,
                hqItem.H多码商品编号
            );
            if (protection.BusinessKeys.Contains(businessKey))
            {
                reason = "HQ 父子业务键与本地 Type1 关系冲突";
                return true;
            }

            reason = string.Empty;
            return false;
        }


        internal static string BuildNormalizedSetCodeBusinessKey(
            string? productCode,
            string? childCode
        ) => $"{NormalizeBusinessCode(productCode)}\u001F{NormalizeBusinessCode(childCode)}";


        internal static string NormalizeBusinessCode(string? value) =>
            value?.Trim().ToUpperInvariant() ?? string.Empty;


        internal static void AddNormalizedCode(ISet<string> target, string? value)
        {
            var normalized = NormalizeBusinessCode(value);
            if (normalized.Length > 0)
            {
                target.Add(normalized);
            }
        }


        internal static async Task<HashSet<string>> GetProtectedSetCodeKeysAsync(
            ISqlSugarClient db
        )
        {
            var protectedRows = await db.Queryable<ProductSetCode>()
                .Where(item => item.SetType == 1 && item.IsActive && !item.IsDeleted)
                .Select(item => new { item.ProductCode, item.SetProductCode })
                .ToListAsync();

            return new HashSet<string>(
                protectedRows.Select(item => $"{item.ProductCode}\u001F{item.SetProductCode}"),
                StringComparer.OrdinalIgnoreCase
            );
        }


        internal static List<string> GetProductCodesFromBusinessKeys(
            IEnumerable<string> businessKeys
        ) =>
            businessKeys
                .Select(key => key.Split('\u001F', 2)[0])
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();


        internal static void EnsureSetChildPurchasePriceRecalculated(
            SetChildPurchasePriceWritebackResultDto recalculation,
            IEnumerable<string> productCodes
        )
        {
            if (
                recalculation.ProductSetCode.SkippedGroupCount == 0
                && recalculation.StoreMultiCodeProduct.SkippedGroupCount == 0
            )
            {
                return;
            }

            var affectedCodes = string.Join(
                ", ",
                productCodes.Distinct(StringComparer.OrdinalIgnoreCase)
            );
            var reasons = string.Join(
                "；",
                recalculation.Errors.Select(error =>
                    $"{error.TableName}/{error.StoreCode ?? "总部"}/{error.ProductCode}: {error.Reason}"
                )
            );
            throw new InvalidOperationException(
                $"HQ 同步后的套装子项成本无法完整重算，主商品: {affectedCodes}。{reasons}"
            );
        }


        internal static async Task<HashSet<string>> GetProtectedStoreMultiCodeKeysAsync(
            ISqlSugarClient db
        )
        {
            var protectedRows = await db.Queryable<ProductSetCode>()
                .Where(item => item.SetType == 1)
                .Select(item => new { item.ProductCode, item.SetProductCode })
                .ToListAsync();

            return new HashSet<string>(
                protectedRows.Select(item =>
                    BuildNormalizedSetCodeBusinessKey(item.ProductCode, item.SetProductCode)
                ),
                StringComparer.OrdinalIgnoreCase
            );
        }
}
