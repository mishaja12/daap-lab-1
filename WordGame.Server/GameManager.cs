using System.Collections.Concurrent;
using WordGame.Grpc;
using Grpc.Core;

namespace WordGame.Server;

public class GameManager
{
    private static readonly char[] Vowels = { 'A', 'E', 'I', 'O', 'U' };
    private static readonly char[] Consonants = { 'B', 'C', 'D', 'F', 'G', 'H', 'J', 'K', 'L', 'M', 'N', 'P', 'Q', 'R', 'S', 'T', 'V', 'W', 'X', 'Y', 'Z' };

    private readonly WordValidator _wordValidator;
    private readonly ILogger<GameManager> _logger;
    private readonly ConcurrentDictionary<string, ClientStream> _streams = new();
    private readonly ConcurrentDictionary<string, string> _playerNames = new();
    private readonly object _roundLock = new();
    private string? _currentLetters;
    private volatile bool _roundActive;
    private string? _winnerId;
    private string? _winningWord;
    private Timer? _roundTimer;
    private int _letterCount = 6;

    public GameManager(WordValidator wordValidator, ILogger<GameManager> logger)
    {
        _wordValidator = wordValidator;
        _logger = logger;
    }

    public string GeneratePlayerId() => Guid.NewGuid().ToString("N")[..8];

    public void RegisterPlayer(string playerId, string playerName)
    {
        _playerNames[playerId] = playerName;
    }

    public void AddStream(string playerId, IServerStreamWriter<GameEvent> writer)
    {
        _streams[playerId] = new ClientStream(writer);
        _logger.LogInformation("Player {PlayerId} connected. Total clients: {Count}", playerId, _streams.Count);

        if (_streams.Count >= 1 && !_roundActive)
            ScheduleNextRound();
    }

    public void RemoveStream(string playerId)
    {
        _streams.TryRemove(playerId, out _);
        _playerNames.TryRemove(playerId, out _);
        _logger.LogInformation("Player {PlayerId} disconnected. Remaining: {Count}", playerId, _streams.Count);
    }

    private void ScheduleNextRound()
    {
        _roundTimer?.Dispose();
        _roundTimer = new Timer(_ => StartRound(), null, 3000, Timeout.Infinite);
    }

    private void StartRound()
    {
        lock (_roundLock)
        {
            if (_roundActive)
                return;

            _roundActive = true;
            _winnerId = null;
            _winningWord = null;
            _currentLetters = GenerateLetters();

            _logger.LogInformation("Round started. Letters: {Letters}", _currentLetters);

            var evt = new GameEvent
            {
                LettersOffered = new LettersOffered { Letters = _currentLetters }
            };

            _ = BroadcastAsync(evt);
        }
    }

    private string GenerateLetters()
    {
        var rnd = Random.Shared;
        var result = new List<char>();

        result.Add(Vowels[rnd.Next(Vowels.Length)]);
        result.Add(Vowels[rnd.Next(Vowels.Length)]);
        for (var i = 2; i < _letterCount; i++)
            result.Add(Consonants[rnd.Next(Consonants.Length)]);

        return new string(result.OrderBy(_ => rnd.Next()).ToArray());
    }

    public (bool Accepted, string Message) SubmitWord(string playerId, string word)
    {
        lock (_roundLock)
        {
            if (!_roundActive)
                return (false, "No active round. Wait for the next one.");

            if (_winnerId != null)
                return (false, "Round already won!");

            if (string.IsNullOrWhiteSpace(_currentLetters))
                return (false, "Invalid round state.");

            if (!_wordValidator.IsValid(word, _currentLetters))
                return (false, "Invalid word. Use only the offered letters and a valid dictionary word.");

            _winnerId = playerId;
            _winningWord = word.Trim();
            _roundActive = false;

            var winnerName = _playerNames.TryGetValue(playerId, out var name) ? name : playerId;
            _logger.LogInformation("Winner: {Name} with word '{Word}'", winnerName, _winningWord);

            var evt = new GameEvent
            {
                RoundResult = new RoundResult
                {
                    WinnerName = winnerName,
                    WinningWord = _winningWord
                }
            };

            _ = BroadcastAsync(evt);

            ScheduleNextRound();

            return (true, "You win!");
        }
    }

    private async Task BroadcastAsync(GameEvent evt)
    {
        var tasks = _streams.Select(async kv =>
        {
            try
            {
                await kv.Value.Writer.WriteAsync(evt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write to client {PlayerId}", kv.Key);
            }
        });
        await Task.WhenAll(tasks);
    }

    private record ClientStream(IServerStreamWriter<GameEvent> Writer);
}
