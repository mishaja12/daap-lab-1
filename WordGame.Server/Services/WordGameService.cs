using Grpc.Core;
using WordGame.Grpc;

namespace WordGame.Server.Services;

public class WordGameGrpcService : WordGame.Grpc.WordGame.WordGameBase
{
    private readonly GameManager _gameManager;
    private readonly ILogger<WordGameGrpcService> _logger;

    public WordGameGrpcService(GameManager gameManager, ILogger<WordGameGrpcService> logger)
    {
        _gameManager = gameManager;
        _logger = logger;
    }

    public override Task<JoinResponse> JoinGame(JoinRequest request, ServerCallContext context)
    {
        var playerId = _gameManager.GeneratePlayerId();
        _gameManager.RegisterPlayer(playerId, string.IsNullOrWhiteSpace(request.PlayerName) ? "Anonymous" : request.PlayerName.Trim());
        return Task.FromResult(new JoinResponse { PlayerId = playerId });
    }

    public override async Task SubscribeToGame(SubscribeRequest request, IServerStreamWriter<GameEvent> responseStream, ServerCallContext context)
    {
        var playerId = request.PlayerId;
        if (string.IsNullOrEmpty(playerId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "PlayerId is required"));
        }

        _gameManager.AddStream(playerId, responseStream);

        try
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
        finally
        {
            _gameManager.RemoveStream(playerId);
        }
    }

    public override Task<SubmitWordResponse> SubmitWord(SubmitWordRequest request, ServerCallContext context)
    {
        var (accepted, message) = _gameManager.SubmitWord(request.PlayerId, request.Word ?? "");
        return Task.FromResult(new SubmitWordResponse { Accepted = accepted, Message = message });
    }
}
