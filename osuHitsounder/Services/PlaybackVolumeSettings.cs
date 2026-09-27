namespace osuHitsounder.Services;

public sealed class PlaybackVolumeSettings
{
    private int applicationVolume = 100;
    private int samplesVolume = 100;

    public int ApplicationVolume
    {
        get => applicationVolume;
        set => applicationVolume = Math.Clamp(value, 0, 100);
    }

    public int SamplesVolume
    {
        get => samplesVolume;
        set => samplesVolume = Math.Clamp(value, 0, 100);
    }
}
