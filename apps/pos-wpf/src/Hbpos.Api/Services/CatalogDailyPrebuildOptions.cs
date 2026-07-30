namespace Hbpos.Api.Services;

/// <summary>每日目录预构建的显式开关；默认关闭，避免未确认的生产负载变化。</summary>
public sealed class CatalogDailyPrebuildOptions
{
    public bool Enabled { get; init; }
}
