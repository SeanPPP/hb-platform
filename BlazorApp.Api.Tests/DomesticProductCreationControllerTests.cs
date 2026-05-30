using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
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
    }
}
