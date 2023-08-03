﻿using System;
using System.Collections.Concurrent;
using System.Net.Mail;
using System.Net.NetworkInformation;
using System.Net;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;

namespace ProSystem.Services;

internal class EmailNotifier : INotifier
{
    private string host;
    private string email;
    private string password;
    private readonly Action<string> Inform;

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

    public EmailNotifier(int port, string host, string email, string password, Action<string> inform)
    {
        Port = port;
        Host = host;
        Email = email;
        Password = password;
        Inform = inform;
    }

    public void Notify(string data)
    {
        if (!dataQueue.Contains(data)) dataQueue.Enqueue(DateTime.Now + ": " + data);
        if (DateTime.Now > triggerNotification && !dataQueue.IsEmpty)
        {
            triggerNotification = DateTime.Now.AddHours(4);
            Task.Run(() => SendEmail());
        }
    }

    private async void SendEmail()
    {
        while (!NetworkInterface.GetIsNetworkAvailable()) await Task.Delay(15000);
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
            Inform("Notifier: Повторная попытка через 30 минут. Исключение: " + e.Message);
            await Task.Delay(TimeSpan.FromMinutes(30));
            for (int i = 0; i < 60; i++)
            {
                try
                {
                    while (!NetworkInterface.GetIsNetworkAvailable()) await Task.Delay(15000);
                    smtp.Send(message);
                    Inform("Notifier: Оповещение отправлено.");
                    break;
                }
                catch (Exception ex)
                {
                    Inform("Notifier: Оповещение не отправлено. Повторная попытка через 6 часов. Исключение: " + ex.Message);
                    dataQueue.Enqueue(body);
                    triggerNotification = DateTime.MinValue;
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