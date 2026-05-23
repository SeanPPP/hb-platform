using System.ComponentModel.DataAnnotations;
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
