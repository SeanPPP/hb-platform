using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hbpos.Api.Services;
using Hbpos.Contracts.Catalog;

namespace Hbpos.Api.Tests;

public sealed class CatalogSnapshotStoreTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 7, 30, 1, 2, 3, TimeSpan.Zero);

    [Fact]
    public void Save_LoadsGzipSnapshotWithVerifiedManifest()
    {
        using var directory = new TemporaryDirectory();
        var store = new GzipCatalogSnapshotStore(directory.Path, maxSnapshotsPerStore: 2);
        var expected = CreateSnapshot("S01", "catalog-v1:one");

        store.Save(expected);

        var actual = Assert.Single(store.LoadAll(GeneratedAt.AddMinutes(1)));
        Assert.Equal(expected.StoreCode, actual.StoreCode);
        Assert.Equal(expected.CatalogVersion, actual.CatalogVersion);
        Assert.Equal(expected.SellableItems, actual.SellableItems);
        Assert.True(File.Exists(Path.Combine(directory.Path, "manifest.json")));
    }

    [Fact]
    public void LoadAll_SkipsCorruptSnapshotAndKeepsLastGoodVersion()
    {
        using var directory = new TemporaryDirectory();
        var store = new GzipCatalogSnapshotStore(directory.Path, maxSnapshotsPerStore: 2);
        var first = CreateSnapshot("S01", "catalog-v1:good");
        var second = CreateSnapshot("S02", "catalog-v1:corrupt");
        store.Save(first);
        store.Save(second);

        var corruptFile = Directory.GetFiles(directory.Path, "*.json.gz", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .First();
        File.WriteAllBytes(corruptFile, [0x00, 0x01, 0x02]);

        var actual = Assert.Single(store.LoadAll(GeneratedAt.AddMinutes(1)));
        Assert.Contains(actual.CatalogVersion, new[] { first.CatalogVersion, second.CatalogVersion });
    }

    [Fact]
    public void LoadDescriptors_ReadsTwentyStoresWithoutOpeningSnapshotBodies()
    {
        using var directory = new TemporaryDirectory();
        var store = new GzipCatalogSnapshotStore(directory.Path);
        for (var index = 1; index <= 20; index++)
        {
            store.Save(CreateSnapshot($"S{index:D2}", $"catalog-v1:{index:D2}"));
        }

        foreach (var bodyPath in Directory.GetFiles(
                     directory.Path,
                     "*.json.gz",
                     SearchOption.AllDirectories))
        {
            File.WriteAllBytes(bodyPath, [0x00, 0x01, 0x02]);
        }

        var restarted = new GzipCatalogSnapshotStore(directory.Path);
        var descriptors = restarted.LoadDescriptors(GeneratedAt.AddMinutes(1));

        Assert.Equal(20, descriptors.Count);
        Assert.All(descriptors, descriptor => Assert.Null(descriptor.Since));
    }

    [Fact]
    public void LoadDescriptorsAndBodies_KeepLastGoodSnapshotAfterRefreshTime()
    {
        using var directory = new TemporaryDirectory();
        var store = new GzipCatalogSnapshotStore(directory.Path);
        var snapshot = CreateSnapshot("S01", "catalog-v1:last-good");
        store.Save(snapshot);
        var afterRefreshTime = snapshot.ExpiresAt.AddDays(7);

        var descriptor = Assert.Single(store.LoadDescriptors(afterRefreshTime));
        var restored = Assert.Single(store.LoadAll(afterRefreshTime));

        Assert.Equal(snapshot.CatalogVersion, descriptor.CatalogVersion);
        Assert.Equal(snapshot.CatalogVersion, restored.CatalogVersion);
    }

    [Fact]
    public void Load_ValidatesOnlyRequestedSnapshotAndIsolatesCorruption()
    {
        using var directory = new TemporaryDirectory();
        var store = new GzipCatalogSnapshotStore(directory.Path);
        var good = CreateSnapshot("S01", "catalog-v1:good");
        var corrupt = CreateSnapshot("S02", "catalog-v1:corrupt");
        store.Save(good);
        var originalBodyPaths = Directory.GetFiles(
                directory.Path,
                "*.json.gz",
                SearchOption.AllDirectories)
            .ToHashSet(StringComparer.Ordinal);
        store.Save(corrupt);
        var corruptBodyPath = Assert.Single(
            Directory.GetFiles(
                directory.Path,
                "*.json.gz",
                SearchOption.AllDirectories),
            path => !originalBodyPaths.Contains(path));
        File.WriteAllBytes(corruptBodyPath, [0x00, 0x01, 0x02]);

        var restoredGood = store.Load(good.StoreCode, good.Since, good.CatalogVersion);
        var restoredCorrupt = store.Load(corrupt.StoreCode, corrupt.Since, corrupt.CatalogVersion);

        Assert.NotNull(restoredGood);
        Assert.Equal(good.SellableItems, restoredGood.SellableItems);
        Assert.Null(restoredCorrupt);
    }

    [Fact]
    public void Load_RejectsBodyWhoseMetadataDoesNotMatchManifest()
    {
        using var directory = new TemporaryDirectory();
        var store = new GzipCatalogSnapshotStore(directory.Path);
        var expected = CreateSnapshot("S01", "catalog-v1:one");
        store.Save(expected);
        var bodyPath = Assert.Single(Directory.GetFiles(
            directory.Path,
            "*.json.gz",
            SearchOption.AllDirectories));
        WriteSnapshotBody(bodyPath, expected with { StoreCode = "S99" });
        UpdateManifestSha256(directory.Path, bodyPath);

        var restored = store.Load(
            expected.StoreCode,
            expected.Since,
            expected.CatalogVersion);

        Assert.Null(restored);
    }

    [Fact]
    public void Save_RetainsNewestVersionsPerStore()
    {
        using var directory = new TemporaryDirectory();
        var store = new GzipCatalogSnapshotStore(directory.Path, maxSnapshotsPerStore: 2);

        store.Save(CreateSnapshot("S01", "catalog-v1:one"));
        store.Save(CreateSnapshot("S01", "catalog-v1:two"));
        store.Save(CreateSnapshot("S01", "catalog-v1:three"));

        var versions = store.LoadAll(GeneratedAt.AddMinutes(1))
            .Select(snapshot => snapshot.CatalogVersion)
            .OrderBy(version => version, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["catalog-v1:three", "catalog-v1:two"], versions);
    }

    [Fact]
    public void Save_DefaultRetentionKeepsThreeVersionsPerStore()
    {
        using var directory = new TemporaryDirectory();
        var store = new GzipCatalogSnapshotStore(directory.Path);
        for (var index = 1; index <= 4; index++)
        {
            store.Save(CreateSnapshot("S01", $"catalog-v1:{index}") with
            {
                GeneratedAt = GeneratedAt.AddMinutes(index),
                ExpiresAt = GeneratedAt.AddHours(2).AddMinutes(index),
            });
        }

        var versions = store.LoadDescriptors(GeneratedAt.AddMinutes(5))
            .Select(descriptor => descriptor.CatalogVersion)
            .OrderBy(version => version, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["catalog-v1:2", "catalog-v1:3", "catalog-v1:4"], versions);
        Assert.Equal(
            3,
            Directory.GetFiles(
                directory.Path,
                "*.json.gz",
                SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void RefreshExpiration_ExtendsManifestWithoutRewritingGzipBody()
    {
        using var directory = new TemporaryDirectory();
        var store = new GzipCatalogSnapshotStore(directory.Path);
        var snapshot = CreateSnapshot("S01", "catalog-v1:one");
        store.Save(snapshot);
        var bodyPath = Assert.Single(Directory.GetFiles(
            directory.Path,
            "*.json.gz",
            SearchOption.AllDirectories));
        var originalBody = File.ReadAllBytes(bodyPath);
        var extendedExpiration = GeneratedAt.AddHours(4);

        store.RefreshExpiration(
            snapshot.StoreCode,
            snapshot.Since,
            snapshot.CatalogVersion,
            extendedExpiration);

        Assert.Equal(originalBody, File.ReadAllBytes(bodyPath));
        var restored = Assert.Single(store.LoadAll(GeneratedAt.AddHours(3)));
        Assert.Equal(extendedExpiration, restored.ExpiresAt);
    }

    [Fact]
    public void RefreshExpiration_CorruptBodyFailsClosedAndPreservesManifest()
    {
        using var directory = new TemporaryDirectory();
        var store = new GzipCatalogSnapshotStore(directory.Path);
        var snapshot = CreateSnapshot("S01", "catalog-v1:one");
        store.Save(snapshot);
        var manifestPath = Path.Combine(directory.Path, "manifest.json");
        var originalManifest = File.ReadAllBytes(manifestPath);
        var bodyPath = Assert.Single(Directory.GetFiles(
            directory.Path,
            "*.json.gz",
            SearchOption.AllDirectories));
        File.WriteAllBytes(bodyPath, [0x00, 0x01, 0x02]);

        Assert.Throws<InvalidDataException>(() => store.RefreshExpiration(
            snapshot.StoreCode,
            snapshot.Since,
            snapshot.CatalogVersion,
            GeneratedAt.AddDays(1)));

        Assert.Equal(originalManifest, File.ReadAllBytes(manifestPath));
    }

    [Theory]
    [InlineData("unsupported-schema", """{"schemaVersion":2,"codec":"gzip","snapshots":[]}""")]
    [InlineData("unsupported-codec", """{"schemaVersion":1,"codec":"zstd","snapshots":[]}""")]
    [InlineData("invalid-json", "{\"schemaVersion\":")]
    public void Save_InvalidExistingManifestFailsClosedAndPreservesDiskArtifacts(
        string _,
        string manifestJson)
    {
        using var directory = new TemporaryDirectory();
        var manifestPath = Path.Combine(directory.Path, "manifest.json");
        var originalManifest = System.Text.Encoding.UTF8.GetBytes(manifestJson);
        var existingBodyPath = Path.Combine(directory.Path, "existing.json.gz");
        var originalBody = new byte[] { 0x01, 0x02, 0x03 };
        File.WriteAllBytes(manifestPath, originalManifest);
        File.WriteAllBytes(existingBodyPath, originalBody);
        var store = new GzipCatalogSnapshotStore(directory.Path);

        Assert.Throws<InvalidDataException>(() => store.Save(CreateSnapshot("S01", "catalog-v1:new")));

        Assert.Equal(originalManifest, File.ReadAllBytes(manifestPath));
        Assert.Equal(originalBody, File.ReadAllBytes(existingBodyPath));
        Assert.Equal(
            [existingBodyPath],
            Directory.GetFiles(directory.Path, "*.json.gz", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("unsupported-schema", """{"schemaVersion":2,"codec":"gzip","snapshots":[]}""")]
    [InlineData("unsupported-codec", """{"schemaVersion":1,"codec":"zstd","snapshots":[]}""")]
    [InlineData("invalid-json", "{\"schemaVersion\":")]
    public void RefreshExpiration_InvalidExistingManifestFailsClosedAndPreservesDiskArtifacts(
        string _,
        string manifestJson)
    {
        using var directory = new TemporaryDirectory();
        var manifestPath = Path.Combine(directory.Path, "manifest.json");
        var originalManifest = System.Text.Encoding.UTF8.GetBytes(manifestJson);
        var existingBodyPath = Path.Combine(directory.Path, "existing.json.gz");
        var originalBody = new byte[] { 0x01, 0x02, 0x03 };
        File.WriteAllBytes(manifestPath, originalManifest);
        File.WriteAllBytes(existingBodyPath, originalBody);
        var store = new GzipCatalogSnapshotStore(directory.Path);

        Assert.Throws<InvalidDataException>(() => store.RefreshExpiration(
            "S01",
            since: null,
            "catalog-v1:existing",
            GeneratedAt.AddHours(4)));

        Assert.Equal(originalManifest, File.ReadAllBytes(manifestPath));
        Assert.Equal(originalBody, File.ReadAllBytes(existingBodyPath));
        Assert.Equal(
            [existingBodyPath],
            Directory.GetFiles(directory.Path, "*.json.gz", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("""{"schemaVersion":1,"codec":"gzip","snapshots":null}""")]
    [InlineData("""{"schemaVersion":1,"codec":"zstd","snapshots":[]}""")]
    [InlineData("""{"schemaVersion":2,"codec":"gzip","snapshots":[]}""")]
    [InlineData("""{"schemaVersion":1,"codec":"gzip","snapshots":[{"storeCode":"S01","since":null,"generatedAt":"2026-07-30T01:02:03Z","expiresAt":"2026-07-30T03:02:03Z","catalogVersion":"v1","fileName":"snapshot.json.gz","sha256":null,"codec":"gzip"}]}""")]
    public void LoadAll_ValidJsonWithUnsupportedManifestContractFallsBackToEmpty(
        string manifestJson)
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(directory.Path, "manifest.json"),
            manifestJson);
        var store = new GzipCatalogSnapshotStore(directory.Path);

        var snapshots = store.LoadAll(GeneratedAt);

        Assert.Empty(snapshots);
    }

    private static void WriteSnapshotBody(
        string bodyPath,
        CatalogPersistedSnapshot snapshot)
    {
        using var output = new FileStream(
            bodyPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        using var gzip = new GZipStream(output, CompressionLevel.Fastest);
        JsonSerializer.Serialize(
            gzip,
            snapshot,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static void UpdateManifestSha256(string rootPath, string bodyPath)
    {
        string sha256;
        using (var input = File.OpenRead(bodyPath))
        {
            sha256 = Convert.ToHexString(SHA256.HashData(input));
        }

        var manifestPath = Path.Combine(rootPath, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var snapshots = manifest["snapshots"]!.AsArray();
        var entry = Assert.IsType<JsonObject>(Assert.Single(snapshots));
        entry["sha256"] = sha256;
        File.WriteAllText(
            manifestPath,
            manifest.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static CatalogPersistedSnapshot CreateSnapshot(string storeCode, string version)
    {
        return new CatalogPersistedSnapshot(
            storeCode,
            Since: null,
            GeneratedAt,
            GeneratedAt.AddHours(2),
            version,
            [new SellableItemDto(
                storeCode,
                "P01",
                null,
                "商品",
                "P01",
                null,
                null,
                12.34m,
                PriceSourceKind.StoreRetailPrice,
                "门店价",
                1m,
                GeneratedAt)]);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hbpos-catalog-snapshot-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
