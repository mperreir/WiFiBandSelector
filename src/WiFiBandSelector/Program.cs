using System.Text;
using WiFiBandSelector.Resources;

namespace WiFiBandSelector;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Support mode: write a text report and exit, without any UI.
        if (args.Length >= 1 && string.Equals(args[0], "/report", StringComparison.OrdinalIgnoreCase))
        {
            WriteReport(args.Length >= 2 ? args[1] : AppPaths.NewReportFile());
            return;
        }

        // Silent support mode: apply a setting without opening the menu.
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
                // Nothing else to do: the exit code is enough.
            }

            Environment.ExitCode = 1;
        }
    }

    private static int ApplyFromCommandLine(string familyArg, string code, string? outputPath)
    {
        var text = string.Empty;
        try
        {
            // French aliases are kept for compatibility with v0.1 support scripts.
            var family = familyArg.ToLowerInvariant() switch
            {
                "preference" or "préférence" => SettingFamily.Preference,
                "lock" or "verrou" => SettingFamily.Lock,
                "roaming" or "itinérance" => SettingFamily.Roaming,
                _ => throw new ArgumentException(Strings.CliUnknownFamily.FormatWith(familyArg)),
            };

            var settings = AppSettings.Load();
            var adapters = AdapterDiscovery.Discover();
            var snapshot = adapters.FirstOrDefault(s =>
                                string.Equals(s.KeyPath, settings.AdapterKeyPath, StringComparison.OrdinalIgnoreCase))
                            ?? adapters.FirstOrDefault()
                            ?? throw new InvalidOperationException(Strings.CliNoAdapter);

            var controller = new BandController(
                snapshot, SettingClassifier.Classify(snapshot.Params), settings);
            var outcome = controller.Apply(family, code);

            text = Strings.CliResult.FormatWith(outcome.Status, outcome.Message);
            Logger.Write(Strings.LogSetCommand.FormatWith(familyArg, code, text));
            if (!string.IsNullOrEmpty(outputPath))
                File.WriteAllText(outputPath, text, new UTF8Encoding(false));
            else
                Console.WriteLine(text);

            return outcome.Ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            text = Strings.ErrorPrefix.FormatWith(ex.Message);
            Logger.Write(Strings.LogSetCommand.FormatWith(familyArg, code, text));
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

        Logger.Write(Strings.LogUnhandled.FormatWith(exception));

        try
        {
            MessageBox.Show(
                Strings.CrashMessage.FormatWith(exception.Message, AppPaths.LogFile),
                Strings.AppTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // An error while reporting an error must not loop.
        }
    }
}
