using Soundie.Core.Enums;
namespace Soundie.Resources.DataModels;
public sealed record OHsExportVoice(int Timestamp, string FileName, int GainPercent, string Sampleset, SampleType Type, int TrackOrder);
