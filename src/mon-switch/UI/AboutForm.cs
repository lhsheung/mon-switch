using MonSwitch.Core;
using MonSwitch.Services;

namespace MonSwitch.UI;

internal sealed class AboutForm : Form
{
    private readonly Label _title = new();
    private readonly Label _version = new();
    private readonly Label _description = new();
    private readonly Label _modes = new();
    private readonly Label _environment = new();
    private readonly Label _license = new();
    private readonly Button _close = new();

    public AboutForm()
    {
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        ClientSize = new Size(540, 300);

        Font titleFont = new(Font.FontFamily, 15f, FontStyle.Bold);

        _title.Font = titleFont;
        _title.AutoSize = true;
        _title.Location = new Point(22, 18);

        _version.AutoSize = true;
        _version.Location = new Point(24, 56);

        _description.AutoSize = false;
        _description.Size = new Size(496, 62);
        _description.Location = new Point(24, 86);

        _modes.AutoSize = false;
        _modes.Size = new Size(496, 44);
        _modes.Location = new Point(24, 150);

        _environment.AutoSize = false;
        _environment.Size = new Size(496, 22);
        _environment.ForeColor = SystemColors.GrayText;
        _environment.Location = new Point(24, 200);

        _license.AutoSize = false;
        _license.Size = new Size(496, 22);
        _license.ForeColor = SystemColors.GrayText;
        _license.Location = new Point(24, 224);

        _close.Size = new Size(104, 30);
        _close.Location = new Point(ClientSize.Width - 126, ClientSize.Height - 44);
        _close.DialogResult = DialogResult.OK;

        Controls.AddRange([_title, _version, _description, _modes, _environment, _license, _close]);

        AcceptButton = _close;
        CancelButton = _close;
        _close.Click += (_, _) => Close();

        ApplyLocalization();

        // The title font is the only GDI object this dialog owns; hand it to the control
        // so it is released together with the form.
        FormClosed += (_, _) => titleFont.Dispose();
    }

    private void ApplyLocalization()
    {
        Text = Loc.T("about.title");
        _title.Text = AppInfo.Name;
        _version.Text = Loc.T("about.version", AppInfo.VersionText);
        _description.Text = Loc.T("about.description");
        _modes.Text = Loc.T("about.modesLine");
        _environment.Text = Loc.T("about.environmentLine", AppInfo.FrameworkDescription, AppInfo.OperatingSystemDescription);
        _license.Text = Loc.T("about.licenseLine");
        _close.Text = Loc.T("about.close");
    }
}
