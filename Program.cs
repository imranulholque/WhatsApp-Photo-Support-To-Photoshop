using ImageMagick;
using Microsoft.Win32;

static class Program
{
    const string AppName = "WhatsAppPS";
    static string ConfigDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
    static string ConfigFile => Path.Combine(ConfigDir, "config.txt");

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(true, "Global_" + AppName, out bool first);
        if (!first) return;

        string? folder = File.Exists(ConfigFile) ? File.ReadAllText(ConfigFile).Trim() : null;
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            folder = AskFolder();
            if (folder == null) return;
        }
        Application.Run(new TrayContext(folder, AskFolderAndRestart));
    }

    static string? AskFolder()
    {
        using var f = new SetupForm();
        if (f.ShowDialog() != DialogResult.OK) return null;
        Directory.CreateDirectory(f.Result);
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigFile, f.Result);
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        key?.SetValue(AppName, "\"" + Environment.ProcessPath + "\"");
        MessageBox.Show($"Developed by Imranul Hoque Sabbir\n\nFolder ready:\n{f.Result}\n\nWhatsApp theke chobi ei folder e download korun. " +
            "Webp/heic/avif chobi auto Photoshop-friendly hoye ei folder-ei thakbe, original auto delete hobe. PC chalu holei eta auto cholbe.",
            AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return f.Result;
    }

    static void AskFolderAndRestart()
    {
        if (AskFolder() != null) Application.Restart();
    }
}

class SetupForm : Form
{
    readonly TextBox name = new() { Text = "WhatsApp-PS" };
    readonly TextBox loc = new() { Text = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), ReadOnly = true };
    public string Result = "";

    public SetupForm()
    {
        Text = "WhatsApp to Photoshop - folder setup";
        ClientSize = new Size(460, 170);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var credit = new Label
        {
            Text = "Developed by Imranul Hoque Sabbir",
            Left = 12, Top = 10, AutoSize = true,
            Font = new Font(Font.FontFamily, 11f, FontStyle.Bold)
        };
        var l1 = new Label { Text = "Folder-er naam:", Left = 12, Top = 55, AutoSize = true };
        name.SetBounds(130, 52, 310, 23);
        var l2 = new Label { Text = "Kothay banabe:", Left = 12, Top = 92, AutoSize = true };
        loc.SetBounds(130, 89, 230, 23);
        var browse = new Button { Text = "Browse...", Left = 366, Top = 88, Width = 74 };
        browse.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { SelectedPath = loc.Text };
            if (d.ShowDialog() == DialogResult.OK) loc.Text = d.SelectedPath;
        };
        var ok = new Button { Text = "Create", Left = 300, Top = 128, Width = 140 };
        ok.Click += (_, _) =>
        {
            var n = name.Text.Trim();
            if (n.Length == 0 || n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show("Folder-er naam thik nai.");
                return;
            }
            Result = Path.Combine(loc.Text, n);
            DialogResult = DialogResult.OK;
        };
        Controls.AddRange(new Control[] { credit, l1, name, l2, loc, browse, ok });
        AcceptButton = ok;
    }
}

class TrayContext : ApplicationContext
{
    readonly string watch;
    readonly NotifyIcon icon;
    readonly System.Threading.Timer timer;
    readonly HashSet<string> failed = new();
    int busy;

    public TrayContext(string folder, Action change)
    {
        watch = folder;
        Directory.CreateDirectory(watch);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Developed by Imranul Hoque Sabbir").Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open folder", null, (_, _) => System.Diagnostics.Process.Start("explorer.exe", watch));
        menu.Items.Add("Change folder", null, (_, _) => change());
        menu.Items.Add("Exit", null, (_, _) => { icon!.Visible = false; ExitThread(); });
        icon = new NotifyIcon { Icon = SystemIcons.Application, Text = "WhatsApp to Photoshop", Visible = true, ContextMenuStrip = menu };

        icon.ShowBalloonTip(4000, "WhatsApp to Photoshop", "Developed by Imranul Hoque Sabbir\nAuto convert cholche.", ToolTipIcon.Info);
        timer = new System.Threading.Timer(_ => Scan(), null, 1000, 2000);
    }

    void Scan()
    {
        if (Interlocked.Exchange(ref busy, 1) == 1) return;
        try
        {
            foreach (var f in Directory.GetFiles(watch))
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext is not (".webp" or ".heic" or ".avif")) continue;
                if (failed.Contains(f)) continue;
                try { Convert(f, ext); }
                catch (IOException) { /* file still downloading, retry next tick */ }
                catch { failed.Add(f); }
            }
        }
        catch { }
        finally { Interlocked.Exchange(ref busy, 0); }
    }

    void Convert(string file, string ext)
    {
        using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        var name = Path.GetFileNameWithoutExtension(file);
        var isPng = ext == ".webp";
        var dest = Unique(watch, name, isPng ? ".png" : ".jpg");
        var tmp = dest + ".tmp";
        using (var img = new MagickImage(file))
        {
            img.AutoOrient();
            img.Write(tmp, isPng ? MagickFormat.Png : MagickFormat.Jpeg);
        }
        if (!File.Exists(tmp) || new FileInfo(tmp).Length == 0) throw new Exception("convert failed");
        File.Move(tmp, dest);
        File.Delete(file);
    }

    static string Unique(string dir, string name, string ext)
    {
        var p = Path.Combine(dir, name + ext);
        for (int i = 1; File.Exists(p); i++) p = Path.Combine(dir, $"{name}_{i}{ext}");
        return p;
    }
}
