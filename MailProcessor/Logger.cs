using System;
using System.IO;

/// <summary>
/// Уровень логирования.
/// </summary>
public enum LogLevel
{
    Error = 0,
    Info = 1,
    Debug = 2
}

/// <summary>
/// Централизованный логгер с поддержкой уровней логирования.
/// Пишет одновременно в консоль и в файл.
/// </summary>
public static class Logger
{
    private static string _logPath = string.Empty;
    private static LogLevel _logLevel = LogLevel.Info;
    private static readonly object _lock = new object();

    public static void Initialize(string logPath, LogLevel logLevel = LogLevel.Info)
    {
        _logPath = logPath;
        _logLevel = logLevel;

        string dir = Path.GetDirectoryName(_logPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    public static LogLevel CurrentLevel => _logLevel;

    public static void Info(string message)
    {
        if (_logLevel >= LogLevel.Info)
            Write("INFO", message);
    }

    public static void Info(string format, params object[] args)
    {
        if (_logLevel >= LogLevel.Info)
            Write("INFO", string.Format(format, args));
    }

    public static void Error(string message)
    {
        Write("ERROR", message);
    }

    public static void Error(string format, params object[] args)
    {
        Write("ERROR", string.Format(format, args));
    }

    public static void Debug(string message)
    {
        if (_logLevel >= LogLevel.Debug)
            Write("DEBUG", message);
    }

    public static void Debug(string format, params object[] args)
    {
        if (_logLevel >= LogLevel.Debug)
            Write("DEBUG", string.Format(format, args));
    }

    private static void Write(string level, string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";
        Console.WriteLine(line);

        if (!string.IsNullOrEmpty(_logPath))
        {
            lock (_lock)
            {
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
        }
    }
}
