// src/Gordian.Core/Input/InputProfileHub.cs
using System;

namespace Gordian.Core.Input
{
    /// <summary>
    /// The user's input profile (key bindings, mouse and gamepad settings, the gamepad button map), shared by every
    /// multi-boxed character (#314). The desktop shell loads it from <c>input_profile.json</c> and publishes it here;
    /// each <see cref="Network.CharacterSession"/> runs on its own copy (<see cref="CreateSessionCopy"/>) because a
    /// session also applies its server-stored config choices (the Mouse/Cam. page's axis inversion) to its profile, and
    /// one character's config page must not change the others'. When the profile is edited (Controls &amp; Input, or the
    /// in-game gamepad key-assignment list) <see cref="Changed"/> is raised and every session takes a fresh copy.
    /// <para>Used from the UI thread (the input loop and the shell both run there).</para>
    /// </summary>
    public sealed class InputProfileHub
    {
        private InputProfile _profile;

        public InputProfileHub(InputProfile? profile = null)
        {
            _profile = profile ?? InputProfile.CreateCompact();
        }

        /// <summary>The application-wide hub the sessions and the shell share.</summary>
        public static InputProfileHub Default { get; } = new();

        /// <summary>The user's profile. Edit it through <see cref="Edit"/>, or edit and then call <see cref="NotifyChanged"/>.</summary>
        public InputProfile Profile => _profile;

        /// <summary>Raised after the profile was replaced or edited; the argument is whoever made the change (null when unknown).</summary>
        public event Action<object?>? Changed;

        /// <summary>Replaces the profile (the shell after loading <c>input_profile.json</c>).</summary>
        public void SetProfile(InputProfile profile, object? source = null)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Changed?.Invoke(source);
        }

        /// <summary>Edits the profile in place and tells every listener.</summary>
        public void Edit(Action<InputProfile> edit, object? source = null)
        {
            ArgumentNullException.ThrowIfNull(edit);
            edit(_profile);
            Changed?.Invoke(source);
        }

        /// <summary>Tells every listener the profile was edited in place.</summary>
        public void NotifyChanged(object? source = null) => Changed?.Invoke(source);

        /// <summary>A deep copy of the profile for one character's session.</summary>
        public InputProfile CreateSessionCopy() => _profile.Clone();
    }
}
