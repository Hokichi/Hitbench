using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using osuHitsounder.Core.Entities;

namespace osuHitsounder.Services.ProjectFiles;

public static class ProjectDataSerializer
{
    public static ProjectData Deserialize(string json)
    {
        JObject root;
        try { root = JObject.Parse(json); }
        catch (JsonException error) { throw new InvalidDataException("Project JSON is invalid.", error); }

        if (root["Tracks"] is not null && root["Tracks"] is not JArray)
            throw new InvalidDataException("Project Tracks must be an array.");
        if (root["Tracks"] is null && root["Samples"] is not null && root["Samples"] is not JArray)
            throw new InvalidDataException("Project Samples must be an array.");

        if (root["Tracks"] is null && root["Samples"] is JArray legacy)
        {
            var tracks = new JArray();
            foreach (var item in legacy)
            {
                if (item is not JObject sample) throw new InvalidDataException("Legacy Sample must be an object.");
                if (sample["FileName"] is null && sample["Path"] is not null)
                    sample["FileName"] = sample["Path"]!.DeepClone();
                sample.Remove("Path");
                tracks.Add(new JObject { ["Volume"] = 100, ["Samples"] = new JArray(sample) });
            }
            root["Tracks"] = tracks;
        }
        root.Remove("Samples");

        if (root["Tracks"] is JArray trackNodes)
        {
            foreach (var item in trackNodes)
            {
                if (item is not JObject track) throw new InvalidDataException("Track must be an object.");
                if (track["Samples"] is null) continue;
                if (track["Samples"] is not JArray samples || samples.Any(sample => sample is not JObject))
                    throw new InvalidDataException("Track Samples must be an array of objects.");
            }
        }

        try { return root.ToObject<ProjectData>() ?? throw new InvalidDataException("Project JSON is empty."); }
        catch (JsonException error) { throw new InvalidDataException("Project data is invalid.", error); }
    }

    public static string Serialize(ProjectData project) => JsonConvert.SerializeObject(project, Formatting.Indented);
}
