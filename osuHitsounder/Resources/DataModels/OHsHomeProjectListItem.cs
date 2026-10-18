namespace osuHitsounder.Resources.DataModels;

public sealed record OHsHomeProjectListItem
{
    private OHsHomeProjectListItem(string? heading, FolderEntry? project) { Heading = heading; Project = project; }
    public string? Heading { get; }
    public FolderEntry? Project { get; }
    public bool IsHeading => Heading is not null;
    public static OHsHomeProjectListItem ForHeading(string heading) => new(heading, null);
    public static OHsHomeProjectListItem ForProject(FolderEntry project) => new(null, project);
}
