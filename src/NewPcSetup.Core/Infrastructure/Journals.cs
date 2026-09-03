using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using NewPcSetup.Core.Abstractions;
using NewPcSetup.Core.Models;

namespace NewPcSetup.Core.Infrastructure;

public sealed class InMemoryJournal : IJournal
{
    private readonly List<JournalEntry> _entries = new();
    public void Record(JournalEntry entry) => _entries.Add(entry);
    public IReadOnlyList<JournalEntry> EntriesFor(string taskId) => _entries.Where(e => string.Equals(e.TaskId, taskId, StringComparison.OrdinalIgnoreCase)).ToList();
    public IReadOnlyList<JournalEntry> All => _entries;
}

/// <summary>JSON Lines 追加写，崩溃不丢；构造时加载已有内容以支持续跑。</summary>
public sealed class FileJournal : IJournal
{
    private readonly string _path;
    private readonly List<JournalEntry> _entries = new();
    private readonly object _gate = new();

    public FileJournal(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(path))
        {
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var e = JsonSerializer.Deserialize<JournalEntry>(line, JsonDefaults.Compact);
                    if (e != null) _entries.Add(e);
                }
                catch { /* 损坏行忽略 */ }
            }
        }
    }

    public void Record(JournalEntry entry)
    {
        lock (_gate)
        {
            _entries.Add(entry);
            File.AppendAllText(_path, JsonSerializer.Serialize(entry, JsonDefaults.Compact) + Environment.NewLine, Encoding.UTF8);
        }
    }

    public IReadOnlyList<JournalEntry> EntriesFor(string taskId)
    {
        lock (_gate) return _entries.Where(e => string.Equals(e.TaskId, taskId, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public IReadOnlyList<JournalEntry> All { get { lock (_gate) return _entries.ToList(); } }
}
