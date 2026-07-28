using System.Globalization;
using System.Text.Json;
using BlazorApp.Shared.DTOs;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class LocalSupplierInvoiceAuditTimestampContractTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ListDto_审计时间输出明确UTC且业务日期保持默认序列化()
    {
        AssertAuditTimestampContract(
            (createdAt, updatedAt, orderDate, inboundDate) =>
                new LocalSupplierInvoiceListDto
                {
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt,
                    OrderDate = orderDate,
                    InboundDate = inboundDate
                }
        );
    }

    [Fact]
    public void DetailDto_审计时间输出明确UTC且业务日期保持默认序列化()
    {
        AssertAuditTimestampContract(
            (createdAt, updatedAt, orderDate, inboundDate) =>
                new LocalSupplierInvoiceDetailDto
                {
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt,
                    OrderDate = orderDate,
                    InboundDate = inboundDate
                }
        );
    }

    private static void AssertAuditTimestampContract(
        Func<DateTime, DateTime?, DateTime?, DateTime?, object> createDto
    )
    {
        var unspecifiedCreatedAt = new DateTime(2026, 7, 28, 1, 2, 3, DateTimeKind.Unspecified);
        var unspecifiedUpdatedAt = new DateTime(2026, 7, 28, 4, 5, 6, DateTimeKind.Unspecified);

        using (var document = Serialize(
                   createDto(
                       unspecifiedCreatedAt,
                       unspecifiedUpdatedAt,
                       unspecifiedCreatedAt,
                       unspecifiedUpdatedAt
                   )
               ))
        {
            var root = document.RootElement;
            AssertUtcTimestamp(
                root.GetProperty("createdAt").GetString(),
                DateTime.SpecifyKind(unspecifiedCreatedAt, DateTimeKind.Utc)
            );
            AssertUtcTimestamp(
                root.GetProperty("updatedAt").GetString(),
                DateTime.SpecifyKind(unspecifiedUpdatedAt, DateTimeKind.Utc)
            );
            Assert.False(
                root.GetProperty("orderDate").GetString()!.EndsWith("Z", StringComparison.Ordinal)
            );
            Assert.False(
                root.GetProperty("inboundDate").GetString()!.EndsWith("Z", StringComparison.Ordinal)
            );
        }

        var utcCreatedAt = new DateTime(2026, 7, 29, 1, 2, 3, DateTimeKind.Utc);
        var utcUpdatedAt = new DateTime(2026, 7, 29, 4, 5, 6, DateTimeKind.Utc);

        using (var document = Serialize(createDto(utcCreatedAt, utcUpdatedAt, null, null)))
        {
            var root = document.RootElement;
            AssertUtcTimestamp(root.GetProperty("createdAt").GetString(), utcCreatedAt);
            AssertUtcTimestamp(root.GetProperty("updatedAt").GetString(), utcUpdatedAt);
        }

        var localCreatedAt = new DateTime(2026, 7, 30, 1, 2, 3, DateTimeKind.Local);
        var localUpdatedAt = new DateTime(2026, 7, 30, 4, 5, 6, DateTimeKind.Local);

        using (var document = Serialize(createDto(localCreatedAt, localUpdatedAt, null, null)))
        {
            var root = document.RootElement;
            AssertUtcTimestamp(root.GetProperty("createdAt").GetString(), localCreatedAt.ToUniversalTime());
            AssertUtcTimestamp(root.GetProperty("updatedAt").GetString(), localUpdatedAt.ToUniversalTime());
        }

        using (var document = Serialize(createDto(utcCreatedAt, null, null, null)))
        {
            Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("updatedAt").ValueKind);
        }
    }

    private static JsonDocument Serialize(object value)
    {
        return JsonDocument.Parse(JsonSerializer.Serialize(value, value.GetType(), WebJsonOptions));
    }

    private static void AssertUtcTimestamp(string? actual, DateTime expectedUtc)
    {
        Assert.NotNull(actual);
        Assert.EndsWith("Z", actual);
        Assert.Equal(
            expectedUtc,
            DateTime.Parse(actual!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
        );
    }
}
