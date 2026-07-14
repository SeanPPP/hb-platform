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
    IMemoryCache memoryCache) : IEmergencyLoginPublicKeyProvider
{
    private const string CacheKey = "emergency-login:verification-public-keys:v1";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    public async Task<IReadOnlyDictionary<string, string>> GetKeysAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (forceRefresh)
        {
            memoryCache.Remove(CacheKey);
        }

        if (memoryCache.TryGetValue(CacheKey, out IReadOnlyDictionary<string, string>? cached) &&
            cached is not null)
        {
            return cached;
        }

        var snapshot = await repository.GetKeySetAsync(cancellationToken);
        var keys = snapshot.Keys
            .Where(key => EmergencyLoginKeyStatuses.IsDistributable(key.Status))
            .ToDictionary(key => key.KeyId, key => key.PublicKeyPem, StringComparer.Ordinal);
        memoryCache.Set(CacheKey, keys, CacheDuration);
        return keys;
    }
}
