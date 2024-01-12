using System.Collections.Concurrent;
using System.Globalization;
using System.IO;

namespace ProSystem.Services;

public class Logger
{
    private int occupied;
    private StreamWriter writer;

    private readonly AddInformation AddInfo;
    private readonly ConcurrentQueue<string> DataQueue = new();
    private readonly CultureInfo IC = CultureInfo.InvariantCulture;

    public Logger(AddInformation addInfo)
    {
        AddInfo = addInfo;

        if (!Directory.Exists("Logs")) Directory.CreateDirectory("Logs");
        var path = "Logs/" + DateTime.Today.ToShortDateString() + ".txt";

        writer = new(path, true, System.Text.Encoding.UTF8);
        WriteLog("Start logging");

        TaskScheduler.UnobservedTaskException += LogTaskException;
        AppDomain.CurrentDomain.UnhandledException += LogUnhandledException;
    }

    public void Start()
    {
        if (!Directory.Exists("Logs")) Directory.CreateDirectory("Logs");
        var path = "Logs/" + DateTime.Today.ToShortDateString() + ".txt";

        writer.Dispose();
        writer = new(path, true, System.Text.Encoding.UTF8);
        WriteLog("Start logging");
        writer.Flush();
    }

    public void WriteLog(string data)
    {
        DataQueue.Enqueue(DateTime.Now.ToString("dd.MM.yy HH:mm:ss.ffff", IC) + " " + data);
        if (Interlocked.Exchange(ref occupied, 1) != 0) return;

        while (DataQueue.TryDequeue(out var dt)) writer.WriteLine(dt);
        writer.Flush();
        Interlocked.Exchange(ref occupied, 0);
    }

    public void Stop()
    {
        WriteLog("Stop logging");
        writer.Close();
        writer.Dispose();
    }

    private void LogTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        var exceptions = args.Exception.InnerExceptions;
        var data = "Task Exception:";
        foreach (var e in exceptions) data += "\n" + e.Message + "\n" + e.StackTrace;
        writer.WriteLine(data);
        AddInfo(data, true, true);
    }

    private void LogUnhandledException(object? sender, UnhandledExceptionEventArgs args)
    {
        var e = (Exception)args.ExceptionObject;
        var path = "UnhandledException " + DateTime.Now.ToString("dd.MM.yyyy HH.mm.ss", IC) + ".txt";
        var data = e.Message + "\n" + e.StackTrace;
        try { File.WriteAllText(path, data); }
        catch { }
        try { writer.WriteLine(data); }
        catch { }
        try { AddInfo(data, true, true); }
        catch { }
    }
}
