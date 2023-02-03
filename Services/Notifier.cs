using System;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using static ProSystem.MainWindow;
namespace ProSystem.Services;

internal static class Notifier
{
    private static DateTime TriggerNotification;
    private static readonly ConcurrentQueue<string> DataQueue = new();

    public static void Notify(string data)
    {
        if (!DataQueue.Contains(data)) DataQueue.Enqueue(data);
        if (DateTime.Now > TriggerNotification && !DataQueue.IsEmpty)
        {
            TriggerNotification = DateTime.Now.AddHours(4);
            Task.Run(() => SendEmail());
        }
    }
    private static void SendEmail()
    {
        while (!NetworkInterface.GetIsNetworkAvailable()) Thread.Sleep(15000);
        SmtpClient smtp = new("smtp.gmail.com", 587)
        {
            EnableSsl = true,
            Credentials = new System.Net.NetworkCredential(MySettings.Email, MySettings.EmailPassword)
        };

        string body = ConstructBody();
        MailMessage message = new(MySettings.Email, MySettings.Email, "Info", body);
        try
        {
            smtp.Send(message);
            AddInfo("Оповещение отправлено.");
        }
        catch (Exception e)
        {
            AddInfo("Повторная попытка отправки оповещения через 10 минут. Исключение: " + e.Message);
            Thread.Sleep(600000);
            try
            {
                while (!NetworkInterface.GetIsNetworkAvailable()) Thread.Sleep(15000);
                smtp.Send(message);
                AddInfo("Оповещение отправлено.");
            }
            catch (Exception ex)
            {
                AddInfo("Оповещение не отправлено. Исключение: " + ex.Message);
                DataQueue.Enqueue(body);
                TriggerNotification = DateTime.MinValue;
            }
        }
        finally
        {
            smtp.Dispose();
            message.Dispose();
        }
    }
    private static string ConstructBody()
    {
        string body = "";
        while (!DataQueue.IsEmpty)
        {
            if (DataQueue.TryDequeue(out string part)) body += part + "\n\n";
            else
            {
                AddInfo("Notifier: не удалось взять объект из очереди.");
                Thread.Sleep(5000);
            }
        }
        return body;
    }
}
