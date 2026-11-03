using System;

namespace NewCom.Runtime;

/// <summary>Generic event args raised by <see cref="NewComControlHost.EventOccurred"/>.</summary>
public class NewComEventArgs : EventArgs
{
    public string EventName { get; }
    public string ArgsJson { get; }
    public NewComEventArgs(string eventName, string argsJson)
    {
        EventName = eventName;
        ArgsJson = argsJson;
    }
}