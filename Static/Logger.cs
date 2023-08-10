using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using static ProSystem.MainWindow;

namespace ProSystem;

public static class Logger
{
    private static int busyMethod;
    private static StreamWriter writer;
    private static readonly ConcurrentQueue<string> dataQueue = new();
    private static readonly CultureInfo IC = CultureInfo.InvariantCulture;

    public static void Start(bool subscribeToUnhandledExceptions = false)
    {
        if (!Directory.Exists("Logs")) Directory.CreateDirectory("Logs");
        var path = "Logs/" + DateTime.Today.ToShortDateString() + ".txt";

        writer = new(path, true, System.Text.Encoding.UTF8);
        WriteLog("Start logging");
        writer.Flush();

        if (subscribeToUnhandledExceptions)
        {
            AppDomain.CurrentDomain.UnhandledException += new(WriteLogUnhandledException);
            TaskScheduler.UnobservedTaskException += new(WriteLogTaskException);
        }
    }

    public static void WriteLog(string data)
    {
        dataQueue.Enqueue(DateTime.Now.ToString("dd.MM.yy HH:mm:ss.ffff", IC) + " " + data);
        if (Interlocked.Exchange(ref busyMethod, 1) != 0) return;

        while (dataQueue.TryDequeue(out string dt)) writer.WriteLine(dt);
        writer.Flush();
        Interlocked.Exchange(ref busyMethod, 0);
    }

    public static void Stop()
    {
        WriteLog("Stop logging");
        writer.Close();
        writer.Dispose();
    }

    private static void WriteLogTaskException(object sender, UnobservedTaskExceptionEventArgs args)
    {
        var exceptions = args.Exception.InnerExceptions;
        var data = "Task Exception:";
        foreach (var e in exceptions) data += "\n" + e.Message + "\n" + e.StackTrace;
        Window.AddInfo(data, true, true);
    }

    private static void WriteLogUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        var e = (Exception)args.ExceptionObject;
        var path = "UnhandledException " + DateTime.Now.ToString("dd.MM.yyyy HH.mm.ss", IC) + ".txt";
        var data = e.Message + "\n" + e.StackTrace;
        try { File.WriteAllText(path, data); }
        catch { }

        var smtp = new SmtpClient("smtp.gmail.com", 587);
        var message = new MailMessage(Window.Settings.Email, Window.Settings.Email, "Info", data);
        try
        {
            smtp.EnableSsl = true;
            smtp.Credentials = new NetworkCredential(Window.Settings.Email, Window.Settings.EmailPassword);
            smtp.Send(message);
        }
        finally
        {
            smtp.Dispose();
            message.Dispose();
            WriteLog(data);
        }
    }
}
