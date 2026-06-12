using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public class ProductWarehouseReactServiceSyncTests
    {
        [Fact]
        public async Task SyncFromHqAsync_应委托给全量同步服务()
        {
            var expected = new SyncResult
            {
                IsSuccess = true,
                Message = "委托同步成功",
                AddedCount = 8,
            };
            var fullSyncServiceMock = new Mock<IDataSyncFullService>();
            fullSyncServiceMock
                .Setup(service => service.SyncWarehouseProductsFromHqAsync(50000, 10000))
                .ReturnsAsync(expected);

            var localContext = CreateContext<SqlSugarContext>();
            var hqContext = CreateContext<HqSqlSugarContext>();
            var configuration = new ConfigurationBuilder().Build();

            var service = new ProductWarehouseReactService(
                localContext,
                hqContext,
                NullLogger<ProductWarehouseReactService>.Instance,
                configuration,
                new ItemBarcodeService(
                    localContext,
                    NullLogger<ItemBarcodeService>.Instance,
                    configuration
                ),
                Mock.Of<IMapper>(),
                fullSyncServiceMock.Object
            );

            var result = await service.SyncFromHqAsync();

            Assert.Same(expected, result);
            fullSyncServiceMock.Verify(
                service => service.SyncWarehouseProductsFromHqAsync(50000, 10000),
                Times.Once
            );
        }

        private static TContext CreateContext<TContext>()
            where TContext : class
        {
            return (TContext)RuntimeHelpers.GetUninitializedObject(typeof(TContext));
        }
    }
}
