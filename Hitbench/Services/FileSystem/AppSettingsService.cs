using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Hitbench.Core.Entities;

namespace Hitbench.Services.FileSystem;

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
            var changed = false;
            if (json["theme"]?.Type != JTokenType.Integer || (json["theme"]!.ToString() is not ("0" or "1")))
            { json["theme"] = 0; changed = true; }
            var volume = json["volume"] is { Type: not JTokenType.Null } token
                ? (int)Math.Clamp((double)token, 0, 100) : 100;
            if (json["volume"]?.Type != JTokenType.Integer || (double)json["volume"]! != volume)
            {
                json["volume"] = volume;
                changed = true;
            }
            if (!json.ContainsKey("osuPath"))
            {
                json["osuPath"] = "";
                changed = true;
            }
            if (!json.ContainsKey("isMaximized"))
            {
                json["isMaximized"] = false;
                changed = true;
            }
            if (!json.ContainsKey("restoredWidth"))
            {
                json["restoredWidth"] = 1280d;
                changed = true;
            }
            if (!json.ContainsKey("restoredHeight"))
            {
                json["restoredHeight"] = 720d;
                changed = true;
            }
            if (changed) await WriteAsync(json, cancellationToken);
            var settings = json.ToObject<AppSettings>(JsonSerializer.Create(new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            }))!;
            settings.OsuPath = string.IsNullOrWhiteSpace(settings.OsuPath) ? "" : settings.OsuPath;
            if (string.IsNullOrWhiteSpace(settings.AsioDriverName)) settings.AsioDriverName = null;
            if (json["isMaximized"]?.Type == JTokenType.Null) settings.IsMaximized = false;
            if (json["restoredWidth"]?.Type == JTokenType.Null) settings.RestoredWidth = 1280;
            if (json["restoredHeight"]?.Type == JTokenType.Null) settings.RestoredHeight = 720;
            return settings;
        }
        finally { gate.Release(); }
    }

    public async Task SaveThemeAsync(int theme, CancellationToken cancellationToken)
    {
        if (theme is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(theme));
        await gate.WaitAsync(cancellationToken);
        try { var json = await ReadAsync(cancellationToken); json["theme"] = theme; await WriteAsync(json, cancellationToken); }
        finally { gate.Release(); }
    }

    public async Task SaveVolumeAsync(int volume, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var json = await ReadAsync(cancellationToken);
            json["volume"] = Math.Clamp(volume, 0, 100);
            await WriteAsync(json, cancellationToken);
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

    public async Task SaveWindowPlacementAsync(bool isMaximized, double restoredWidth, double restoredHeight,
        CancellationToken cancellationToken)
    {
        if (!double.IsFinite(restoredWidth) || restoredWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(restoredWidth));
        if (!double.IsFinite(restoredHeight) || restoredHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(restoredHeight));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var json = await ReadAsync(cancellationToken);
            json["isMaximized"] = isMaximized;
            json["restoredWidth"] = restoredWidth;
            json["restoredHeight"] = restoredHeight;
            await WriteAsync(json, cancellationToken);
        }
        finally { gate.Release(); }
    }

    private async Task<JObject> ReadAsync(CancellationToken token)
    {
        try
        {
            var json = JObject.Parse(await File.ReadAllTextAsync(path, token));
            if (json.TryGetValue("volume", out var volume) && volume.Type != JTokenType.Null &&
                (volume.Type is not (JTokenType.Integer or JTokenType.Float) || !double.IsFinite((double)volume)))
                throw new JsonSerializationException("volume must be a finite number or null.");
            if (json.TryGetValue("asioDriverName", out var driver) && driver.Type is not (JTokenType.String or JTokenType.Null))
                throw new JsonSerializationException("asioDriverName must be a string or null.");
            if (json.TryGetValue("osuPath", out var value) &&
                value.Type is not (JTokenType.String or JTokenType.Null))
                throw new JsonSerializationException("osuPath must be a string or null.");
            if (json.TryGetValue("isMaximized", out value) &&
                value.Type is not (JTokenType.Boolean or JTokenType.Null))
                throw new JsonSerializationException("isMaximized must be a boolean or null.");
            if (json.TryGetValue("restoredWidth", out value) &&
                value.Type is not (JTokenType.Integer or JTokenType.Float or JTokenType.Null))
                throw new JsonSerializationException("restoredWidth must be a number or null.");
            if (json.TryGetValue("restoredHeight", out value) &&
                value.Type is not (JTokenType.Integer or JTokenType.Float or JTokenType.Null))
                throw new JsonSerializationException("restoredHeight must be a number or null.");
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
