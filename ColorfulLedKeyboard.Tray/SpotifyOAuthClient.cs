using ColorfulLedKeyboard.Core;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ColorfulLedKeyboard.Tray;

internal sealed class SpotifyOAuthClient
{
    public const string RedirectUri = "http://127.0.0.1:43875/callback/";
    private const string Scope = "user-read-currently-playing";

    public async Task<string> AuthorizeAsync(string clientId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("请先填写 Spotify Client ID。");
        }

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(18));
        using var listener = new HttpListener();
        listener.Prefixes.Add(RedirectUri);
        listener.Start();

        var url = "https://accounts.spotify.com/authorize" +
            $"?client_id={Uri.EscapeDataString(clientId.Trim())}" +
            "&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
            $"&scope={Uri.EscapeDataString(Scope)}" +
            $"&state={Uri.EscapeDataString(state)}" +
            "&code_challenge_method=S256" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}";

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        using var registration = cancellationToken.Register(() => listener.Stop());
        var context = await listener.GetContextAsync();
        var request = context.Request;
        var code = request.QueryString["code"];
        var returnedState = request.QueryString["state"];
        var responseBytes = Encoding.UTF8.GetBytes("<html><body>Spotify 授权完成，可以关闭此页面。</body></html>");
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.OutputStream.Write(responseBytes, 0, responseBytes.Length);
        context.Response.Close();

        if (string.IsNullOrWhiteSpace(code) || returnedState != state)
        {
            throw new InvalidOperationException("Spotify 授权失败或状态校验失败。");
        }

        using var http = new HttpClient();
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId.Trim(),
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = verifier
        });
        using var tokenResponse = await http.PostAsync("https://accounts.spotify.com/api/token", body, cancellationToken);
        tokenResponse.EnsureSuccessStatusCode();
        using var stream = await tokenResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return json.RootElement.GetProperty("refresh_token").GetString() ?? "";
    }

    public async Task<SpotifyTrackInfo?> GetCurrentTrackAsync(SpotifySettings settings, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.RefreshToken))
        {
            return null;
        }

        var accessToken = await RefreshAccessTokenAsync(settings.ClientId, settings.RefreshToken, cancellationToken);
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me/player/currently-playing?additional_types=track");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("item", out var item) || item.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var trackId = item.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
        if (!item.TryGetProperty("album", out var album) ||
            !album.TryGetProperty("images", out var images) ||
            images.GetArrayLength() == 0)
        {
            return null;
        }

        var imageUrl = images.EnumerateArray()
            .OrderBy(image => image.TryGetProperty("width", out var width) ? Math.Abs(width.GetInt32() - 300) : 1000)
            .First()
            .GetProperty("url")
            .GetString();

        return string.IsNullOrWhiteSpace(imageUrl) ? null : new SpotifyTrackInfo(trackId, imageUrl);
    }

    private static async Task<string> RefreshAccessTokenAsync(string clientId, string refreshToken, CancellationToken cancellationToken)
    {
        using var http = new HttpClient();
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId.Trim(),
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken.Trim()
        });
        using var tokenResponse = await http.PostAsync("https://accounts.spotify.com/api/token", body, cancellationToken);
        tokenResponse.EnsureSuccessStatusCode();
        using var stream = await tokenResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return json.RootElement.GetProperty("access_token").GetString() ?? "";
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

internal sealed record SpotifyTrackInfo(string TrackId, string ImageUrl);
