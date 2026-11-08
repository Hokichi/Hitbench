using Hitbench.Resources.DataModels;
namespace Hitbench.Resources.Messages;
public sealed record OHsSetAllowDefaultsRequestedMessage(SampleManagementContext Context, bool AllowDefaults);
