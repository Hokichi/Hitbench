using Hitbench.Core.Entities;

namespace Hitbench.Resources.DataModels;

public sealed record OHsHitNormalConfiguration(string NormalPath = "", string SoftPath = "", string DrumPath = "")
{
    public static OHsHitNormalConfiguration FromProject(ProjectData project) =>
        new(project.NormalHitNormalPath, project.SoftHitNormalPath, project.DrumHitNormalPath);

    public string GetPath(string sampleset) => sampleset switch
    {
        "Normal" => NormalPath, "Soft" => SoftPath, "Drum" => DrumPath,
        _ => throw new ArgumentException("Invalid sampleset.", nameof(sampleset))
    };

    public void ApplyTo(ProjectData project)
    {
        project.NormalHitNormalPath = NormalPath;
        project.SoftHitNormalPath = SoftPath;
        project.DrumHitNormalPath = DrumPath;
    }

    public OHsHitNormalConfiguration Rewrite(Func<string, string> rewrite) =>
        new(rewrite(NormalPath), rewrite(SoftPath), rewrite(DrumPath));
}
