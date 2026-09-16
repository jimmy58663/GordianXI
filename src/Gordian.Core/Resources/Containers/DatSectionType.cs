// src/Gordian.Core/Resources/Containers/DatSectionType.cs
namespace Gordian.Core.Resources.Containers
{
    /// <summary>
    /// Section type codes for FFXI 16-byte header chunk containers.
    /// Derived from community research in xi-model-viewer (https://github.com/vekien/xi-model-viewer)
    /// and xi-tools (SECTION_TYPE_NAMES / xim SectionType).
    /// </summary>
    public enum DatSectionType : byte
    {
        End = 0x00,
        Directory = 0x01,
        Table = 0x04,
        ParticleGenerator = 0x05,
        Route = 0x06,
        EffectRoutine = 0x07,
        ParticleKeyFrameData = 0x19,
        ZoneDef = 0x1C,
        ParticleMesh = 0x1F,
        Texture = 0x20,
        SpriteSheetMesh = 0x21,
        WeightedMesh = 0x25,
        Skeleton = 0x29,
        SkeletonMesh = 0x2A,
        SkeletonAnimation = 0x2B,
        ZoneMesh = 0x2E,
        Environment = 0x2F,
        UiMenu = 0x30,
        UiElementGroup = 0x31,
        ZoneInteractions = 0x36,
        SoundEffectPointer = 0x3D,
        PointList = 0x3E,
        Info = 0x45,
        SpellList = 0x49,
        Path = 0x4A,
        AbilityList = 0x53,
        WeaponTrace = 0x54,
        BumpMap = 0x5D,
        Blur = 0x5E,
    }
}
