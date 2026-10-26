using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.Services.FileSystem;
using Hitbench.Services.Ui;
using Hitbench.Services.Dialogs;
using Hitbench.Services.ProjectFiles;
using Hitbench.Resources.Messages;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.IO;

using Hitbench.Core.Constants;
using Hitbench.Resources.DataModels;

namespace Hitbench.ViewModels.Shell;

public partial class WorkstationPageVM
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectionMode))]
    private bool isAdditionMode = true;
    partial void OnIsAdditionModeChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCreateSampleNodes));
        PublishInstrumentTimeline();
    }
    public bool IsSelectionMode => !IsAdditionMode;
}
