// src/Gordian.App/ViewModels/ProfileItemViewModel.cs
using System;
using System.Linq;
using Gordian.Core.Network;
using Gordian.Core.Profiles;
using Gordian.Engine.Sessions;

namespace Gordian.App.ViewModels
{
    /// <summary>
    /// ViewModel representing a single account profile in the launch checklist,
    /// tracking its selection state and real-time in-memory online status.
    /// </summary>
    public sealed class ProfileItemViewModel : LaunchTreeNodeViewModel
    {
        private readonly AccountProfile _profile;
        private readonly SessionRegistry _sessionRegistry;
        private bool _isSelectedForLaunch;
        private bool _isOnline;

        public AccountProfile Profile => _profile;

        public override string DisplayName => _profile.ProfileName;
        public override bool IsFolder => false;
        public override System.Collections.ObjectModel.ObservableCollection<LaunchTreeNodeViewModel>? Children => null;

        public string ProfileName => _profile.ProfileName;
        public string CharacterName => _profile.CharacterName;
        public bool HasCharacterSubtitle => !string.IsNullOrWhiteSpace(_profile.CharacterName) || _profile.CharacterSlot > 0;
        public string CharacterSubtitle => !string.IsNullOrWhiteSpace(_profile.CharacterName)
            ? $"Char: {_profile.CharacterName}"
            : $"Char: slot {_profile.CharacterSlot}";
        public string Username => _profile.Username;
        public string CurrentTwoFactorCode => _profile.CurrentTwoFactorCode;

        /// <summary>Whether the card shows the code computed from the profile's authenticator key.</summary>
        public bool ShowsComputedCode => _profile.EffectiveTwoFactorMode == TwoFactorMode.AuthenticatorKey;

        /// <summary>Whether the card has the box for typing the authenticator code before launching.</summary>
        public bool AsksForCode => _profile.EffectiveTwoFactorMode == TwoFactorMode.EnterAtLaunch;

        /// <summary>
        /// The code typed on the card (<see cref="TwoFactorMode.EnterAtLaunch"/>). Digits only, at most 6; memory only.
        /// </summary>
        public string EnteredCode
        {
            get => _profile.EnteredOneTimeCode;
            set
            {
                string digits = new string((value ?? string.Empty).Where(char.IsAsciiDigit).Take(6).ToArray());
                if (digits == _profile.EnteredOneTimeCode) return;
                _profile.EnteredOneTimeCode = digits;
                OnPropertyChanged();
            }
        }

        /// <summary>Re-reads <see cref="EnteredCode"/> after a launch used (and cleared) the typed code.</summary>
        public void RefreshEnteredCode() => OnPropertyChanged(nameof(EnteredCode));

        public override bool? IsSelectedForLaunch
        {
            get => _isSelectedForLaunch;
            set
            {
                bool actual = value ?? false;
                if (SetProperty(ref _isSelectedForLaunch, actual))
                {
                    _profile.IsSelectedForLaunch = actual;
                    Parent?.UpdateCheckStateFromChildren();
                    SelectionChanged?.Invoke(this, this);
                }
            }
        }

        public bool IsOnline
        {
            get => _isOnline;
            private set
            {
                if (SetProperty(ref _isOnline, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }

        public string StatusText => IsOnline ? "Online" : "Offline";
        public string StatusColor => IsOnline ? "#4CAF50" : "#F44336";

        private bool IsLsb => _profile.EffectiveServerType == ProfileServerType.LandSandBoat;

        public string ServerBadgeText => IsLsb
            ? $"LSB ({_profile.EffectiveServerHost}:{_profile.EffectiveServerPort})"
            : "Retail";

        public string ServerBadgeColor => IsLsb ? "#9CDCFE" : "#CE9178";

        /// <summary>Whether the profile plays with its own FFXI install rather than the registry one.</summary>
        public bool HasCustomGameDirectory => !string.IsNullOrWhiteSpace(_profile.GameDirectory);

        public string GameDirectoryTip => $"FFXI install: {_profile.GameDirectory}";

        public System.Windows.Input.ICommand TerminateCommand { get; }
        public System.Windows.Input.ICommand EditCommand { get; }
        public System.Windows.Input.ICommand DeleteCommand { get; }
        public System.Windows.Input.ICommand CopyCommand { get; }
        public System.Windows.Input.ICommand MoveUpCommand { get; }
        public System.Windows.Input.ICommand MoveDownCommand { get; }

        public event EventHandler<ProfileItemViewModel>? EditRequested;
        public event EventHandler<ProfileItemViewModel>? DeleteRequested;
        public event EventHandler<ProfileItemViewModel>? CopyRequested;
        public event EventHandler<ProfileItemViewModel>? MoveUpRequested;
        public event EventHandler<ProfileItemViewModel>? MoveDownRequested;
        public event EventHandler<ProfileItemViewModel>? SelectionChanged;

        public ProfileItemViewModel(AccountProfile profile, SessionRegistry? registry = null)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _sessionRegistry = registry ?? SessionRegistry.Default;
            _isSelectedForLaunch = profile.IsSelectedForLaunch;

            TerminateCommand = new Gordian.App.Common.RelayCommand(Terminate);
            EditCommand = new Gordian.App.Common.RelayCommand(() => EditRequested?.Invoke(this, this));
            DeleteCommand = new Gordian.App.Common.RelayCommand(() => DeleteRequested?.Invoke(this, this));
            CopyCommand = new Gordian.App.Common.RelayCommand(() => CopyRequested?.Invoke(this, this));
            MoveUpCommand = new Gordian.App.Common.RelayCommand(() => MoveUpRequested?.Invoke(this, this));
            MoveDownCommand = new Gordian.App.Common.RelayCommand(() => MoveDownRequested?.Invoke(this, this));

            RefreshOnlineStatus();
        }

        public void Terminate()
        {
            // Only this profile's own session: terminating by account would also end the account's other characters.
            if (_sessionRegistry.TryGetSessionForProfile(_profile.ProfileName, _profile.CharacterName, out var session) && session != null)
            {
                _sessionRegistry.UnregisterSession(session.SessionId);
            }
            RefreshOnlineStatus();
        }

        public void RefreshOnlineStatus()
        {
            IsOnline = _sessionRegistry.IsProfileOnline(_profile.ProfileName, _profile.CharacterName);
        }
    }
}
