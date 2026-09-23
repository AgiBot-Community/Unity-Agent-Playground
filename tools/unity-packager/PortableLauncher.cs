using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class PortableLauncher
{
    static string Hash(string path)
    {
        using (var sha = SHA256.Create())
        using (var file = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
    }

    static void Verify(string directory)
    {
        using (var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("manifest")))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                int tab = line.IndexOf('\t');
                if (tab < 0) throw new IOException("Invalid package manifest");
                string relative = line.Substring(tab + 1).Replace('/', Path.DirectorySeparatorChar);
                string path = Path.GetFullPath(Path.Combine(directory, relative));
                if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || !File.Exists(path) || Hash(path) != line.Substring(0, tab))
                    throw new IOException("Package verification failed: " + relative);
            }
        }
    }

    static string Prepare()
    {
        string parent = Environment.GetEnvironmentVariable("UNITY_PORTABLE_CACHE");
        if (String.IsNullOrEmpty(parent))
            parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                  "UnityPortable");
        parent = Path.GetFullPath(parent);
        Directory.CreateDirectory(parent);
        string root = Path.Combine(parent, BuildInfo.Version.Substring(0, 16));
        using (var mutex = new Mutex(false, "Local\\UnityPortable_" + BuildInfo.Version))
        {
            bool locked = false;
            try
            {
                try { locked = mutex.WaitOne(TimeSpan.FromMinutes(10)); }
                catch (AbandonedMutexException) { locked = true; }
                if (!locked) throw new IOException("Timed out waiting for another extraction");
                if (File.Exists(Path.Combine(root, ".complete"))
                    && File.ReadAllText(Path.Combine(root, ".complete")) == BuildInfo.Version
                    && File.Exists(Path.Combine(root, BuildInfo.Player))) return root;
                string staging = Path.Combine(parent, "s-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                try
                {
                    using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload"))
                    using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                    {
                        foreach (var entry in zip.Entries)
                        {
                            string path = Path.GetFullPath(Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                            if (!path.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                                throw new IOException("Unsafe archive entry: " + entry.FullName);
                            if (entry.Name.Length == 0) continue;
                            Directory.CreateDirectory(Path.GetDirectoryName(path));
                            using (var input = entry.Open())
                            using (var output = File.Create(path)) input.CopyTo(output);
                        }
                    }
                    Verify(staging);
                    File.WriteAllText(Path.Combine(staging, ".complete"), BuildInfo.Version);
                    if (Directory.Exists(root))
                        Directory.Move(root, Path.Combine(parent, "old-" + Guid.NewGuid().ToString("N")));
                    Directory.Move(staging, root);
                }
                finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
                return root;
            }
            finally { if (locked) mutex.ReleaseMutex(); }
        }
    }

    static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            result.Append(c);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        try
        {
            string root = Prepare();
            if (Array.IndexOf(args, "--portable-extract-only") >= 0) return;
            var forwarded = new StringBuilder();
            foreach (string arg in args) forwarded.Append(Quote(arg)).Append(' ');
            var start = new ProcessStartInfo(Path.Combine(root, BuildInfo.Player), forwarded.ToString());
            start.WorkingDirectory = root;
            start.UseShellExecute = false;
            using (var process = Process.Start(start))
            {
                for (int attempt = 0; attempt < 100; attempt++)
                {
                    process.Refresh();
                    if (process.HasExited) break;
                    if (process.MainWindowHandle != IntPtr.Zero) break;
                    Thread.Sleep(200);
                }
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, BuildInfo.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
    }
}
