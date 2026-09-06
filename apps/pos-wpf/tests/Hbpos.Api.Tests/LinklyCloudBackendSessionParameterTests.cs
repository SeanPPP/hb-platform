using System.Reflection;
using Hbpos.Api.Services;
using SqlSugar;

namespace Hbpos.Api.Tests;

public sealed class LinklyCloudBackendSessionParameterTests
{
    [Fact]
    public void ToSessionParameters_binds_active_terminal_updated_at_as_datetime2_without_losing_ticks()
    {
        var value = new DateTime(2026, 9, 6, 4, 5, 17, 426, DateTimeKind.Utc)
            .AddTicks(6667);

        var parameter = GetParameter(new LinklyCloudBackendSessionRecord
        {
            TerminalUpdatedAt = value,
        });

        Assert.Equal(System.Data.DbType.DateTime2, parameter.DbType);
        Assert.Equal(value, parameter.Value);
    }

    [Fact]
    public void ToSessionParameters_keeps_terminal_updated_at_null_for_legacy_sessions()
    {
        var parameter = GetParameter(new LinklyCloudBackendSessionRecord
        {
            TerminalUpdatedAt = null,
        });

        Assert.Equal(System.Data.DbType.DateTime2, parameter.DbType);
        Assert.Null(parameter.Value);
    }

    private static SugarParameter GetParameter(LinklyCloudBackendSessionRecord session)
    {
        var method = typeof(SqlSugarLinklyCloudBackendAsyncRepository).GetMethod(
            "ToSessionParameters",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var parameters = Assert.IsType<SugarParameter[]>(method!.Invoke(null, [session]));
        return Assert.Single(parameters, parameter => parameter.ParameterName == "@TerminalUpdatedAt");
    }
}
