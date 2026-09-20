// src/Gordian.Core/Animation/NpcStanceResolver.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Animation
{
    /// <summary>
    /// Generalized NPC stance and animation resolver.
    /// Supports multi-stance mob families (e.g. Uragnite open/shell, Omega quadruped/biped,
    /// Hpemde normal/open/diving, Adamantoise normal/shell guard) through convention-based
    /// auto-discovery of clip families and protocol AnimationSub mapping.
    /// Clean-room implementation referencing LandSandBoat family mixins (https://github.com/LandSandBoat/server)
    /// and xi-model-viewer animation chunk specifications (https://github.com/vekien/xi-model-viewer).
    /// </summary>
    public static class NpcStanceResolver
    {
        /// <summary>
        /// Resolves the effective stance index (0 = default, 1 = alternate/standing/defense, 2 = submerged/specialized)
        /// based on the model's available animation clips and the entity's network AnimationSub byte.
        /// </summary>
        public static byte ResolveEffectiveStance(EntityModel model, byte animationSub)
        {
            if (model == null || model.Animations.Count == 0) return 0;
            var anims = model.Animations;

            // 1. Check for Uragnite and defense/shell sub-animation 5 pattern: inShell = 5, outOfShell = 4
            // Protocol specification referenced from LandSandBoat scripts/mixins/families/uragnite.lua
            if (animationSub == 5 && (anims.ContainsKey("shel") || anims.ContainsKey("1tl0") || anims.ContainsKey("1tl") || anims.ContainsKey("gid0") || anims.ContainsKey("gid") || anims.ContainsKey("gud0") || anims.ContainsKey("gud")))
            {
                // Note: If Hpemde has 2dl0/2tl0, sub 5 is diving (stance 2), handled below
                if (!anims.ContainsKey("2dl0") && !anims.ContainsKey("2tl0") && !anims.ContainsKey("sp20"))
                {
                    return 1;
                }
            }
            if (animationSub == 4 && (anims.ContainsKey("shel") || anims.ContainsKey("1tl0") || anims.ContainsKey("1tl")))
            {
                return 0;
            }

            // 2. Check for Adamantoise / Tortoise family pattern: inShell = 1, outOfShell = 0 or 2
            // Protocol specification referenced from LandSandBoat scripts/zones/Valley_of_Sorrows/mobs/Aspidochelone.lua
            if (anims.ContainsKey("adam") || anims.ContainsKey("gid0") || (anims.ContainsKey("gud0") && anims.ContainsKey("1tl0") && anims.ContainsKey("atm5")))
            {
                if (animationSub == 1) return 1;
                if (animationSub == 0 || animationSub == 2) return 0;
            }

            // 3. Check for Hpemde family pattern: open mouth = 3, diving = 5, normal/surface = 0 or 6
            // Protocol specification referenced from LandSandBoat scripts/mixins/families/hpemde.lua
            if (anims.ContainsKey("2dl0") || anims.ContainsKey("2tl0") || (anims.ContainsKey("sp20") && anims.ContainsKey("sp30")))
            {
                if (animationSub == 3) return 1; // Open mouth
                if (animationSub == 5) return 2; // Diving / submerged
                if (animationSub == 0 || animationSub == 6) return 0; // Normal / surfaced
            }

            // 4. Check for Omega bipedal stance pattern: biped = 1, quadruped = 0
            // Protocol specification referenced from LandSandBoat Apollyon / Proto-Omega skill lists
            if (anims.ContainsKey("omeg") || (anims.ContainsKey("1dl0") && anims.ContainsKey("1lk0") && anims.ContainsKey("1un0")))
            {
                if (animationSub == 1) return 1;
                if (animationSub == 0) return 0;
            }

            // 5. Convention-based auto-discovery fallback:
            // If the model possesses mode 1 clips (1dl/1tl) and animationSub is 1, route to stance 1.
            if (animationSub == 1 && (anims.ContainsKey("1dl0") || anims.ContainsKey("1dl") || anims.ContainsKey("1tl0") || anims.ContainsKey("1tl") || anims.ContainsKey("gid0")))
            {
                return 1;
            }

            // If the model possesses mode 2 clips (2dl/2tl) and animationSub is 2, route to stance 2.
            if (animationSub == 2 && (anims.ContainsKey("2dl0") || anims.ContainsKey("2dl") || anims.ContainsKey("2tl0") || anims.ContainsKey("2tl")))
            {
                return 2;
            }

            return 0;
        }

        /// <summary>
        /// Resolves the appropriate continuous animation clip for an entity given its category and stance index.
        /// </summary>
        public static AnimationClip? ResolveTargetClip(EntityModel model, AnimationCategory category, byte stanceIndex = 0)
        {
            if (model == null || model.Animations.Count == 0) return null;
            var anims = model.Animations;

            // Death is universal across stances unless specialized death clips exist
            if (category == AnimationCategory.Death)
            {
                return stanceIndex switch
                {
                    1 => TryGetClip(anims, "de10", "de1", "dd10", "dd1", "ded0", "ded", "dth0", "dth"),
                    2 => TryGetClip(anims, "de20", "de2", "dd20", "dd2", "ded0", "ded", "dth0", "dth"),
                    _ => TryGetClip(anims, "ded0", "ded", "dth0", "dth")
                };
            }

            // Stance 1: Alternate / Standing / Guard / In-Shell
            if (stanceIndex == 1)
            {
                return category switch
                {
                    AnimationCategory.Combat => TryGetClip(anims, "1tl0", "1tl", "gid0", "gid", "gud0", "gud", "btl0", "btl", "idl0", "idl"),
                    AnimationCategory.Walk => TryGetClip(anims, "gdm1", "gdm", "1lk0", "1lk", "1tl0", "1tl", "gid0", "gid", "gud0", "gud", "wlk0", "wlk"),
                    AnimationCategory.Run => TryGetClip(anims, "gdm1", "gdm", "1un0", "1un", "1lk0", "1lk", "1tl0", "1tl", "gid0", "gid", "gud0", "gud", "run0", "run"),
                    _ => TryGetClip(anims, "1dl0", "1dl", "1tl0", "1tl", "gid0", "gid", "gud0", "gud", "idl0", "idl", "std0", "std")
                };
            }

            // Stance 2: Submerged / Diving / Third Form
            if (stanceIndex == 2)
            {
                return category switch
                {
                    AnimationCategory.Combat => TryGetClip(anims, "2tl0", "2tl", "btl0", "btl", "idl0", "idl"),
                    AnimationCategory.Walk => TryGetClip(anims, "2lk0", "2lk", "wlk0", "wlk"),
                    AnimationCategory.Run => TryGetClip(anims, "2un0", "2un", "2lk0", "2lk", "run0", "run"),
                    _ => TryGetClip(anims, "2dl0", "2dl", "2tl0", "2tl", "idl0", "idl", "std0", "std")
                };
            }

            // Stance 0: Standard Default Stance
            return category switch
            {
                AnimationCategory.Combat => TryGetClip(anims, "btl0", "btl", "cmb0", "cmb", "idl0", "idl"),
                AnimationCategory.Walk => TryGetClip(anims, "wlk0", "wlk", "cwlk", "run0", "run", "idl0", "idl"),
                AnimationCategory.Run => TryGetClip(anims, "run0", "run", "crun", "wlk0", "wlk", "idl0", "idl"),
                _ => TryGetClip(anims, "idl0", "idl", "std0", "std")
            };
        }

        /// <summary>
        /// Resolves an explicit one-shot stance transition clip (e.g. sp00 stand-up, sp10 drop-down)
        /// when shifting from fromStance to toStance, or null if no dedicated transition clip exists.
        /// </summary>
        public static AnimationClip? ResolveTransitionClip(EntityModel model, byte fromStance, byte toStance)
        {
            if (model == null || model.Animations.Count == 0 || fromStance == toStance) return null;
            var anims = model.Animations;

            // Transition 0 -> 1 (e.g. Quadruped to Biped, Out to In Shell, Normal to Open Mouth)
            if (fromStance == 0 && toStance == 1)
            {
                return TryGetClip(anims, "sp00", "sp0");
            }

            // Transition 1 -> 0 (e.g. Biped to Quadruped, In to Out of Shell, Open to Closed Mouth)
            if (fromStance == 1 && toStance == 0)
            {
                return TryGetClip(anims, "sp10", "sp1");
            }

            // Transition 0 -> 2 (e.g. Surface to Dive)
            if (fromStance == 0 && toStance == 2)
            {
                return TryGetClip(anims, "sp20", "sp2");
            }

            // Transition 2 -> 0 (e.g. Dive to Surface)
            if (fromStance == 2 && toStance == 0)
            {
                return TryGetClip(anims, "sp30", "sp3");
            }

            return null;
        }

        private static AnimationClip? TryGetClip(IReadOnlyDictionary<string, AnimationClip> anims, params string[] candidateNames)
        {
            foreach (var name in candidateNames)
            {
                if (anims.TryGetValue(name, out var clip))
                {
                    return clip;
                }
            }
            return null;
        }
    }
}
