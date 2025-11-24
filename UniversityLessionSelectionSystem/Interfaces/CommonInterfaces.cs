using System; 

namespace University.Lms.Ports
{ 
    /// <summary>Clock abstraction for deterministic tests.</summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    /// <summary>Simple logger for verifiable behavior.</summary>
    public interface ILogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
    } 

}
