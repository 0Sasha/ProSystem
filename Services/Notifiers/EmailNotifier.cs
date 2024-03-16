using System.Collections.Concurrent;
using System.Net;
using System.Net.Mail;
using System.Net.NetworkInformation;

namespace ProSystem.Services;

internal class EmailNotifier(Settings settings, AddInformation addInfo) : INotifier
{
    private readonly int Port = 587;
    private readonly string Host = "smtp.gmail.com";
    private readonly Settings Settings = settings;
    private readonly AddInformation AddInfo = addInfo;
    private readonly ConcurrentQueue<string> DataQueue = new();

    private DateTime trigger;

    public void Notify(string data)
    {
        if (DataQueue.IsEmpty || !DataQueue.Last().EndsWith(data)) DataQueue.Enqueue(DateTime.Now + ": " + data);
        if (DateTime.Now > trigger && !DataQueue.IsEmpty)
        {
            trigger = DateTime.Now.AddHours(4);
            _ = SendEmail();
        }
    }

    private async Task SendEmail()
    {
        while (!NetworkInterface.GetIsNetworkAvailable()) await Task.Delay(15000);
        var smtp = new SmtpClient(Host, Port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(Settings.Email, Settings.EmailPassword)
        };

        var body = ConstructBody();
        var message = new MailMessage(Settings.Email, Settings.Email, "Info", body);
        try
        {
            smtp.Send(message);
            AddInfo("Notifier: email is sent.");
        }
        catch (Exception e)
        {
            AddInfo("Notifier: new attempt in 30 minutes: " + e.Message);
            await Task.Delay(TimeSpan.FromMinutes(30));
            for (int i = 0; i < 60; i++)
            {
                try
                {
                    while (!NetworkInterface.GetIsNetworkAvailable()) await Task.Delay(15000);
                    smtp.Send(message);
                    AddInfo("Notifier: email is sent.");
                    break;
                }
                catch (Exception ex)
                {
                    AddInfo("Notifier: email is not sent. New attempt in 6 hours: " + ex.Message);
                    DataQueue.Enqueue(body);
                    trigger = DateTime.MinValue;
                }
                await Task.Delay(TimeSpan.FromHours(6));
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
        var body = "";
        while (!DataQueue.IsEmpty)
        {
            if (DataQueue.TryDequeue(out var part)) body += part + "\n\n";
            else
            {
                AddInfo("Notifier: failed to get object");
                Thread.Sleep(5000);
            }
        }
        return body;
    }
}
