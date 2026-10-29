// tests/Gordian.App.Tests/Graphics/ViewportRenderSettingsCollection.cs
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// The tests that set and assert on the process-wide <see cref="Gordian.App.Graphics.ViewportRenderSettings"/> (the
    /// performance overlay mode, frame rates, the zone cache budget). Every <c>ViewportViewModel</c> applies its settings
    /// file to them when built, and view models are built by test classes in other collections (<c>MainWindowViewModel</c>
    /// builds one), so these run on their own, after the parallel collections (#451: the overlay mode test failed about
    /// half of the full runs).
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class ViewportRenderSettingsCollection
    {
        public const string Name = "ViewportRenderSettings";
    }
}
