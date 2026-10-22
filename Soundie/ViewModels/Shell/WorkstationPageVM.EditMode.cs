using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Soundie.Core.Entities;
using Soundie.Services;
using Soundie.Services.FileSystem;
using Soundie.Services.Ui;
using Soundie.Services.Dialogs;
using Soundie.Services.ProjectFiles;
using Soundie.Resources.Messages;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.IO;

using Soundie.Core.Constants;
using Soundie.Resources.DataModels;

namespace Soundie.ViewModels.Shell;

public partial class WorkstationPageVM
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectionMode))]
    private bool isAdditionMode = true;
    partial void OnIsAdditionModeChanged(bool value) => PublishInstrumentTimeline();
    public bool IsSelectionMode => !IsAdditionMode;
}
