using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32.SafeHandles;

// Dedicated Windows-only ckan:// protocol handler.
// ckan.exe is a console app so on Windows it always gets a console window
// popup on launch. Doing that every time a ckan URL opens is ugly and looks dodgy.

// This stub is a WinExe instead, so no popup.
// Called via reg as `ckan-urlhandler.exe <url> <path to ckan.exe> <verb>`. It sends the url to the
// running CKAN over the pipe, or starts CKAN with `<verb> --url <url> --no-handoff`.

// Must not reference anything outside the BCL so that it can be compiled
// with Native AOT for packing into ckan-windows.exe (needed to avoid substantially larger binary).

namespace CKAN.URLHandler
{
    [ExcludeFromCodeCoverage]
    public static class Program
    {
        // Must match CKAN.IO.URLPipe.Name.
        private const string PipeName = "CKAN_URL_PIPE";

        public static int Main(string[] args)
        {
            // Call should only ever come from the URL handler reg entry, so the args are known ahead of time:
            // <url> <path to ckan.exe> <verb> [any N ckan args].
            // The verb is whichever UI registered last.

            if (args.Length < 3 || string.IsNullOrWhiteSpace(args[0]) || string.IsNullOrWhiteSpace(args[1]))
            {
                return 1;
            }

            var url = args[0];
            var ckanExe = args[1];
            var launchArgs = new string[args.Length - 2];
            Array.Copy(args, 2, launchArgs, 0, launchArgs.Length);

            return (TrySendToRunningInstance(url) || LaunchCKAN(ckanExe, launchArgs, url)) ? 0 : 1;
        }

        // Duplicates URLPipe.TrySend because this project can't reference Core.
        // Necessary to make the url handler as small as possible.
        private static bool TrySendToRunningInstance(string url)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(50);

                // CKAN GUI can't bring its own window to the front. This program inherits that right
                // from the browser's link click. Grant it to CKAN before sending.
                if (GetNamedPipeServerProcessId(client.SafePipeHandle, out var pid))
                {
                    _ = AllowSetForegroundWindow(pid);
                }

                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine(url);

                return true;
            }
            catch
            {
                return false;
            }
        }
              
        private static bool LaunchCKAN(string ckanExe, string[] launchArgs, string url)
        {
            try
            {
                var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = ckanExe,
                    Arguments = $"{string.Join(" ", launchArgs)} --url \"{url}\" --no-handoff", // no-handoff: skip double TrySend.
                    UseShellExecute = false,
                    CreateNoWindow = launchArgs[0] == "gui",
                });

                // Same foreground rights handoff as the pipe path.
                if (proc != null)
                {
                    _ = AllowSetForegroundWindow((uint)proc.Id);
                }

                return true;
            }
            catch (Exception ex)
            {
                _ = MessageBox(IntPtr.Zero, $"Could not launch CKAN to handle the link:\n{ex.Message}", "CKAN", MB_ICONERROR);

                return false;
            }
        }

        private const uint MB_ICONERROR = 0x00000010;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(uint pid);
    }
}
