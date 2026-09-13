using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using osuHitsounder.Core.Entities;

namespace osuHitsounder.Services.FileSystem;

public sealed class AppSettingsService(string applicationDirectory) : IAppSettingsService, IDisposable
{
    private readonly string path = Path.Combine(applicationDirectory, "appsettings.json");
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var json = await ReadAsync(cancellationToken);
            if (!json.ContainsKey("osuPath"))
            {
                json["osuPath"] = "";
                await WriteAsync(json, cancellationToken);
            }
            var settings = json.ToObject<AppSettings>(JsonSerializer.Create(new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            }))!;
            settings.OsuPath = string.IsNullOrWhiteSpace(settings.OsuPath) ? "" : settings.OsuPath;
            return settings;
        }
        finally { gate.Release(); }
    }

    public async Task SaveOsuPathAsync(string osuPath, CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(osuPath))
            throw new ArgumentException("The installation path must be absolute.", nameof(osuPath));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var json = await ReadAsync(cancellationToken);
            json["osuPath"] = osuPath;
            await WriteAsync(json, cancellationToken);
        }
        finally { gate.Release(); }
    }

    private async Task<JObject> ReadAsync(CancellationToken token)
    {
        try
        {
            var json = JObject.Parse(await File.ReadAllTextAsync(path, token));
            if (json.TryGetValue("osuPath", out var value) &&
                value.Type is not (JTokenType.String or JTokenType.Null))
                throw new JsonSerializationException("osuPath must be a string or null.");
            return json;
        }
        catch (FileNotFoundException) { return new JObject(); }
        catch (JsonException error)
        {
            throw new IOException($"Invalid settings in {path}: {error.Message}", error);
        }
    }

    private async Task WriteAsync(JObject json, CancellationToken token)
    {
        var temporary = Path.Combine(applicationDirectory, $".appsettings.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, json.ToString(Formatting.Indented), token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error)
        {
            try { File.Delete(temporary); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                throw new IOException($"Unable to save {path} or remove temporary file {temporary}.",
                    new AggregateException(error, cleanup));
            }
            if (error is OperationCanceledException) throw;
            throw new IOException($"Unable to save settings in {path}: {error.Message}", error);
        }
    }

    public void Dispose() => gate.Dispose();
}

