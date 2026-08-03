using BlazorApp.Api.Models.Linkly;
using ClosedXML.Excel;

namespace BlazorApp.Api.Services.React;

internal sealed class LinklySettlementExcelExporter
{
    private static readonly string[] Headers =
    [
        "ID", "结算 GUID", "门店", "设备", "营业日期", "连接模式", "环境", "状态", "提交状态",
        "请求时间 UTC", "完成时间 UTC", "响应码", "响应文本", "币种", "购买金额", "购买笔数",
        "Cash Out 金额", "Cash Out 笔数", "退款金额", "退款笔数", "总金额", "总笔数", "金额解析状态",
        "小票数", "打印次数", "最后打印错误", "接收时间 UTC", "更新时间 UTC", "Provider Session ID",
        "Cloud Backend Session ID", "客户端版本",
    ];

    public LinklySettlementExportResult Export(
        IReadOnlyList<LinklySettlementExportRow> rows,
        DateOnly businessDateFrom,
        DateOnly businessDateTo)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Linkly Settlements");
        for (var column = 0; column < Headers.Length; column++)
            sheet.Cell(1, column + 1).Value = Headers[column];

        var header = sheet.Range(1, 1, 1, Headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAF7");

        for (var index = 0; index < rows.Count; index++)
            WriteRow(sheet, index + 2, rows[index]);

        sheet.SheetView.FreezeRows(1);
        sheet.RangeUsed()?.SetAutoFilter();
        sheet.Columns().AdjustToContents(8, 45);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new LinklySettlementExportResult
        {
            Content = stream.ToArray(),
            FileName = $"linkly-settlements-{businessDateFrom:yyyyMMdd}-{businessDateTo:yyyyMMdd}.xlsx",
        };
    }

    private static void WriteRow(IXLWorksheet sheet, int rowNumber, LinklySettlementExportRow row)
    {
        var item = row.Item;
        var amount = item.AmountSummary;
        var column = 1;
        SetText(sheet.Cell(rowNumber, column++), item.Id);
        SetText(sheet.Cell(rowNumber, column++), item.SettlementGuid.ToString());
        SetText(sheet.Cell(rowNumber, column++), item.StoreCode);
        SetText(sheet.Cell(rowNumber, column++), item.DeviceCode);
        sheet.Cell(rowNumber, column++).Value = item.BusinessDate.ToDateTime(TimeOnly.MinValue);
        sheet.Cell(rowNumber, column - 1).Style.DateFormat.Format = "yyyy-mm-dd";
        SetText(sheet.Cell(rowNumber, column++), item.ConnectionMode);
        SetText(sheet.Cell(rowNumber, column++), item.Environment);
        SetText(sheet.Cell(rowNumber, column++), item.Status);
        SetText(sheet.Cell(rowNumber, column++), item.ProviderSubmissionState);
        SetUtcDateTime(sheet.Cell(rowNumber, column++), item.RequestedAtUtc);
        SetNullableUtcDateTime(sheet.Cell(rowNumber, column++), item.CompletedAtUtc);
        SetText(sheet.Cell(rowNumber, column++), item.ResponseCode);
        SetText(sheet.Cell(rowNumber, column++), item.ResponseText);
        SetText(sheet.Cell(rowNumber, column++), amount?.CurrencyCode ?? "AUD");
        SetMoney(sheet.Cell(rowNumber, column++), amount?.PurchaseAmountMinor);
        sheet.Cell(rowNumber, column++).Value = amount?.PurchaseCount;
        SetMoney(sheet.Cell(rowNumber, column++), amount?.CashOutAmountMinor);
        sheet.Cell(rowNumber, column++).Value = amount?.CashOutCount;
        SetMoney(sheet.Cell(rowNumber, column++), amount?.RefundAmountMinor);
        sheet.Cell(rowNumber, column++).Value = amount?.RefundCount;
        SetMoney(sheet.Cell(rowNumber, column++), amount?.TotalAmountMinor);
        sheet.Cell(rowNumber, column++).Value = amount?.TotalCount;
        SetText(sheet.Cell(rowNumber, column++), item.AmountParseStatus);
        sheet.Cell(rowNumber, column++).Value = item.ReceiptCount;
        sheet.Cell(rowNumber, column++).Value = item.PrintCount;
        SetText(sheet.Cell(rowNumber, column++), item.LastPrintError);
        SetUtcDateTime(sheet.Cell(rowNumber, column++), item.ReceivedAtUtc);
        SetUtcDateTime(sheet.Cell(rowNumber, column++), item.UpdatedAtUtc);
        SetText(sheet.Cell(rowNumber, column++), row.ProviderSessionId);
        SetText(sheet.Cell(rowNumber, column++), row.CloudBackendSessionId);
        SetText(sheet.Cell(rowNumber, column), row.ClientRevision);
    }

    private static void SetText(IXLCell cell, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        cell.Value = RequiresFormulaEscape(value) ? $"'{value}" : value;
        cell.Style.NumberFormat.Format = "@";
    }

    private static bool RequiresFormulaEscape(string value) =>
        value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n';

    private static void SetMoney(IXLCell cell, long? minorUnits)
    {
        if (!minorUnits.HasValue)
            return;
        cell.Value = minorUnits.Value / 100m;
        cell.Style.NumberFormat.Format = "$" + "#,##0.00;[Red]-$#,##0.00";
    }

    private static void SetUtcDateTime(IXLCell cell, DateTime value)
    {
        cell.Value = value;
        cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
    }

    private static void SetNullableUtcDateTime(IXLCell cell, DateTime? value)
    {
        if (value.HasValue)
            SetUtcDateTime(cell, value.Value);
    }
}
