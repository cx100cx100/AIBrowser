using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace AIBrowser.Services
{
    internal sealed class SingleInstanceService : IDisposable
    {
        private readonly string _mutexName;
        private readonly string _pipeName;
        private Mutex? _mutex;
        private CancellationTokenSource? _cts;

        public bool IsFirstInstance { get; private set; }

        public event Action? ShowRequested;

        public SingleInstanceService(string appId)
        {
            _mutexName = $@"Global\{appId}.SingleInstance";
            _pipeName = $@"{appId}.Pipe";
        }

        public void Start()
        {
            _mutex = new Mutex(initiallyOwned: true, name: _mutexName, createdNew: out bool createdNew);
            IsFirstInstance = createdNew;

            if (!IsFirstInstance)
                return;

            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ListenLoopAsync(_cts.Token));
        }

        public void SignalFirstInstanceToShow()
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
                client.Connect(200); // 200ms 足够了
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine("SHOW");
            }
            catch
            {
                // 这里不需要弹窗，失败就算了
            }
        }

        private async Task ListenLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Message, PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(server);
                    var msg = await reader.ReadLineAsync();

                    if (string.Equals(msg, "SHOW", StringComparison.OrdinalIgnoreCase))
                    {
                        ShowRequested?.Invoke();
                    }
                }
                catch
                {
                    // 忽略，继续循环
                }
            }
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            try { _mutex?.ReleaseMutex(); } catch { }
            try { _mutex?.Dispose(); } catch { }
        }
    }
}
