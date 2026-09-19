// src/Gordian.Core/Resources/Tables/CharacterEquipmentResolver.cs
using System;
using System.IO;

namespace Gordian.Core.Resources.Tables
{
    /// <summary>
    /// Playable character races matching FFXI protocol look specifications.
    /// </summary>
    public enum CharacterRace : byte
    {
        Unknown = 0,
        HumeMale = 1,
        HumeFemale = 2,
        ElvaanMale = 3,
        ElvaanFemale = 4,
        TaruMale = 5,
        TaruFemale = 6,
        Mithra = 7,
        Galka = 8
    }

    /// <summary>
    /// Visual equipment and model appearance slots.
    /// </summary>
    public enum CharacterSlot : byte
    {
        Face = 0,
        Head = 1,
        Body = 2,
        Hands = 3,
        Legs = 4,
        Feet = 5,
        Main = 6,
        Sub = 7,
        Ranged = 8
    }

    /// <summary>
    /// Resolves character equipment slots (Race, Slot, ModelId) and monster/NPC models
    /// to FFXI master file IDs.
    /// Protocol tables referenced from Atom0s' common_geartables.php, xi-tools (xi_core.py),
    /// and xi-model-viewer (https://github.com/vekien/xi-model-viewer).
    /// </summary>
    public static class CharacterEquipmentResolver
    {
        public const int EntityModelOffset = 98239;

        private readonly record struct GearGroup(int BaseFileId, int Count);

        // Retail FFXI gear tables mapping (Race, Slot) -> list of [base_file_id, count] groups.
        private static readonly (int Base, int Count)[][] HumeMaleGear =
        {
            new[] { (7080, 32) }, // Face
            new[] { (7112, 256), (63323, 48), (63371, 16), (71247, 256), (98787, 32), (102961, 64) }, // Head
            new[] { (7368, 256), (63387, 48), (63435, 16), (71503, 256), (98819, 32), (103025, 64) }, // Body
            new[] { (7624, 256), (63451, 48), (63499, 16), (71759, 256), (98851, 32), (103089, 64) }, // Hands
            new[] { (7880, 256), (63515, 48), (63563, 16), (72015, 256), (98883, 32), (103153, 64) }, // Legs
            new[] { (8136, 256), (63579, 48), (63627, 16), (72271, 256), (98915, 32), (103217, 64) }, // Feet
            new[] { (8392, 512), (63643, 128), (72527, 256), (107301, 300), (0, 64) }, // Main
            new[] { (41199, 512), (66459, 128), (81999, 256), (105201, 300), (0, 64) }, // Sub
            new[] { (9416, 256) } // Ranged
        };

        private static readonly (int Base, int Count)[][] HumeFemaleGear =
        {
            new[] { (10256, 32) }, // Face
            new[] { (10288, 256), (63771, 48), (63819, 16), (72783, 256), (98947, 32), (103281, 64) }, // Head
            new[] { (10544, 256), (63835, 48), (63883, 16), (73039, 256), (98979, 32), (103345, 64) }, // Body
            new[] { (10800, 256), (63899, 48), (63947, 16), (73295, 256), (99011, 32), (103409, 64) }, // Hands
            new[] { (11056, 256), (63963, 48), (64011, 16), (73551, 256), (99043, 32), (103473, 64) }, // Legs
            new[] { (11312, 256), (64027, 48), (64075, 16), (73807, 256), (99075, 32), (103537, 64) }, // Feet
            new[] { (11568, 512), (64091, 128), (74063, 256), (107601, 300), (0, 64) }, // Main
            new[] { (42479, 512), (66587, 128), (82255, 256), (105501, 300), (0, 64) }, // Sub
            new[] { (12592, 256) } // Ranged
        };

        private static readonly (int Base, int Count)[][] ElvaanMaleGear =
        {
            new[] { (13432, 32) }, // Face
            new[] { (13464, 256), (64219, 48), (64267, 16), (74319, 256), (99107, 32), (103601, 64) }, // Head
            new[] { (13720, 256), (64283, 48), (64331, 16), (74575, 256), (99139, 32), (103665, 64) }, // Body
            new[] { (13976, 256), (64347, 48), (64395, 16), (74831, 256), (99171, 32), (103729, 64) }, // Hands
            new[] { (14232, 256), (64411, 48), (64459, 16), (75087, 256), (99203, 32), (103793, 64) }, // Legs
            new[] { (14488, 256), (64475, 48), (64523, 16), (75343, 256), (99235, 32), (103857, 64) }, // Feet
            new[] { (14744, 512), (64539, 128), (75599, 256), (107901, 300), (0, 64) }, // Main
            new[] { (43759, 512), (66715, 128), (82511, 256), (105801, 300), (0, 64) }, // Sub
            new[] { (15768, 256) } // Ranged
        };

