using Grpc.Core;
using Grpc.Net.Client;
using WordGame.Grpc;

namespace WordGame.Server.Services;

public class GameClientService : IDisposable
{
    private readonly IConfiguration _config;
    private readonly ILogger<GameClientService> _logger;
    private GrpcChannel? _channel;
    private WordGame.Grpc.WordGame.WordGameClient? _client;
    private CancellationTokenSource? _streamCts;

    public string PlayerId { get; private set; } = "";
    public string PlayerName { get; private set; } = "";
    public string CurrentLetters { get; private set; } = "";
    public bool CanSubmit { get; private set; }
    public List<string> StatusMessages { get; } = new();
    public bool IsConnected { get; private set; }

    public event Action? OnStateChanged;

    public GameClientService(IConfiguration config, ILogger<GameClientService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task ConnectAsync(string name)
    {
        if (IsConnected) return;

        var serverUrl = _config["GrpcServerUrl"] ?? "http://localhost:5119";
        var normalizedName = string.IsNullOrWhiteSpace(name) ? "Anonymous" : name.Trim();

        try
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            _channel = GrpcChannel.ForAddress(serverUrl);
            _client = new WordGame.Grpc.WordGame.WordGameClient(_channel);

            var joinResponse = await _client.JoinGameAsync(new JoinRequest { PlayerName = normalizedName });
            PlayerId = joinResponse.PlayerId;
            PlayerName = normalizedName;
            IsConnected = true;

            AddStatus($"Joined as {normalizedName}. Waiting for letters...");
            _streamCts = new CancellationTokenSource();
            _ = RunStreamAsync(_streamCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect");
            AddStatus($"Error: {ex.Message}");
        }
    }

    private async Task RunStreamAsync(CancellationToken ct)
    {
        if (_client == null) return;

        try
        {
            using var call = _client.SubscribeToGame(new SubscribeRequest { PlayerId = PlayerId });
            while (await call.ResponseStream.MoveNext(ct))
            {
                var evt = call.ResponseStream.Current;
                if (evt.LettersOffered != null)
                {
                    CurrentLetters = evt.LettersOffered.Letters;
                    CanSubmit = true;
                    AddStatus("New round! Type a word and submit.");
                    NotifyStateChanged();
                }
                else if (evt.RoundResult != null)
                {
                    CanSubmit = false;
                    AddStatus($"*** {evt.RoundResult.WinnerName} wins with '{evt.RoundResult.WinningWord}'! ***");
                    CurrentLetters = "";
                    NotifyStateChanged();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stream error");
            AddStatus($"Stream error: {ex.Message}");
            NotifyStateChanged();
        }
    }

    public async Task<(bool Accepted, string Message)> SubmitWordAsync(string word)
    {
        if (_client == null || !CanSubmit)
            return (false, "Not connected or no active round.");

        var trimmed = word?.Trim() ?? "";
        if (trimmed.Length < 2)
            return (false, "Word must be at least 2 letters.");

        try
        {
            var response = await _client.SubmitWordAsync(new SubmitWordRequest
            {
                PlayerId = PlayerId,
                Word = trimmed
            });

            AddStatus(response.Message);
            if (response.Accepted)
            {
                CanSubmit = false;
                NotifyStateChanged();
            }
            return (response.Accepted, response.Message);
        }
        catch (Exception ex)
        {
            var msg = $"Error: {ex.Message}";
            AddStatus(msg);
            return (false, msg);
        }
    }

    public void Disconnect()
    {
        _streamCts?.Cancel();
        _channel?.Dispose();
        _channel = null;
        _client = null;
        IsConnected = false;
        PlayerId = "";
        PlayerName = "";
        CurrentLetters = "";
        CanSubmit = false;
        StatusMessages.Clear();
    }

    private void AddStatus(string msg)
    {
        StatusMessages.Add(msg);
        if (StatusMessages.Count > 50)
            StatusMessages.RemoveAt(0);
    }

    private void NotifyStateChanged()
    {
        try { OnStateChanged?.Invoke(); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    public void Dispose() => Disconnect();
}
