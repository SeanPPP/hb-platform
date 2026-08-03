using System.Globalization;
using System.Text.Json;
using BlazorApp.Api.Models.Linkly;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Services.React;

internal interface ILinklySettlementAmountParser
{
    LinklySettlementAmountParseResult Parse(string? value);
}

internal sealed class LinklySettlementAmountParser : ILinklySettlementAmountParser
{
    private const int CardRecordLength = 69;

    public LinklySettlementAmountParseResult Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return LinklySettlementAmountParseResult.Missing;

        var first = value.AsSpan().TrimStart()[0];
        if (first is '{' or '[')
            return ParseJson(value);

        if (value.Length < 12 || !AllDigits(value.AsSpan(0, Math.Min(12, value.Length))))
            return LinklySettlementAmountParseResult.Unsupported;

        return ParseFixedWidth(value);
    }

    private LinklySettlementAmountParseResult ParseJson(string value)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(value);
        }
        catch (JsonException)
        {
            return LinklySettlementAmountParseResult.Invalid;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return LinklySettlementAmountParseResult.Unsupported;

            if (TryFindOfficialProperty(root, "SettlementTotalsData", out var totals))
                return ParseJsonTotals(totals);

            if (TryFindOfficialProperty(root, "SettlementData", out var fixedWidth))
            {
                if (fixedWidth.ValueKind != JsonValueKind.String)
                    return LinklySettlementAmountParseResult.Invalid;
                return Parse(fixedWidth.GetString());
            }

            return LinklySettlementAmountParseResult.Unsupported;
        }
    }

    private static bool TryFindOfficialProperty(
        JsonElement root,
        string name,
        out JsonElement value)
    {
        if (TryGetProperty(root, name, out value))
            return true;

        if (TryGetProperty(root, "Response", out var response)
            && response.ValueKind == JsonValueKind.Object
            && TryGetProperty(response, name, out value))
            return true;

        value = default;
        return false;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static LinklySettlementAmountParseResult ParseJsonTotals(JsonElement totals)
    {
        if (totals.ValueKind != JsonValueKind.Array)
            return LinklySettlementAmountParseResult.Invalid;

        var rows = new List<LinklySettlementCardTotalDto>();
        foreach (var element in totals.EnumerateArray())
        {
            if (!TryParseJsonRecord(element, out var row))
                return LinklySettlementAmountParseResult.Invalid;
            rows.Add(row);
        }

        return BuildResult(rows);
    }

    private static bool TryParseJsonRecord(
        JsonElement element,
        out LinklySettlementCardTotalDto row)
    {
        row = new LinklySettlementCardTotalDto();
        if (element.ValueKind != JsonValueKind.Object
            || !TryGetProperty(element, "CardName", out var cardName)
            || cardName.ValueKind != JsonValueKind.String)
            return false;

        var normalizedName = cardName.GetString()?.Trim();
        if (string.IsNullOrEmpty(normalizedName)
            || !TryGetBoundedUnsigned(element, "PurchaseAmount", 999_999_999, out var purchaseAmount)
            || !TryGetBoundedUnsigned(element, "PurchaseCount", 999, out var purchaseCount)
            || !TryGetBoundedUnsigned(element, "CashOutAmount", 999_999_999, out var cashOutAmount)
            || !TryGetBoundedUnsigned(element, "CashOutCount", 999, out var cashOutCount)
            || !TryGetBoundedUnsigned(element, "RefundAmount", 999_999_999, out var refundAmount)
            || !TryGetBoundedUnsigned(element, "RefundCount", 999, out var refundCount)
            || !TryGetBoundedTotalAmount(element, "TotalAmount", out var totalAmount)
            || !TryGetBoundedUnsigned(element, "TotalCount", 999, out var totalCount))
            return false;

        row = new LinklySettlementCardTotalDto
        {
            CardName = normalizedName,
            PurchaseAmountMinor = purchaseAmount,
            PurchaseCount = purchaseCount,
            CashOutAmountMinor = cashOutAmount,
            CashOutCount = cashOutCount,
            RefundAmountMinor = refundAmount,
            RefundCount = refundCount,
            TotalAmountMinor = totalAmount,
            TotalCount = totalCount,
        };
        return true;
    }

    private static bool TryGetBoundedUnsigned(
        JsonElement element,
        string name,
        long maximum,
        out long value)
    {
        value = 0;
        return TryGetProperty(element, name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt64(out value)
            && value >= 0
            && value <= maximum;
    }

    private static bool TryGetBoundedTotalAmount(
        JsonElement element,
        string name,
        out long value)
    {
        value = 0;
        return TryGetProperty(element, name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt64(out value)
            && value is >= -999_999_999 and <= 999_999_999;
    }

    private static LinklySettlementAmountParseResult ParseFixedWidth(string value)
    {
        if (!TryReadUnsigned(value.AsSpan(0, 9), out var cardCountLong)
            || cardCountLong > int.MaxValue
            || !TryReadUnsigned(value.AsSpan(9, 3), out var cardDataLengthLong)
            || cardDataLengthLong > int.MaxValue)
            return LinklySettlementAmountParseResult.Invalid;

        var cardCount = (int)cardCountLong;
        var cardDataLength = (int)cardDataLengthLong;
        if (cardCount > 999_999
            || cardDataLength != cardCount * CardRecordLength
            || value.Length < 12 + cardDataLength + 3)
            return LinklySettlementAmountParseResult.Invalid;

        var offset = 12;
        var rows = new List<LinklySettlementCardTotalDto>(cardCount + 1);
        for (var index = 0; index < cardCount; index++)
        {
            if (!TryParseFixedRecord(value.AsSpan(offset, CardRecordLength), out var row))
                return LinklySettlementAmountParseResult.Invalid;
            rows.Add(row);
            offset += CardRecordLength;
        }

        if (!TryReadUnsigned(value.AsSpan(offset, 3), out var totalsLength)
            || totalsLength != CardRecordLength
            || value.Length < offset + 3 + CardRecordLength)
            return LinklySettlementAmountParseResult.Invalid;

        offset += 3;
        if (!TryParseFixedRecord(value.AsSpan(offset, CardRecordLength), out var total))
            return LinklySettlementAmountParseResult.Invalid;
        rows.Add(total);
        offset += CardRecordLength;

        // 官方响应允许一个可选尾段；有尾段时声明长度必须与剩余字符精确相等。
        if (offset != value.Length)
        {
            if (value.Length - offset < 3
                || !TryReadUnsigned(value.AsSpan(offset, 3), out var tailLength)
                || tailLength != value.Length - offset - 3)
                return LinklySettlementAmountParseResult.Invalid;
        }

        return BuildResult(rows);
    }

    private static bool TryParseFixedRecord(
        ReadOnlySpan<char> record,
        out LinklySettlementCardTotalDto row)
    {
        row = new LinklySettlementCardTotalDto();
        if (record.Length != CardRecordLength)
            return false;

        var name = record[..20].Trim().ToString();
        if (name.Length == 0
            || !TryReadUnsigned(record.Slice(20, 9), out var purchaseAmount)
            || !TryReadUnsigned(record.Slice(29, 3), out var purchaseCount)
            || !TryReadUnsigned(record.Slice(32, 9), out var cashOutAmount)
            || !TryReadUnsigned(record.Slice(41, 3), out var cashOutCount)
            || !TryReadUnsigned(record.Slice(44, 9), out var refundAmount)
            || !TryReadUnsigned(record.Slice(53, 3), out var refundCount)
            || record[56] is not ('+' or '-')
            || !TryReadUnsigned(record.Slice(57, 9), out var totalAmount)
            || !TryReadUnsigned(record.Slice(66, 3), out var totalCount))
            return false;

        row = new LinklySettlementCardTotalDto
        {
            CardName = name,
            PurchaseAmountMinor = purchaseAmount,
            PurchaseCount = purchaseCount,
            CashOutAmountMinor = cashOutAmount,
            CashOutCount = cashOutCount,
            RefundAmountMinor = refundAmount,
            RefundCount = refundCount,
            TotalAmountMinor = record[56] == '-' ? -totalAmount : totalAmount,
            TotalCount = totalCount,
        };
        return true;
    }

    private static LinklySettlementAmountParseResult BuildResult(
        IReadOnlyList<LinklySettlementCardTotalDto> rows)
    {
        var totals = rows
            .Where(row => row.CardName.Equals("TOTAL", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (totals.Count != 1)
            return LinklySettlementAmountParseResult.Invalid;

        var total = totals[0];
        return new LinklySettlementAmountParseResult
        {
            Status = LinklySettlementAmountParseStatus.Parsed,
            Summary = new LinklySettlementAmountDto
            {
                CurrencyCode = "AUD",
                PurchaseAmountMinor = total.PurchaseAmountMinor,
                PurchaseCount = total.PurchaseCount,
                CashOutAmountMinor = total.CashOutAmountMinor,
                CashOutCount = total.CashOutCount,
                RefundAmountMinor = total.RefundAmountMinor,
                RefundCount = total.RefundCount,
                TotalAmountMinor = total.TotalAmountMinor,
                TotalCount = total.TotalCount,
            },
            CardTotals = rows
                .Where(row => !row.CardName.Equals("TOTAL", StringComparison.OrdinalIgnoreCase))
                .ToList(),
        };
    }

    private static bool TryReadUnsigned(ReadOnlySpan<char> value, out long parsed) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed);

    private static bool AllDigits(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (character is < '0' or > '9')
                return false;
        }

        return true;
    }
}
