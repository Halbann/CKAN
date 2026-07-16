using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;

// Dedicated Windows-only ckan:// protocol handler.
// ckan.exe is a console app so on Windows it always gets a console window
// popup on launch. Doing that every time a ckan URL opens is ugly and looks dodgy.

// This stub is a WinExe instead, so no popup.
// Invoked via reg as `ckan-urlhandler.exe <path to ckan.exe> <url>` which sends
// the URL to a running CKAN over the pipe and starts CKAN if nothing is listening.

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
            // Call should only ever come from URL handler reg entry, so the args are known ahead of time.

            if (args.Length < 2
                || string.IsNullOrWhiteSpace(args[0])
                || string.IsNullOrWhiteSpace(args[1]))
            {
                return 1;
            }

            var ckanExe = args[0];
            var url = args[1];

            return (TrySendToRunningInstance(url) || LaunchCKAN(ckanExe, url)) ? 0 : 1;
        }

        // Duplicates URLPipe.TrySend because this project can't reference Core.
        // Necessary to make the url handler as small as possible.
        private static bool TrySendToRunningInstance(string url)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(50);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine(url);

                return true;
            }
            catch
            {
                return false;
            }
        }
              
        private static bool LaunchCKAN(string ckanExe, string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = ckanExe,
                    Arguments = $"gui \"{url}\"",
                    UseShellExecute = false, 
                    CreateNoWindow = true, // Prevents a console popup. GUI window is unaffected.
                });

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
    }
}
