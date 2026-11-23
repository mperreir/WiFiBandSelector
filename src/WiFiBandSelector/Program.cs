using System.Text;

namespace WiFiBandSelector;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Mode support : écrit un rapport texte et sort, sans interface.
        if (args.Length >= 1 && string.Equals(args[0], "/report", StringComparison.OrdinalIgnoreCase))
        {
            WriteReport(args.Length >= 2 ? args[1] : AppPaths.NewReportFile());
            return;
        }

        // Mode support discret : applique un réglage sans ouvrir le menu.
        if (args.Length >= 3 && string.Equals(args[0], "/set", StringComparison.OrdinalIgnoreCase))
        {
            Environment.ExitCode = ApplyFromCommandLine(
                args[1], args[2], args.Length >= 4 ? args[3] : null);
            return;
        }

        using var mutex = new Mutex(initiallyOwned: true, @"Local\WiFiBandSelector", out var isFirstInstance);
        if (!isFirstInstance)
            return;

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            Application.Run(new TrayContext());
        }
        catch (Exception ex)
        {
            ReportCrash(ex);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static void WriteReport(string path)
    {
        try
        {
            AppPaths.Ensure();
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, Diagnostics.BuildReport(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(path, ex.ToString());
            }
            catch
            {
                // Rien de plus à faire : le code de sortie suffit.
            }

            Environment.ExitCode = 1;
        }
    }

    private static int ApplyFromCommandLine(string familyArg, string code, string? outputPath)
    {
        var text = string.Empty;
        try
        {
            var family = familyArg.ToLowerInvariant() switch
            {
                "preference" or "préférence" => SettingFamily.Preference,
                "lock" or "verrou" => SettingFamily.Lock,
                "roaming" or "itinérance" => SettingFamily.Roaming,
                _ => throw new ArgumentException($"Famille inconnue : {familyArg}"),
            };

            var settings = AppSettings.Load();
            var adapters = AdapterDiscovery.Discover();
            var snapshot = adapters.FirstOrDefault(s =>
                                string.Equals(s.KeyPath, settings.AdapterKeyPath, StringComparison.OrdinalIgnoreCase))
                            ?? adapters.FirstOrDefault()
                            ?? throw new InvalidOperationException("Aucune carte Wi-Fi physique trouvée.");

            var controller = new BandController(
                snapshot, SettingClassifier.Classify(snapshot.Params), settings);
            var outcome = controller.Apply(family, code);

            text = $"{outcome.Status} : {outcome.Message}";
            Logger.Write($"/set {familyArg} {code} → {text}");
            if (!string.IsNullOrEmpty(outputPath))
                File.WriteAllText(outputPath, text, new UTF8Encoding(false));
            else
                Console.WriteLine(text);

            return outcome.Ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            text = $"Erreur : {ex.Message}";
            Logger.Write($"/set {familyArg} {code} → {text}");
            if (!string.IsNullOrEmpty(outputPath))
                File.WriteAllText(outputPath, text, new UTF8Encoding(false));
            else
                Console.WriteLine(text);
            return 2;
        }
    }

    private static void ReportCrash(Exception? exception)
    {
        if (exception is null)
            return;

        Logger.Write($"Exception non gérée : {exception}");

        try
        {
            MessageBox.Show(
                "WiFiBandSelector a rencontré une erreur.\n\n" +
                exception.Message +
                $"\n\nJournal : {AppPaths.LogFile}",
                "WiFiBandSelector",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // Une erreur dans le rapport d'erreur ne doit pas boucler.
        }
    }
}
