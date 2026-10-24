using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Onset.Core.Entities;
using Onset.Services;
using Onset.Services.FileSystem;
using Onset.Services.Ui;
using Onset.Services.Dialogs;
using Onset.Services.ProjectFiles;
using Onset.Resources.Messages;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.IO;

using Onset.Core.Constants;
using Onset.Resources.DataModels;

namespace Onset.ViewModels.Shell;

public partial class WorkstationPageVM
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectionMode))]
    private bool isAdditionMode = true;
    partial void OnIsAdditionModeChanged(bool value) => PublishInstrumentTimeline();
    public bool IsSelectionMode => !IsAdditionMode;
}
