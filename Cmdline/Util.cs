using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace CKAN.CmdLine
{
    // todo: delete. None of this seems to work quickly enough.

    public static class Util
    {
        // Hides the console window on Windows
        // useful when running the GUI
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int FreeConsole();

        [ExcludeFromCodeCoverage]
        public static void HideConsoleWindow()
        {
            if (Platform.IsWindows)
            {
                FreeConsole();
            }
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_HIDE = 0;

        [ExcludeFromCodeCoverage]
        public static void HideConsoleWindow2() {
            var handle = GetConsoleWindow();
            ShowWindow(handle, SW_HIDE); // Immediately hides the console
        }
    }
}
