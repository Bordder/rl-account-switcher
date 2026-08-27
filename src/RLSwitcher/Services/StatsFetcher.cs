using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace RLSwitcher.Services;

/// <summary>
/// Fetches Tracker Network ranks through a single hidden WebView2 (a real browser
/// context, so Cloudflare clears). Calls are serialized because one control can
/// only run one navigation at a time.
/// </summary>
public sealed class StatsFetcher
{
    private readonly WebView2 _web;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _init;

    public StatsFetcher(WebView2 web) => _web = web;

    public async Task<StatsResult> FetchAsync(string epicDisplayName)
    {
        if (string.IsNullOrWhiteSpace(epicDisplayName))
            return StatsResult.Fail("This account has no Epic display name to look up.");

        await _gate.WaitAsync();
        try
        {
            await EnsureAsync();

            var tcs = new TaskCompletionSource<string>();

            async void OnResponse(object? s, CoreWebView2WebResourceResponseReceivedEventArgs e)
            {
                try
                {
                    if (tcs.Task.IsCompleted) return;
                    if (!RlStats.IsProfileApi(e.Request.Uri)) return;

                    var stream = await e.Response.GetContentAsync();
                    if (stream is null) return;
                    using var reader = new StreamReader(stream);
                    var body = await reader.ReadToEndAsync();

                    if (string.IsNullOrWhiteSpace(body)) return;
                    if (!body.Contains("segments") && !body.Contains("errors")) return;
                    tcs.TrySetResult(body);
                }
                catch { /* keep waiting for a usable response */ }
            }

            _web.CoreWebView2.WebResourceResponseReceived += OnResponse;
            try
            {
                _web.CoreWebView2.Navigate(RlStats.ProfilePageUrl(epicDisplayName));
                var finished = await Task.WhenAny(tcs.Task, Task.Delay(25000));
                if (finished != tcs.Task)
                    return StatsResult.Fail("Timed out. The profile may be private, or the tracker is blocking requests.");
                return RlStats.Parse(tcs.Task.Result);
            }
            finally
            {
                _web.CoreWebView2.WebResourceResponseReceived -= OnResponse;
            }
        }
        catch (Exception ex)
        {
            return StatsResult.Fail("Could not load stats: " + ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureAsync()
    {
        if (_init) return;
        var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewDir);
        await _web.EnsureCoreWebView2Async(env);
        _init = true;
    }
}
