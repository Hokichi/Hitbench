using Hitbench.Resources.DataModels;
namespace Hitbench.Resources.Messages;
public sealed record AddSampleTrackRequestedMessage(OHsSampleTrackDragData? Sample=null);
