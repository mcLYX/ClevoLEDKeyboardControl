namespace ColorfulLedKeyboard.Core;

public sealed class SpotifySettings
{
    public bool AlbumColorEnabled { get; set; }

    public AlbumColorSource AlbumColorSource { get; set; } = AlbumColorSource.WindowsMediaSession;

    public string ClientId { get; set; } = "";

    public string RefreshToken { get; set; } = "";

    public string LastAlbumColor { get; set; } = "#FFFFFF";

    public SpotifySettings Normalize()
    {
        if (!Enum.IsDefined(AlbumColorSource))
        {
            AlbumColorSource = AlbumColorSource.WindowsMediaSession;
        }

        ClientId = ClientId?.Trim() ?? "";
        RefreshToken = RefreshToken?.Trim() ?? "";
        LastAlbumColor = LightingEffectSettings.NormalizeHex(LastAlbumColor, "#FFFFFF");
        return this;
    }
}

public enum AlbumColorSource
{
    WindowsMediaSession = 0,
    SpotifyOAuth = 1,
    Automatic = 2
}
