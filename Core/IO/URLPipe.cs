using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using log4net;

namespace CKAN.IO
{
    // Passes ckan:// URLs to the running CKAN instance over a named pipe.
    // The receiver dispatches them via ProtocolRouter on a pipe thread.
    public static class URLPipe
    {
        // Internal set so that tests don't fight each other or a running CKAN over one global pipe.
        public static string Name { get; internal set; } = "CKAN_URL_PIPE";

        private static readonly ILog log = LogManager.GetLogger(typeof(URLPipe));
        private static CancellationTokenSource? cts;
        private static Task? serverTask;

        // Returns false if another instance already owns the pipe.
        public static bool StartServer()
        {
            if (serverTask != null)
            {
                return true;
            }

            NamedPipeServerStream initialServer;
            try
            {
                initialServer = NewPipeServer();
            }
            catch (IOException ex)
            {
                log.WarnFormat("Could not bind URL pipe '{0}': {1}. This instance will not receive ckan:// URLs.", Name, ex.Message);
                return false;
            }

            cts = new CancellationTokenSource();
            var token = cts.Token;

            log.Debug("Starting URL pipe server task");
            serverTask = Task.Run(async () =>
            {
                // Keep one server instance for all clients. On Unix a send can connect before the
                // server accepts. Rebinding the pipe after each URL would drop those sends.
                using var server = initialServer;
                while (true)
                {
                    try
                    {
                        await server.WaitForConnectionAsync(token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    // Cancelling the wait can also surface as an IOException.
                    catch (IOException) when (token.IsCancellationRequested)
                    {
                        return;
                    }

                    string? url = null;
                    try
                    {
                        using var reader = new StreamReader(server, Encoding.UTF8, true, 1024, leaveOpen: true);
                        url = await reader.ReadLineAsync();
                    }
                    catch (IOException ex)
                    {
                        log.WarnFormat("URL pipe read failed: {0}", ex.Message);
                    }
                    server.Disconnect();

                    log.DebugFormat("Pipe received URL: {0}", url);
                    if (url != null && !string.IsNullOrWhiteSpace(url))
                    {
                        ProtocolRouter.Handle(url);
                    }

                    if (token.IsCancellationRequested)
                    {
                        return;
                    }
                }
            });

            return true;
        }

        // PipeTransmissionMode.Byte is required for Unix compatibility.
        private static NamedPipeServerStream NewPipeServer()
            => new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        public static async Task StopServer()
        {
            if (cts == null)
            {
                return;
            }

            cts.Cancel();

            // net481 and the Unix implementation ignore the Cancel once WaitForConnectionAsync is waiting.
            // Therefore connect to complete the wait instead.
            try
            {
                using var client = new NamedPipeClientStream(".", Name, PipeDirection.Out);
                await client.ConnectAsync(50);
            }
            catch
            { }

            // Reset even if the task faulted. Otherwise StartServer can never run again.
            try
            {
                if (serverTask != null)
                {
                    await serverTask;
                }
            }
            finally
            {
                cts.Dispose();
                cts = null;
                serverTask = null;
            }
        }

        // Forward a URL to the running instance. Returns true if delivered.
        // Duplicated in URLHandler/Program.cs (Windows only URL handler exe).
        public static bool TrySend(string url, int timeoutMs = 50)
        {
            using var client = new NamedPipeClientStream(".", Name, PipeDirection.Out);
            try
            {
                client.Connect(timeoutMs);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine(url);

                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}
