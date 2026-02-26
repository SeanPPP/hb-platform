using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using SqlSugar;
using ClosedXML.Excel;

using System.Text;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 义乌订单服务实现
    /// </summary>
    public class YiwuOrderService : IYiwuOrderService
    {
        private readonly SqlSugarContext _context;
        private readonly IMapper _mapper;
        private readonly ILogger<YiwuOrderService> _logger;
        private readonly HttpClient _httpClient;

        public YiwuOrderService(
            SqlSugarContext context,
            IMapper mapper,
            ILogger<YiwuOrderService> logger,
            HttpClient httpClient)
        {
            _context = context;
            _mapper = mapper;
            _logger = logger;
            _httpClient = httpClient;
        }

        #region 义乌订单主表相关

        public async Task<PagedResult<YIWU_Order>> GetOrdersAsync(int pageIndex = 1, int pageSize = 20, string? keyword = null)
        {
            try
            {
                var query = _context.Db.Queryable<YIWU_Order>()
                    .LeftJoin<ChinaSupplier>((o, cs) => o.SupplierCode == cs.SupplierCode)
                    .Select((o, cs) => new YIWU_Order
                    {
                        ID = o.ID,
                        OrderNo = o.OrderNo,
                        SupplierCode = o.SupplierCode,
                        TotalAmount = o.TotalAmount,
                        TotalVolume = o.TotalVolume,
                        OrderStatus = o.OrderStatus,
                        Remarks = o.Remarks,
                        CreatedAt = o.CreatedAt,
                        UpdatedAt = o.UpdatedAt,
                        ChinaSupplier = cs
                    });

                if (!string.IsNullOrEmpty(keyword))
                {
                    query = query.Where(o => o.OrderNo!.Contains(keyword) || o.SupplierCode!.Contains(keyword) || o.Remarks!.Contains(keyword));
                }

                var totalCount = await query.CountAsync();
                var items = await query
                    .OrderByDescending(o => o.CreatedAt)
                    .Skip((pageIndex - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                return new PagedResult<YIWU_Order>
                {
                    Items = items,
                    Total = totalCount,
                    Page = pageIndex,
                    PageSize = pageSize
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取义乌订单列表时发生错误");
                throw;
            }
        }

        public async Task<YIWU_Order?> GetOrderByIdAsync(int orderId)
        {
            try
            {
                return await _context.Db.Queryable<YIWU_Order>()
                    .Includes(o => o.OrderDetails)
                    .Includes(o => o.ChinaSupplier)
                    .Where(o => o.ID == orderId)
                    .FirstAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据ID获取义乌订单时发生错误: {OrderId}", orderId);
                throw;
            }
        }

        public async Task<YIWU_Order?> GetOrderByOrderNoAsync(string orderNo)
        {
            try
            {
                return await _context.Db.Queryable<YIWU_Order>()
                    .Includes(o => o.ChinaSupplier)
                    .Includes(o => o.OrderDetails)
                    .FirstAsync(o => o.OrderNo == orderNo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据订单编号获取义乌订单时发生错误: {OrderNo}", orderNo);
                throw;
            }
        }

        public async Task<YIWU_Order> CreateOrderAsync(YIWU_Order order)
        {
            try
            {
                // 生成订单编号
                if (string.IsNullOrEmpty(order.OrderNo))
                {
                    order.OrderNo = await GenerateOrderNoAsync();
                }

                // 设置创建时间
                order.CreatedAt = DateTime.Now;
                order.UpdatedAt = DateTime.Now;

                var result = await _context.YiwuOrderDb.InsertReturnEntityAsync(order);

                _logger.LogInformation("创建义乌订单成功: {OrderNo}", result.OrderNo);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建义乌订单时发生错误");
                throw;
            }
        }

        public async Task<bool> UpdateOrderAsync(YIWU_Order order)
        {
            try
            {
                order.UpdatedAt = DateTime.Now;

                // 重新计算订单总金额和体积
                await UpdateOrderTotalsAsync(order.OrderNo!);

                var result = await _context.YiwuOrderDb.UpdateAsync(order);

                _logger.LogInformation("更新义乌订单成功: {OrderNo}", order.OrderNo);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新义乌订单时发生错误: {OrderNo}", order.OrderNo);
                throw;
            }
        }

        public async Task<bool> DeleteOrderAsync(int orderId)
        {
            try
            {
                // 先删除订单明细
                await _context.Db.Deleteable<YIWU_OrderDetail>()
                    .Where(d => d.OrderNo == _context.Db.Queryable<YIWU_Order>().Where(o => o.ID == orderId).Select(o => o.OrderNo).First())
                    .ExecuteCommandAsync();

                // 再删除订单主表
                var result = await _context.YiwuOrderDb.DeleteByIdAsync(orderId);

                _logger.LogInformation("删除义乌订单成功: {OrderId}", orderId);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除义乌订单时发生错误: {OrderId}", orderId);
                throw;
            }
        }

        public async Task<string> GenerateOrderNoAsync()
        {
            try
            {
                var today = DateTime.Today;
                var dateString = today.ToString("yyMMdd");
                var prefix = $"ORD-{dateString}-";

                // 查找今日最大订单号
                var maxOrderNo = await _context.Db.Queryable<YIWU_Order>()
                    .Where(o => o.OrderNo!.StartsWith(prefix))
                    .OrderByDescending(o => o.OrderNo)
                    .Select(o => o.OrderNo)
                    .FirstAsync();

                int sequence = 1;
                if (!string.IsNullOrEmpty(maxOrderNo))
                {
                    var lastSequence = maxOrderNo.Substring(prefix.Length);
                    if (int.TryParse(lastSequence, out int lastNum))
                    {
                        sequence = lastNum + 1;
                    }
                }

                return $"{prefix}{sequence:D2}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成订单编号时发生错误");
                throw;
            }
        }

        #endregion

        #region 义乌订单明细相关

        public async Task<PagedResult<YIWU_OrderDetail>> GetOrderDetailsAsync(string? orderNo = null, int pageIndex = 1, int pageSize = 20)
        {
            try
            {
                var query = _context.Db.Queryable<YIWU_OrderDetail>()
                    .Includes(d => d.Order);

                if (!string.IsNullOrEmpty(orderNo))
                {
                    query = query.Where(d => d.OrderNo == orderNo);
                }

                var totalCount = await query.CountAsync();
                var items = await query
                    .OrderByDescending(d => d.CreatedAt)
                    .Skip((pageIndex - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                return new PagedResult<YIWU_OrderDetail>
                {
                    Items = items,
                    Total = totalCount,
                    Page = pageIndex,
                    PageSize = pageSize
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取义乌订单明细列表时发生错误");
                throw;
            }
        }

        public async Task<YIWU_OrderDetail?> GetOrderDetailByIdAsync(int detailId)
        {
            try
            {
                return await _context.YiwuOrderDetailDb.GetByIdAsync(detailId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据ID获取订单明细时发生错误: {DetailId}", detailId);
                throw;
            }
        }

        public async Task<YIWU_OrderDetail> CreateOrderDetailAsync(YIWU_OrderDetail orderDetail)
        {
            try
            {
                // 设置创建时间
                orderDetail.CreatedAt = DateTime.Now;
                orderDetail.UpdatedAt = DateTime.Now;

                // 计算订货金额和体积
                CalculateOrderDetailTotals(orderDetail);

                var result = await _context.YiwuOrderDetailDb.InsertReturnEntityAsync(orderDetail);

                // 更新主表总金额和体积
                if (!string.IsNullOrEmpty(orderDetail.OrderNo))
                {
                    await UpdateOrderTotalsAsync(orderDetail.OrderNo);
                }

                _logger.LogInformation("创建义乌订单明细成功: {OrderNo}-{ProductCode}", orderDetail.OrderNo, orderDetail.ProductCode);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建义乌订单明细时发生错误");
                throw;
            }
        }

        public async Task<bool> UpdateOrderDetailAsync(YIWU_OrderDetail orderDetail)
        {
            try
            {
                orderDetail.UpdatedAt = DateTime.Now;

                // 重新计算订货金额和体积
                CalculateOrderDetailTotals(orderDetail);

                var result = await _context.YiwuOrderDetailDb.UpdateAsync(orderDetail);

                // 更新主表总金额和体积
                if (!string.IsNullOrEmpty(orderDetail.OrderNo))
                {
                    await UpdateOrderTotalsAsync(orderDetail.OrderNo);
                }

                _logger.LogInformation("更新义乌订单明细成功: {OrderNo}-{ProductCode}", orderDetail.OrderNo, orderDetail.ProductCode);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新义乌订单明细时发生错误");
                throw;
            }
        }

        public async Task<bool> DeleteOrderDetailAsync(int detailId)
        {
            try
            {
                var orderDetail = await _context.YiwuOrderDetailDb.GetByIdAsync(detailId);
                if (orderDetail == null) return false;

                var result = await _context.YiwuOrderDetailDb.DeleteByIdAsync(detailId);

                // 更新主表总金额和体积
                if (!string.IsNullOrEmpty(orderDetail.OrderNo))
                {
                    await UpdateOrderTotalsAsync(orderDetail.OrderNo);
                }

                _logger.LogInformation("删除义乌订单明细成功: {DetailId}", detailId);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除义乌订单明细时发生错误: {DetailId}", detailId);
                throw;
            }
        }

        public async Task<List<YIWU_OrderDetail>> CreateOrderDetailsAsync(List<YIWU_OrderDetail> orderDetails)
        {
            try
            {
                foreach (var detail in orderDetails)
                {
                    detail.CreatedAt = DateTime.Now;
                    detail.UpdatedAt = DateTime.Now;
                    CalculateOrderDetailTotals(detail);
                }

                var result = await _context.YiwuOrderDetailDb.InsertRangeAsync(orderDetails);

                // 更新相关订单的总金额和体积
                var orderNos = orderDetails.Select(d => d.OrderNo).Distinct().Where(o => !string.IsNullOrEmpty(o)).ToList();
                foreach (var orderNo in orderNos)
                {
                    await UpdateOrderTotalsAsync(orderNo!);
                }

                _logger.LogInformation("批量创建义乌订单明细成功: {Count}条", orderDetails.Count);
                return orderDetails;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量创建义乌订单明细时发生错误");
                throw;
            }
        }

        #endregion

        #region 从PDA订单创建义乌订单

        public async Task<List<YIWU_Order>> CreateOrdersFromPDAAsync()
        {
            try
            {
                _logger.LogInformation("开始从PDA订单明细创建义乌订单");

                // 获取并分组PDA订单明细
                var supplierGroups = await GroupPDADetailsBySupplierAsync();

                if (!supplierGroups.Any())
                {
                    _logger.LogWarning("没有找到PDA订单明细数据");
                    return new List<YIWU_Order>();
                }

                var createdOrders = new List<YIWU_Order>();

                // 为每个供应商创建订单
                foreach (var group in supplierGroups)
                {
                    var supplierCode = group.Key;
                    var details = group.Value;

                    // 创建订单主表
                    var order = new YIWU_Order
                    {
                        OrderNo = await GenerateOrderNoAsync(),
                        SupplierCode = supplierCode,
                        OrderStatus = 0, // 草稿状态
                        Remarks = $"从PDA订单自动生成，包含{details.Count}个商品",
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };

                    var createdOrder = await CreateOrderAsync(order);

                    // 更新明细的订单编号并重新保存
                    foreach (var detail in details)
                    {
                        detail.OrderNo = createdOrder.OrderNo;
                        detail.UpdatedAt = DateTime.Now;
                        CalculateOrderDetailTotals(detail);
                    }

                    await _context.YiwuOrderDetailDb.UpdateRangeAsync(details);

                    // 更新订单总金额和体积
                    await UpdateOrderTotalsAsync(createdOrder.OrderNo!);

                    createdOrders.Add(createdOrder);

                    _logger.LogInformation("为供应商 {SupplierCode} 创建义乌订单: {OrderNo}，包含 {DetailCount} 个明细",
                        supplierCode, createdOrder.OrderNo, details.Count);
                }

                _logger.LogInformation("从PDA订单创建义乌订单完成，共创建 {OrderCount} 个订单", createdOrders.Count);
                return createdOrders;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从PDA订单创建义乌订单时发生错误");
                throw;
            }
        }

        public async Task<List<YIWU_OrderDetail>> GetPDAOrderDetailsAsync()
        {
            try
            {
                return await _context.Db.Queryable<YIWU_OrderDetail>()
                    .Where(d => d.OrderNo == "PDA")
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取PDA订单明细时发生错误");
                throw;
            }
        }

        public async Task<Dictionary<string, List<YIWU_OrderDetail>>> GroupPDADetailsBySupplierAsync()
        {
            try
            {
                var pdaDetails = await GetPDAOrderDetailsAsync();

                return pdaDetails
                    .Where(d => !string.IsNullOrEmpty(d.SupplierCode))
                    .GroupBy(d => d.SupplierCode!)
                    .ToDictionary(g => g.Key, g => g.ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "按供应商分组PDA订单明细时发生错误");
                throw;
            }
        }

        #endregion

        #region 导出功能

        public async Task<byte[]> ExportOrderToExcelAsync(int orderId)
        {
            try
            {
                var order = await GetOrderByIdAsync(orderId);
                if (order == null) throw new ArgumentException("订单不存在");

                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("HB订单");

                // 设置表头
                worksheet.Cell(1, 1).Value = "订单编号";
                worksheet.Cell(1, 2).Value = order.OrderNo;
                worksheet.Cell(2, 1).Value = "供应商编码";
                worksheet.Cell(2, 2).Value = order.SupplierCode;
                worksheet.Cell(3, 1).Value = "订单状态";
                worksheet.Cell(3, 2).Value = GetOrderStatusText(order.OrderStatus);
                worksheet.Cell(4, 1).Value = "总金额";
                worksheet.Cell(4, 2).Value = order.TotalAmount;
                worksheet.Cell(5, 1).Value = "总体积";
                worksheet.Cell(5, 2).Value = order.TotalVolume;

                // 明细表头（移除商品编码列）
                int row = 7;
                worksheet.Cell(row, 1).Value = "HB货号";
                worksheet.Cell(row, 2).Value = "条形码";
                worksheet.Cell(row, 3).Value = "英文名称";
                worksheet.Cell(row, 4).Value = "国内价格";
                worksheet.Cell(row, 5).Value = "贴牌价格";
                worksheet.Cell(row, 6).Value = "订货数量";
                worksheet.Cell(row, 7).Value = "订货箱数";
                worksheet.Cell(row, 8).Value = "订货金额";
                worksheet.Cell(row, 9).Value = "订货体积";

                // 设置表头样式
                var headerRange = worksheet.Range(row, 1, row, 9);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                // 明细数据（移除商品编码列）
                if (order.OrderDetails != null)
                {
                    foreach (var detail in order.OrderDetails)
                    {
                        row++;
                        worksheet.Cell(row, 1).Value = detail.HBProductNo;

                        // 条形码特殊处理 - 设置为文本格式避免数字格式化
                        var barcodeCell = worksheet.Cell(row, 2);
                        if (!string.IsNullOrEmpty(detail.Barcode))
                        {
                            barcodeCell.Style.NumberFormat.Format = "@"; // 设置为文本格式
                            barcodeCell.Value = detail.Barcode;
                        }
                        else
                        {
                            barcodeCell.Value = "-"; // 空值显示为横线
                        }

                        worksheet.Cell(row, 3).Value = detail.EnglishName;
                        worksheet.Cell(row, 4).Value = detail.DomesticPrice;
                        worksheet.Cell(row, 5).Value = detail.OEMPrice;
                        worksheet.Cell(row, 6).Value = detail.OrderQuantity;
                        worksheet.Cell(row, 7).Value = detail.OrderBoxes;
                        worksheet.Cell(row, 8).Value = detail.OrderAmount;
                        worksheet.Cell(row, 9).Value = detail.OrderVolume;

                        // 设置数据行居中对齐
                        var dataRange = worksheet.Range(row, 1, row, 9);
                        dataRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        dataRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    }
                }

                // 优化列宽设置
                worksheet.ColumnsUsed().AdjustToContents();
                // 设置最小宽度，确保内容可见
                for (int col = 1; col <= 9; col++)
                {
                    if (worksheet.Column(col).Width < 12)
                        worksheet.Column(col).Width = 12;
                }

                // 转换为字节数组
                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导出订单到Excel时发生错误: {OrderId}", orderId);
                throw;
            }
        }



        public async Task<byte[]> ExportOrderToExcelWithImagesAsync(int orderId)
        {
            try
            {
                var order = await GetOrderByIdAsync(orderId);
                if (order == null) throw new ArgumentException("订单不存在");

                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("HB订单");

                // 设置表头信息
                worksheet.Cell(1, 1).Value = "订单编号";
                worksheet.Cell(1, 2).Value = order.OrderNo;
                worksheet.Cell(2, 1).Value = "供应商编码";
                worksheet.Cell(2, 2).Value = order.SupplierCode;
                worksheet.Cell(3, 1).Value = "订单状态";
                worksheet.Cell(3, 2).Value = GetOrderStatusText(order.OrderStatus);

                // 明细表头（图片列在第一列）
                int row = 5;
                worksheet.Cell(row, 1).Value = "商品图片";
                worksheet.Cell(row, 2).Value = "HB货号";
                worksheet.Cell(row, 3).Value = "条形码";
                worksheet.Cell(row, 4).Value = "英文名称";
                worksheet.Cell(row, 5).Value = "国内价格";
                worksheet.Cell(row, 6).Value = "订货数量";
                worksheet.Cell(row, 7).Value = "订货箱数";
                worksheet.Cell(row, 8).Value = "订货金额";

                // 设置表头样式
                var headerRange = worksheet.Range(row, 1, row, 8);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                // 明细数据和图片
                if (order.OrderDetails != null)
                {
                    // 批量并行下载所有图片
                    var imageUrls = order.OrderDetails
                        .Where(d => !string.IsNullOrEmpty(d.ProductImage))
                        .Select(d => d.ProductImage!)
                        .Distinct()
                        .ToList();

                    _logger.LogInformation("开始并行下载 {ImageCount} 张图片", imageUrls.Count);
                    var imageDict = await DownloadImagesInParallelAsync(imageUrls);
                    _logger.LogInformation("图片下载完成，成功下载 {SuccessCount} 张", imageDict.Count);

                    foreach (var detail in order.OrderDetails)
                    {
                        row++;

                        // 数据填充（图片列在第一列）
                        // 插入图片到第1列（从已下载的字典中获取）
                        if (!string.IsNullOrEmpty(detail.ProductImage) && imageDict.TryGetValue(detail.ProductImage, out var imageBytes))
                        {
                            try
                            {
                                using var imageStream = new MemoryStream(imageBytes);
                                var imageCell = worksheet.Cell(row, 1);

                                // 使用ClosedXML正确方式嵌入图片到单元格
                                var picture = worksheet.AddPicture(imageStream, $"Image_{row}");
                                
                                // 将图片锚定到单元格 - 这是嵌入图片的正确方式
                                picture.MoveTo(imageCell);
                                
                                // 设置图片大小以填充单元格（固定尺寸：80x80）
                                var cellWidthInPixels = 80 * 7; // 固定列宽80转像素
                                var cellHeightInPixels = 80 * 1.33; // 固定行高80转像素
                                
                                // 计算适合的图片尺寸，保持宽高比
                                var aspectRatio = (double)picture.Width / picture.Height;
                                var targetWidth = cellWidthInPixels - 4; // 留出4像素边距
                                var targetHeight = cellHeightInPixels - 4;
                                
                                if (targetWidth / aspectRatio <= targetHeight)
                                {
                                    // 宽度是限制因素
                                    picture.Width = (int)targetWidth;
                                    picture.Height = (int)(targetWidth / aspectRatio);
                                }
                                else
                                {
                                    // 高度是限制因素
                                    picture.Height = (int)targetHeight;
                                    picture.Width = (int)(targetHeight * aspectRatio);
                                }
                                
                                // 设置图片在单元格中居中
                                var offsetX = Math.Max(0, (cellWidthInPixels - picture.Width) / 2);
                                var offsetY = Math.Max(0, (cellHeightInPixels - picture.Height) / 2);
                                picture.MoveTo(imageCell, (int)offsetX, (int)offsetY);
                                
                                // 设置图片单元格样式
                                imageCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                                imageCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "插入图片到Excel失败: {ImageUrl}", detail.ProductImage);
                                worksheet.Cell(row, 1).Value = "图片插入失败";
                                worksheet.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                                worksheet.Cell(row, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                            }
                        }
                        else if (!string.IsNullOrEmpty(detail.ProductImage))
                        {
                            worksheet.Cell(row, 1).Value = "图片下载失败";
                            worksheet.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            worksheet.Cell(row, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        }
                        else
                        {
                            worksheet.Cell(row, 1).Value = "无图片";
                            worksheet.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            worksheet.Cell(row, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        }

                        worksheet.Cell(row, 2).Value = detail.HBProductNo;

                        // 条形码特殊处理 - 设置为文本格式避免数字格式化
                        var barcodeCell = worksheet.Cell(row, 3);
                        if (!string.IsNullOrEmpty(detail.Barcode))
                        {
                            barcodeCell.Style.NumberFormat.Format = "@"; // 设置为文本格式
                            barcodeCell.Value = detail.Barcode;
                        }
                        else
                        {
                            barcodeCell.Value = "-"; // 空值显示为横线
                        }

                        worksheet.Cell(row, 4).Value = detail.EnglishName;
                        worksheet.Cell(row, 5).Value = detail.DomesticPrice;
                        worksheet.Cell(row, 6).Value = detail.OrderQuantity;
                        worksheet.Cell(row, 7).Value = detail.OrderBoxes;
                        worksheet.Cell(row, 8).Value = detail.OrderAmount;

                        // 设置数据行居中对齐（除了图片列）
                        var dataRange1 = worksheet.Range(row, 2, row, 4); // HB货号、条形码、英文名称
                        var dataRange2 = worksheet.Range(row, 5, row, 8); // 国内价格到订货金额
                        dataRange1.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        dataRange1.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        dataRange2.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        dataRange2.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                        // 设置固定行高为80
                        worksheet.Row(row).Height = 80; // 固定行高为80
                    }
                }

                // 设置固定列宽为80
                worksheet.Column(1).Width = 80; // 固定图片列宽度为80
                worksheet.Columns(2, 8).AdjustToContents(); // 其他列自动调整
                // 设置最小宽度，确保内容可见
                for (int col = 2; col <= 8; col++)
                {
                    if (worksheet.Column(col).Width < 12)
                        worksheet.Column(col).Width = 12;
                }

                // 特殊列宽调整
                worksheet.Column(2).Width = Math.Max(worksheet.Column(2).Width, 18); // HB货号列
                worksheet.Column(3).Width = Math.Max(worksheet.Column(3).Width, 15); // 条形码列
                worksheet.Column(4).Width = Math.Max(worksheet.Column(4).Width, 25); // 英文名称列

                // 转换为字节数组
                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导出带图片的Excel时发生错误: {OrderId}", orderId);
                throw;
            }
        }

        #endregion

        #region 批量导出功能

        public async Task<byte[]> ExportMultipleOrdersToExcelWithImagesAsync(IEnumerable<int> orderIds, int maxConcurrency = 3)
        {
            try
            {
                _logger.LogInformation("开始批量导出 {OrderCount} 个订单的Excel", orderIds.Count());

                // 并行获取所有订单数据
                var orderTasks = orderIds.Select(async orderId =>
                {
                    try
                    {
                        return await GetOrderByIdAsync(orderId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "获取订单 {OrderId} 失败", orderId);
                        return null;
                    }
                });

                var orders = (await Task.WhenAll(orderTasks))
                    .Where(o => o != null)
                    .Cast<YIWU_Order>() // 明确告诉编译器这些都不是null
                    .ToList();

                if (!orders.Any())
                {
                    throw new InvalidOperationException("没有找到有效的订单数据");
                }

                // 收集所有需要下载的图片URL
                var allImageUrls = orders
                    .SelectMany(o => o.OrderDetails ?? Enumerable.Empty<YIWU_OrderDetail>())
                    .Where(d => !string.IsNullOrEmpty(d.ProductImage))
                    .Select(d => d.ProductImage!)
                    .Distinct()
                    .ToList();

                _logger.LogInformation("开始并行下载 {ImageCount} 张图片", allImageUrls.Count);
                var imageDict = await DownloadImagesInParallelAsync(allImageUrls, maxConcurrency * 3);
                _logger.LogInformation("图片下载完成，成功下载 {SuccessCount} 张", imageDict.Count);

                // 创建Excel工作簿
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("批量订单");

                // 设置表头（图片列在第四列，保持批量导出的逻辑）
                int row = 1;
                worksheet.Cell(row, 1).Value = "订单编号";
                worksheet.Cell(row, 2).Value = "供应商编码";
                worksheet.Cell(row, 3).Value = "订单状态";
                worksheet.Cell(row, 4).Value = "商品图片";
                worksheet.Cell(row, 5).Value = "HB货号";
                worksheet.Cell(row, 6).Value = "条形码";
                worksheet.Cell(row, 7).Value = "英文名称";
                worksheet.Cell(row, 8).Value = "国内价格";
                worksheet.Cell(row, 9).Value = "订货数量";
                worksheet.Cell(row, 10).Value = "订货箱数";
                worksheet.Cell(row, 11).Value = "订货金额";

                // 设置表头样式
                var headerRange = worksheet.Range(row, 1, row, 11);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                // 填充数据
                foreach (var order in orders)
                {
                    if (order.OrderDetails != null)
                    {
                        foreach (var detail in order.OrderDetails)
                        {
                            row++;

                            // 数据填充（图片列调整到第四列）
                            worksheet.Cell(row, 1).Value = order.OrderNo;
                            worksheet.Cell(row, 2).Value = order.SupplierCode;
                            worksheet.Cell(row, 3).Value = GetOrderStatusText(order.OrderStatus);

                            // 插入图片到第4列
                            if (!string.IsNullOrEmpty(detail.ProductImage) && imageDict.TryGetValue(detail.ProductImage, out var imageBytes))
                            {
                                try
                                {
                                    using var imageStream = new MemoryStream(imageBytes);
                                    var imageCell = worksheet.Cell(row, 4);

                                    // 使用ClosedXML正确方式嵌入图片到单元格
                                    var picture = worksheet.AddPicture(imageStream, $"BatchImage_{row}");
                                    
                                    // 将图片锚定到单元格 - 这是嵌入图片的正确方式
                                    picture.MoveTo(imageCell);
                                    
                                    // 设置图片大小以填充单元格（固定尺寸：80x80）
                                    var cellWidthInPixels = 80 * 7; // 固定列宽80转像素
                                    var cellHeightInPixels = 80 * 1.33; // 固定行高80转像素
                                    
                                    // 计算适合的图片尺寸，保持宽高比
                                    var aspectRatio = (double)picture.Width / picture.Height;
                                    var targetWidth = cellWidthInPixels - 4; // 留出4像素边距
                                    var targetHeight = cellHeightInPixels - 4;
                                    
                                    if (targetWidth / aspectRatio <= targetHeight)
                                    {
                                        // 宽度是限制因素
                                        picture.Width = (int)targetWidth;
                                        picture.Height = (int)(targetWidth / aspectRatio);
                                    }
                                    else
                                    {
                                        // 高度是限制因素
                                        picture.Height = (int)targetHeight;
                                        picture.Width = (int)(targetHeight * aspectRatio);
                                    }
                                    
                                    // 设置图片在单元格中居中
                                    var offsetX = Math.Max(0, (cellWidthInPixels - picture.Width) / 2);
                                    var offsetY = Math.Max(0, (cellHeightInPixels - picture.Height) / 2);
                                    picture.MoveTo(imageCell, (int)offsetX, (int)offsetY);
                                    
                                    // 设置图片单元格居中对齐
                                    imageCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                                    imageCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "插入图片到Excel失败: {ImageUrl}", detail.ProductImage);
                                    worksheet.Cell(row, 4).Value = "图片插入失败";
                                    worksheet.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                                    worksheet.Cell(row, 4).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                                }
                            }
                            else if (!string.IsNullOrEmpty(detail.ProductImage))
                            {
                                worksheet.Cell(row, 4).Value = "图片下载失败";
                                worksheet.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                                worksheet.Cell(row, 4).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                            }
                            else
                            {
                                worksheet.Cell(row, 4).Value = "无图片";
                                worksheet.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                                worksheet.Cell(row, 4).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                            }

                            worksheet.Cell(row, 5).Value = detail.HBProductNo;

                            // 条形码特殊处理 - 设置为文本格式避免数字格式化
                            var barcodeCell = worksheet.Cell(row, 6);
                            if (!string.IsNullOrEmpty(detail.Barcode))
                            {
                                barcodeCell.Style.NumberFormat.Format = "@"; // 设置为文本格式
                                barcodeCell.Value = detail.Barcode;
                            }
                            else
                            {
                                barcodeCell.Value = "-"; // 空值显示为横线
                            }

                            worksheet.Cell(row, 7).Value = detail.EnglishName;
                            worksheet.Cell(row, 8).Value = detail.DomesticPrice;
                            worksheet.Cell(row, 9).Value = detail.OrderQuantity;
                            worksheet.Cell(row, 10).Value = detail.OrderBoxes;
                            worksheet.Cell(row, 11).Value = detail.OrderAmount;

                            // 设置数据行居中对齐（除了图片列）
                            var dataRange1 = worksheet.Range(row, 1, row, 3); // 订单信息
                            var dataRange2 = worksheet.Range(row, 5, row, 7); // HB货号到英文名称
                            var dataRange3 = worksheet.Range(row, 8, row, 11); // 国内价格到订货金额
                            dataRange1.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            dataRange1.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                            dataRange2.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            dataRange2.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                            dataRange3.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            dataRange3.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                            // 设置固定行高为80
                            worksheet.Row(row).Height = 80; // 固定行高为80
                        }
                    }
                }

                // 设置固定列宽为80
                worksheet.Column(4).Width = 80; // 固定图片列宽度为80
                worksheet.Columns(1, 3).AdjustToContents(); // 订单信息列自动调整
                worksheet.Columns(5, 7).AdjustToContents(); // HB货号到英文名称列自动调整
                worksheet.Columns(8, 11).AdjustToContents(); // 国内价格到订货金额列自动调整

                // 设置最小宽度，确保内容可见
                for (int col = 1; col <= 11; col++)
                {
                    if (col != 4 && worksheet.Column(col).Width < 12) // 图片列除外
                        worksheet.Column(col).Width = 12;
                }

                // 特殊列宽调整
                worksheet.Column(5).Width = Math.Max(worksheet.Column(5).Width, 18); // HB货号列
                worksheet.Column(6).Width = Math.Max(worksheet.Column(6).Width, 15); // 条形码列
                worksheet.Column(7).Width = Math.Max(worksheet.Column(7).Width, 25); // 英文名称列

                // 转换为字节数组
                using var stream = new MemoryStream();
                workbook.SaveAs(stream);

                _logger.LogInformation("批量导出完成，共导出 {OrderCount} 个订单，{RowCount} 行数据", orders.Count, row - 1);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量导出订单Excel时发生错误");
                throw;
            }
        }

        #endregion

        #region 统计功能

        public async Task<OrderStatisticsDto> GetOrderStatisticsAsync()
        {
            try
            {
                var orders = await _context.Db.Queryable<YIWU_Order>().ToListAsync();
                var today = DateTime.Today;

                return new OrderStatisticsDto
                {
                    TotalOrders = orders.Count,
                    DraftOrders = orders.Count(o => o.OrderStatus == 0),
                    ConfirmedOrders = orders.Count(o => o.OrderStatus == 1),
                    CancelledOrders = orders.Count(o => o.OrderStatus == 2),
                    TotalAmount = orders.Sum(o => o.TotalAmount ?? 0),
                    TotalVolume = orders.Sum(o => o.TotalVolume ?? 0),
                    TodayNewOrders = orders.Count(o => o.CreatedAt.Date == today),
                    SupplierCount = orders.Select(o => o.SupplierCode).Distinct().Count()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取订单统计信息时发生错误");
                throw;
            }
        }

        public async Task<bool> UpdateOrderTotalsAsync(string orderNo)
        {
            try
            {
                var details = await _context.Db.Queryable<YIWU_OrderDetail>()
                    .Where(d => d.OrderNo == orderNo)
                    .ToListAsync();

                var totalAmount = details.Sum(d => d.OrderAmount ?? 0);
                var totalVolume = details.Sum(d => d.OrderVolume ?? 0);

                await _context.Db.Updateable<YIWU_Order>()
                    .SetColumns(o => new YIWU_Order
                    {
                        TotalAmount = totalAmount,
                        TotalVolume = totalVolume,
                        UpdatedAt = DateTime.Now
                    })
                    .Where(o => o.OrderNo == orderNo)
                    .ExecuteCommandAsync();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新订单总金额和体积时发生错误: {OrderNo}", orderNo);
                throw;
            }
        }

        #endregion

        #region 私有方法

        private void CalculateOrderDetailTotals(YIWU_OrderDetail orderDetail)
        {
            // 计算订货金额 = 价格 * 数量
            var price = orderDetail.DomesticPrice ?? orderDetail.OEMPrice ?? 0;
            orderDetail.OrderAmount = price * (orderDetail.OrderQuantity ?? 0);

            // 计算订货体积 = 单件体积 * 箱数
            orderDetail.OrderVolume = (orderDetail.UnitVolume ?? 0) * (orderDetail.OrderBoxes ?? 0);
        }

        private string GetOrderStatusText(int? status)
        {
            return status switch
            {
                0 => "草稿",
                1 => "已确认",
                2 => "已取消",
                _ => "未知"
            };
        }

        private async Task<byte[]?> DownloadImageAsync(string imageUrl)
        {
            try
            {
                using var response = await _httpClient.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsByteArrayAsync();
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "下载图片失败: {ImageUrl}", imageUrl);
                return null;
            }
        }

        private async Task<Dictionary<string, byte[]>> DownloadImagesInParallelAsync(IEnumerable<string> imageUrls, int maxConcurrency = 10)
        {
            var semaphore = new SemaphoreSlim(maxConcurrency);
            var imageTasks = imageUrls.Select(async imageUrl =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var imageBytes = await DownloadImageAsync(imageUrl);
                    return new { ImageUrl = imageUrl, ImageBytes = imageBytes };
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(imageTasks);
            return results
                .Where(r => r.ImageBytes != null)
                .ToDictionary(r => r.ImageUrl, r => r.ImageBytes!);
        }

        #endregion
    }
}