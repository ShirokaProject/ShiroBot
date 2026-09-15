namespace ShiroBot.SDK.Plugin;

public interface IConsoleLogger
{
    bool IsEnabled { get; set; }
    void Log(string message);
    void Info(string message);
    void Success(string message);
    void Warning(string message);
    void Error(string message);
}
