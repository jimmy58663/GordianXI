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
    public sealed class ProfileItemViewModel : ViewModelBase
    {
        private readonly AccountProfile _profile;
        private readonly SessionRegistry _sessionRegistry;
        private bool _isSelectedForLaunch;
        private bool _isOnline;

        public AccountProfile Profile => _profile;

        public string ProfileName => _profile.ProfileName;
        public string Username => _profile.Username;
        public string CurrentTwoFactorCode => _profile.CurrentTwoFactorCode;

        public bool IsSelectedForLaunch
        {
            get => _isSelectedForLaunch;
            set
            {
                if (SetProperty(ref _isSelectedForLaunch, value))
                {
                    _profile.IsSelectedForLaunch = value;
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

        public ProfileItemViewModel(AccountProfile profile, SessionRegistry? registry = null)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _sessionRegistry = registry ?? SessionRegistry.Default;
            _isSelectedForLaunch = profile.IsSelectedForLaunch;

            RefreshOnlineStatus();
        }

        public void RefreshOnlineStatus()
        {
            IsOnline = _sessionRegistry.IsAccountActive(_profile.Username) ||
                       _sessionRegistry.IsCharacterActive(_profile.ProfileName);
        }
    }
}
