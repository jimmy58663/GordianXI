// src/Gordian.App/ViewModels/LaunchFolderViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Gordian.App.Common;

namespace Gordian.App.ViewModels
{
    /// <summary>
    /// ViewModel representing a folder container node in the launch tree.
    /// Manages nested folders/profiles and coordinates tri-state cascading checkboxes.
    /// </summary>
    public sealed class LaunchFolderViewModel : LaunchTreeNodeViewModel
    {
        private string _name;
        private string _folderPath;
        private bool? _isSelectedForLaunch = false;
        private bool _isUpdatingSelection;

        public override string DisplayName => _name;

        public string Name
        {
            get => _name;
            set
            {
                if (SetProperty(ref _name, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string FolderPath
        {
            get => _folderPath;
            set => SetProperty(ref _folderPath, value);
        }

        public override bool IsFolder => true;

        public override ObservableCollection<LaunchTreeNodeViewModel> Children { get; } = new();

        public override bool? IsSelectedForLaunch
        {
            get => _isSelectedForLaunch;
            set
            {
                if (_isSelectedForLaunch != value)
                {
                    _isSelectedForLaunch = value;
                    OnPropertyChanged(nameof(IsSelectedForLaunch));

                    // If toggled directly by user (true or false), cascade to all descendants
                    if (!_isUpdatingSelection && value.HasValue)
                    {
                        SetChildrenSelection(value.Value);
                        Parent?.UpdateCheckStateFromChildren();
                    }
                }
            }
        }

        private bool _isRenaming;
        private string _renameText = string.Empty;

        public bool IsRenaming
        {
            get => _isRenaming;
            set => SetProperty(ref _isRenaming, value);
        }

        public string RenameText
        {
            get => _renameText;
            set => SetProperty(ref _renameText, value);
        }

        public ICommand ToggleExpandedCommand { get; }
        public ICommand DeleteFolderCommand { get; }
        public ICommand BeginRenameCommand { get; }
        public ICommand CommitRenameCommand { get; }
        public ICommand CancelRenameCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }

        public event EventHandler<LaunchFolderViewModel>? DeleteRequested;
        public event EventHandler<string>? RenameCommitted;
        public event EventHandler<LaunchFolderViewModel>? MoveUpRequested;
        public event EventHandler<LaunchFolderViewModel>? MoveDownRequested;

        public LaunchFolderViewModel(string name, string folderPath, LaunchFolderViewModel? parent = null)
        {
            _name = name;
            _folderPath = folderPath;
            _renameText = name;
            Parent = parent;

            ToggleExpandedCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
            DeleteFolderCommand = new RelayCommand(() => DeleteRequested?.Invoke(this, this));
            BeginRenameCommand = new RelayCommand(BeginRename);
            CommitRenameCommand = new RelayCommand(CommitRename);
            CancelRenameCommand = new RelayCommand(CancelRename);
            MoveUpCommand = new RelayCommand(() => MoveUpRequested?.Invoke(this, this));
            MoveDownCommand = new RelayCommand(() => MoveDownRequested?.Invoke(this, this));
        }

        public void BeginRename()
        {
            RenameText = Name;
            IsRenaming = true;
        }

        public void CommitRename()
        {
            if (!IsRenaming) return;
            string target = RenameText.Trim();
            IsRenaming = false;
            if (!string.IsNullOrWhiteSpace(target) && !string.Equals(target, Name, StringComparison.Ordinal))
            {
                RenameCommitted?.Invoke(this, target);
            }
        }

        public void CancelRename()
        {
            IsRenaming = false;
            RenameText = Name;
        }

        public void AddChild(LaunchTreeNodeViewModel node)
        {
            node.Parent = this;
            Children.Add(node);
            UpdateCheckStateFromChildren();
        }

        public void RemoveChild(LaunchTreeNodeViewModel node)
        {
            if (Children.Remove(node))
            {
                node.Parent = null;
                UpdateCheckStateFromChildren();
            }
        }

        private void SetChildrenSelection(bool isSelected)
        {
            _isUpdatingSelection = true;
            try
            {
                foreach (var child in Children)
                {
                    if (child is LaunchFolderViewModel folder)
                    {
                        folder.SetChildrenSelection(isSelected);
                        folder._isSelectedForLaunch = isSelected;
                        folder.OnPropertyChanged(nameof(IsSelectedForLaunch));
                    }
                    else if (child is ProfileItemViewModel profile)
                    {
                        profile.IsSelectedForLaunch = isSelected;
                    }
                }
            }
            finally
            {
                _isUpdatingSelection = false;
            }
        }

        public void UpdateCheckStateFromChildren()
        {
            if (_isUpdatingSelection) return;

            _isUpdatingSelection = true;
            try
            {
                int totalLeafProfiles = 0;
                int selectedLeafProfiles = 0;
                bool hasIndeterminate = false;

                CountLeafProfiles(this, ref totalLeafProfiles, ref selectedLeafProfiles, ref hasIndeterminate);

                bool? newState;
                if (totalLeafProfiles == 0)
                {
                    newState = false;
                }
                else if (hasIndeterminate || (selectedLeafProfiles > 0 && selectedLeafProfiles < totalLeafProfiles))
                {
                    newState = null; // Indeterminate
                }
                else if (selectedLeafProfiles == totalLeafProfiles)
                {
                    newState = true;
                }
                else
                {
                    newState = false;
                }

                if (_isSelectedForLaunch != newState)
                {
                    _isSelectedForLaunch = newState;
                    OnPropertyChanged(nameof(IsSelectedForLaunch));
                }

                Parent?.UpdateCheckStateFromChildren();
            }
            finally
            {
                _isUpdatingSelection = false;
            }
        }

        private static void CountLeafProfiles(LaunchFolderViewModel folder, ref int total, ref int selected, ref bool hasIndeterminate)
        {
            foreach (var child in folder.Children)
            {
                if (child is LaunchFolderViewModel subFolder)
                {
                    if (subFolder.IsSelectedForLaunch == null)
                    {
                        hasIndeterminate = true;
                    }
                    CountLeafProfiles(subFolder, ref total, ref selected, ref hasIndeterminate);
                }
                else if (child is ProfileItemViewModel profile)
                {
                    total++;
                    if (profile.IsSelectedForLaunch == true)
                    {
                        selected++;
                    }
                }
            }
        }

        /// <summary>
        /// Recursively counts all leaf profile items contained within this folder and its subfolders.
        /// </summary>
        public int CountDescendantProfiles()
        {
            int count = 0;
            foreach (var child in Children)
            {
                if (child is ProfileItemViewModel)
                {
                    count++;
                }
                else if (child is LaunchFolderViewModel subFolder)
                {
                    count += subFolder.CountDescendantProfiles();
                }
            }
            return count;
        }

        /// <summary>
        /// Recursively retrieves all leaf profile items contained within this folder and its subfolders.
        /// </summary>
        public System.Collections.Generic.List<ProfileItemViewModel> GetDescendantProfiles()
        {
            var list = new System.Collections.Generic.List<ProfileItemViewModel>();
            CollectDescendantProfiles(this, list);
            return list;
        }

        private static void CollectDescendantProfiles(LaunchFolderViewModel folder, System.Collections.Generic.List<ProfileItemViewModel> list)
        {
            foreach (var child in folder.Children)
            {
                if (child is ProfileItemViewModel profile)
                {
                    list.Add(profile);
                }
                else if (child is LaunchFolderViewModel subFolder)
                {
                    CollectDescendantProfiles(subFolder, list);
                }
            }
        }
    }
}
