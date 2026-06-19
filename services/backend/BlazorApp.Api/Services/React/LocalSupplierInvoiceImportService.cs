using System.Globalization;
using System.Text.RegularExpressions;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace BlazorApp.Api.Services.React
{
    public class LocalSupplierInvoiceImportService : ILocalSupplierInvoiceImportService
    {
        private const long MaxFileSizeBytes = 10 * 1024 * 1024;
        private const int MaxSourceColumnCount = 80;
        private const int MaxImportLineCount = 2000;
        private const int MaxPdfTextCharacters = 1_000_000;

        private static readonly Dictionary<string, string[]> MappingAliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["itemNumber"] = new[]
                {
                    "货号",
                    "小货号",
                    "商品编码",
                    "产品编码",
                    "货品编码",
                    "itemnumber",
                    "itemno",
                    "sku",
                    "item"
                },
                ["barcode"] = new[]
                {
                    "条码",
                    "条形码",
                    "barcode",
                    "ean",
                    "upc"
                },
                ["productName"] = new[]
                {
                    "名称",
                    "商品名称",
                    "品名",
                    "货品名称",
                    "productname",
                    "name",
                    "description"
                },
                ["quantity"] = new[]
                {
                    "数量",
                    "订货数量",
                    "收货数量",
                    "qty",
                    "quantity",
                    "pcs"
                },
                ["price"] = new[]
                {
                    "价格",
                    "单价",
                    "进货价",
                    "采购价",
                    "本次进货价",
                    "price",
                    "unitprice",
                    "purchaseprice",
                    "cost"
                }
            };

        private static readonly Dictionary<string, string[]> HeaderAliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["storeCode"] = new[] { "分店代码", "门店代码", "storecode", "branchcode" },
                ["storeName"] = new[] { "分店", "分店名称", "门店", "门店名称", "storename", "branchname" },
                ["supplierCode"] = new[] { "供应商代码", "供应商编码", "suppliercode", "localsuppliercode" },
                ["supplierName"] = new[] { "供应商", "供应商名称", "supplier", "suppliername" },
                ["invoiceNo"] = new[] { "单号", "单据号", "进货单号", "invoiceno", "billno" },
                ["orderDate"] = new[] { "订单日期", "下单日期", "orderdate" },
                ["inboundDate"] = new[] { "入库日期", "到货日期", "inbounddate" },
                ["remarks"] = new[] { "备注", "说明", "remarks", "remark" }
            };

        private readonly SqlSugarContext _context;
        private readonly ILocalSupplierInvoicesReactService _invoiceService;
        private readonly ILocalSupplierInvoiceOcrService _ocrService;
        private readonly ILogger<LocalSupplierInvoiceImportService> _logger;

        public LocalSupplierInvoiceImportService(
            SqlSugarContext context,
            ILocalSupplierInvoicesReactService invoiceService,
            ILocalSupplierInvoiceOcrService ocrService,
            ILogger<LocalSupplierInvoiceImportService> logger
        )
        {
            _context = context;
            _invoiceService = invoiceService;
            _ocrService = ocrService;
            _logger = logger;
        }

        public async Task<ApiResponse<LocalSupplierInvoiceImportPreviewDto>> PreviewAsync(
            IFormFile file,
            CancellationToken cancellationToken = default
        )
        {
            var validationError = ValidateFile(file);
            if (validationError != null)
            {
                return ApiResponse<LocalSupplierInvoiceImportPreviewDto>.Error(
                    validationError.Value.Message,
                    validationError.Value.Code
                );
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            try
            {
                await using var memoryStream = new MemoryStream();
                await file.CopyToAsync(memoryStream, cancellationToken);
                memoryStream.Position = 0;

                ParsedImportTable parsedTable = extension switch
                {
                    ".xlsx" or ".xlsm" => ParseExcel(memoryStream),
                    ".pdf" => await ParsePdfAsync(memoryStream.ToArray(), file.FileName, cancellationToken),
                    _ => throw new ImportPreviewException("不支持的文件格式", "UNSUPPORTED_FILE")
                };

                var preview = await BuildPreviewAsync(parsedTable, file.FileName, extension, cancellationToken);
                if (preview.Errors.Count > 0)
                {
                    return ApiResponse<LocalSupplierInvoiceImportPreviewDto>.Error(
                        preview.Errors[0],
                        "IMPORT_PREVIEW_ERROR",
                        preview
                    );
                }

                return ApiResponse<LocalSupplierInvoiceImportPreviewDto>.OK(preview);
            }
            catch (ImportPreviewException ex)
            {
                _logger.LogWarning(ex, "进货单导入预览失败: {FileName}", file.FileName);
                return ApiResponse<LocalSupplierInvoiceImportPreviewDto>.Error(ex.Message, ex.Code);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解析进货单导入文件失败: {FileName}", file.FileName);
                return ApiResponse<LocalSupplierInvoiceImportPreviewDto>.Error(
                    "文件解析失败，请检查文件格式或重新导出后再上传",
                    "IMPORT_PREVIEW_ERROR"
                );
            }
        }

        public async Task<ApiResponse<LocalSupplierInvoiceImportConfirmResultDto>> ConfirmAsync(
            LocalSupplierInvoiceImportConfirmRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var validationMessages = ValidateConfirmRequest(request);
            if (validationMessages.Count > 0)
            {
                return ApiResponse<LocalSupplierInvoiceImportConfirmResultDto>.Error(
                    validationMessages[0],
                    "VALIDATION_ERROR",
                    validationMessages
                );
            }

            var storeResolution = await ResolveStoreAsync(
                request.Header.StoreCode,
                request.Header.StoreName,
                cancellationToken
            );
            if (storeResolution.Status != LocalSupplierInvoiceImportMatchStatus.Matched)
            {
                return ApiResponse<LocalSupplierInvoiceImportConfirmResultDto>.Error(
                    "分店未能唯一匹配，请先确认分店",
                    "STORE_MATCH_REQUIRED",
                    storeResolution
                );
            }

            var supplierResolution = await ResolveSupplierAsync(
                request.Header.SupplierCode,
                request.Header.SupplierName,
                cancellationToken
            );
            if (supplierResolution.Status != LocalSupplierInvoiceImportMatchStatus.Matched)
            {
                return ApiResponse<LocalSupplierInvoiceImportConfirmResultDto>.Error(
                    "供应商未能唯一匹配，请先确认供应商",
                    "SUPPLIER_MATCH_REQUIRED",
                    supplierResolution
                );
            }

            var lineErrors = new List<string>();
            var items = new List<PastedDetailItem>();

            // 统一按映射读取原始值，确保确认阶段使用的是用户最终确认过的列。
            foreach (var line in request.Lines ?? new List<LocalSupplierInvoiceImportLineDto>())
            {
                var itemNumber = GetMappedValue(line, request.Mapping.ItemNumberColumnKey);
                var barcode = GetMappedValue(line, request.Mapping.BarcodeColumnKey);
                var productName = GetMappedValue(line, request.Mapping.ProductNameColumnKey);
                var quantityText = GetMappedValue(line, request.Mapping.QuantityColumnKey);
                var priceText = GetMappedValue(line, request.Mapping.PriceColumnKey);

                if (
                    string.IsNullOrWhiteSpace(itemNumber)
                    && string.IsNullOrWhiteSpace(barcode)
                    && string.IsNullOrWhiteSpace(productName)
                    && string.IsNullOrWhiteSpace(quantityText)
                    && string.IsNullOrWhiteSpace(priceText)
                )
                {
                    continue;
                }

                // 用户确认的是列映射，但真正创建前仍要逐行校验三项商品身份字段，避免空货号/空条码的数据进库。
                if (string.IsNullOrWhiteSpace(itemNumber))
                {
                    lineErrors.Add($"第 {line.RowNumber} 行缺少货号");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(barcode))
                {
                    lineErrors.Add($"第 {line.RowNumber} 行缺少条码");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(productName))
                {
                    lineErrors.Add($"第 {line.RowNumber} 行缺少名称");
                    continue;
                }

                if (!TryParseQuantity(quantityText, out var quantity))
                {
                    lineErrors.Add($"第 {line.RowNumber} 行数量无效: {quantityText}");
                    continue;
                }

                if (!TryParseDecimal(priceText, out var price) || price <= 0)
                {
                    lineErrors.Add($"第 {line.RowNumber} 行价格无效: {priceText}");
                    continue;
                }

                items.Add(
                    new PastedDetailItem
                    {
                        ItemNumber = itemNumber,
                        Barcode = barcode,
                        ProductName = productName,
                        NameOrBarcode = !string.IsNullOrWhiteSpace(barcode) ? barcode : productName,
                        Quantity = quantity,
                        Price = price
                    }
                );
            }

            if (lineErrors.Count > 0)
            {
                return ApiResponse<LocalSupplierInvoiceImportConfirmResultDto>.Error(
                    "导入数据校验失败",
                    "VALIDATION_ERROR",
                    lineErrors
                );
            }

            if (items.Count == 0)
            {
                return ApiResponse<LocalSupplierInvoiceImportConfirmResultDto>.Error(
                    "未找到可导入的有效明细",
                    "VALIDATION_ERROR"
                );
            }

            var createRequest = new CreateInvoiceRequest
            {
                StoreCode = storeResolution.SelectedCode ?? string.Empty,
                SupplierCode = supplierResolution.SelectedCode ?? string.Empty,
                InvoiceNo = request.Header.InvoiceNo?.Trim() ?? string.Empty,
                OrderDate = request.Header.OrderDate,
                InboundDate = request.Header.InboundDate,
                Remarks = request.Header.Remarks,
                Items = items
            };

            var createResult = await _invoiceService.CreateAsync(createRequest);
            if (!createResult.Success || string.IsNullOrWhiteSpace(createResult.Data))
            {
                return ApiResponse<LocalSupplierInvoiceImportConfirmResultDto>.Error(
                    createResult.Message,
                    createResult.ErrorCode,
                    createResult.Details
                );
            }

            return ApiResponse<LocalSupplierInvoiceImportConfirmResultDto>.OK(
                new LocalSupplierInvoiceImportConfirmResultDto
                {
                    InvoiceGuid = createResult.Data,
                    Warnings = new List<string>()
                }
            );
        }

        private static (string Message, string Code)? ValidateFile(IFormFile? file)
        {
            if (file == null || file.Length <= 0)
            {
                return ("请选择导入文件", "INVALID_FILE");
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension == ".xls")
            {
                return ("旧版 Excel 不支持，请另存为 .xlsx 后上传", "UNSUPPORTED_XLS");
            }

            if (extension is not (".xlsx" or ".xlsm" or ".pdf"))
            {
                return ("仅支持 .xlsx、.xlsm、.pdf 文件导入", "UNSUPPORTED_FILE");
            }

            if (file.Length > MaxFileSizeBytes)
            {
                return ("文件大小不能超过10MB", "FILE_TOO_LARGE");
            }

            return null;
        }

        private ParsedImportTable ParseExcel(Stream stream)
        {
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);
            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            if (lastRow == 0)
            {
                throw new ImportPreviewException("Excel 文件为空，无法导入", "EMPTY_EXCEL");
            }

            var headerRowNumber = DetectHeaderRow(
                Enumerable.Range(1, Math.Min(lastRow, 15))
                    .Select(
                        rowNumber => new CandidateRow(
                            rowNumber,
                            ReadWorksheetRow(worksheet, rowNumber)
                        )
                    )
                    .ToList()
            );

            var headerCells = ReadWorksheetRow(worksheet, headerRowNumber);
            if (headerCells.Count == 0)
            {
                throw new ImportPreviewException("未识别到可导入的表头", "HEADER_NOT_FOUND");
            }

            var sourceColumns = headerCells
                .Select(
                    (header, index) => new LocalSupplierInvoiceImportSourceColumnDto
                    {
                        Key = $"col_{index + 1}",
                        Header = string.IsNullOrWhiteSpace(header) ? $"列{index + 1}" : header.Trim()
                    }
                )
                .ToList();
            EnsureSourceColumnLimit(sourceColumns.Count);

            var lines = new List<LocalSupplierInvoiceImportLineDto>();
            for (var rowNumber = headerRowNumber + 1; rowNumber <= lastRow; rowNumber++)
            {
                var rowValues = sourceColumns
                    .Select((column, index) => worksheet.Cell(rowNumber, index + 1).GetFormattedString())
                    .ToList();

                if (rowValues.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                EnsureLineLimit(lines.Count + 1);

                lines.Add(
                    new LocalSupplierInvoiceImportLineDto
                    {
                        RowNumber = rowNumber,
                        RawValues = sourceColumns.ToDictionary(
                            column => column.Key,
                            column =>
                            {
                                var columnIndex = sourceColumns.IndexOf(column);
                                return rowValues[columnIndex]?.Trim();
                            }
                        )
                    }
                );
            }

            var metadata = ExtractWorksheetMetadata(worksheet, headerRowNumber, sourceColumns);
            return new ParsedImportTable(sourceColumns, lines, metadata, new List<string>(), new List<string>());
        }

        private async Task<ParsedImportTable> ParsePdfAsync(
            byte[] fileBytes,
            string fileName,
            CancellationToken cancellationToken
        )
        {
            var extractedText = ExtractPdfText(fileBytes);
            var warnings = new List<string>();

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                var ocrResult = await _ocrService.ExtractTextAsync(
                    fileBytes,
                    fileName,
                    cancellationToken
                );
                if (!ocrResult.Success || string.IsNullOrWhiteSpace(ocrResult.Data))
                {
                    throw new ImportPreviewException(
                        ocrResult.Message,
                        ocrResult.ErrorCode ?? "OCR_NOT_CONFIGURED"
                    );
                }

                extractedText = ocrResult.Data;
                warnings.Add("PDF 文本为空，已使用 OCR 结果生成预览");
            }

            if (extractedText.Length > MaxPdfTextCharacters)
            {
                throw new ImportPreviewException(
                    $"PDF 文本内容超过 {MaxPdfTextCharacters} 字符，请拆分文件后再上传",
                    "PDF_TEXT_TOO_LARGE"
                );
            }

            return ParseTextTable(extractedText, warnings);
        }

        private ParsedImportTable ParseTextTable(string text, List<string> warnings)
        {
            var rawLines = text
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            var candidateRows = rawLines
                .Select((line, index) => new CandidateRow(index + 1, SplitTextLine(line)))
                .Where(row => row.Cells.Count > 0)
                .ToList();

            if (candidateRows.Count == 0)
            {
                throw new ImportPreviewException("PDF 中未识别到可导入的文本内容", "PDF_TEXT_EMPTY");
            }

            if (candidateRows.Max(CalculateHeaderScore) < 2)
            {
                return ParseHeaderlessTextTable(rawLines, candidateRows, warnings);
            }

            var headerRowNumber = DetectHeaderRow(candidateRows);
            var headerRow = candidateRows.First(row => row.RowNumber == headerRowNumber);
            var sourceColumns = headerRow.Cells
                .Select(
                    (header, index) => new LocalSupplierInvoiceImportSourceColumnDto
                    {
                        Key = $"col_{index + 1}",
                        Header = string.IsNullOrWhiteSpace(header) ? $"列{index + 1}" : header.Trim()
                    }
                )
                .ToList();
            EnsureSourceColumnLimit(sourceColumns.Count);

            var lines = candidateRows
                .Where(row => row.RowNumber > headerRowNumber)
                .Take(MaxImportLineCount + 1)
                .Select(
                    row => new LocalSupplierInvoiceImportLineDto
                    {
                        RowNumber = row.RowNumber,
                        RawValues = sourceColumns.ToDictionary(
                            column => column.Key,
                            column =>
                            {
                                var index = sourceColumns.IndexOf(column);
                                return index < row.Cells.Count ? row.Cells[index]?.Trim() : null;
                            }
                        )
                    }
                )
                .Where(line => line.RawValues.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
                .ToList();
            EnsureLineLimit(lines.Count);

            var metadata = ExtractTextMetadata(rawLines, headerRowNumber, sourceColumns, lines);
            return new ParsedImportTable(sourceColumns, lines, metadata, warnings, new List<string>());
        }

        private ParsedImportTable ParseHeaderlessTextTable(
            List<string> rawLines,
            List<CandidateRow> candidateRows,
            List<string> warnings
        )
        {
            var detailRows = candidateRows
                .Where(row => row.Cells.Count >= 5 && LooksLikeDetailRow(row.Cells))
                .Take(MaxImportLineCount + 1)
                .ToList();
            EnsureLineLimit(detailRows.Count);

            if (detailRows.Count == 0)
            {
                throw new ImportPreviewException("未识别到可导入的表头", "HEADER_NOT_FOUND");
            }

            var sourceColumns = new List<LocalSupplierInvoiceImportSourceColumnDto>
            {
                new() { Key = "col_1", Header = "货号" },
                new() { Key = "col_2", Header = "条码" },
                new() { Key = "col_3", Header = "名称" },
                new() { Key = "col_4", Header = "数量" },
                new() { Key = "col_5", Header = "价格" }
            };

            var lines = detailRows
                .Select(
                    row => new LocalSupplierInvoiceImportLineDto
                    {
                        RowNumber = row.RowNumber,
                        RawValues = sourceColumns.ToDictionary(
                            column => column.Key,
                            column =>
                            {
                                var index = sourceColumns.IndexOf(column);
                                return index < row.Cells.Count ? row.Cells[index]?.Trim() : null;
                            }
                        )
                    }
                )
                .ToList();

            var metadata = ExtractTextMetadata(rawLines, detailRows[0].RowNumber, sourceColumns, lines);
            ApplyHeaderlessPdfMetadata(metadata, candidateRows, detailRows[0].RowNumber);

            // 部分 PDF 字体不会暴露中文表头，只能依据明细行形状推断固定列，前端仍会要求用户确认列映射。
            warnings.Add("PDF 未识别到明确表头，已按货号/条码/名称/数量/价格推断列，请确认后再创建");
            return new ParsedImportTable(sourceColumns, lines, metadata, warnings, new List<string>());
        }

        private async Task<LocalSupplierInvoiceImportPreviewDto> BuildPreviewAsync(
            ParsedImportTable parsedTable,
            string fileName,
            string extension,
            CancellationToken cancellationToken
        )
        {
            var header = new LocalSupplierInvoiceImportHeaderDto
            {
                StoreCode = parsedTable.Metadata.GetValueOrDefault("storeCode"),
                StoreName = parsedTable.Metadata.GetValueOrDefault("storeName"),
                SupplierCode = parsedTable.Metadata.GetValueOrDefault("supplierCode"),
                SupplierName = parsedTable.Metadata.GetValueOrDefault("supplierName"),
                InvoiceNo = parsedTable.Metadata.GetValueOrDefault("invoiceNo"),
                OrderDate = TryParseDate(parsedTable.Metadata.GetValueOrDefault("orderDate")),
                InboundDate = TryParseDate(parsedTable.Metadata.GetValueOrDefault("inboundDate")),
                Remarks = parsedTable.Metadata.GetValueOrDefault("remarks")
            };

            header.StoreMatch = await ResolveStoreAsync(
                header.StoreCode,
                header.StoreName,
                cancellationToken
            );
            header.SupplierMatch = await ResolveSupplierAsync(
                header.SupplierCode,
                header.SupplierName,
                cancellationToken
            );

            if (header.StoreMatch.Status == LocalSupplierInvoiceImportMatchStatus.Matched)
            {
                header.StoreCode = header.StoreMatch.SelectedCode;
                header.StoreName = header.StoreMatch.SelectedName;
            }

            if (header.SupplierMatch.Status == LocalSupplierInvoiceImportMatchStatus.Matched)
            {
                header.SupplierCode = header.SupplierMatch.SelectedCode;
                header.SupplierName = header.SupplierMatch.SelectedName;
            }

            var preview = new LocalSupplierInvoiceImportPreviewDto
            {
                FileName = fileName,
                FileType = extension,
                SourceColumns = parsedTable.SourceColumns,
                RecommendedMapping = RecommendMapping(parsedTable.SourceColumns),
                Header = header,
                Lines = parsedTable.Lines,
                Warnings = parsedTable.Warnings.ToList(),
                Errors = parsedTable.Errors.ToList()
            };

            if (parsedTable.Lines.Count == 0)
            {
                preview.Errors.Add("未识别到可导入的明细行");
            }

            if (header.StoreMatch.Status != LocalSupplierInvoiceImportMatchStatus.Matched)
            {
                preview.Warnings.Add("分店未能自动唯一匹配，请用户确认");
            }

            if (header.SupplierMatch.Status != LocalSupplierInvoiceImportMatchStatus.Matched)
            {
                preview.Warnings.Add("供应商未能自动唯一匹配，请用户确认");
            }

            if (string.IsNullOrWhiteSpace(header.InvoiceNo))
            {
                preview.Warnings.Add("未从文件中识别到单号，确认时需要用户补充");
            }

            return preview;
        }

        private static LocalSupplierInvoiceImportMappingDto RecommendMapping(
            List<LocalSupplierInvoiceImportSourceColumnDto> sourceColumns
        )
        {
            return new LocalSupplierInvoiceImportMappingDto
            {
                ItemNumberColumnKey = FindBestMatch(sourceColumns, MappingAliases["itemNumber"]),
                BarcodeColumnKey = FindBestMatch(sourceColumns, MappingAliases["barcode"]),
                ProductNameColumnKey = FindBestMatch(sourceColumns, MappingAliases["productName"]),
                QuantityColumnKey = FindBestMatch(sourceColumns, MappingAliases["quantity"]),
                PriceColumnKey = FindBestMatch(sourceColumns, MappingAliases["price"])
            };
        }

        private static string? FindBestMatch(
            IEnumerable<LocalSupplierInvoiceImportSourceColumnDto> sourceColumns,
            IEnumerable<string> aliases
        )
        {
            var aliasSet = aliases.Select(NormalizeAlias).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return sourceColumns
                .Select(
                    column => new
                    {
                        column.Key,
                        Score = aliasSet.Contains(NormalizeAlias(column.Header)) ? 2 : aliasSet.Any(alias => NormalizeAlias(column.Header).Contains(alias)) ? 1 : 0
                    }
                )
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .Select(item => item.Key)
                .FirstOrDefault();
        }

        private static List<string> ValidateConfirmRequest(
            LocalSupplierInvoiceImportConfirmRequest request
        )
        {
            var errors = new List<string>();
            if (request.SourceColumns == null || request.SourceColumns.Count == 0)
            {
                errors.Add("缺少原始列信息");
            }
            else if (request.SourceColumns.Count > MaxSourceColumnCount)
            {
                errors.Add($"导入列数不能超过 {MaxSourceColumnCount} 列");
            }

            if (request.Lines == null || request.Lines.Count == 0)
            {
                errors.Add("缺少导入明细");
            }
            else if (request.Lines.Count > MaxImportLineCount)
            {
                errors.Add($"导入明细不能超过 {MaxImportLineCount} 行");
            }

            if (request.Header == null)
            {
                errors.Add("缺少导入头信息");
                return errors;
            }

            if (request.Mapping == null)
            {
                errors.Add("缺少字段映射");
                return errors;
            }

            var requiredMappings = new Dictionary<string, string?>
            {
                ["货号"] = request.Mapping.ItemNumberColumnKey,
                ["条码"] = request.Mapping.BarcodeColumnKey,
                ["名称"] = request.Mapping.ProductNameColumnKey,
                ["数量"] = request.Mapping.QuantityColumnKey,
                ["价格"] = request.Mapping.PriceColumnKey
            };

            foreach (var mapping in requiredMappings)
            {
                if (string.IsNullOrWhiteSpace(mapping.Value))
                {
                    errors.Add($"{mapping.Key}字段映射为必填");
                }
            }

            if (string.IsNullOrWhiteSpace(request.Header.StoreCode))
            {
                errors.Add("确认导入前必须确定分店");
            }

            if (string.IsNullOrWhiteSpace(request.Header.SupplierCode))
            {
                errors.Add("确认导入前必须确定供应商");
            }

            if (string.IsNullOrWhiteSpace(request.Header.InvoiceNo))
            {
                errors.Add("确认导入前必须填写单号");
            }

            var availableColumnKeys = (request.SourceColumns ?? new List<LocalSupplierInvoiceImportSourceColumnDto>())
                .Select(column => column.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var mappingValue in requiredMappings.Values.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                if (!availableColumnKeys.Contains(mappingValue!))
                {
                    errors.Add($"字段映射列不存在: {mappingValue}");
                }
            }

            var selectedMappingValues = requiredMappings.Values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .ToList();
            if (selectedMappingValues.Distinct(StringComparer.OrdinalIgnoreCase).Count() != selectedMappingValues.Count)
            {
                errors.Add("货号、条码、名称、数量、价格必须分别选择不同来源列");
            }

            return errors;
        }

        private async Task<LocalSupplierInvoiceImportMatchDto> ResolveStoreAsync(
            string? storeCode,
            string? storeName,
            CancellationToken cancellationToken
        )
        {
            var rawValue = JoinRawValues(storeCode, storeName);
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return new LocalSupplierInvoiceImportMatchDto
                {
                    Status = LocalSupplierInvoiceImportMatchStatus.Missing
                };
            }

            var db = _context.Db;
            var candidates = new List<Store>();

            if (!string.IsNullOrWhiteSpace(storeCode))
            {
                candidates = await db.Queryable<Store>()
                    .Where(store => store.IsDeleted == false && store.StoreCode == storeCode.Trim())
                    .ToListAsync();
            }

            if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(storeName))
            {
                candidates = await db.Queryable<Store>()
                    .Where(store => store.IsDeleted == false && store.StoreName == storeName.Trim())
                    .ToListAsync();
            }

            if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(storeName))
            {
                candidates = await db.Queryable<Store>()
                    .Where(
                        store => store.IsDeleted == false && store.StoreName.Contains(storeName.Trim())
                    )
                    .Take(10)
                    .ToListAsync();
            }

            return BuildMatchResult(
                rawValue,
                candidates.Select(
                    store => new LocalSupplierInvoiceImportLookupOptionDto
                    {
                        Code = store.StoreCode,
                        Name = store.StoreName
                    }
                )
            );
        }

        private async Task<LocalSupplierInvoiceImportMatchDto> ResolveSupplierAsync(
            string? supplierCode,
            string? supplierName,
            CancellationToken cancellationToken
        )
        {
            var rawValue = JoinRawValues(supplierCode, supplierName);
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return new LocalSupplierInvoiceImportMatchDto
                {
                    Status = LocalSupplierInvoiceImportMatchStatus.Missing
                };
            }

            var db = _context.Db;
            var candidates = new List<HBLocalSupplier>();

            if (!string.IsNullOrWhiteSpace(supplierCode))
            {
                candidates = await db.Queryable<HBLocalSupplier>()
                    .Where(
                        supplier =>
                            supplier.IsDeleted == false
                            && supplier.LocalSupplierCode == supplierCode.Trim()
                    )
                    .ToListAsync();
            }

            if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(supplierName))
            {
                candidates = await db.Queryable<HBLocalSupplier>()
                    .Where(
                        supplier =>
                            supplier.IsDeleted == false && supplier.Name == supplierName.Trim()
                    )
                    .ToListAsync();
            }

            if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(supplierName))
            {
                candidates = await db.Queryable<HBLocalSupplier>()
                    .Where(
                        supplier =>
                            supplier.IsDeleted == false
                            && supplier.Name.Contains(supplierName.Trim())
                    )
                    .Take(10)
                    .ToListAsync();
            }

            return BuildMatchResult(
                rawValue,
                candidates.Select(
                    supplier => new LocalSupplierInvoiceImportLookupOptionDto
                    {
                        Code = supplier.LocalSupplierCode,
                        Name = supplier.Name
                    }
                )
            );
        }

        private static LocalSupplierInvoiceImportMatchDto BuildMatchResult(
            string rawValue,
            IEnumerable<LocalSupplierInvoiceImportLookupOptionDto> options
        )
        {
            var candidates = options
                .GroupBy(option => $"{option.Code}::{option.Name}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (candidates.Count == 1)
            {
                return new LocalSupplierInvoiceImportMatchDto
                {
                    Status = LocalSupplierInvoiceImportMatchStatus.Matched,
                    RawValue = rawValue,
                    SelectedCode = candidates[0].Code,
                    SelectedName = candidates[0].Name,
                    Candidates = candidates
                };
            }

            return new LocalSupplierInvoiceImportMatchDto
            {
                Status = candidates.Count == 0
                    ? LocalSupplierInvoiceImportMatchStatus.NotFound
                    : LocalSupplierInvoiceImportMatchStatus.MultipleMatches,
                RawValue = rawValue,
                Candidates = candidates
            };
        }

        private static string JoinRawValues(string? code, string? name)
        {
            var values = new[] { code?.Trim(), name?.Trim() }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return string.Join(" / ", values);
        }

        private static int DetectHeaderRow(List<CandidateRow> candidateRows)
        {
            var bestRow = candidateRows
                .Select(
                    row => new
                    {
                        row.RowNumber,
                        Score = CalculateHeaderScore(row),
                        CellCount = row.Cells.Count(cell => !string.IsNullOrWhiteSpace(cell))
                    }
                )
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.CellCount)
                .ThenBy(item => item.RowNumber)
                .FirstOrDefault();

            if (bestRow == null || bestRow.CellCount == 0)
            {
                throw new ImportPreviewException("未识别到可导入的表头", "HEADER_NOT_FOUND");
            }

            return bestRow.RowNumber;
        }

        private static int CalculateHeaderScore(CandidateRow row)
        {
            return row.Cells.Sum(
                cell =>
                    MappingAliases.Values.Any(
                        aliases => aliases.Any(alias => NormalizeAlias(cell) == NormalizeAlias(alias))
                    )
                        ? 2
                        : MappingAliases.Values.Any(
                            aliases => aliases.Any(alias => NormalizeAlias(cell).Contains(NormalizeAlias(alias)))
                        )
                            ? 1
                            : 0
            );
        }

        private static bool LooksLikeDetailRow(List<string> cells)
        {
            if (cells.Count < 5)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(cells[0])
                && !string.IsNullOrWhiteSpace(cells[1])
                && TryParseQuantity(cells[3], out _)
                && TryParseDecimal(cells[4], out var price)
                && price > 0;
        }

        private static void ApplyHeaderlessPdfMetadata(
            IDictionary<string, string?> metadata,
            List<CandidateRow> candidateRows,
            int firstDetailRowNumber
        )
        {
            var singleValueRows = candidateRows
                .Where(row => row.RowNumber < firstDetailRowNumber && row.Cells.Count == 1)
                .Select(row => row.Cells[0]?.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            if (singleValueRows.Count > 0 && !metadata.ContainsKey("storeCode"))
            {
                metadata["storeCode"] = singleValueRows[0];
            }

            if (singleValueRows.Count > 1 && !metadata.ContainsKey("supplierCode"))
            {
                metadata["supplierCode"] = singleValueRows[1];
            }

            if (singleValueRows.Count > 2 && !metadata.ContainsKey("invoiceNo"))
            {
                metadata["invoiceNo"] = singleValueRows[2];
            }
        }

        private static List<string> ReadWorksheetRow(IXLWorksheet worksheet, int rowNumber)
        {
            var lastColumn = worksheet.Row(rowNumber).LastCellUsed()?.Address.ColumnNumber ?? 0;
            if (lastColumn == 0)
            {
                return new List<string>();
            }

            return Enumerable.Range(1, lastColumn)
                .Select(column => worksheet.Cell(rowNumber, column).GetFormattedString().Trim())
                .ToList();
        }

        private static Dictionary<string, string?> ExtractWorksheetMetadata(
            IXLWorksheet worksheet,
            int headerRowNumber,
            List<LocalSupplierInvoiceImportSourceColumnDto> sourceColumns
        )
        {
            var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            for (var rowNumber = 1; rowNumber < headerRowNumber; rowNumber++)
            {
                var cells = ReadWorksheetRow(worksheet, rowNumber);
                ApplyMetadata(metadata, cells);
            }

            ApplyColumnDerivedMetadata(metadata, sourceColumns, rowNumber => worksheet.Cell(rowNumber, 1), worksheet, headerRowNumber);
            return metadata;
        }

        private static Dictionary<string, string?> ExtractTextMetadata(
            List<string> rawLines,
            int headerRowNumber,
            List<LocalSupplierInvoiceImportSourceColumnDto> sourceColumns,
            List<LocalSupplierInvoiceImportLineDto> lines
        )
        {
            var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in rawLines.Take(headerRowNumber - 1))
            {
                ApplyMetadata(metadata, SplitMetadataLine(line));
            }

            ApplyColumnDerivedMetadata(metadata, sourceColumns, lines);
            return metadata;
        }

        private static void ApplyMetadata(
            IDictionary<string, string?> metadata,
            IReadOnlyList<string> cells
        )
        {
            if (cells.Count == 0)
            {
                return;
            }

            if (cells.Count == 1)
            {
                var parts = SplitMetadataLine(cells[0]);
                if (parts.Count >= 2)
                {
                    SetMetadataValue(metadata, parts[0], parts[1]);
                }

                return;
            }

            SetMetadataValue(metadata, cells[0], cells[1]);
        }

        private static void SetMetadataValue(
            IDictionary<string, string?> metadata,
            string label,
            string? value
        )
        {
            var normalizedLabel = NormalizeAlias(label);
            if (string.IsNullOrWhiteSpace(normalizedLabel) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            foreach (var alias in HeaderAliases)
            {
                if (alias.Value.Any(item => normalizedLabel.Contains(NormalizeAlias(item))))
                {
                    metadata[alias.Key] = value?.Trim();
                    return;
                }
            }
        }

        private static void ApplyColumnDerivedMetadata(
            IDictionary<string, string?> metadata,
            List<LocalSupplierInvoiceImportSourceColumnDto> sourceColumns,
            List<LocalSupplierInvoiceImportLineDto> lines
        )
        {
            foreach (var headerAlias in HeaderAliases)
            {
                var columnKey = FindBestMatch(sourceColumns, headerAlias.Value);
                if (string.IsNullOrWhiteSpace(columnKey))
                {
                    continue;
                }

                var distinctValues = lines
                    .Select(line => GetLineValue(line, columnKey))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(2)
                    .ToList();

                if (distinctValues.Count == 1 && !metadata.ContainsKey(headerAlias.Key))
                {
                    metadata[headerAlias.Key] = distinctValues[0];
                }
            }
        }

        private static void ApplyColumnDerivedMetadata(
            IDictionary<string, string?> metadata,
            List<LocalSupplierInvoiceImportSourceColumnDto> sourceColumns,
            Func<int, IXLCell> _,
            IXLWorksheet worksheet,
            int headerRowNumber
        )
        {
            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? headerRowNumber;
            var lines = new List<LocalSupplierInvoiceImportLineDto>();
            for (var rowNumber = headerRowNumber + 1; rowNumber <= lastRow; rowNumber++)
            {
                var rawValues = sourceColumns.ToDictionary(
                    column => column.Key,
                    column =>
                    {
                        var index = sourceColumns.IndexOf(column);
                        return worksheet.Cell(rowNumber, index + 1).GetFormattedString().Trim();
                    }
                );
                lines.Add(new LocalSupplierInvoiceImportLineDto { RowNumber = rowNumber, RawValues = rawValues });
            }

            ApplyColumnDerivedMetadata(metadata, sourceColumns, lines);
        }

        private static string? GetMappedValue(
            LocalSupplierInvoiceImportLineDto line,
            string columnKey
        )
        {
            return GetLineValue(line, columnKey)?.Trim();
        }

        private static string? GetLineValue(LocalSupplierInvoiceImportLineDto line, string columnKey)
        {
            if (line.RawValues == null)
            {
                return null;
            }

            foreach (var kv in line.RawValues)
            {
                if (string.Equals(kv.Key, columnKey, StringComparison.OrdinalIgnoreCase))
                {
                    return kv.Value;
                }
            }

            return null;
        }

        private static bool TryParseQuantity(string? text, out int quantity)
        {
            quantity = 0;
            if (!TryParseDecimal(text, out var decimalValue) || decimalValue <= 0)
            {
                return false;
            }

            if (decimal.Truncate(decimalValue) != decimalValue)
            {
                return false;
            }

            quantity = (int)decimalValue;
            return true;
        }

        private static bool TryParseDecimal(string? text, out decimal value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var normalized = text.Trim().Replace(",", string.Empty);
            return decimal.TryParse(
                    normalized,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out value
                )
                || decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.CurrentCulture, out value);
        }

        private static DateTime? TryParseDate(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return DateTime.TryParse(text, out var date) ? date : null;
        }

        private static string NormalizeAlias(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9\u4e00-\u9fa5]", string.Empty);
        }

        private static List<string> SplitTextLine(string line)
        {
            var parts = Regex.Split(line, @"\s{2,}|\t+|\|")
                .Select(part => part.Trim())
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToList();

            if (parts.Count > 0)
            {
                return parts;
            }

            return line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private static List<string> SplitMetadataLine(string line)
        {
            return Regex.Split(line, @"[:：]")
                .Select(part => part.Trim())
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToList();
        }

        private static string ExtractPdfText(byte[] fileBytes)
        {
            using var stream = new MemoryStream(fileBytes);
            using var document = PdfDocument.Open(stream);
            var pages = new List<string>();
            foreach (var page in document.GetPages())
            {
                var text = ExtractPdfPageText(page);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }
                pages.Add(text.Trim());
            }

            return string.Join(Environment.NewLine, pages);
        }

        private static string ExtractPdfPageText(Page page)
        {
            var words = page.GetWords()
                .Where(word => !string.IsNullOrWhiteSpace(word.Text))
                .OrderByDescending(word => word.BoundingBox.Top)
                .ThenBy(word => word.BoundingBox.Left)
                .ToList();

            if (words.Count == 0)
            {
                return page.Text ?? string.Empty;
            }

            var lines = new List<List<Word>>();
            var currentLine = new List<Word>();
            double? currentTop = null;
            const double lineTolerance = 4d;

            // PDF 的原始文本顺序经常不是阅读顺序；这里按坐标重建行，提升表格型随货单解析稳定性。
            foreach (var word in words)
            {
                if (currentTop == null || Math.Abs(word.BoundingBox.Top - currentTop.Value) <= lineTolerance)
                {
                    currentLine.Add(word);
                    currentTop ??= word.BoundingBox.Top;
                    continue;
                }

                lines.Add(currentLine);
                currentLine = new List<Word> { word };
                currentTop = word.BoundingBox.Top;
            }

            if (currentLine.Count > 0)
            {
                lines.Add(currentLine);
            }

            return string.Join(
                Environment.NewLine,
                lines.Select(line => string.Join("    ", line.OrderBy(word => word.BoundingBox.Left).Select(word => word.Text)))
            );
        }

        private static void EnsureSourceColumnLimit(int count)
        {
            if (count > MaxSourceColumnCount)
            {
                throw new ImportPreviewException(
                    $"导入列数不能超过 {MaxSourceColumnCount} 列",
                    "TOO_MANY_COLUMNS"
                );
            }
        }

        private static void EnsureLineLimit(int count)
        {
            if (count > MaxImportLineCount)
            {
                throw new ImportPreviewException(
                    $"导入明细不能超过 {MaxImportLineCount} 行",
                    "TOO_MANY_LINES"
                );
            }
        }

        private sealed record CandidateRow(int RowNumber, List<string> Cells);

        private sealed record ParsedImportTable(
            List<LocalSupplierInvoiceImportSourceColumnDto> SourceColumns,
            List<LocalSupplierInvoiceImportLineDto> Lines,
            Dictionary<string, string?> Metadata,
            List<string> Warnings,
            List<string> Errors
        );

        private sealed class ImportPreviewException : Exception
        {
            public ImportPreviewException(string message, string code)
                : base(message)
            {
                Code = code;
            }

            public string Code { get; }
        }
    }
}
