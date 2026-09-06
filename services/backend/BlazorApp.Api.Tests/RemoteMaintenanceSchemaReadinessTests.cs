using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class RemoteMaintenanceSchemaReadinessTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public async Task ReadinessUsesSqlParametersAndPreservesDatabaseResult(int result, bool expected)
    {
        var ado = new Mock<IAdo>(MockBehavior.Strict);
        ado.Setup(x => x.GetIntAsync(It.IsAny<string>(), It.Is<SugarParameter[]>(p => p.Length == 0)))
            .ReturnsAsync(result);
        var client = new Mock<ISqlSugarClient>();
        client.SetupGet(x => x.Ado).Returns(ado.Object);
        // 仅注入数据库边界，避免测试连接生产数据库；严格 mock 会拒绝将取消令牌误当成 SQL 参数。
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, client.Object);
        var readiness = new RemoteMaintenanceSchemaReadiness(context, NullLogger<RemoteMaintenanceSchemaReadiness>.Instance);
        using var cancellation = new CancellationTokenSource();

        Assert.Equal(expected, await readiness.IsReadyAsync(cancellation.Token));
        ado.VerifyAll();
    }
}
