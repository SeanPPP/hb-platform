using Hbpos.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Hbpos.Api.Tests;

public sealed class StoreTimeZoneResolverTests
{
    // 2026-10-05 已进入悉尼夏令时（UTC+11），布里斯班仍是 UTC+10，两者此时相差 1 小时。
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-05T12:00:00Z");

    [Fact]
    public async Task ResolveAsync_keeps_last_resolved_time_zone_when_refresh_lookup_fails()
    {
        var clock = new MutableTimeProvider(Start);
        var lookup = new ScriptedLookup();
        var resolver = new StoreTimeZoneResolver(lookup.InvokeAsync, timeProvider: clock);

        lookup.Next(() => "Australia/Brisbane");
        Assert.Equal(TestStoreTimeZones.Brisbane.Id, (await resolver.ResolveAsync("1042", CancellationToken.None)).Id);

        // 10 分钟缓存过期后查库失败：必须沿用布里斯班，退回悉尼会让订单时间晚记 1 小时且事后无法更正。
        clock.Advance(TimeSpan.FromMinutes(11));
        lookup.Next(() => throw new InvalidOperationException("MainDb timeout"));
        Assert.Equal(TestStoreTimeZones.Brisbane.Id, (await resolver.ResolveAsync("1042", CancellationToken.None)).Id);
        Assert.Equal(2, lookup.CallCount);

        // 失败后的短暂重试间隔内不重复查库。
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(TestStoreTimeZones.Brisbane.Id, (await resolver.ResolveAsync("1042", CancellationToken.None)).Id);
        Assert.Equal(2, lookup.CallCount);

        // 重试间隔过后恢复查库。
        clock.Advance(TimeSpan.FromMinutes(1));
        lookup.Next(() => "Australia/Brisbane");
        Assert.Equal(TestStoreTimeZones.Brisbane.Id, (await resolver.ResolveAsync("1042", CancellationToken.None)).Id);
        Assert.Equal(3, lookup.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_falls_back_to_sydney_only_until_first_successful_lookup()
    {
        var clock = new MutableTimeProvider(Start);
        var lookup = new ScriptedLookup();
        var resolver = new StoreTimeZoneResolver(lookup.InvokeAsync, timeProvider: clock);

        // 冷启动就查库失败时没有可沿用的值，只能回退悉尼，并短暂缓存避免持续压库。
        lookup.Next(() => throw new InvalidOperationException("MainDb timeout"));
        Assert.Equal(TestStoreTimeZones.Sydney.Id, (await resolver.ResolveAsync("1042", CancellationToken.None)).Id);

        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(TestStoreTimeZones.Sydney.Id, (await resolver.ResolveAsync("1042", CancellationToken.None)).Id);
        Assert.Equal(1, lookup.CallCount);

        clock.Advance(TimeSpan.FromMinutes(1));
        lookup.Next(() => "Australia/Brisbane");
        Assert.Equal(TestStoreTimeZones.Brisbane.Id, (await resolver.ResolveAsync("1042", CancellationToken.None)).Id);
        Assert.Equal(2, lookup.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_failed_lookup_finishing_late_does_not_replace_concurrent_success()
    {
        var clock = new MutableTimeProvider(Start);
        var failing = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var succeeding = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new Queue<TaskCompletionSource<string?>>([failing, succeeding]);
        var resolver = new StoreTimeZoneResolver((_, _) => pending.Dequeue().Task, timeProvider: clock);

        // 两个上传请求同时未命中缓存：先发的查询失败，但比后发的成功查询更晚返回。
        var first = resolver.ResolveAsync("1042", CancellationToken.None);
        var second = resolver.ResolveAsync("1042", CancellationToken.None);
        succeeding.SetResult("Australia/Brisbane");
        Assert.Equal(TestStoreTimeZones.Brisbane.Id, (await second).Id);

        failing.SetException(new InvalidOperationException("MainDb timeout"));
        Assert.Equal(TestStoreTimeZones.Brisbane.Id, (await first).Id);
        Assert.Equal(TestStoreTimeZones.Brisbane.Id, (await resolver.ResolveAsync("1042", CancellationToken.None)).Id);
    }

    [Fact]
    public async Task ResolveAsync_does_not_cache_canceled_lookup()
    {
        var clock = new MutableTimeProvider(Start);
        var lookup = new ScriptedLookup();
        var resolver = new StoreTimeZoneResolver(lookup.InvokeAsync, timeProvider: clock);

        lookup.Next(() => throw new OperationCanceledException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => resolver.ResolveAsync("1042", CancellationToken.None));

        lookup.Next(() => "Australia/Brisbane");
        Assert.Equal(TestStoreTimeZones.Brisbane.Id, (await resolver.ResolveAsync("1042", CancellationToken.None)).Id);
        Assert.Equal(2, lookup.CallCount);
    }

    [Fact]
    public void AddHbposApiServices_resolves_store_time_zone_resolver()
    {
        // 生产装配只能走 public 构造函数（注入作用域工厂与 TimeProvider），测试用的 internal 构造函数不参与 DI。
        var services = new ServiceCollection();
        services.AddHbposApiServices();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<StoreTimeZoneResolver>(provider.GetRequiredService<IStoreTimeZoneResolver>());
    }

    private sealed class ScriptedLookup
    {
        private Func<string?> next = () => throw new InvalidOperationException("未安排查库结果");

        public int CallCount { get; private set; }

        public void Next(Func<string?> result) => next = result;

        public Task<string?> InvokeAsync(string storeCode, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(next());
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
