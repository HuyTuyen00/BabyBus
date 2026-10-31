using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CafeManagement.Database
{
    public static class SqlBridgeHelper
    {
        private static TcpListener? _listener;
        private static CancellationTokenSource? _cts;
        private static bool _isRunning = false;
        private const int Port = 1433;

        public static void Start()
        {
            if (_isRunning) return;

            try
            {
                string pipeName = GetLocalDbPipeName();
                if (string.IsNullOrEmpty(pipeName))
                {
                    File.AppendAllText("db_error_log.txt", $"{DateTime.Now}: SqlBridgeHelper: Khong tim thay LocalDB Pipe Name!\n");
                    return;
                }

                _cts = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Any, Port);
                _listener.Start();
                _isRunning = true;

                File.AppendAllText("db_error_log.txt", $"{DateTime.Now}: SqlBridgeHelper dang lang nghe TCP Port {Port} -> Pipe: {pipeName}\n");

                _ = Task.Run(() => AcceptLoopAsync(pipeName, _cts.Token));
            }
            catch (Exception ex)
            {
                File.AppendAllText("db_error_log.txt", $"{DateTime.Now}: Lỗi khoi tao SqlBridgeHelper: {ex.Message}\n");
            }
        }

        public static void Stop()
        {
            _isRunning = false;
            try
            {
                _cts?.Cancel();
                _listener?.Stop();
            }
            catch { }
        }

        private static string GetLocalDbPipeName()
        {
            try
            {
                var psi = new ProcessStartInfo("sqllocaldb", "info mssqllocaldb")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    foreach (var line in output.Split('\n'))
                    {
                        if (line.Contains("Instance pipe name:"))
                        {
                            string pipe = line.Substring(line.IndexOf("np:") + 3).Trim();
                            if (pipe.StartsWith("\\\\.\\pipe\\"))
                                return pipe.Substring(9);
                            return pipe;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText("db_error_log.txt", $"{DateTime.Now}: Lỗi sqllocaldb info: {ex.Message}\n");
            }
            return "";
        }

        private static async Task AcceptLoopAsync(string pipeName, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null)
            {
                try
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync(ct);
                    _ = Task.Run(() => BridgeConnectionAsync(tcpClient, pipeName, ct), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    File.AppendAllText("db_error_log.txt", $"{DateTime.Now}: Lỗi AcceptLoopAsync: {ex.Message}\n");
                    await Task.Delay(1000, ct);
                }
            }
        }

        private static async Task BridgeConnectionAsync(TcpClient tcpClient, string pipeName, CancellationToken ct)
        {
            try
            {
                using (tcpClient)
                using (var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    await pipeClient.ConnectAsync(3000, ct);
                    using var netStream = tcpClient.GetStream();

                    var t1 = netStream.CopyToAsync(pipeClient, ct);
                    var t2 = pipeClient.CopyToAsync(netStream, ct);

                    await Task.WhenAny(t1, t2);
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText("db_error_log.txt", $"{DateTime.Now}: Lỗi BridgeConnectionAsync: {ex.Message}\n");
            }
        }
    }
}
