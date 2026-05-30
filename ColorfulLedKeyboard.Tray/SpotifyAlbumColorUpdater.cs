using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tray;

internal sealed class SpotifyAlbumColorUpdater : IDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly SpotifyOAuthClient _client = new();
    private readonly WindowsMediaAlbumColorReader _mediaReader = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30000 };
    private bool _updating;
    private string _lastKey = "";

    public SpotifyAlbumColorUpdater(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        _timer.Tick += async (_, _) => await UpdateAsync();
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            _timer.Start();
            _ = UpdateAsync();
            return;
        }

        _timer.Stop();
        _lastKey = "";
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }

    private async Task UpdateAsync()
    {
        if (_updating)
        {
            return;
        }

        _updating = true;
        try
        {
            var settings = _settingsStore.Load();
            var spotify = settings.Effect.Music.Spotify.Normalize();
            if (!spotify.AlbumColorEnabled)
            {
                return;
            }

            var color = await TryGetColorAsync(spotify);
            if (color is null)
            {
                return;
            }

            var current = SpotifyAlbumColorState.Load();
            var stateIsFresh = current is not null &&
                DateTimeOffset.UtcNow - current.UpdatedUtc < TimeSpan.FromMinutes(2);
            var sameState = current is not null &&
                string.Equals(current.TrackId, color.Key, StringComparison.Ordinal) &&
                string.Equals(current.Color, color.Color, StringComparison.OrdinalIgnoreCase);
            if (sameState && stateIsFresh && string.Equals(color.Key, _lastKey, StringComparison.Ordinal))
            {
                return;
            }

            SpotifyAlbumColorState.Save(color.Color, color.Key);
            _lastKey = color.Key;
        }
        catch
        {
        }
        finally
        {
            _updating = false;
        }
    }

    private async Task<MediaAlbumColor?> TryGetColorAsync(SpotifySettings settings)
    {
        if (settings.AlbumColorSource is AlbumColorSource.WindowsMediaSession or AlbumColorSource.Automatic)
        {
            var mediaColor = await _mediaReader.TryReadAsync();
            if (mediaColor is not null)
            {
                return mediaColor;
            }
        }

        if (settings.AlbumColorSource is AlbumColorSource.SpotifyOAuth or AlbumColorSource.Automatic)
        {
            var track = await _client.GetCurrentTrackAsync(settings);
            if (track is null)
            {
                return null;
            }

            var color = await AlbumColorExtractor.ExtractAsync(track.ImageUrl);
            return new MediaAlbumColor($"Spotify|{track.TrackId}", color);
        }

        return null;
    }
}
