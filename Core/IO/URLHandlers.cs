using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Diagnostics.CodeAnalysis;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif

using log4net;

namespace CKAN.IO
{
    [ExcludeFromCodeCoverage]
    public static class URLHandlers
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(URLHandlers));

        private static readonly string ApplicationsPath = ".local/share/applications/";
        private const           string LinuxHandlerFilename  = "ckan-handler.desktop";

        // WindowsHandlerResName must match the LogicalName in CKAN-cmdline.csproj.
        private const string WindowsHandlerResName = "CKAN.CmdLine.ckan-urlhandler.exe";
        private const string WindowsHandlerExeName = "ckan-urlhandler.exe";

        static URLHandlers()
        {
            if (Platform.IsUnix)
            {
                var XDGDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                if (XDGDataHome != null)
                {
                    ApplicationsPath = Path.Combine(XDGDataHome, "applications");
                }
                else
                {
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    ApplicationsPath = Path.Combine(home, ApplicationsPath);
                }
                Directory.CreateDirectory(ApplicationsPath);
            }
        }

        public static void RegisterURLHandler()
        {
            try
            {
                if (Platform.IsUnix)
                {
                    RegisterURLHandler_Linux();
                }
                else if (Platform.IsWindows)
                {
                    RegisterURLHandler_Win32();
                }

                // macOS URL handler is defined in CKAN.app info.plist.
            }
            catch (Exception ex)
            {
                log.ErrorFormat(
                    "There was an error while registering the URL handler for ckan:// - {0}",
                    ex.Message
                );
                log.ErrorFormat("{0}", ex.StackTrace);
            }
        }

        private static string PathToRunningExe()
            #if NET5_0_OR_GREATER
            => Environment.ProcessPath ?? "";
            #else
            => Assembly.GetEntryAssembly()?.Location ?? "";
            #endif

        #if NET5_0_OR_GREATER
        [SupportedOSPlatform("windows")]
        #endif
        private static void RegisterURLHandler_Win32()
        {
            log.InfoFormat("Adding URL handler to registry");

            var stub = ExtractURLHandlerStub();
            if (stub == null)
            {
                return;
            }

            var urlCmd = $"\"{stub}\" \"{PathToRunningExe()}\" \"%1\"";

            // Register per user so no admin rights are needed.
            // Windows automatically gives this precedence over the old handler we used to register for all users.
            using var classes = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes");
            var existing = classes.OpenSubKey(@"ckan\shell\open\command")?.GetValue("")?.ToString();
            if (existing == urlCmd)
            {
                log.InfoFormat("URL handler already registered with the same command");
                return;
            }

            using var ckanKey = classes.CreateSubKey("ckan");
            ckanKey.SetValue("", "URL: ckan Protocol");
            ckanKey.SetValue("URL Protocol", "");

            using var commandKey = ckanKey.CreateSubKey(@"shell\open\command");
            commandKey.SetValue("", urlCmd);
        }

        // Extract the embedded URL handler to %LOCALAPPDATA%\CKAN and return its path.
        // Returns null if the stub is not embedded.
        private static string? ExtractURLHandlerStub()
        {
            using var resource = Assembly.GetEntryAssembly()?.GetManifestResourceStream(WindowsHandlerResName);
            if (resource == null)
            {
                log.InfoFormat("URL handler stub '{0}' not embedded. Skipping URL handler registration",
                               WindowsHandlerResName);
                return null;
            }

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CKAN");
            Directory.CreateDirectory(dir);
            var stubPath = Path.Combine(dir, WindowsHandlerExeName);

            // Read all bytes of embedded url handler.
            using var ms = new MemoryStream();
            resource.CopyTo(ms);
            var wanted = ms.ToArray();

            // Write when non-existent or the bytes differ.
            if (!File.Exists(stubPath) || !File.ReadAllBytes(stubPath).SequenceEqual(wanted))
            {
                File.WriteAllBytes(stubPath, wanted);
            }

            return stubPath;
        }

        #if NET5_0_OR_GREATER
        [SupportedOSPlatform("linux")]
        #endif
        private static void RegisterURLHandler_Linux()
        {
            log.InfoFormat("Trying to register URL handler");

            var handlerPath = Path.Combine(ApplicationsPath, LinuxHandlerFilename);
            var desiredExec = "mono \"" + PathToRunningExe() + "\" gui %u";

            var desiredContent = new StringBuilder()
                .AppendLine("[Desktop Entry]")
                .AppendLine("Version=1.0")
                .AppendLine("Type=Application")
                .AppendLine($"Exec={desiredExec}")
                .AppendLine("Icon=ckan")
                .AppendLine("StartupNotify=true")
                .AppendLine("NoDisplay=true")
                .AppendLine("Terminal=false")
                .AppendLine("Categories=Utility")
                .AppendLine("MimeType=x-scheme-handler/ckan")
                .AppendLine("Name=CKAN Launcher")
                .AppendLine("Comment=Launch CKAN")
                .ToString();

            var existingContent = File.Exists(handlerPath)
                ? File.ReadAllText(handlerPath)
                : null;

            if (existingContent != desiredContent)
            {
                log.InfoFormat("Writing URL handler desktop file to {0}", handlerPath);

                // Write without a Byte Order Mark. update-desktop-database errors on BOM-prefixed files.
                File.WriteAllText(handlerPath, desiredContent, new UTF8Encoding(false));
                AutoUpdate.SetExecutable(handlerPath);

                RunCommand("xdg-mime", $"default {LinuxHandlerFilename} x-scheme-handler/ckan");
                RunCommand("update-desktop-database", ApplicationsPath);
            }
            else
            {
                log.InfoFormat("URL handler desktop file is already up to date");
            }
        }

        private static void RunCommand(string command, string args)
        {
            try
            {
                log.InfoFormat("Running {0} {1}", command, args);
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName               = command,
                    Arguments              = args,
                    UseShellExecute        = false,
                    RedirectStandardError  = true,
                    RedirectStandardOutput = true,
                });
                if (process != null)
                {
                    var stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        log.WarnFormat("{0} exited with code {1}: {2}",
                                       command, process.ExitCode, stderr);
                    }
                }
            }
            catch (Exception ex)
            {
                // xdg-mime and update-desktop-database are not guaranteed to be on all systems.
                log.WarnFormat("Could not run {0}: {1}", command, ex.Message);
            }
        }
    }
}
