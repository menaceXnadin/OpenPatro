using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenPatro.Models;

namespace OpenPatro.Services;

public sealed class ShareHubNepseClient
{
    private static readonly Uri HomePageDataUri = new("https://sharehubnepal.com/live/api/v2/nepselive/home-page-data", UriKind.Absolute);

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    public ShareHubNepseClient()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenPatro/1.0");
    }

    public async Task<StockMarketHomeData?> FetchHomePageDataAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(HomePageDataUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<StockMarketHomeData>(stream, cancellationToken: cancellationToken);
    }
}
