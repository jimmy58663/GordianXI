using Avalonia.Input;
using Gordian.App.Services;
using Gordian.Engine.Input;
using Xunit;
namespace Gordian.App.Tests.Services;

public class KeypadIdentityTests
{
    [Theory]
    [InlineData(Key.Insert, PhysicalKey.NumPad0, GordianKey.NumPad0)]
    [InlineData(Key.End, PhysicalKey.NumPad1, GordianKey.NumPad1)]
    [InlineData(Key.Down, PhysicalKey.NumPad2, GordianKey.NumPad2)]
    [InlineData(Key.PageDown, PhysicalKey.NumPad3, GordianKey.NumPad3)]
    [InlineData(Key.Left, PhysicalKey.NumPad4, GordianKey.NumPad4)]
    [InlineData(Key.Clear, PhysicalKey.NumPad5, GordianKey.NumPad5)]
    [InlineData(Key.Right, PhysicalKey.NumPad6, GordianKey.NumPad6)]
    [InlineData(Key.Home, PhysicalKey.NumPad7, GordianKey.NumPad7)]
    [InlineData(Key.Up, PhysicalKey.NumPad8, GordianKey.NumPad8)]
    [InlineData(Key.PageUp, PhysicalKey.NumPad9, GordianKey.NumPad9)]
    [InlineData(Key.Delete, PhysicalKey.NumPadDecimal, GordianKey.NumPadDecimal)]
    [InlineData(Key.Divide, PhysicalKey.NumPadDivide, GordianKey.NumPadDivide)]
    [InlineData(Key.Multiply, PhysicalKey.NumPadMultiply, GordianKey.NumPadMultiply)]
    [InlineData(Key.Subtract, PhysicalKey.NumPadSubtract, GordianKey.NumPadSubtract)]
    [InlineData(Key.Add, PhysicalKey.NumPadAdd, GordianKey.NumPadAdd)]
    [InlineData(Key.Enter, PhysicalKey.NumPadEnter, GordianKey.NumPadEnter)]
    public void GameplayUsesPhysicalKeypadIdentity(Key logical, PhysicalKey physical, GordianKey expected)
    {
        Assert.Equal(expected, AvaloniaInputMapper.ToGordianKey(logical, physical));
        // No physical evidence: retain logical behavior, including dedicated navigation.
        Assert.Equal(AvaloniaInputMapper.ToGordianKey(logical), AvaloniaInputMapper.ToGordianKey(logical, PhysicalKey.None));
    }

    [Fact]
    public void OrdinaryTypingRemainsLogicalRatherThanPhysicalLayout()
    {
        Assert.Equal(GordianKey.Z, AvaloniaInputMapper.ToGordianKey(Key.Z, PhysicalKey.Y));
        Assert.Equal(GordianKey.Up, AvaloniaInputMapper.ToGordianKey(Key.Up, PhysicalKey.ArrowUp));
    }
}
