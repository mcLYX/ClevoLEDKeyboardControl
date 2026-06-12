using System.Diagnostics;

namespace ColorfulLedKeyboard.Tray;

public sealed class AboutForm : Form
{
    private const string RepositoryUrl = "https://github.com/silent-ram/ClevoLEDKeyboardControl";
    public AboutForm()
    {
        Text = "关于 ClevoLEDKeyboardControl";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(480, 230);

        BuildUi();
    }

    private void BuildUi()
    {
        var title = new Label
        {
            Text = "ClevoLEDKeyboardControl",
            Font = new Font(SystemFonts.MessageBoxFont ?? Control.DefaultFont, FontStyle.Bold),
            Location = new Point(24, 22),
            Size = new Size(360, 28)
        };

        var description = new Label
        {
            Text = "Clevo 系列键盘 RGB 控制工具，支持固定颜色、RGB 循环、单色呼吸、循环呼吸和音乐灯效。",
            Location = new Point(24, 56),
            Size = new Size(420, 48)
        };

        var timing = new Label
        {
            Text = "RGB 循环使用停留时长控制；呼吸模式使用呼吸周期控制。",
            Location = new Point(24, 104),
            Size = new Size(420, 24)
        };

        var github = new LinkLabel
        {
            Text = "GitHub 仓库",
            Location = new Point(24, 145),
            Size = new Size(360, 24)
        };
        github.LinkClicked += (_, _) => OpenUrl(RepositoryUrl);

        var close = new Button
        {
            Text = "关闭",
            DialogResult = DialogResult.OK,
            Location = new Point(368, 184),
            Size = new Size(88, 30)
        };

        Controls.Add(title);
        Controls.Add(description);
        Controls.Add(timing);
        Controls.Add(github);
        Controls.Add(close);

        AcceptButton = close;
        CancelButton = close;
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
