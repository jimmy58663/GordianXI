// src/Gordian.App/ViewModels/ProfileItemViewModel.cs
using System;
using Gordian.Core.Network;
using Gordian.Core.Profiles;

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
        public bool HasCharacterSubtitle => !string.IsNullOrWhiteSpace(_profile.CharacterName);
        public string CharacterSubtitle => $"Char: {_profile.CharacterName}";
        public string Username => _profile.Username;
        public string CurrentTwoFactorCode => _profile.CurrentTwoFactorCode;

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

        public string ServerBadgeText => !string.IsNullOrWhiteSpace(_profile.ServerHost)
            ? $"Private ({_profile.ServerHost}:{(_profile.ServerPort > 0 ? _profile.ServerPort : 54231)})"
            : "Retail (POL)";

        public string ServerBadgeColor => !string.IsNullOrWhiteSpace(_profile.ServerHost)
            ? "#9CDCFE"
            : "#CE9178";

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
            _sessionRegistry.TerminateSession(_profile.Username);
            _sessionRegistry.TerminateSession(_profile.ProfileName);
            if (!string.IsNullOrWhiteSpace(_profile.CharacterName))
            {
                _sessionRegistry.TerminateSession(_profile.CharacterName);
            }
            RefreshOnlineStatus();
        }

        public void RefreshOnlineStatus()
        {
            IsOnline = _sessionRegistry.IsAccountActive(_profile.Username) ||
                       _sessionRegistry.IsCharacterActive(_profile.ProfileName) ||
                       (!string.IsNullOrWhiteSpace(_profile.CharacterName) && _sessionRegistry.IsCharacterActive(_profile.CharacterName));
        }
    }
}
