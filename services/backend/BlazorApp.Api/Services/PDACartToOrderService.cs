using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// PDA购物车转订单服务实现
    /// </summary>
    public class PDACartToOrderService : IPDACartToOrderService
    {
        private readonly ISqlSugarClient _db;
        private readonly ILogger<PDACartToOrderService> _logger;
        private readonly IStoreService _storeService;
        private readonly IOrderNumberGenerator _orderNumberGenerator;

        public PDACartToOrderService(
            SqlSugarContext context,
            ILogger<PDACartToOrderService> logger,
            IStoreService storeService,
            IOrderNumberGenerator orderNumberGenerator
        )
        {
            _db = context.Db;
            _logger = logger;
            _storeService = storeService;
            _orderNumberGenerator = orderNumberGenerator;
        }

        /// <summary>
        /// 将购物车转换为仓库订单
        /// </summary>
        public async Task<CartToOrderResponseDto> ConvertCartToOrderAsync(
            CartToOrderRequestDto request,
            string deviceHardwareId,
            string expectedStoreCode
        )
        {
            var response = new CartToOrderResponseDto();

            try
            {
                _logger.LogInformation(
                    "开始转换购物车为订单，购物车GUID: {CartGUID}, 设备ID: {DeviceHardwareId}",
                    request.CartGUID,
                    deviceHardwareId
                );

                await _db.Ado.BeginTranAsync();

                var cart = await _db.Queryable<Cart>()
                    .Where(c => c.CartGUID == request.CartGUID && !c.IsDeleted)
                    .FirstAsync();

                if (cart == null)
                {
                    await _db.Ado.RollbackTranAsync();
                    return new CartToOrderResponseDto
                    {
                        Success = false,
                        Message = "购物车不存在或已删除",
                    };
                }

                if (string.IsNullOrEmpty(cart.StoreGUID))
                {
                    await _db.Ado.RollbackTranAsync();
                    return new CartToOrderResponseDto
                    {
                        Success = false,
                        Message = "购物车未关联分店",
                    };
                }

                var storeResult = await _storeService.GetStoreByGuidAsync(cart.StoreGUID);
                if (storeResult?.Success != true || storeResult?.Data == null)
                {
                    await _db.Ado.RollbackTranAsync();
                    return new CartToOrderResponseDto
                    {
                        Success = false,
                        Message = "无法找到分店信息",
                    };
                }

                var storeCode = storeResult.Data.StoreCode;
                if (!string.Equals(
                    storeCode,
                    expectedStoreCode,
                    StringComparison.OrdinalIgnoreCase
                ))
                {
                    await _db.Ado.RollbackTranAsync();
                    return new CartToOrderResponseDto
                    {
                        Success = false,
                        ErrorCode = "PDA_CART_STORE_MISMATCH",
                        Message = "购物车不属于设备绑定分店",
                    };
                }

                var cartItems = await _db.Queryable<CartItem>()
                    .Where(ci => ci.CartGUID == request.CartGUID && !ci.IsDeleted)
                    .ToListAsync();
                if (cartItems == null || cartItems.Count == 0)
                {
                    await _db.Ado.RollbackTranAsync();
                    return new CartToOrderResponseDto { Success = false, Message = "购物车为空" };
                }
                _logger.LogInformation(
                    "分店信息: {StoreCode} - {StoreName}",
                    storeCode,
                    storeResult.Data.StoreName
                );

                var existingOrder = await _db.Queryable<WareHouseOrder>()
                    .Where(o => o.StoreCode == storeCode && o.FlowStatus == 0 && !o.IsDeleted)
                    .FirstAsync();

                WareHouseOrder order;
                var isNewOrder = existingOrder == null;

                if (existingOrder == null)
                {
                    var orderNo = await _orderNumberGenerator.GetNextOrderNoAsync();
                    _logger.LogInformation("创建新订单，订单号: {OrderNo}", orderNo);

                    order = new WareHouseOrder
                    {
                        OrderGUID = Guid.NewGuid().ToString("N"),
                        StoreCode = storeCode,
                        OrderNo = orderNo,
                        OrderDate = request.OrderDate ?? DateTime.Now,
                        Remarks = string.IsNullOrEmpty(request.Remarks)
                            ? cart.CartName
                            : (
                                string.IsNullOrEmpty(cart.CartName)
                                    ? request.Remarks
                                    : $"{cart.CartName} - {request.Remarks}"
                            ),
                        FlowStatus = 0,
                        ImportTotalAmount = 0,
                        OEMTotalAmount = 0,
                    };

                    await _db.Insertable(order).ExecuteCommandAsync();
                    _logger.LogInformation("新订单已创建: {OrderGUID}", order.OrderGUID);
                }
                else
                {
                    order = existingOrder;
                    _logger.LogInformation(
                        "使用现有订单: {OrderGUID}, 订单号: {OrderNo}",
                        order.OrderGUID,
                        order.OrderNo
                    );

                    if (!string.IsNullOrEmpty(request.Remarks))
                    {
                        order.Remarks = string.IsNullOrEmpty(order.Remarks)
                            ? request.Remarks
                            : $"{order.Remarks}; {request.Remarks}";
                    }
                }

                var existingDetails = await _db.Queryable<WareHouseOrderDetails>()
                    .Where(d => d.OrderGUID == order.OrderGUID)
                    .ToListAsync();

                var existingDetailMap = existingDetails.ToDictionary(d => d.ProductCode ?? "");

                decimal totalImportAmount = 0;
                decimal totalOEMAmount = 0;

                var newOrderDetails = new List<WareHouseOrderDetails>();
                var updateOrderDetails = new List<WareHouseOrderDetails>();

                foreach (var cartItem in cartItems)
                {
                    if (existingDetailMap.TryGetValue(cartItem.ProductCode, out var existingDetail))
                    {
                        existingDetail.Quantity += cartItem.Quantity;

                        existingDetail.ImportAmount =
                            existingDetail.Quantity * (existingDetail.ImportPrice ?? 0);
                        existingDetail.OEMAmount =
                            existingDetail.Quantity * (existingDetail.OEMPrice ?? 0);
                        updateOrderDetails.Add(existingDetail);

                        totalImportAmount += existingDetail.ImportAmount ?? 0;
                        totalOEMAmount += existingDetail.OEMAmount ?? 0;

                        _logger.LogInformation(
                            "更新现有订单明细: {DetailGUID}, 商品: {ProductCode}, 新数量: {Quantity}",
                            existingDetail.DetailGUID,
                            existingDetail.ProductCode,
                            existingDetail.Quantity
                        );
                    }
                    else
                    {
                        var detail = new WareHouseOrderDetails
                        {
                            DetailGUID = Guid.NewGuid().ToString("N"),
                            OrderGUID = order.OrderGUID,
                            StoreCode = storeCode,
                            StoreProductCode = cartItem.ItemNumber,
                            ProductCode = cartItem.ProductCode,
                            Quantity = cartItem.Quantity,
                            ImportPrice = cartItem.UnitPrice,
                            OEMPrice = cartItem.ActualPrice ?? cartItem.UnitPrice,
                            LastCost = cartItem.UnitPrice,
                        };

                        detail.ImportAmount = detail.Quantity * (detail.ImportPrice ?? 0);
                        detail.OEMAmount = detail.Quantity * (detail.OEMPrice ?? 0);

                        totalImportAmount += detail.ImportAmount ?? 0;
                        totalOEMAmount += detail.OEMAmount ?? 0;

                        newOrderDetails.Add(detail);

                        _logger.LogInformation(
                            "新增订单明细: 商品: {ProductCode}, 数量: {Quantity}",
                            detail.ProductCode,
                            detail.Quantity
                        );
                    }
                }

                if (newOrderDetails.Count > 0)
                {
                    await _db.Insertable(newOrderDetails).ExecuteCommandAsync();
                    _logger.LogInformation(
                        "批量插入订单明细完成: {Count} 条明细",
                        newOrderDetails.Count
                    );
                }

                if (updateOrderDetails.Count > 0)
                {
                    await _db.Updateable(updateOrderDetails).ExecuteCommandAsync();
                    _logger.LogInformation(
                        "批量更新订单明细完成: {Count} 条明细",
                        updateOrderDetails.Count
                    );
                }

                order.ImportTotalAmount = (order.ImportTotalAmount ?? 0) + totalImportAmount;
                order.OEMTotalAmount = (order.OEMTotalAmount ?? 0) + totalOEMAmount;

                await _db.Updateable(order).ExecuteCommandAsync();
                _logger.LogInformation(
                    "订单金额已更新: ImportTotalAmount={ImportTotalAmount}, OEMTotalAmount={OEMTotalAmount}",
                    order.ImportTotalAmount,
                    order.OEMTotalAmount
                );

                cart.IsDeleted = true;
                await _db.Updateable(cart).ExecuteCommandAsync();

                foreach (var cartItem in cartItems)
                {
                    cartItem.IsDeleted = true;
                }

                await _db.Updateable(cartItems).ExecuteCommandAsync();
                _logger.LogInformation("购物车已软删除: {CartGUID}", cart.CartGUID);

                await _db.Ado.CommitTranAsync();

                response = new CartToOrderResponseDto
                {
                    Success = true,
                    OrderGUID = order.OrderGUID,
                    OrderNo = order.OrderNo,
                    IsNewOrder = isNewOrder,
                    Message = isNewOrder ? "已创建新订单" : "已添加到现有订单",
                };

                _logger.LogInformation(
                    "购物车转换订单成功: {OrderGUID}, IsNewOrder: {IsNewOrder}",
                    order.OrderGUID,
                    isNewOrder
                );
            }
            catch (Exception ex)
            {
                await _db.Ado.RollbackTranAsync();
                _logger.LogError(ex, "转换购物车为订单失败: {CartGUID}", request.CartGUID);
                response = new CartToOrderResponseDto
                {
                    Success = false,
                    Message = "购物车转换失败，请稍后重试",
                };
            }

            return response;
        }
    }
}