        private static readonly (int Base, int Count)[][] ElvaanFemaleGear =
        {
            new[] { (16608, 32) }, // Face
            new[] { (16640, 256), (64667, 48), (64715, 16), (75855, 256), (99267, 32), (103921, 64) }, // Head
            new[] { (16896, 256), (64731, 48), (64779, 16), (76111, 256), (99299, 32), (103985, 64) }, // Body
            new[] { (17152, 256), (64795, 48), (64843, 16), (76367, 256), (99331, 32), (104049, 64) }, // Hands
            new[] { (17408, 256), (64859, 48), (64907, 16), (76623, 256), (99363, 32), (104113, 64) }, // Legs
            new[] { (17664, 256), (64923, 48), (64971, 16), (76879, 256), (99395, 32), (104177, 64) }, // Feet
            new[] { (17920, 512), (64987, 128), (77135, 256), (108201, 300), (0, 64) }, // Main
            new[] { (45039, 512), (66843, 128), (82767, 256), (106101, 300), (0, 64) }, // Sub
            new[] { (18944, 256) } // Ranged
        };

        private static readonly (int Base, int Count)[][] TaruMaleGear =
        {
            new[] { (19784, 32) }, // Face
            new[] { (19816, 256), (65115, 48), (65163, 16), (77391, 256), (99427, 32), (104241, 64) }, // Head
            new[] { (20072, 256), (65179, 48), (65227, 16), (77647, 256), (99459, 32), (104305, 64) }, // Body
            new[] { (20328, 256), (65243, 48), (65291, 16), (77903, 256), (99491, 32), (104369, 64) }, // Hands
            new[] { (20584, 256), (65307, 48), (65355, 16), (78159, 256), (99523, 32), (104433, 64) }, // Legs
            new[] { (20840, 256), (65371, 48), (65419, 16), (78415, 256), (99555, 32), (104497, 64) }, // Feet
            new[] { (21096, 512), (65435, 128), (78671, 256), (108501, 300), (0, 64) }, // Main
            new[] { (46319, 512), (66971, 128), (83023, 256), (106401, 300), (0, 64) }, // Sub
            new[] { (22120, 256) } // Ranged
        };

        private static readonly (int Base, int Count)[][] TaruFemaleGear =
        {
            new[] { (22952, 32) }, // Face
            new[] { (19816, 256), (65115, 48), (65171, 16), (77391, 256), (99443, 32), (104241, 64) }, // Head
            new[] { (20072, 256), (65179, 48), (65235, 16), (77647, 256), (99475, 32), (104305, 64) }, // Body
            new[] { (20328, 256), (65243, 48), (65299, 16), (77903, 256), (99507, 32), (104369, 64) }, // Hands
            new[] { (20584, 256), (65307, 48), (65363, 16), (78159, 256), (99539, 32), (104433, 64) }, // Legs
            new[] { (20840, 256), (65371, 48), (65427, 16), (78415, 256), (99571, 32), (104497, 64) }, // Feet
            new[] { (21096, 512), (65435, 128), (78671, 256), (108501, 300), (0, 64) }, // Main
            new[] { (46319, 512), (66971, 128), (83023, 256), (106401, 300), (0, 64) }, // Sub
            new[] { (22120, 256) } // Ranged
        };

        private static readonly (int Base, int Count)[][] MithraGear =
        {
            new[] { (23184, 32) }, // Face
            new[] { (23216, 256), (65563, 48), (65611, 16), (78927, 256), (99587, 32), (104561, 64) }, // Head
            new[] { (23472, 256), (65627, 48), (65675, 16), (79183, 256), (99619, 32), (104625, 64) }, // Body
            new[] { (23728, 256), (65691, 48), (65739, 16), (79439, 256), (99651, 32), (104689, 64) }, // Hands
            new[] { (23984, 256), (65755, 48), (65803, 16), (79695, 256), (99683, 32), (104753, 64) }, // Legs
            new[] { (24240, 256), (65819, 48), (65867, 16), (79951, 256), (99715, 32), (104817, 64) }, // Feet
            new[] { (24496, 512), (65883, 128), (80207, 256), (108801, 300), (0, 64) }, // Main
            new[] { (47599, 512), (67099, 128), (83279, 256), (106701, 300), (0, 64) }, // Sub
            new[] { (25520, 256) } // Ranged
        };

