using System;
namespace ProSystem.Services;

public abstract class Notifier
{
    public abstract Action<string> Inform { get; set; }
    public abstract void Notify(string data);
}
