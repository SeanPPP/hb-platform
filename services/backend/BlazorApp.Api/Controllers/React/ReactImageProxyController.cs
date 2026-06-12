using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/image-proxy")]
    [AllowAnonymous]
    public class ReactImageProxyController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public ReactImageProxyController(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return BadRequest("url is required");

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return BadRequest("invalid url");

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return BadRequest("unsupported scheme");

            // 放宽：允许任意 http/https 域名

            var client = _httpClientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36"
            );
            req.Headers.Referrer = new Uri($"{uri.Scheme}://{uri.Host}/");
            req.Headers.TryAddWithoutValidation(
                "Accept",
                "image/avif,image/webp,image/apng,image/*,*/*;q=0.8"
            );

            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode)
                return StatusCode((int)resp.StatusCode);

            var contentType =
                resp.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return NotFound("not image");

            var bytes = await resp.Content.ReadAsByteArrayAsync();
            return File(bytes, contentType);
        }
    }
}
