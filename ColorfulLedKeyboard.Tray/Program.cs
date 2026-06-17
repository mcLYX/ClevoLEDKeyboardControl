namespace ColorfulLedKeyboard.Tray;

using ColorfulLedKeyboard.Core;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var openSettingsOnStartup = args.Any(arg =>
            string.Equals(arg, "--settings", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "/settings", StringComparison.OrdinalIgnoreCase));

        Application.Run(new TrayApplicationContext(new SettingsStore(), openSettingsOnStartup));
    }    
}
