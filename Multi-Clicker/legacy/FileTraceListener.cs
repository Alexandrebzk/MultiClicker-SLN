using System;
using System.Diagnostics;
using System.IO;

public class FileTraceListener : TraceListener
{
    private readonly string _filePath;

    public FileTraceListener(string filePath)
    {
        _filePath = filePath;
    }

    public override void Write(string message)
    {
        if (!File.Exists(_filePath))
        {
            using (var stream = File.Create(_filePath))
            {

            }
        }
        File.AppendAllText(_filePath, message);
    }

    public override void WriteLine(string message)
    {
        if (!File.Exists(_filePath))
        {
            using (var stream = File.Create(_filePath))
            {

            }
        }
        File.AppendAllText(_filePath, message + Environment.NewLine);
    }
}
