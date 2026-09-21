namespace MobreadModernDock.Infrastructure.Windows.Adapters;

using System.Diagnostics;
using MobreadModernDock.Core.Domain;

/// <summary>
/// Direct port of DefaultProgramLauncher.java. Launches executables,
/// with special handling for Discord (Squirrel-installed apps) and
/// automatic elevation (UAC) when the standard launch fails with error=740.
/// </summary>
public class WindowsProgramLauncher : IProgramLauncher
{
    public bool Launch(string executablePath, string label) => Launch(executablePath, label, null);

    /// <summary>
    /// Opens dropped files with this program: the file paths become the
    /// program's arguments. Uses ArgumentList so each path is quoted by the
    /// runtime — dropped names routinely contain spaces.
    /// </summary>
    public bool LaunchWithFiles(string executablePath, string label, IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0) return Launch(executablePath, label, null);
        if (string.IsNullOrWhiteSpace(executablePath)) return false;

        try
        {
            var launchCommand = ResolveLaunchCommand(executablePath);
            if (!File.Exists(launchCommand.ExecutablePath)) return false;

            var startInfo = new ProcessStartInfo
            {
                FileName = launchCommand.ExecutablePath,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(launchCommand.ExecutablePath) ?? ""
            };
            foreach (var arg in launchCommand.Arguments)
                startInfo.ArgumentList.Add(arg);
            foreach (var file in filePaths)
                startInfo.ArgumentList.Add(file);

            Process.Start(startInfo);
            Debug.WriteLine($"Opening {filePaths.Count} file(s) with: {label}");
            return true;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Failed to open files with {label}: {e.Message}");
            return false;
        }
    }

    public bool Launch(string executablePath, string label, string? arguments)
    {
        Debug.WriteLine($"{label} Clicked");

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            Debug.WriteLine($"Executable path not defined for: {label}");
            return false;
        }

        try
        {
            return ExecuteAndHandleElevation(executablePath, label, arguments);
        }
        catch (Exception e)
        {
            // A failed launch must never take the dock down: elevation
            // (error 740) is handled inside; any other failure (missing
            // file, bad path, invalid exe) is logged and reported as a
            // failed launch.
            Debug.WriteLine($"Failed to open: {label}");
            Debug.WriteLine($"Path: {executablePath}");
            Debug.WriteLine($"Error: {e.Message}");
            return false;
        }
    }

    private bool ExecuteAndHandleElevation(string path, string label, string? arguments)
    {
        var launchCommand = ResolveLaunchCommand(path);
        if (!File.Exists(launchCommand.ExecutablePath))
        {
            Debug.WriteLine($"Executable not found: {launchCommand.ExecutablePath}");
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = launchCommand.ExecutablePath,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(launchCommand.ExecutablePath) ?? ""
            };
            foreach (var arg in launchCommand.Arguments)
                startInfo.ArgumentList.Add(arg);
            // A raw shortcut argument string is passed through verbatim (the
            // target parses its own command line); ArgumentList and Arguments
            // are mutually exclusive, so only use Arguments when there are no
            // resolver-provided tokens (the Discord/Squirrel case).
            if (!string.IsNullOrWhiteSpace(arguments) && launchCommand.Arguments.Length == 0)
                startInfo.Arguments = arguments;

            Process.Start(startInfo);
            Debug.WriteLine($"Executing: {label}");
            return true;
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            // error=740 means elevation is required (ERROR_ELEVATION_REQUIRED)
            if (e.NativeErrorCode == 740)
            {
                Debug.WriteLine("Standard execution failed. Requesting elevation...");
                string command = BuildElevationCommand(launchCommand, arguments);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command {command}",
                    UseShellExecute = false
                });
                Debug.WriteLine($"(Elevated) Executing: {label}");
                return true;
            }

            Debug.WriteLine("Error trying to execute program");
            return false;
        }
    }

    /// <summary>
    /// Resolves the actual launch command for an executable, handling
    /// Discord's Squirrel installer pattern (app runs via Update.exe --processStart).
    /// </summary>
    public static LaunchCommand ResolveLaunchCommand(string executablePath)
    {
        string normalizedPath = Path.GetFullPath(executablePath);
        if (IsDiscordExecutable(normalizedPath))
        {
            string? versionDirectory = Path.GetDirectoryName(normalizedPath);
            string? installDirectory = versionDirectory != null
                ? Path.GetDirectoryName(versionDirectory) : null;

            if (installDirectory != null)
            {
                string updateExecutable = Path.Combine(installDirectory, "Update.exe");
                if (File.Exists(updateExecutable))
                {
                    return new LaunchCommand(
                        updateExecutable,
                        new[] { "--processStart", Path.GetFileName(normalizedPath) }
                    );
                }
            }
        }

        return new LaunchCommand(normalizedPath, Array.Empty<string>());
    }

    private static bool IsDiscordExecutable(string executablePath)
    {
        string? fileName = Path.GetFileName(executablePath);
        string? versionDirectory = Path.GetDirectoryName(executablePath);
        string? installDirectory = versionDirectory != null
            ? Path.GetDirectoryName(versionDirectory) : null;

        if (fileName == null || versionDirectory == null || installDirectory == null)
            return false;

        return string.Equals(fileName, "Discord.exe", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(versionDirectory)?.StartsWith("app-", StringComparison.OrdinalIgnoreCase) == true
            && string.Equals(Path.GetFileName(installDirectory), "Discord", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildElevationCommand(LaunchCommand launchCommand, string? rawArguments = null)
    {
        string escapedFilePath = EscapePowerShellArgument(launchCommand.ExecutablePath);
        if (launchCommand.Arguments.Length == 0)
        {
            if (string.IsNullOrWhiteSpace(rawArguments))
                return $"Start-Process -FilePath '{escapedFilePath}' -Verb RunAs";
            return $"Start-Process -FilePath '{escapedFilePath}' -ArgumentList '{EscapePowerShellArgument(rawArguments)}' -Verb RunAs";
        }

        string escapedArguments = string.Join(", ",
            launchCommand.Arguments.Select(a => $"'{EscapePowerShellArgument(a)}'"));

        return $"Start-Process -FilePath '{escapedFilePath}' -ArgumentList {escapedArguments} -Verb RunAs";
    }

    private static string EscapePowerShellArgument(string argument) => argument.Replace("'", "''");

    /// <summary>Direct port of the Java LaunchCommand record.</summary>
    public sealed record LaunchCommand(string ExecutablePath, string[] Arguments);
}
