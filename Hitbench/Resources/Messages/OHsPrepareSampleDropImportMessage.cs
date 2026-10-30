using CommunityToolkit.Mvvm.Messaging.Messages;
using Hitbench.Resources.DataModels;
namespace Hitbench.Resources.Messages;

public sealed class OHsPrepareSampleDropImportMessage(SampleManagementContext context) : AsyncRequestMessage<bool>
{
    public SampleManagementContext Context { get; } = context;
}
