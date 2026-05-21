using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Services;
using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Tests;

public sealed class LocalizationAndSettingsTests
{
    [Fact]
    public void Localization_defaults_to_en_us_and_returns_english_text()
    {
        var localization = new LocalizationService();

        Assert.Equal("en-US", localization.CurrentCulture.Name);
        Assert.Contains(localization.AvailableCultures, culture => culture.Name == "en-US");
        Assert.Contains(localization.AvailableCultures, culture => culture.Name == "zh-CN");
        Assert.Equal("POS Terminal", localization.T("PosTerminal"));
    }

    [Fact]
    public void Localization_switches_to_zh_cn_and_notifies_consumers()
    {
        var localization = new LocalizationService();
        var notificationCount = 0;
        localization.CultureChanged += (_, _) => notificationCount++;

        localization.SetCulture("zh-CN");

        Assert.Equal("zh-CN", localization.CurrentCulture.Name);
        Assert.Equal("POS 收银台", localization.T("PosTerminal"));
        Assert.Equal(1, notificationCount);
    }

    [Fact]
    public void Localization_returns_placeholder_for_missing_key()
    {
        var localization = new LocalizationService();

        Assert.Equal("[[DefinitelyMissingKey]]", localization.T("DefinitelyMissingKey"));
    }

    [Fact]
    public async Task App_settings_store_and_restore_language()
    {
        var databasePath = CreateTempDatabasePath();

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var settings = new LocalAppSettingsRepository(store);
            await schema.InitializeAsync();

            await settings.SetValueAsync("Language", "zh-CN");

            Assert.Equal("zh-CN", await settings.GetValueAsync("Language"));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Local_schema_creates_app_settings_table()
    {
        var databasePath = CreateTempDatabasePath();

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            await schema.InitializeAsync();

            await using var connection = await store.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table' AND name = 'AppSettings';
                """;

            var count = (long)(await command.ExecuteScalarAsync() ?? 0L);
            Assert.Equal(1L, count);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    private static string CreateTempDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), $"hbpos-client-{Guid.NewGuid():N}.db");
    }

    private static void DeleteTempDatabase(string databasePath)
    {
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }
}
