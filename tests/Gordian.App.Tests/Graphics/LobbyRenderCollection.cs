// tests/Gordian.App.Tests/Graphics/LobbyRenderCollection.cs
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// The lobby render tests, which drive the lobby's background requests and its wall-clock fade: they run on their own,
    /// after the parallel collections, so a loaded full run cannot starve them (#484: they failed now and then in full runs
    /// and passed alone).
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class LobbyRenderCollection
    {
        public const string Name = "LobbyRender";
    }
}
