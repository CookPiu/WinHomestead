using System;
using System.IO;
using System.Text;
using System.Text.Json;
using NewPcSetup.Core.Models;

namespace NewPcSetup.Core.Infrastructure;

/// <summary>%ProgramData%\NewPcSetup 下的 JSON 持久化。</summary>
public sealed class StateStore
{
    public StateStore(string? baseDir = null)
    {
        BaseDir = baseDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NewPcSetup");
        Directory.CreateDirectory(BaseDir);
        LogDir = Path.Combine(BaseDir, "logs");
        Directory.CreateDirectory(LogDir);
    }

    public string BaseDir { get; }
    public string LogDir { get; }

    public string PathFor(string fileName) => Path.Combine(BaseDir, fileName);

    public void Save<T>(string fileName, T value)
        => File.WriteAllText(PathFor(fileName), JsonSerializer.Serialize(value, JsonDefaults.Options), Encoding.UTF8);

    public T? Load<T>(string fileName) where T : class
    {
        var p = PathFor(fileName);
        if (!File.Exists(p)) return null;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(p, Encoding.UTF8), JsonDefaults.Options); }
        catch { return null; }
    }

    public AppState LoadState() => Load<AppState>("state.json") ?? new AppState(null, false);
    public void SaveState(AppState state) => Save("state.json", state);
}
