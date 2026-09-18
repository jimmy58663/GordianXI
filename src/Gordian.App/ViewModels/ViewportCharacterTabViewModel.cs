// src/Gordian.App/ViewModels/ViewportCharacterTabViewModel.cs
using System;
using System.Windows.Input;
using Gordian.App.Common;
using Gordian.Core.Network;

namespace Gordian.App.ViewModels
{
    /// <summary>
    /// ViewModel representing a single character session tab inside the 3D viewport window.
    /// </summary>
    public sealed class ViewportCharacterTabViewModel : ViewModelBase
    {
        private readonly CharacterSession _session;
        private bool _isActive;
        private bool _isPoppedOut;

        public ViewportCharacterTabViewModel(
            CharacterSession session,
            Action<ViewportCharacterTabViewModel> onSelect,
            Action<ViewportCharacterTabViewModel> onPopOut)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            SelectTabCommand = new RelayCommand(() => onSelect(this));
            PopOutCommand = new RelayCommand(() => onPopOut(this));
        }

        public CharacterSession Session => _session;

        public Guid SessionId => _session.SessionId;

        public string CharacterName => string.IsNullOrWhiteSpace(_session.CharacterName)
            ? $"Character ({_session.CharacterId})"
            : _session.CharacterName;

        public string JobDisplay
        {
            get
            {
                var lp = _session.LocalPlayer;
                if (lp.MainJob == Gordian.Core.Network.Packets.JobId.None)
                {
                    return "Connecting...";
                }

                if (lp.SubJob != Gordian.Core.Network.Packets.JobId.None && lp.SubJobLevel > 0)
                {
                    return $"{lp.MainJob}{lp.MainJobLevel}/{lp.SubJob}{lp.SubJobLevel}";
                }

                return $"{lp.MainJob}{lp.MainJobLevel}";
            }
        }

        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        public bool IsPoppedOut
        {
            get => _isPoppedOut;
            set => SetProperty(ref _isPoppedOut, value);
        }

        public double HpPercent
        {
            get
            {
                var lp = _session.LocalPlayer;
                return lp.MaxHp > 0 ? Math.Clamp((double)lp.CurrentHp / lp.MaxHp * 100.0, 0.0, 100.0) : 100.0;
            }
        }

        public string VitalsSummary
        {
            get
            {
                var lp = _session.LocalPlayer;
                return lp.MaxHp > 0 ? $"HP {lp.CurrentHp}/{lp.MaxHp}" : "Connecting...";
            }
        }

        public ICommand SelectTabCommand { get; }
        public ICommand PopOutCommand { get; }

        public void RefreshDisplay()
        {
            OnPropertyChanged(nameof(CharacterName));
            OnPropertyChanged(nameof(JobDisplay));
            OnPropertyChanged(nameof(HpPercent));
            OnPropertyChanged(nameof(VitalsSummary));
        }
    }
}
