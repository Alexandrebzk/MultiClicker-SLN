using System;
using System.Diagnostics;
using System.IO;

/// <summary>
/// Trace listener that keeps a single appending stream open instead of
/// re-opening the file for every line. Trace lines are written from hot paths
/// (hook callbacks, the trigger dispatcher), so per-line open/close cost and
/// AV rescans are worth avoiding. Trace's global lock serializes writers.
/// </summary>
public class FileTraceListener : TraceListener
{
    private readonly StreamWriter _writer;

    public FileTraceListener(string filePath)
    {
        try
        {
            _writer = new StreamWriter(filePath, append: true) { AutoFlush = true };
        }
        catch (Exception)
        {
            // Tracing must never take the app down (read-only dir, locked file...).
            _writer = null;
        }
    }

    public override void Write(string message)
    {
        try
        {
            _writer?.Write(message);
        }
        catch (Exception) { }
    }

    public override void WriteLine(string message)
    {
        try
        {
            _writer?.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");
        }
        catch (Exception) { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _writer?.Dispose(); } catch (Exception) { }
        }
        base.Dispose(disposing);
    }
}
