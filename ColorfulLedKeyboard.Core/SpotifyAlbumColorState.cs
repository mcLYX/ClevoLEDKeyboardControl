using System.Text.Json;

namespace ColorfulLedKeyboard.Core;

public sealed class SpotifyAlbumColorState
{
    public string Color { get; set; } = "#FFFFFF";

    public string TrackId { get; set; } = "";

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public SpotifyAlbumColorState Normalize()
    {
        Color = LightingEffectSettings.NormalizeHex(Color, "#FFFFFF");
        TrackId = TrackId?.Trim() ?? "";
        return this;
    }

    public static SpotifyAlbumColorState? Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SpotifyAlbumColorStatePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<SpotifyAlbumColorState>(File.ReadAllText(AppPaths.SpotifyAlbumColorStatePath))?.Normalize();
        }
        catch
        {
            return null;
        }
    }

    public static void Save(string color, string trackId)
    {
        Directory.CreateDirectory(AppPaths.ProgramDataDirectory);
        var state = new SpotifyAlbumColorState
        {
            Color = color,
            TrackId = trackId,
            UpdatedUtc = DateTimeOffset.UtcNow
        }.Normalize();
        File.WriteAllText(AppPaths.SpotifyAlbumColorStatePath, JsonSerializer.Serialize(state));
    }
}
