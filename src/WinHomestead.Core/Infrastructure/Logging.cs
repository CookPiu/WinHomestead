using System;
using System.IO;
using System.Text;
using WinHomestead.Core.Abstractions;

namespace WinHomestead.Core.Infrastructure;

public sealed class NullLogger : ILogger
{
    public static readonly NullLogger Instance = new();
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }
}

/// <summary>按天一个文件，单文件超过 5 MB 时截断重写。</summary>
public sealed class FileLogger : ILogger
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private readonly string _dir;
    private readonly object _gate = new();

    public FileLogger(string dir) { _dir = dir; Directory.CreateDirectory(dir); }

    public string CurrentFile => Path.Combine(_dir, $"app-{DateTime.Now:yyyyMMdd}.log");

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message, Exception? exception = null)
        => Write("ERROR", exception == null ? message : message + Environment.NewLine + exception);

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
        lock (_gate)
        {
            try
            {
                var file = CurrentFile;
                if (File.Exists(file) && new FileInfo(file).Length > MaxBytes) File.WriteAllText(file, string.Empty);
                File.AppendAllText(file, line, Encoding.UTF8);
            }
            catch { /* 日志失败不影响主流程 */ }
        }
    }
}
