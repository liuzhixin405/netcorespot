using System.Buffers;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.MarketData;

/// <summary>
/// 一条到交易所的 WebSocket 连接的生命周期：连接、心跳、接收、空闲检测、指数退避重连。
/// 协议差异由调用方通过回调处理。
/// </summary>
internal sealed class ResilientWebSocketConnection : IAsyncDisposable
{
    private const int ReceiveBufferSize = 64 * 1024;

    private readonly string _name;
    private readonly Uri _endpoint;
    private readonly Func<ResilientWebSocketConnection, CancellationToken, Task> _onConnected;
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task> _onMessage;
    private readonly Func<string?>? _heartbeat;
    private readonly MarketDataFeedOptions _options;
    private readonly MarketDataMetrics _metrics;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ExponentialBackoff _backoff;

    private readonly TaskCompletionSource<bool> _firstConnection =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private int _connected;
    private long _lastMessageUnixMs;

    public ResilientWebSocketConnection(
        string name,
        Uri endpoint,
        Func<ResilientWebSocketConnection, CancellationToken, Task> onConnected,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> onMessage,
        Func<string?>? heartbeat,
        MarketDataFeedOptions options,
        MarketDataMetrics metrics,
        ILogger logger)
    {
        _name = name;
        _endpoint = endpoint;
        _onConnected = onConnected;
        _onMessage = onMessage;
        _heartbeat = heartbeat;
        _options = options;
        _metrics = metrics;
        _logger = logger;
        _backoff = new ExponentialBackoff(options.ReconnectDelayMin, options.ReconnectDelayMax);
    }

    public bool IsConnected => Volatile.Read(ref _connected) == 1 && _socket?.State == WebSocketState.Open;

    public DateTimeOffset? LastMessageAt =>
        Volatile.Read(ref _lastMessageUnixMs) == 0
            ? null
            : DateTimeOffset.FromUnixTimeMilliseconds(Volatile.Read(ref _lastMessageUnixMs));

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_runTask is not null)
        {
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runTask = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task WaitUntilStartedAsync(CancellationToken cancellationToken) =>
        _firstConnection.Task.WaitAsync(cancellationToken);

    public async Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        var socket = _socket;
        if (socket is null || socket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException($"{_name} WebSocket is not open");
        }

        byte[] bytes = Encoding.UTF8.GetBytes(text);

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _socket?.Abort();

        if (_runTask is { } runTask)
        {
            try
            {
                await runTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 停止阶段忽略取消
            }
            catch (Exception)
            {
                // 停止阶段忽略连接循环的历史异常，避免影响宿主关闭
            }
        }

        _cts?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _sendLock.Dispose();
    }

    private async Task RunAsync(CancellationToken stop)
    {
        var backoff = _backoff;
        bool everUp = false;
        bool outage = false;

        while (!stop.IsCancellationRequested)
        {
            Exception? failure = null;
            long upAtTimestamp = 0;
            bool connected = false;

            try
            {
                using var socket = new ClientWebSocket();

                // OKX/Binance 等交易所依赖 WebSocket 层 keep-alive 检测半开连接。
                // [联网-官方: https://learn.microsoft.com/en-us/dotnet/api/system.net.websockets.clientwebsocketoptions.keepaliveinterval]
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

                using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop))
                {
                    connectTimeout.CancelAfter(_options.ConnectTimeout);
                    await socket.ConnectAsync(_endpoint, connectTimeout.Token);
                }

                _socket = socket;
                Volatile.Write(ref _connected, 1);
                _firstConnection.TrySetResult(true);
                _metrics.ConnectionUp(_name);
                connected = true;
                upAtTimestamp = Stopwatch.GetTimestamp();

                await _onConnected(this, stop);

                using var connectionEnd = CancellationTokenSource.CreateLinkedTokenSource(stop);
                Task heartbeat = HeartbeatAsync(connectionEnd.Token);

                try
                {
                    await ReceiveAsync(socket, connectionEnd.Token);
                }
                finally
                {
                    await connectionEnd.CancelAsync();

                    try
                    {
                        await heartbeat;
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }

                if (!everUp)
                {
                    everUp = true;
                    _logger.LogInformation("{Name} connection established", _name);
                }
                else if (outage)
                {
                    outage = false;
                    _logger.LogInformation("{Name} connection restored", _name);
                }

                return;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                _socket = null;
                Volatile.Write(ref _connected, 0);
                if (connected)
                {
                    _metrics.ConnectionDown(_name);
                }
            }

            if (stop.IsCancellationRequested)
            {
                return;
            }

            _metrics.Reconnect(_name, failure?.GetType().Name ?? "closed");

            if (outage)
            {
                _logger.LogDebug(failure, "{Name} connection still failing; retrying", _name);
            }
            else
            {
                outage = true;
                _logger.LogWarning(failure, "{Name} connection lost; reconnecting", _name);
            }

            // 连接稳定过才重置退避；否则退避持续增长，避免因订阅被拒而快速抖动。
            if (upAtTimestamp != 0 &&
                Stopwatch.GetElapsedTime(upAtTimestamp) >= _options.StableAfter)
            {
                backoff.Reset();
            }

            try
            {
                await Task.Delay(backoff.Next(), stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ReceiveAsync(ClientWebSocket socket, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            var message = new ArrayBufferWriter<byte>(ReceiveBufferSize);
            ValueWebSocketReceiveResult result;

            using (var idle = CancellationTokenSource.CreateLinkedTokenSource(stop))
            {
                idle.CancelAfter(_options.IdleTimeout);

                try
                {
                    do
                    {
                        result = await socket.ReceiveAsync(message.GetMemory(ReceiveBufferSize), idle.Token);
                        message.Advance(result.Count);
                    } while (!result.EndOfMessage);
                }
                catch (OperationCanceledException) when (!stop.IsCancellationRequested)
                {
                    throw new TimeoutException($"{_name} received no message for {_options.IdleTimeout.TotalSeconds:0}s");
                }
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new WebSocketException(
                    $"closed by venue: {socket.CloseStatus} {socket.CloseStatusDescription}".TrimEnd());
            }

            Volatile.Write(ref _lastMessageUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _metrics.MessageReceived(_name);

            try
            {
                await _onMessage(message.WrittenMemory, stop);
            }
            catch (Exception ex)
            {
                _metrics.MessageUnreadable(_name);
                _logger.LogWarning(ex, "{Name} message could not be read", _name);
            }
        }
    }

    private async Task HeartbeatAsync(CancellationToken stop)
    {
        if (_heartbeat is null)
        {
            return;
        }

        using var timer = new PeriodicTimer(_options.HeartbeatInterval);
        while (await timer.WaitForNextTickAsync(stop))
        {
            var text = _heartbeat();
            if (text is null)
            {
                continue;
            }

            try
            {
                await SendTextAsync(text, stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Name} heartbeat failed", _name);
            }
        }
    }
}
