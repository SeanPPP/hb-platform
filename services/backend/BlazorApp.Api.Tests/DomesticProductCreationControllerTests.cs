using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public class DomesticProductCreationControllerTests
    {
        [Fact]
        public void CreateBatchItemDto_ProductName_IsOptional()
        {
            var property = typeof(CreateBatchItemDto).GetProperty(nameof(CreateBatchItemDto.ProductName));

            Assert.NotNull(property);
            Assert.Empty(property!.GetCustomAttributes(typeof(RequiredAttribute), inherit: true));
        }

        [Fact]
        public void CreateBatchItemDto_SupportsNestedSetTemplate()
        {
            var item = new CreateBatchItemDto
            {
                ProductName = "套装模板",
                ProductType = 1,
                CreateCount = 4,
                SubItems = new List<CreateBatchItemDto>
                {
                    new()
                    {
                        ProductName = "子项A",
                        ProductType = 2,
                        PrivateLabelPrice = 12.5m,
                    },
                    new()
                    {
                        ProductName = "子项B",
                        ProductType = 2,
                        PrivateLabelPrice = 15m,
                    },
                },
            };

            var json = JsonSerializer.Serialize(item);

            Assert.Contains("\"createCount\":4", json);
            Assert.Contains("\"subItems\"", json);
            Assert.Equal(2, item.SubItems.Count);
            Assert.All(item.SubItems, subItem => Assert.Equal(2, subItem.ProductType));
        }

        [Fact]
        public void ExportBatch_OrdersSetSubItemsUnderParentSet()
        {
            var method = typeof(DomesticProductCreationService).GetMethod(
                "OrderBatchDetailItemsForExport",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
            );

            Assert.NotNull(method);

            var items = new List<BatchDetailItemDto>
            {
                new()
                {
                    HBProductNo = "HB001-8001-02",
                    Barcode = "9527800100002",
                    ProductName = "子项2",
                    ProductType = 2,
                    ParentHBProductNo = "HB001-8001",
                },
                new()
                {
                    HBProductNo = "HB001-9001",
                    Barcode = "9527900100001",
                    ProductName = "普通商品",
                    ProductType = 0,
                },
                new()
                {
                    HBProductNo = "HB001-8001",
                    Barcode = "9527800100001",
                    ProductName = "套装商品",
                    ProductType = 1,
                },
                new()
                {
                    HBProductNo = "HB001-8001-01",
                    Barcode = "9527800100003",
                    ProductName = "子项1",
                    ProductType = 2,
                    ParentHBProductNo = "HB001-8001",
                },
                new()
                {
                    HBProductNo = "HB001-0000-01",
                    Barcode = "9527000000001",
                    ProductName = "父货号异常子项",
                    ProductType = 2,
                    ParentHBProductNo = "HB001-MISSING",
                },
            };

            var ordered = Assert.IsAssignableFrom<List<BatchDetailItemDto>>(
                method!.Invoke(null, new object[] { items })
            );

            Assert.Equal(
                new[]
                {
                    "HB001-8001",
                    "HB001-8001-01",
                    "HB001-8001-02",
                    "HB001-0000-01",
                    "HB001-9001",
                },
                ordered.Select(x => x.HBProductNo).ToArray()
            );
        }

        [Fact]
        public void ExportBatch_GeneratesBarcodePngImage()
        {
            var method = typeof(DomesticProductCreationService).GetMethod(
                "GenerateBarcodeImagePng",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
            );

            Assert.NotNull(method);

            var imageBytes = Assert.IsType<byte[]>(method!.Invoke(null, new object?[] { "9527800100001" }));

            Assert.True(imageBytes.Length > 8);
            Assert.Equal(0x89, imageBytes[0]);
            Assert.Equal((byte)'P', imageBytes[1]);
            Assert.Equal((byte)'N', imageBytes[2]);
            Assert.Equal((byte)'G', imageBytes[3]);
        }

        [Fact]
        public void UpdateBatchItemDto_AllowsEmptyProductNameAndPrice()
        {
            var item = new UpdateBatchItemDto
            {
                ProductCode = "P001",
                ProductName = "",
                PrivateLabelPrice = null,
            };
            var context = new ValidationContext(item);

            var results = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(
                item,
                context,
                results,
                validateAllProperties: true
            );

            Assert.True(isValid);
            Assert.Empty(results);
        }

        [Fact]
        public async Task ExportBatch_ReturnsExcelFile_WhenServiceSucceeds()
        {
            var service = new Mock<IDomesticProductCreationService>();
            service
                .Setup(x => x.ExportBatchAsync("B20260521001"))
                .ReturnsAsync(
                    ApiResponse<DomesticProductBatchExportFileDto>.OK(
                        new DomesticProductBatchExportFileDto
                        {
                            Content = new byte[] { 1, 2, 3 },
                            FileName = "domestic-product-batch-B20260521001.xlsx",
                        }
                    )
                );
            var controller = new DomesticProductCreationController(
                service.Object,
                Mock.Of<ILogger<DomesticProductCreationController>>()
            );

            var result = await controller.ExportBatch("B20260521001");

            var file = Assert.IsType<FileContentResult>(result);
            Assert.Equal(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                file.ContentType
            );
            Assert.Equal("domestic-product-batch-B20260521001.xlsx", file.FileDownloadName);
            Assert.Equal(new byte[] { 1, 2, 3 }, file.FileContents);
        }

        [Fact]
        public async Task ExportBatch_ReturnsNotFound_WhenBatchDoesNotExist()
        {
            var service = new Mock<IDomesticProductCreationService>();
            service
                .Setup(x => x.ExportBatchAsync("missing"))
                .ReturnsAsync(
                    ApiResponse<DomesticProductBatchExportFileDto>.Error(
                        "批次不存在",
                        "BATCH_NOT_FOUND"
                    )
                );
            var controller = new DomesticProductCreationController(
                service.Object,
                Mock.Of<ILogger<DomesticProductCreationController>>()
            );

            var result = await controller.ExportBatch("missing");

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task UpdateBatchItems_ReturnsOk_WhenServiceSucceeds()
        {
            var request = new UpdateBatchItemsRequest
            {
                Items = new List<UpdateBatchItemDto>
                {
                    new()
                    {
                        ProductCode = "P001",
                        ProductName = "",
                        PrivateLabelPrice = null,
                    },
                },
            };
            var service = new Mock<IDomesticProductCreationService>();
            service
                .Setup(x => x.UpdateBatchItemsAsync("B20260521001", request))
                .ReturnsAsync(ApiResponse<object>.CreateSuccess("成功更新 1 个商品"));
            var controller = new DomesticProductCreationController(
                service.Object,
                Mock.Of<ILogger<DomesticProductCreationController>>()
            );

            var result = await controller.UpdateBatchItems("B20260521001", request);

            var ok = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<ApiResponse<object>>(ok.Value);
            Assert.True(response.Success);
        }

        [Fact]
        public async Task UpdateBatchItems_ReturnsNotFound_WhenBatchDoesNotExist()
        {
            var request = new UpdateBatchItemsRequest
            {
                Items = new List<UpdateBatchItemDto>
                {
                    new()
                    {
                        ProductCode = "P001",
                        ProductName = "Updated",
                        PrivateLabelPrice = 12.5m,
                    },
                },
            };
            var service = new Mock<IDomesticProductCreationService>();
            service
                .Setup(x => x.UpdateBatchItemsAsync("missing", request))
                .ReturnsAsync(ApiResponse<object>.Error("批次不存在", "BATCH_NOT_FOUND"));
            var controller = new DomesticProductCreationController(
                service.Object,
                Mock.Of<ILogger<DomesticProductCreationController>>()
            );

            var result = await controller.UpdateBatchItems("missing", request);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task CreateBatchAsync_套装子项只创建关系表且日志关联父商品()
        {
            using var database = new DomesticProductCreationTestDatabase();
            var service = database.CreateService();

            var result = await service.CreateBatchAsync(
                new CreateDomesticProductBatchRequest
                {
                    SupplierCode = "HB001",
                    Items = new List<CreateBatchItemDto>
                    {
                        new()
                        {
                            ProductName = "父套装",
                            ProductType = 1,
                            PrivateLabelPrice = 30m,
                            SetPrice = 50m,
                            SubItems = new List<CreateBatchItemDto>
                            {
                                new()
                                {
                                    ProductName = "套装子项",
                                    ProductType = 2,
                                    PrivateLabelPrice = 12m,
                                },
                            },
                        },
                    },
                }
            );

            Assert.True(result.Success, result.Message);
            var products = await database.Db.Queryable<DomesticProduct>().ToListAsync();
            var setProducts = await database.Db.Queryable<DomesticSetProduct>().ToListAsync();
            var logs = await database.Db.Queryable<DomesticProductCreationLog>().ToListAsync();
            var histories = await database.Db.Queryable<WarehouseProductChangeHistory>().ToListAsync();

            var parent = Assert.Single(products);
            Assert.Equal(2, setProducts.Count);
            var childRelation = Assert.Single(
                setProducts,
                setProduct => setProduct.SetProductNo != setProduct.ProductNo
            );
            var childLog = Assert.Single(
                logs,
                log => log.Remark != null && log.Remark.StartsWith("Parent:")
            );
            Assert.Equal(parent.ProductCode, childLog.ProductCode);
            Assert.Equal(childRelation.SetProductNo, childLog.HBProductNo);
            Assert.Equal(childRelation.SetBarcode, childLog.Barcode);
            Assert.Equal("套装子项", childRelation.SetProductName);
            var history = Assert.Single(histories);
            Assert.Equal(parent.ProductCode, history.ProductCode);
            Assert.Equal("Create", history.Action);
            Assert.Equal("DomesticProductCreation", history.Source);
            Assert.NotNull(history.BatchGuid);
        }

        [Fact]
        public async Task CreateBatchAsync_普通商品与套装父项货号不重复()
        {
            using var database = new DomesticProductCreationTestDatabase();

            var result = await database.CreateService().CreateBatchAsync(
                new CreateDomesticProductBatchRequest
                {
                    SupplierCode = "HB001",
                    Items = new List<CreateBatchItemDto>
                    {
                        new() { ProductName = "普通商品", ProductType = 0 },
                        new() { ProductName = "套装父项", ProductType = 1 },
                    },
                }
            );

            Assert.True(result.Success, result.Message);
            var itemNumbers = result.Data!.Items.Select(item => item.HBProductNo).ToList();
            Assert.Equal(
                itemNumbers.Count,
                itemNumbers.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            );
        }

        [Fact]
        public async Task CreateBatchAsync_套装父项与所有子项货号和条码不重复()
        {
            using var database = new DomesticProductCreationTestDatabase();

            var result = await database.CreateService().CreateBatchAsync(
                new CreateDomesticProductBatchRequest
                {
                    SupplierCode = "HB001",
                    Items = new List<CreateBatchItemDto>
                    {
                        new()
                        {
                            ProductName = "套装A",
                            ProductType = 1,
                            SubItems = new List<CreateBatchItemDto>
                            {
                                new() { ProductName = "套装A子项1", ProductType = 2 },
                                new() { ProductName = "套装A子项2", ProductType = 2 },
                            },
                        },
                        new()
                        {
                            ProductName = "套装B",
                            ProductType = 1,
                            SubItems = new List<CreateBatchItemDto>
                            {
                                new() { ProductName = "套装B子项1", ProductType = 2 },
                                new() { ProductName = "套装B子项2", ProductType = 2 },
                            },
                        },
                    },
                }
            );

            Assert.True(result.Success, result.Message);
            var itemNumbers = result
                .Data!.Items.Select(item => item.HBProductNo)
                .Concat(
                    result.Data.Items.SelectMany(item => item.SubItems).Select(item => item.HBProductNo)
                )
                .Where(itemNumber => !string.IsNullOrWhiteSpace(itemNumber))
                .Cast<string>()
                .ToList();
            Assert.Equal(
                itemNumbers.Count,
                itemNumbers.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            );
            var barcodes = result
                .Data.Items.Select(item => item.Barcode)
                .Concat(result.Data.Items.SelectMany(item => item.SubItems).Select(item => item.Barcode))
                .Where(barcode => !string.IsNullOrWhiteSpace(barcode))
                .Cast<string>()
                .ToList();
            Assert.Equal(
                barcodes.Count,
                barcodes.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            );
        }

        [Fact]
        public async Task ItemBarcodeService_并发生成的货号和条码不重复()
        {
            using var database = new DomesticProductCreationTestDatabase();
            var generator = database.CreateItemBarcodeService();

            var generated = await Task.WhenAll(
                Enumerable
                    .Range(0, 4)
                    .Select(_ =>
                        generator.GenerateItemNumberAndBarcodeAsync(
                            "HB001",
                            ProductTypeEnum.Normal
                        )
                    )
            );

            Assert.Equal(
                generated.Length,
                generated
                    .Select(item => item.itemNumber)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count()
            );
            Assert.Equal(
                generated.Length,
                generated
                    .Select(item => item.barcode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count()
            );
        }

        [Fact]
        public async Task CreateBatchAsync_历史写入失败时回滚主档关系和创建日志()
        {
            using var database = new DomesticProductCreationTestDatabase();
            database.Db.Ado.ExecuteCommand(
                "CREATE TRIGGER block_history_insert BEFORE INSERT ON WarehouseProductChangeHistory "
                    + "BEGIN SELECT RAISE(ABORT, 'forced history failure'); END;"
            );

            var result = await database.CreateService().CreateBatchAsync(
                new CreateDomesticProductBatchRequest
                {
                    SupplierCode = "HB001",
                    Items = new List<CreateBatchItemDto>
                    {
                        new() { ProductName = "历史失败商品", ProductType = 0 },
                    },
                }
            );

            Assert.False(result.Success);
            Assert.Empty(await database.Db.Queryable<DomesticProduct>().ToListAsync());
        Assert.Empty(await database.Db.Queryable<DomesticProductCreationLog>().ToListAsync());
        Assert.Empty(await database.Db.Queryable<WarehouseProductChangeHistory>().ToListAsync());
    }

    [Fact]
    public async Task CreateBatchAsync_历史记录使用当前请求操作人和用户GUID()
    {
        using var database = new DomesticProductCreationTestDatabase();
        var currentUser = new Mock<ICurrentUserService>(MockBehavior.Strict);
        currentUser.Setup(item => item.GetCurrentUsername()).Returns("创建请求用户");
        currentUser.Setup(item => item.GetCurrentUserGuid()).Returns("creation-user-guid");

        var result = await database.CreateService(currentUser.Object).CreateBatchAsync(
            new CreateDomesticProductBatchRequest
            {
                SupplierCode = "HB001",
                Items = new List<CreateBatchItemDto>
                {
                    new() { ProductName = "操作人商品", ProductType = 0 },
                },
            }
        );

        Assert.True(result.Success, result.Message);
        var history = await database.Db.Queryable<WarehouseProductChangeHistory>().SingleAsync();
        Assert.Equal("creation-user-guid", history.ActorUserGuid);
        Assert.Equal("创建请求用户", history.ActorName);
        Assert.Equal("User", history.ActorType);
    }

    [Fact]
    public async Task GetBatchDetailAsync_子日志共享父商品编码时从套装关系读取子项价格()
        {
            using var database = new DomesticProductCreationTestDatabase();
            const string batchNumber = "B20260724001";
            const string parentProductCode = "parent-product";
            const string parentProductNo = "HB001-001";
            const string childProductNo = "HB001-001-01";

            await database.Db.Insertable(
                new DomesticProduct
                {
                    ProductCode = parentProductCode,
                    SupplierCode = "HB001",
                    ProductName = "父套装",
                    HBProductNo = parentProductNo,
                    Barcode = "9527800100016",
                    ProductType = 1,
                    OEMPrice = 30m,
                    IsActive = true,
                }
            ).ExecuteCommandAsync();
            await database.Db.Insertable(
                new[]
                {
                    new DomesticSetProduct
                    {
                        SetProductCode = "set-parent",
                        ProductCode = parentProductCode,
                        ProductNo = parentProductNo,
                        SetProductNo = parentProductNo,
                        SetBarcode = "9527800100016",
                        OEMPrice = 30m,
                        DomesticPrice = 50m,
                    },
                    new DomesticSetProduct
                    {
                        SetProductCode = "set-child",
                        ProductCode = parentProductCode,
                        ProductNo = parentProductNo,
                        SetProductNo = childProductNo,
                        SetProductName = "关系子项名称",
                        SetBarcode = "9527800100023",
                        OEMPrice = 12m,
                    },
                }
            ).ExecuteCommandAsync();
            await database.Db.Insertable(
                new[]
                {
                    CreateLog(batchNumber, parentProductCode, parentProductNo, "父套装", null),
                    CreateLog(
                        batchNumber,
                        parentProductCode,
                        childProductNo,
                        "套装子项",
                        $"Parent: {parentProductNo}"
                    ),
                }
            ).ExecuteCommandAsync();

            var service = database.CreateService();
            var result = await service.GetBatchDetailAsync(batchNumber);

            Assert.True(result.Success, result.Message);
            var child = Assert.Single(result.Data!.Items, item => item.ProductType == 2);
            Assert.Equal("set-child", child.ProductCode);
            Assert.Equal("关系子项名称", child.ProductName);
            Assert.Equal(12m, child.PrivateLabelPrice);
            Assert.Equal(parentProductCode, child.ParentProductCode);
            Assert.Equal(parentProductNo, child.ParentHBProductNo);
            Assert.Null(child.SetQuantity);
            Assert.Null(child.SetPrice);

            var export = await service.ExportBatchAsync(batchNumber);
            Assert.True(export.Success, export.Message);
            Assert.NotEmpty(export.Data!.Content);

            var updateChild = await service.UpdateBatchItemsAsync(
                batchNumber,
                new UpdateBatchItemsRequest
                {
                    Items = new List<UpdateBatchItemDto>
                    {
                        new()
                        {
                            ProductCode = child.ProductCode,
                            ProductName = "已编辑子项",
                            PrivateLabelPrice = 16m,
                        },
                    },
                }
            );
            Assert.True(updateChild.Success, updateChild.Message);
            Assert.Equal(
                16m,
                (await database.Db.Queryable<DomesticSetProduct>()
                    .InSingleAsync("set-child"))!.OEMPrice
            );
            Assert.Equal(
                "已编辑子项",
                (await database.Db.Queryable<DomesticProductCreationLog>()
                    .Where(log => log.HBProductNo == childProductNo)
                    .SingleAsync()).ProductName
            );
            Assert.Equal(
                "已编辑子项",
                (await database.Db.Queryable<DomesticSetProduct>()
                    .InSingleAsync("set-child"))!.SetProductName
            );

            var updateParentPrice = await service.UpdatePrivateLabelPriceAsync(
                batchNumber,
                new UpdatePrivateLabelPriceRequest
                {
                    Items = new List<UpdatePriceItemDto>
                    {
                        new() { ProductCode = parentProductCode, PrivateLabelPrice = 20m },
                    },
                }
            );
            Assert.True(updateParentPrice.Success, updateParentPrice.Message);
            Assert.Equal(
                20m,
                (await database.Db.Queryable<DomesticSetProduct>()
                    .InSingleAsync("set-parent"))!.OEMPrice
            );
            Assert.Equal(
                16m,
                (await database.Db.Queryable<DomesticSetProduct>()
                    .InSingleAsync("set-child"))!.OEMPrice
            );

            var updateChildPrice = await service.UpdatePrivateLabelPriceAsync(
                batchNumber,
                new UpdatePrivateLabelPriceRequest
                {
                    Items = new List<UpdatePriceItemDto>
                    {
                        new() { ProductCode = child.ProductCode, PrivateLabelPrice = 22m },
                    },
                }
            );
            Assert.True(updateChildPrice.Success, updateChildPrice.Message);
            Assert.Equal(
                22m,
                (await database.Db.Queryable<DomesticSetProduct>()
                    .InSingleAsync("set-child"))!.OEMPrice
            );
        }

        [Fact]
        public async Task SetItemBarcodeGenerators_关系表已有子项时避开货号和条码()
        {
            using var database = new DomesticProductCreationTestDatabase();
            const string parentProductCode = "parent-product";
            const string parentProductNo = "HB001-001";
            const string usedChildProductNo = "HB001-001-01";
            var usedChildBarcode = BarcodeHelper.GenerateEAN13Barcode(
                "HB001",
                (int)ProductTypeEnum.Set,
                new List<string>(),
                true
            );

            await database.Db.Insertable(
                new DomesticProduct
                {
                    ProductCode = parentProductCode,
                    SupplierCode = "HB001",
                    ProductName = "父套装",
                    HBProductNo = parentProductNo,
                    Barcode = "9527800100092",
                    ProductType = 1,
                    IsActive = true,
                }
            ).ExecuteCommandAsync();
            await database.Db.Insertable(
                new DomesticSetProduct
                {
                    SetProductCode = "set-child",
                    ProductCode = parentProductCode,
                    ProductNo = parentProductNo,
                    SetProductNo = usedChildProductNo,
                    SetBarcode = usedChildBarcode,
                }
            ).ExecuteCommandAsync();

            var generator = database.CreateItemBarcodeService();
            var single = await generator.GenerateSetItemNumberAndBarcodeAsync(
                parentProductNo,
                ProductTypeEnum.Set
            );
            var batch = await generator.GenerateBatchSetItemNumbersAndBarcodesAsync(
                parentProductNo,
                ProductTypeEnum.Set,
                1
            );

            Assert.NotEqual(usedChildProductNo, single.itemNumber);
            Assert.NotEqual(usedChildBarcode, single.barcode);
            Assert.NotEqual(usedChildProductNo, Assert.Single(batch).itemNumber);
            Assert.NotEqual(usedChildBarcode, Assert.Single(batch).barcode);
        }

        [Fact]
        public async Task GetBatchDetailAsync_历史子项仍使用独立主档标识和价格()
        {
            using var database = new DomesticProductCreationTestDatabase();
            const string batchNumber = "B20260724002";
            const string parentProductCode = "legacy-parent";
            const string childProductCode = "legacy-child";
            const string parentProductNo = "HB001-002";
            const string childProductNo = "HB001-002-01";

            await database.Db.Insertable(
                new[]
                {
                    new DomesticProduct
                    {
                        ProductCode = parentProductCode,
                        SupplierCode = "HB001",
                        ProductName = "旧父套装",
                        HBProductNo = parentProductNo,
                        ProductType = 1,
                        OEMPrice = 30m,
                        IsActive = true,
                    },
                    new DomesticProduct
                    {
                        ProductCode = childProductCode,
                        SupplierCode = "HB001",
                        ProductName = "旧子项",
                        HBProductNo = childProductNo,
                        ProductType = 0,
                        OEMPrice = 12m,
                        IsActive = true,
                    },
                }
            ).ExecuteCommandAsync();
            await database.Db.Insertable(
                new DomesticSetProduct
                {
                    SetProductCode = "legacy-set-child",
                    ProductCode = parentProductCode,
                    ProductNo = parentProductNo,
                    SetProductNo = childProductNo,
                    OEMPrice = 99m,
                }
            ).ExecuteCommandAsync();
            await database.Db.Insertable(
                new[]
                {
                    CreateLog(batchNumber, parentProductCode, parentProductNo, "旧父套装", null),
                    CreateLog(
                        batchNumber,
                        childProductCode,
                        childProductNo,
                        "旧子项",
                        $"Parent: {parentProductNo}"
                    ),
                }
            ).ExecuteCommandAsync();

            var result = await database.CreateService().GetBatchDetailAsync(batchNumber);

            Assert.True(result.Success, result.Message);
            var child = Assert.Single(result.Data!.Items, item => item.ProductType == 2);
            Assert.Equal(childProductCode, child.ProductCode);
            Assert.Equal("旧子项", child.ProductName);
            Assert.Equal(12m, child.PrivateLabelPrice);
        }

        [Fact]
        public async Task UpdatePrivateLabelPriceAsync_跨批次主档和子项关系均拒绝()
        {
            using var database = new DomesticProductCreationTestDatabase();
            const string currentBatch = "B20260724003";
            const string otherBatch = "B20260724004";

            await database.Db.Insertable(
                new[]
                {
                    new DomesticProduct
                    {
                        ProductCode = "current-product",
                        SupplierCode = "HB001",
                        ProductName = "当前批次商品",
                        HBProductNo = "HB001-003",
                        ProductType = 0,
                        OEMPrice = 10m,
                        IsActive = true,
                    },
                    new DomesticProduct
                    {
                        ProductCode = "other-product",
                        SupplierCode = "HB001",
                        ProductName = "其他批次父套装",
                        HBProductNo = "HB001-004",
                        ProductType = 1,
                        OEMPrice = 30m,
                        IsActive = true,
                    },
                }
            ).ExecuteCommandAsync();
            await database.Db.Insertable(
                new DomesticSetProduct
                {
                    SetProductCode = "other-set-child",
                    ProductCode = "other-product",
                    ProductNo = "HB001-004",
                    SetProductNo = "HB001-004-01",
                    OEMPrice = 12m,
                }
            ).ExecuteCommandAsync();
            await database.Db.Insertable(
                new[]
                {
                    CreateLog(currentBatch, "current-product", "HB001-003", "当前批次商品", null),
                    CreateLog(otherBatch, "other-product", "HB001-004", "其他批次父套装", null),
                    CreateLog(
                        otherBatch,
                        "other-product",
                        "HB001-004-01",
                        "其他批次子项",
                        "Parent: HB001-004"
                    ),
                }
            ).ExecuteCommandAsync();

            var result = await database.CreateService().UpdatePrivateLabelPriceAsync(
                currentBatch,
                new UpdatePrivateLabelPriceRequest
                {
                    Items = new List<UpdatePriceItemDto>
                    {
                        new() { ProductCode = "other-product", PrivateLabelPrice = 99m },
                        new() { ProductCode = "other-set-child", PrivateLabelPrice = 88m },
                    },
                }
            );

            Assert.False(result.Success);
            Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
            Assert.Equal(
                30m,
                (await database.Db.Queryable<DomesticProduct>()
                    .InSingleAsync("other-product"))!.OEMPrice
            );
            Assert.Equal(
                12m,
                (await database.Db.Queryable<DomesticSetProduct>()
                    .InSingleAsync("other-set-child"))!.OEMPrice
            );
        }

        [Fact]
        public async Task CreateBatchAsync_日志写入失败时回滚父商品和套装关系()
        {
            using var database = new DomesticProductCreationTestDatabase();
            database.Db.Ado.ExecuteCommand(
                "CREATE TRIGGER block_creation_log_insert BEFORE INSERT ON DomesticProductCreationLog "
                    + "BEGIN SELECT RAISE(ABORT, 'forced creation log failure'); END;"
            );

            var result = await database.CreateService().CreateBatchAsync(
                new CreateDomesticProductBatchRequest
                {
                    SupplierCode = "HB001",
                    Items = new List<CreateBatchItemDto>
                    {
                        new()
                        {
                            ProductName = "回滚套装",
                            ProductType = 1,
                            PrivateLabelPrice = 30m,
                            SubItems = new List<CreateBatchItemDto>
                            {
                                new()
                                {
                                    ProductName = "回滚子项",
                                    ProductType = 2,
                                    PrivateLabelPrice = 12m,
                                },
                            },
                        },
                    },
                }
            );

            Assert.False(result.Success);
            Assert.Empty(await database.Db.Queryable<DomesticProduct>().ToListAsync());
            Assert.Empty(await database.Db.Queryable<DomesticSetProduct>().ToListAsync());
            Assert.Empty(await database.Db.Queryable<DomesticProductCreationLog>().ToListAsync());
        }

        [Fact]
        public async Task UpdateBatchItemsAsync_日志更新失败时回滚商品更新()
        {
            using var database = new DomesticProductCreationTestDatabase();
            const string batchNumber = "B20260724005";
            await database.Db.Insertable(
                new DomesticProduct
                {
                    ProductCode = "rollback-product",
                    SupplierCode = "HB001",
                    ProductName = "更新前名称",
                    HBProductNo = "HB001-005",
                    ProductType = 0,
                    OEMPrice = 10m,
                    IsActive = true,
                }
            ).ExecuteCommandAsync();
            await database.Db.Insertable(
                CreateLog(batchNumber, "rollback-product", "HB001-005", "更新前名称", null)
            ).ExecuteCommandAsync();
            database.Db.Ado.ExecuteCommand(
                "CREATE TRIGGER block_creation_log_update BEFORE UPDATE ON DomesticProductCreationLog "
                    + "BEGIN SELECT RAISE(ABORT, 'forced creation log update failure'); END;"
            );

            var result = await database.CreateService().UpdateBatchItemsAsync(
                batchNumber,
                new UpdateBatchItemsRequest
                {
                    Items = new List<UpdateBatchItemDto>
                    {
                        new()
                        {
                            ProductCode = "rollback-product",
                            ProductName = "更新后名称",
                            PrivateLabelPrice = 20m,
                        },
                    },
                }
            );

            Assert.False(result.Success);
            var product = await database.Db.Queryable<DomesticProduct>()
                .InSingleAsync("rollback-product");
            var log = await database.Db.Queryable<DomesticProductCreationLog>()
                .SingleAsync(item => item.HBProductNo == "HB001-005");
            Assert.Equal("更新前名称", product!.ProductName);
            Assert.Equal(10m, product.OEMPrice);
            Assert.Equal("更新前名称", log.ProductName);
        }

        [Fact]
        public async Task GetBatchDetailAsync_批量加载主档和关系而非逐项查询()
        {
            using var database = new DomesticProductCreationTestDatabase();
            const string batchNumber = "B20260724006";
            var products = Enumerable
                .Range(1, 5)
                .Select(index =>
                    new DomesticProduct
                    {
                        ProductCode = $"detail-product-{index}",
                        SupplierCode = "HB001",
                        ProductName = $"详情商品{index}",
                        HBProductNo = $"HB001-006-{index:D2}",
                        ProductType = 0,
                        OEMPrice = index,
                        IsActive = true,
                    }
                )
                .ToList();
            await database.Db.Insertable(products).ExecuteCommandAsync();
            await database.Db.Insertable(
                products.Select(product =>
                    CreateLog(
                        batchNumber,
                        product.ProductCode,
                        product.HBProductNo!,
                        product.ProductName!,
                        null
                    )
                ).ToList()
            ).ExecuteCommandAsync();

            var selectCount = 0;
            database.Db.Aop.OnLogExecuting = (sql, _) =>
            {
                if (sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                    Interlocked.Increment(ref selectCount);
            };
            try
            {
                var result = await database.CreateService().GetBatchDetailAsync(batchNumber);

                Assert.True(result.Success, result.Message);
                Assert.Equal(5, result.Data!.Items.Count);
            }
            finally
            {
                database.Db.Aop.OnLogExecuting = null;
            }

            Assert.InRange(selectCount, 1, 3);
        }

        private static DomesticProductCreationLog CreateLog(
            string batchNumber,
            string productCode,
            string productNo,
            string productName,
            string? remark
        ) =>
            new()
            {
                LogId = Guid.NewGuid().ToString(),
                ProductCode = productCode,
                SupplierCode = "HB001",
                HBProductNo = productNo,
                ProductName = productName,
                BatchNumber = batchNumber,
                Remark = remark,
            };

        private sealed class DomesticProductCreationTestDatabase : IDisposable
        {
            private readonly string _databasePath = Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid():N}.db"
            );
            private readonly SqliteConnection _connection;
            private readonly IConfiguration _configuration;

            public DomesticProductCreationTestDatabase()
            {
                _connection = new SqliteConnection($"Data Source={_databasePath}");
                _connection.Open();
                Db = new SqlSugarClient(
                    new ConnectionConfig
                    {
                        ConnectionString = _connection.ConnectionString,
                        DbType = DbType.Sqlite,
                        IsAutoCloseConnection = false,
                        InitKeyType = InitKeyType.Attribute,
                    }
                );
                Db.CodeFirst.InitTables(
                    typeof(ChinaSupplier),
                    typeof(DomesticProduct),
                    typeof(ItemBarcodeReservation),
                    typeof(DomesticSetProduct),
                    typeof(DomesticProductCreationLog),
                    typeof(Product),
                    typeof(WarehouseProduct)
                );
                Db.Ado.ExecuteCommand(
                    """
                    CREATE TABLE WarehouseProductChangeHistory (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EventGuid TEXT NOT NULL,
                        ProductCode TEXT NOT NULL,
                        Action TEXT NOT NULL,
                        Source TEXT NOT NULL,
                        SourceReference TEXT NULL,
                        BatchGuid TEXT NULL,
                        ActorUserGuid TEXT NULL,
                        ActorName TEXT NOT NULL,
                        ActorType TEXT NOT NULL,
                        OccurredAtUtc TEXT NOT NULL,
                        ChangesJson TEXT NOT NULL
                    )
                    """
                );
                _configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:DefaultConnection"] = _connection.ConnectionString,
                        }
                    )
                    .Build();
            }

            public SqlSugarClient Db { get; }

            public DomesticProductCreationService CreateService(
                ICurrentUserService? currentUserService = null
            )
            {
                var resolvedCurrentUserService = currentUserService ?? Mock.Of<ICurrentUserService>();
                return new DomesticProductCreationService(
                    CreateSqlSugarContext(Db),
                    CreateItemBarcodeService(),
                    NullLogger<DomesticProductCreationService>.Instance,
                    new WarehouseProductChangeHistoryService(
                        CreateSqlSugarContext(Db),
                        NullLogger<WarehouseProductChangeHistoryService>.Instance,
                        resolvedCurrentUserService
                    ),
                    resolvedCurrentUserService
                );
            }

            public ItemBarcodeService CreateItemBarcodeService() =>
                new(
                    CreateSqlSugarContext(Db),
                    NullLogger<ItemBarcodeService>.Instance,
                    _configuration
                );

            public void Dispose()
            {
                Db.Dispose();
                _connection.Dispose();
                SqliteTempFileCleanup.DeleteIfExists(_databasePath);
            }
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
    }
}
