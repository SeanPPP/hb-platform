using System.Threading.Channels;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Services.Logging
{
    public interface IApplicationLogQueue
    {
        bool TryEnqueue(ApplicationLogIngestItemDto item);
        ValueTask<ApplicationLogIngestItemDto> ReadAsync(CancellationToken cancellationToken);
        bool TryRead(out ApplicationLogIngestItemDto? item);
    }

    public class ApplicationLogQueue : IApplicationLogQueue
    {
        private readonly Channel<ApplicationLogIngestItemDto> _channel = Channel.CreateBounded<ApplicationLogIngestItemDto>(
            new BoundedChannelOptions(5000)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            }
        );

        public bool TryEnqueue(ApplicationLogIngestItemDto item)
        {
            return _channel.Writer.TryWrite(item);
        }

        public ValueTask<ApplicationLogIngestItemDto> ReadAsync(
            CancellationToken cancellationToken
        )
        {
            return _channel.Reader.ReadAsync(cancellationToken);
        }

        public bool TryRead(out ApplicationLogIngestItemDto? item)
        {
            return _channel.Reader.TryRead(out item);
        }
    }
}
