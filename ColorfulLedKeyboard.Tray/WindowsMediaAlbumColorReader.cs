using System.IO;
using Windows.Media.Control;

namespace ColorfulLedKeyboard.Tray;

internal sealed class WindowsMediaAlbumColorReader
{
    public async Task<MediaAlbumColor?> TryReadAsync(CancellationToken cancellationToken = default)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var session = manager.GetCurrentSession();
        if (session is null)
        {
            return null;
        }

        var info = await session.TryGetMediaPropertiesAsync();
        var thumbnail = info.Thumbnail;
        if (thumbnail is null)
        {
            return null;
        }

        using var randomStream = await thumbnail.OpenReadAsync();
        using var stream = randomStream.AsStreamForRead();
        var color = AlbumColorExtractor.Extract(stream);
        var key = $"{session.SourceAppUserModelId}|{info.Title}|{info.Artist}";
        return new MediaAlbumColor(key, color);
    }
}

internal sealed record MediaAlbumColor(string Key, string Color);
