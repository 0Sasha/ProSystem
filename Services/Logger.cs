using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static ProSystem.MainWindow;
namespace ProSystem.Services;

public static class Logger
{
    private static StreamWriter Writer;
    public static void StartLogging(bool subscribe = false)
    {
        if (!Directory.Exists("Logs")) Directory.CreateDirectory("Logs");
        string Path = "Logs/" + DateTime.Today.ToShortDateString() + ".txt";

        Writer = new StreamWriter(Path, true, System.Text.Encoding.UTF8);
        WriteLogSystem("Start logging");
        Writer.Flush();

        if (subscribe)
        {
            AppDomain CurrentDomain = AppDomain.CurrentDomain;
            CurrentDomain.UnhandledException += new(WriteLogUnhandledException);
            TaskScheduler.UnobservedTaskException += new(WriteLogTaskException);
        }
    }
    public static void WriteLogSystem(string Data)
    {
        Writer.WriteLine(DateTime.Now.ToString("dd.MM.yy HH:mm:ss.ffff", IC) + " " + Data);
        Writer.Flush();
    }
    public static void StopLogging()
    {
        WriteLogSystem("Stop logging");
        Writer.Close();
    }
    public static void WriteLogTaskException(object sender, UnobservedTaskExceptionEventArgs args)
    {
        Exception[] MyExceptions = args.Exception.InnerExceptions.ToArray();
        string Data = "Task Exception:";
        foreach (Exception e in MyExceptions) Data += "\n" + e.Message + "\n" + e.StackTrace;
        AddInfo(Data, true, true);
    }
    public static void WriteLogUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        Exception e = (Exception)args.ExceptionObject;
        string Path = "UnhandledException " + DateTime.Now.ToString("dd.MM.yyyy HH.mm.ss", IC) + ".txt";
        string Data = e.Message + "\n" + e.StackTrace;
        try { File.WriteAllText(Path, Data); }
        catch { }

        System.Net.Mail.SmtpClient Smtp = new("smtp.gmail.com", 587);
        System.Net.Mail.MailMessage Message = new(MySettings.Email, MySettings.Email, "Info", Data);
        try
        {
            Smtp.EnableSsl = true;
            Smtp.Credentials = new System.Net.NetworkCredential(MySettings.Email, MySettings.EmailPassword);
            Smtp.Send(Message);
        }
        finally
        {
            Smtp.Dispose();
            Message.Dispose();
            WriteLogSystem(Data);
        }
    }
}
