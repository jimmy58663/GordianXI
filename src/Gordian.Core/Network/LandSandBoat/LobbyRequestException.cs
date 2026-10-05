// src/Gordian.Core/Network/LandSandBoat/LobbyRequestException.cs
using System;

namespace Gordian.Core.Network.LandSandBoat
{
    /// <summary>
    /// The lobby error codes LandSandBoat sends in S2C 0x04 ResponseError. The client shows them with 3000 added
    /// ("FFXI-3305"). Codes referenced from LandSandBoat (https://github.com/LandSandBoat/server,
    /// src/login/login_errors.h) and XiPackets (https://github.com/atom0s/XiPackets, lobby/Protocol.md).
    /// </summary>
    public static class LobbyErrorCode
    {
        public const int CharacterAlreadyLoggedIn = 201;
        public const int WorldIsFull = 208;
        public const int UnableToConnectToWorldServer = 305;
        public const int CharacterNameUnavailable = 313;
        public const int FailedToRegisterWithNameServer = 314;
        public const int CharacterParametersIncorrect = 321;
        public const int GameDataUpdated = 331;
        public const int CouldNotConnectToLobbyServer = 332;

        /// <summary>The number the client shows (code + 3000).</summary>
        public static int DisplayCode(int code) => code + 3000;

        /// <summary>A short name for logs.</summary>
        public static string Describe(int code) => code switch
        {
            CharacterAlreadyLoggedIn => "CHARACTER_ALREADY_LOGGED_IN",
            WorldIsFull => "WORLD_IS_FULL",
            UnableToConnectToWorldServer => "UNABLE_TO_CONNECT_TO_WORLD_SERVER",
            CharacterNameUnavailable => "CHARACTER_NAME_UNAVAILABLE",
            FailedToRegisterWithNameServer => "FAILED_TO_REGISTER_WITH_THE_NAME_SERVER",
            CharacterParametersIncorrect => "CHARACTERS_PARAMETERS_ARE_INCORRECT",
            GameDataUpdated => "GAMES_DATA_HAS_BEEN_UPDATED",
            CouldNotConnectToLobbyServer => "COULD_NOT_CONNECT_TO_LOBBY_SERVER",
            _ => "UNKNOWN",
        };
    }

    /// <summary>
    /// A lobby request the server refused with S2C 0x04 (<see cref="ErrorCode"/> set), or that failed on the wire
    /// (no reply, closed connection: <see cref="ErrorCode"/> 0).
    /// </summary>
    public sealed class LobbyRequestException : InvalidOperationException
    {
        public LobbyRequestException(string request, int errorCode, string message, Exception? inner = null)
            : base(message, inner)
        {
            Request = request;
            ErrorCode = errorCode;
        }

        /// <summary>The request that failed ("select", "delete", "name check"...).</summary>
        public string Request { get; }

        /// <summary>The server's error code (0x04 <c>err_code</c>), 0 for a transport failure.</summary>
        public int ErrorCode { get; }

        /// <summary>Whether the server answered with an error code (as opposed to a lost connection).</summary>
        public bool IsServerError => ErrorCode != 0;

        public static LobbyRequestException FromServer(string request, int code) =>
            new(request, code, $"Lobby {request} failed: server returned error code {code} ({LobbyErrorCode.Describe(code)}, FFXI-{LobbyErrorCode.DisplayCode(code)}).");
    }
}
