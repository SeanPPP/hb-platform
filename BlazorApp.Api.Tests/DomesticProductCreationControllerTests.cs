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
    }
}
