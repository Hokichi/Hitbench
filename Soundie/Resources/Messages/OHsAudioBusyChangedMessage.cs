namespace Soundie.Resources.Messages;

public sealed record OHsAudioBusyChangedMessage(Guid OperationId, bool IsBusy, string Phase);
