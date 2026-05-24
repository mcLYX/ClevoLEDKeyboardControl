namespace ColorfulLedKeyboard.Tray;

using ColorfulLedKeyboard.Core;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext(new SettingsStore()));
    }    
}
