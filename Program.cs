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
        Directory.CreateDirectory(Path.Combine(f.Result, "PS-Ready"));
        Directory.CreateDirectory(Path.Combine(f.Result, "Originals"));
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigFile, f.Result);
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        key?.SetValue(AppName, "\"" + Environment.ProcessPath + "\"");
        MessageBox.Show($"Folder ready:\n{f.Result}\n\nWhatsApp theke chobi ei folder e download korun. " +
            "Converted chobi PS-Ready folder e pabein. PC chalu holei eta auto cholbe.",
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
        ClientSize = new Size(460, 100);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var l1 = new Label { Text = "Folder-er naam:", Left = 12, Top = 15, AutoSize = true };
        name.SetBounds(130, 12, 310, 23);
        var l2 = new Label { Text = "Kothay banabe:", Left = 12, Top = 52, AutoSize = true };
        loc.SetBounds(130, 49, 230, 23);
        var browse = new Button { Text = "Browse...", Left = 366, Top = 48, Width = 74 };
        browse.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { SelectedPath = loc.Text };
            if (d.ShowDialog() == DialogResult.OK) loc.Text = d.SelectedPath;
        };
        var ok = new Button { Text = "Create", Left = 300, Top = 150, Width = 140 };
        ClientSize = new Size(460, 190);
        ok.Top = 90;
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
        Controls.AddRange(new Control[] { l1, name, l2, loc, browse, ok });
        AcceptButton = ok;
        ClientSize = new Size(460, 130);
    }
}

class TrayContext : ApplicationContext
{
    readonly string watch, outDir, origDir;
    readonly NotifyIcon icon;
    readonly System.Threading.Timer timer;
    readonly HashSet<string> failed = new();
    int busy;

    public TrayContext(string folder, Action change)
    {
        watch = folder;
        outDir = Path.Combine(folder, "PS-Ready");
        origDir = Path.Combine(folder, "Originals");
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(origDir);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open folder", null, (_, _) => System.Diagnostics.Process.Start("explorer.exe", watch));
        menu.Items.Add("Open PS-Ready", null, (_, _) => System.Diagnostics.Process.Start("explorer.exe", outDir));
        menu.Items.Add("Change folder", null, (_, _) => change());
        menu.Items.Add("Exit", null, (_, _) => { icon!.Visible = false; ExitThread(); });
        icon = new NotifyIcon { Icon = SystemIcons.Application, Text = "WhatsApp to Photoshop", Visible = true, ContextMenuStrip = menu };

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
                if (ext is not (".webp" or ".jpg" or ".jpeg" or ".png" or ".heic" or ".avif")) continue;
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
        var outExt = ext is ".webp" or ".png" ? ".png" : ".jpg";
        var dest = Unique(outDir, name, outExt);
        using (var img = new MagickImage(file))
        {
            img.AutoOrient();
            img.Write(dest);
        }
        File.Move(file, Unique(origDir, name, ext));
    }

    static string Unique(string dir, string name, string ext)
    {
        var p = Path.Combine(dir, name + ext);
        for (int i = 1; File.Exists(p); i++) p = Path.Combine(dir, $"{name}_{i}{ext}");
        return p;
    }
}
