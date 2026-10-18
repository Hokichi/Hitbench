using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using osuHitsounder.Core.Entities;
using osuHitsounder.Core.Constants;
using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.Services.ProjectFiles;

public static class ProjectDataSerializer
{
    public static ProjectData Deserialize(string json)
    {
        JObject root;
        try { root = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }); }
        catch (JsonException error) { throw new InvalidDataException("Project JSON is invalid.", error); }

        var pinned = InstrumentMember(root, nameof(ProjectData.IsPinned));
        if (pinned is not null && pinned.Type != JTokenType.Boolean)
            throw new InvalidDataException("Project IsPinned must be a boolean.");
        var changed = InstrumentMember(root, nameof(ProjectData.LastChanged));
        if (changed is not null)
        {
            DateTimeOffset instant;
            if (changed.Type == JTokenType.Date) instant = changed.ToObject<DateTimeOffset>();
            else if (changed.Type != JTokenType.String ||
                !DateTimeOffset.TryParse(changed.Value<string>(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out instant))
                throw new InvalidDataException("Project LastChanged must be a timestamp.");
            root[nameof(ProjectData.LastChanged)] = instant.ToUniversalTime();
        }

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
        var collapsed = InstrumentMember(root, "IsCollapsed");
        if (collapsed is not null && collapsed.Type != JTokenType.Boolean)
            throw new InvalidDataException("Project IsCollapsed must be a boolean.");
        root.Remove("IsCollapsed");
        NormalizeControlPoints(root);

        if (root["Tracks"] is JArray trackNodes)
        {
            foreach (var item in trackNodes)
            {
                if (item is not JObject track) throw new InvalidDataException("Track must be an object.");
                var trackCollapsed = InstrumentMember(track, nameof(Track.IsCollapsed));
                if (trackCollapsed is not null && trackCollapsed.Type != JTokenType.Boolean)
                    throw new InvalidDataException("Track IsCollapsed must be a boolean.");
                if (trackCollapsed is null) track[nameof(Track.IsCollapsed)] = collapsed?.Value<bool>() ?? false;
                var kind = InstrumentMember(track, "Kind");
                if (kind is not null && (kind.Type != JTokenType.String || kind.Value<string>() is not ("Instrumental" or "Sample")))
                    throw new InvalidDataException("Track Kind must be Sample or Instrumental.");
                if (kind?.Value<string>() == "Instrumental") NormalizeInstrument(track);
                if (track["Name"] is { Type: not (JTokenType.String or JTokenType.Null) })
                    throw new InvalidDataException("Track Name must be a string or null.");
                if (track["Name"] is { Type: JTokenType.Null }) track["Name"] = string.Empty;
                if (track["SelectedSampleFileName"] is { Type: not (JTokenType.String or JTokenType.Null) })
                    throw new InvalidDataException("Track SelectedSampleFileName must be a string or null.");
                foreach (var member in track.Properties().Where(p => string.Equals(p.Name, nameof(Track.SampleType), StringComparison.OrdinalIgnoreCase)).ToArray())
                {
                    var value = member.Value;
                    if (value.Type == JTokenType.Null) member.Value = 0;
                    else if (value.Type == JTokenType.Integer && value.Value<long>() is >= 0 and <= 3) { }
                    else if (value.Type == JTokenType.String && Enum.GetNames<osuHitsounder.Core.Enums.SampleType>().Contains(value.Value<string>()))
                        member.Value = (int)Enum.Parse<osuHitsounder.Core.Enums.SampleType>(value.Value<string>()!);
                    else throw new InvalidDataException("Track SampleType must be Normal, Clap, Finish, or Whistle.");
                }
                NormalizeTrackOption(track, nameof(Track.Addition), "None", Track.AdditionOptions);
                foreach (var property in new[] { nameof(Track.IsTrackMuted), nameof(Track.IsTrackSoloed) })
                    if (track.Properties().Any(member => string.Equals(member.Name, property, StringComparison.OrdinalIgnoreCase) &&
                        member.Value.Type != JTokenType.Boolean))
                        throw new InvalidDataException($"Track {property} must be a boolean.");
                if (track["Samples"] is null) continue;
                if (track["Samples"] is not JArray samples || samples.Any(sample => sample is not JObject))
                    throw new InvalidDataException("Track Samples must be an array of objects.");
                NormalizeSamples(track, samples);
            }
        }

        try
        {
            var nodes = (root["Tracks"] as JArray)?.ToArray() ?? [];
            root.Remove("Tracks");
            var result = root.ToObject<ProjectData>() ?? throw new InvalidDataException("Project JSON is empty.");
            result.Tracks = nodes.Select(node => ((JObject)node)["Kind"]?.Value<string>() == "Instrumental"
                ? (Track)node.ToObject<InstrumentalTrack>()! : node.ToObject<Track>()!).ToList();
            return result;
        }
        catch (JsonException error) { throw new InvalidDataException("Project data is invalid.", error); }
    }

    public static string Serialize(ProjectData project, DateTimeOffset? lastChanged = null)
    {
        var root=JObject.FromObject(project);
        root[nameof(ProjectData.LastChanged)] = (lastChanged ?? project.LastChanged).ToUniversalTime();
        var nodes=(JArray)root["Tracks"]!;
        for (var i=0;i<project.Tracks.Count;i++)
            if (project.Tracks[i] is InstrumentalTrack)
            { var node=(JObject)nodes[i]; node["Kind"]="Instrumental"; NormalizeInstrument(node); }
        return root.ToString(Formatting.Indented);
    }

    private static JToken? InstrumentMember(JObject node,string name)
    {
        var members=node.Properties().Where(p=>string.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase)).ToArray();
        if (members.Length>1) throw new InvalidDataException($"Duplicate instrumental field: {name}.");
        if (members.Length==0) return null;
        var value=members[0].Value;
        if (members[0].Name!=name) { members[0].Remove(); node[name]=value; }
        return value;
    }
    private static void NormalizeInstrument(JObject track)
    {
        var folder = InstrumentMember(track, "FolderName");
        if (folder is null) track["FolderName"] = "";
        else if (folder.Type != JTokenType.String || folder.Value<string>() is { Length: > 0 } folderName && SamplePaths.GetFolderNameError(folderName) is not null)
            throw new InvalidDataException("Instrumental FolderName must be a safe single directory name.");
        foreach (var name in new[] { "Addition","SampleType","SelectedSampleFileName","Samples" }) InstrumentMember(track,name);
        track["Addition"]="Soft"; track["SampleType"]=3; track["SelectedSampleFileName"]=null;
        if (track["Samples"] is JArray inherited && inherited.Count==0 || track["Samples"] is null) track["Samples"]=new JArray();
        else throw new InvalidDataException("Instrumental tracks cannot contain sample-grid nodes.");
        var keys=InstrumentMember(track,"KeyFiles");
        if (keys is null) track["KeyFiles"]=keys=new JObject();
        if (keys is not JObject mappings) throw new InvalidDataException("Instrumental KeyFiles must be an object.");
        foreach (var pair in mappings.Properties())
        {
            if (!InstrumentalKeys.TryGetIndex(pair.Name,out _) || pair.Value.Type!=JTokenType.String ||
                SamplePaths.GetRelativePathError(pair.Value.Value<string>()!) is not null)
                throw new InvalidDataException($"Invalid instrumental key mapping: {pair.Name}.");
            pair.Value = pair.Value.Value<string>()!.Replace('\\', '/');
        }
        var notes=InstrumentMember(track,"Notes");
        if (notes is null) track["Notes"]=notes=new JArray();
        if (notes is not JArray list) throw new InvalidDataException("Instrumental Notes must be an array.");
        var seen=new HashSet<(string,int)>();
        foreach (var item in list)
        {
            if (item is not JObject note) throw new InvalidDataException("Instrumental note must be an object.");
            var key=InstrumentMember(note,"Key"); var time=InstrumentMember(note,"Timestamp"); var volume=InstrumentMember(note,"Volume");
            if (key?.Type!=JTokenType.String || !InstrumentalKeys.TryGetIndex(key.Value<string>()!,out _) ||
                !IsIntegerInRange(time,int.MaxValue) ||
                volume is not null && !IsIntegerInRange(volume,100))
                throw new InvalidDataException("Instrumental note needs a valid key, integer timestamp, and volume 0–100.");
            if (!seen.Add((key.Value<string>()!,time!.Value<int>()))) throw new InvalidDataException("Duplicate instrumental note key/timestamp.");
        }
    }

    private static bool IsIntegerInRange(JToken? value,int maximum) =>
        value?.Type==JTokenType.Integer && long.TryParse(value.ToString(Formatting.None),
            System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out var number) &&
        number>=0 && number<=maximum;

    private static void NormalizeControlPoints(JObject root)
    {
        var value = InstrumentMember(root, nameof(ProjectData.ControlPoints));
        if (value is null or { Type: JTokenType.Null })
        { root[nameof(ProjectData.ControlPoints)] = new JArray(); return; }
        if (value is not JArray points) throw new InvalidDataException("ControlPoints must be an array of objects.");
        foreach (var item in points)
        {
            if (item is not JObject point) throw new InvalidDataException("Control point must be an object.");
            var time = InstrumentMember(point, nameof(ControlPoint.Timestamp));
            var volume = InstrumentMember(point, nameof(ControlPoint.Volume));
            var sampleset = InstrumentMember(point, nameof(ControlPoint.Sampleset));
            if (!IsIntegerInRange(time, int.MaxValue)) throw new InvalidDataException("Control point Timestamp must be a nonnegative integer.");
            if (volume is null) point[nameof(ControlPoint.Volume)] = 100;
            else if (!IsIntegerInRange(volume, 100)) throw new InvalidDataException("Control point Volume must be an integer from 0 to 100.");
            if (sampleset is null or { Type: JTokenType.Null }) point[nameof(ControlPoint.Sampleset)] = "Normal";
            else if (sampleset.Type != JTokenType.String || !ControlPoint.SamplesetOptions.Contains(sampleset.Value<string>()!))
                throw new InvalidDataException("Control point Sampleset must be Normal, Soft, or Drum.");
        }
    }

    private static void NormalizeTrackOption(JObject track, string property, string defaultValue, IReadOnlyList<string> options)
    {
        if (track[property] is null or { Type: JTokenType.Null }) track[property] = defaultValue;
        else if (track[property] is not { Type: JTokenType.String } value || !options.Contains(value.Value<string>()!))
            throw new InvalidDataException($"Track {property} must be one of: {string.Join(", ", options)}.");
    }

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
