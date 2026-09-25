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
                if (track["SelectedSampleFileName"] is { Type: not (JTokenType.String or JTokenType.Null) })
                    throw new InvalidDataException("Track SelectedSampleFileName must be a string or null.");
                if (track["Samples"] is null) continue;
                if (track["Samples"] is not JArray samples || samples.Any(sample => sample is not JObject))
                    throw new InvalidDataException("Track Samples must be an array of objects.");
                NormalizeSamples(track, samples);
            }
        }

        try { return root.ToObject<ProjectData>() ?? throw new InvalidDataException("Project JSON is empty."); }
        catch (JsonException error) { throw new InvalidDataException("Project data is invalid.", error); }
    }

    public static string Serialize(ProjectData project) => JsonConvert.SerializeObject(project, Formatting.Indented);

    private static void NormalizeSamples(JObject track, JArray samples)
    {
        var normalized = new JArray();
        foreach (var token in samples.Cast<JObject>())
        {
            if (track["SelectedSampleFileName"] is null && token["FileName"] is { Type: JTokenType.String } fileName)
                track["SelectedSampleFileName"] = fileName.DeepClone();
            if (token["Timestamp"] is not null)
            {
                normalized.Add(token);
                continue;
            }
            if (token["Timestamps"] is null)
            {
                normalized.Add(token);
                continue;
            }
            if (token["Timestamps"] is not JArray timestamps)
                throw new InvalidDataException("Sample Timestamps must be an array.");
            foreach (var timestamp in timestamps)
            {
                if (timestamp.Type != JTokenType.Integer)
                    throw new InvalidDataException("Sample Timestamps must contain integers.");
                var copy = (JObject)token.DeepClone();
                copy.Remove("Timestamps");
                copy["Timestamp"] = timestamp.DeepClone();
                normalized.Add(copy);
            }
        }
        track["Samples"] = normalized;
    }
}
