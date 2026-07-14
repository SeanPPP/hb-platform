using Hbpos.Api.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Hbpos.Api.Auth;

public interface IEmergencyLoginPublicKeyProvider
{
    Task<IReadOnlyDictionary<string, string>> GetKeysAsync(
        bool forceRefresh,
        CancellationToken cancellationToken);
}

public sealed class EmergencyLoginPublicKeyProvider(
    IEmergencyLoginPublicKeyRepository repository,
    IMemoryCache memoryCache,
    TimeProvider? timeProvider = null) : IEmergencyLoginPublicKeyProvider
{
    private const string CacheKey = "emergency-login:verification-public-keys:v1";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyDictionary<string, string>> GetKeysAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (forceRefresh)
        {
            memoryCache.Remove(CacheKey);
        }

        var now = _timeProvider.GetUtcNow();
        if (memoryCache.TryGetValue(CacheKey, out CachedPublicKeys? cached) &&
            cached is not null &&
            now >= cached.LoadedAtUtc &&
            now - cached.LoadedAtUtc < CacheDuration)
        {
            return cached.Keys;
        }

        memoryCache.Remove(CacheKey);

        var snapshot = await repository.GetKeySetAsync(cancellationToken);
        var keys = snapshot.Keys
            .Where(key => EmergencyLoginKeyStatuses.IsDistributable(key.Status))
            .ToDictionary(key => key.KeyId, key => key.PublicKeyPem, StringComparer.Ordinal);
        memoryCache.Set(CacheKey, new CachedPublicKeys(now, keys));
        return keys;
    }

    private sealed record CachedPublicKeys(
        DateTimeOffset LoadedAtUtc,
        IReadOnlyDictionary<string, string> Keys);
}