        private static readonly (int Base, int Count)[][] GalkaGear =
        {
            new[] { (26360, 32) }, // Face
            new[] { (26392, 256), (66011, 48), (66059, 16), (80463, 256), (99747, 32), (104881, 64) }, // Head
            new[] { (26648, 256), (66075, 48), (66123, 16), (80719, 256), (99779, 32), (104945, 64) }, // Body
            new[] { (26904, 256), (66139, 48), (66187, 16), (80975, 256), (99811, 32), (105009, 64) }, // Hands
            new[] { (27160, 256), (66203, 48), (66251, 16), (81231, 256), (99843, 32), (105073, 64) }, // Legs
            new[] { (27416, 256), (66267, 48), (66315, 16), (81487, 256), (99875, 32), (105137, 64) }, // Feet
            new[] { (27672, 512), (66331, 128), (81743, 256), (109101, 300), (0, 64) }, // Main
            new[] { (48879, 512), (67227, 128), (83535, 256), (107001, 300), (0, 64) }, // Sub
            new[] { (28696, 256) } // Ranged
        };

        /// <summary>
        /// Attempts to resolve a numeric FFXI file ID for a specific character race, equipment slot, and model ID.
        /// </summary>
        public static bool TryResolveGearFileId(CharacterRace race, CharacterSlot slot, ushort modelId, out int fileId)
        {
            var table = race switch
            {
                CharacterRace.HumeMale => HumeMaleGear,
                CharacterRace.HumeFemale => HumeFemaleGear,
                CharacterRace.ElvaanMale => ElvaanMaleGear,
                CharacterRace.ElvaanFemale => ElvaanFemaleGear,
                CharacterRace.TaruMale => TaruMaleGear,
                CharacterRace.TaruFemale => TaruFemaleGear,
                CharacterRace.Mithra => MithraGear,
                CharacterRace.Galka => GalkaGear,
                _ => null
            };

            if (table == null || (int)slot >= table.Length)
            {
                fileId = 0;
                return false;
            }

            var groups = table[(int)slot];
            int cursor = modelId;

            for (int i = 0; i < groups.Length; i++)
            {
                var (baseId, count) = groups[i];
                if (cursor < count)
                {
                    if (baseId == 0)
                    {
                        // Reserved empty entry
                        fileId = 0;
                        return false;
                    }

                    fileId = baseId + cursor;
                    return true;
                }

                cursor -= count;
            }

            fileId = 0;
            return false;
        }

        /// <summary>
        /// Returns the base skeleton DAT relative path for a given playable character race.
        /// </summary>
        public static string GetBaseSkeletonPath(CharacterRace race) => race switch
        {
            CharacterRace.HumeMale => Path.Combine("ROM", "27", "82.DAT"),
            CharacterRace.HumeFemale => Path.Combine("ROM", "32", "58.DAT"),
            CharacterRace.ElvaanMale => Path.Combine("ROM", "37", "31.DAT"),
            CharacterRace.ElvaanFemale => Path.Combine("ROM", "42", "4.DAT"),
            CharacterRace.TaruMale or CharacterRace.TaruFemale => Path.Combine("ROM", "46", "93.DAT"),
            CharacterRace.Mithra => Path.Combine("ROM", "51", "89.DAT"),
            CharacterRace.Galka => Path.Combine("ROM", "56", "59.DAT"),
            _ => string.Empty
        };

        /// <summary>
        /// Calculates the canonical FFXI ROM File ID for an NPC, Monster, or Trust entity model
        /// using the retail FFXI client's piecewise model-to-file mapping (NpcTable.getNpcModelIndex).
        /// Protocol specification referenced from FFXiMain.dll NpcTable.getNpcModelIndex,
        /// xi-model-viewer (https://github.com/vekien/xi-model-viewer), and cexi-tools.
        /// </summary>
        public static int GetMonsterFileId(uint modelId)
        {
            if (modelId < 1500)
            {
                return (int)modelId + 1300;
            }
            if (modelId < 3000)
            {
                return (int)modelId + 50295;
            }
            if (modelId < 3193)
            {
                return (int)modelId + 96907;
            }
            return (int)modelId + 98546;
        }
    }
}
