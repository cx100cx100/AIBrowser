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

        // 当收到唤醒信号时触发
        public event Action? ShowRequested;

        public SingleInstanceService(string appId)
        {
            // 确保名称全局唯一
            _mutexName = $@"Global\{appId}.Mutex";
            _pipeName = $@"{appId}.Pipe";
        }

        public void Start()
        {
            _mutex = new Mutex(initiallyOwned: true, name: _mutexName, createdNew: out bool createdNew);
            IsFirstInstance = createdNew;

            // 只有第一个实例才需要启动监听服务
            if (IsFirstInstance)
            {
                _cts = new CancellationTokenSource();
                _ = Task.Run(() => ListenLoopAsync(_cts.Token));
            }
        }

        // 第二个实例调用此方法：发送信号给第一个实例
        public void SignalFirstInstanceToShow()
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
                client.Connect(500); // 500ms 超时
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine("SHOW");
            }
            catch
            {
                // 连接失败或超时，直接忽略
            }
        }

        // 第一个实例的监听循环
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
                    // 管道错误忽略，重新等待连接
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