namespace ColorfulLedKeyboard.Core;

public static class AppPaths
{
    public const string ServiceName = "ClevoRGBControlService";
    public const string DisplayName = "ClevoRGBControl Service";
    public const string ProgramDataFolderName = "ClevoRGBControl";
    public const string SettingsFileName = "settings.json";

    public static string ProgramDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProgramDataFolderName);

    public static string SettingsPath => Path.Combine(ProgramDataDirectory, SettingsFileName);
}
