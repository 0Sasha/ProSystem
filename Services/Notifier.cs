using System;
using System.Net;
using System.Net.Mail;
using System.Net.NetworkInformation;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
namespace ProSystem.Services;

public abstract class Notifier
{
    public abstract Action<string> Inform { get; set; }
    public abstract void Notify(string data);
}

internal class EmailNotifier : Notifier
{
    private string host;
    private string email;
    private string password;
    private Action<string> inform;

    private DateTime triggerNotification;
    private readonly ConcurrentQueue<string> dataQueue = new();

    public int Port { get; set; }
    public string Host
    {
        get => host;
        set => host = value == null || value.Length == 0 ? throw new ArgumentException("Null or empty", nameof(value)) : value;
    }
    public string Email
    {
        get => email;
        set => email = value == null || value.Length == 0 ? throw new ArgumentException("Null or empty", nameof(value)) : value;
    }
    public string Password
    {
        get => password;
        set => password = value == null || value.Length == 0 ? throw new ArgumentException("Null or empty", nameof(value)) : value;
    }
    public override Action<string> Inform
    {
        get => inform;
        set => inform = value ?? throw new ArgumentNullException(nameof(value));
    }

    public EmailNotifier(int port, string host, string email, string password, Action<string> inform)
    {
        Port = port;
        Host = host;
        Email = email;
        Password = password;
        Inform = inform;
    }

    public override void Notify(string data)
    {
        if (!dataQueue.Contains(data)) dataQueue.Enqueue(data);
        if (DateTime.Now > triggerNotification && !dataQueue.IsEmpty)
        {
            triggerNotification = DateTime.Now.AddHours(4);
            Task.Run(() => SendEmail());
        }
    }

    private void SendEmail()
    {
        while (!NetworkInterface.GetIsNetworkAvailable()) Thread.Sleep(15000);
        SmtpClient smtp = new(Host, Port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(Email, Password)
        };

        string body = ConstructBody();
        MailMessage message = new(Email, Email, "Info", body);
        try
        {
            smtp.Send(message);
            Inform("Notifier: Оповещение отправлено.");
        }
        catch (Exception e)
        {
            Inform("Notifier: Повторная попытка отправки оповещения через 10 минут. Исключение: " + e.Message);
            Thread.Sleep(600000);
            try
            {
                while (!NetworkInterface.GetIsNetworkAvailable()) Thread.Sleep(15000);
                smtp.Send(message);
                Inform("Notifier: Оповещение отправлено.");
            }
            catch (Exception ex)
            {
                Inform("Notifier: Оповещение не отправлено. Исключение: " + ex.Message);
                dataQueue.Enqueue(body);
                triggerNotification = DateTime.MinValue;
            }
        }
        finally
        {
            smtp.Dispose();
            message.Dispose();
        }
    }

    private string ConstructBody()
    {
        string body = "";
        while (!dataQueue.IsEmpty)
        {
            if (dataQueue.TryDequeue(out string part)) body += part + "\n\n";
            else
            {
                Inform("Notifier: Не удалось взять объект из очереди.");
                Thread.Sleep(5000);
            }
        }
        return body;
    }
}
