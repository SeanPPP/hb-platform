using Hbpos.Api.Services;

namespace Hbpos.Api.Tests;

public sealed class SquareTokenServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetActiveTokenAsync_ReturnsNullWhenRepositoryTokenHasBlankAccessToken(string? accessToken)
    {
        var repository = new RecordingSquareTokenRepository(new SquareTokenRecord
        {
            Environment = "Production",
            AccessToken = accessToken,
            IsEnabled = true,
            UpdatedAt = new DateTime(2026, 5, 26, 4, 0, 0, DateTimeKind.Utc)
        });
        var service = new SquareTokenService(repository);

        var response = await service.GetActiveTokenAsync("production", CancellationToken.None);

        Assert.Null(response);
        Assert.Equal("Production", repository.LastEnvironment);
    }

    private sealed class RecordingSquareTokenRepository(SquareTokenRecord? response) : ISquareTokenRepository
    {
        public string? LastEnvironment { get; private set; }

        public Task<SquareTokenRecord?> GetActiveTokenAsync(
            string environment,
            CancellationToken cancellationToken)
        {
            LastEnvironment = environment;
            return Task.FromResult(response);
        }
    }
}
