// src/Gordian.Core/Config/CharacterFolderKey.cs
using System.IO;

namespace Gordian.Core.Config
{
    /// <summary>
    /// Which character a per-character folder belongs to (#639): the server, the world on it and the character's name. Its
    /// <see cref="RelativePath"/> names the character's config folder (<see cref="CharacterConfigStorage"/>) and its chat
    /// log folder (<c>logs/chat/</c>), so the same name on two servers or two worlds never shares a file.
    /// </summary>
    /// <param name="Server">The launching profile's <see cref="Profiles.AccountProfile.ServerFolderName"/>: <c>Retail</c>, or a LandSandBoat profile's host.</param>
    /// <param name="World">The character's world from the lobby's character list (<c>LobbyCharacter.WorldName</c>), e.g. <c>Asura</c>.</param>
    /// <param name="Character">The character's name.</param>
    public readonly record struct CharacterFolderKey(string? Server, string? World, string? Character)
    {
        /// <summary>
        /// <c>&lt;server&gt;/&lt;world&gt;/&lt;character&gt;</c>, each through <see cref="GordianStorage.SafeFolderName"/>, so
        /// an empty one becomes <c>Unknown</c>.
        /// </summary>
        public string RelativePath =>
            Path.Combine(GordianStorage.SafeFolderName(Server), GordianStorage.SafeFolderName(World), GordianStorage.SafeFolderName(Character));
    }
}
