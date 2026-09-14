namespace SmartX.Api;

internal static class HealthProbe
{
    // the runtime image ships without curl, so the app answers its own health check and turns the answer into an exit code
    public static async Task<int> RunAsync()
    {
        var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:8080";
        var first = urls.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "http://localhost:8080";
        var target = first.Replace("+", "localhost").Replace("*", "localhost").TrimEnd('/');

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        try
        {
            using var response = await client.GetAsync($"{target}/api/health").ConfigureAwait(false);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Health probe failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
    }
}
