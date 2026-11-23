using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace WiFiBandSelector;

internal static class Interop
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static void ReleaseIcon(IntPtr hIcon)
    {
        if (hIcon != IntPtr.Zero)
            DestroyIcon(hIcon);
    }

    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Relance l'exécutable avec le verbe « runas ». Faux si l'utilisateur refuse l'UAC.</summary>
    public static bool TryRestartElevated()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                return false;

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
            });
            return true;
        }
        catch (Exception ex)
        {
            Logger.Write($"Relance élevée impossible : {ex.Message}");
            return false;
        }
    }

    /// <summary>Ouvre un dossier dans l'Explorateur, fichier sélectionné si fourni.</summary>
    public static bool ShowInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"",
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex)
        {
            Logger.Write($"Ouverture de l'Explorateur impossible : {ex.Message}");
            return false;
        }
    }

    public static bool ShellOpen(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Logger.Write($"Ouverture de {path} impossible : {ex.Message}");
            return false;
        }
    }

    /// <summary>Lance un programme sans fenêtre et attend sa fin. Retourne le code de sortie, ou -1.</summary>
    public static int RunHidden(string fileName, string arguments, int timeoutMs)
    {
        var (code, _) = RunCapture(fileName, arguments, timeoutMs);
        return code;
    }

    /// <summary>Lance un programme, capture sa sortie standard et attend sa fin.</summary>
    public static (int Code, string Output) RunCapture(string fileName, string arguments, int timeoutMs)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            });

            if (process is null)
                return (-1, string.Empty);

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignoré : on a déjà perdu la course.
                }

                Logger.Write($"{fileName} {arguments} : délai dépassé");
                return (-1, string.Empty);
            }

            return (process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
        }
        catch (Exception ex)
        {
            Logger.Write($"Exécution de {fileName} impossible : {ex.Message}");
            return (-1, string.Empty);
        }
    }
}
