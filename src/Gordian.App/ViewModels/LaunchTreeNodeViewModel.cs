// src/Gordian.App/ViewModels/LaunchTreeNodeViewModel.cs
using System.Collections.ObjectModel;

namespace Gordian.App.ViewModels
{
    /// <summary>
    /// Base ViewModel representing a node in the hierarchical profile launch tree.
    /// Supports both folder containers and leaf account profile nodes.
    /// </summary>
    public abstract class LaunchTreeNodeViewModel : ViewModelBase
    {
        private LaunchFolderViewModel? _parent;
        private bool _isExpanded = true;

        public LaunchFolderViewModel? Parent
        {
            get => _parent;
            internal set => SetProperty(ref _parent, value);
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public abstract string DisplayName { get; }
        public abstract bool? IsSelectedForLaunch { get; set; }
        public abstract bool IsFolder { get; }

        public abstract ObservableCollection<LaunchTreeNodeViewModel>? Children { get; }
    }
}
