using System;
using University.Lms.Ports;

namespace University.Lms.Domain
{
    /// <summary>
    /// ILogger arayüzünü basitçe System.Console üzerine yazan,
    /// eğitim ve geliştirme ortamlarında kullanılabilecek bir logger implementasyonudur.
    /// Gerçek sistemlerde dosya, database, Application Insights vb. adaptörlerle değiştirilebilir.
    /// </summary>
    public sealed class ConsoleLogger : ILogger
    {
        /// <summary>
        /// Bilgi seviyesindeki log mesajlarını UTC zaman damgası ile konsola yazar.
        /// </summary>
        public void Info(string message)
        {
            if (message == null) return;
            Console.WriteLine($"[INFO ] {DateTime.UtcNow:O} {message}");
        }

        /// <summary>
        /// Uyarı seviyesindeki log mesajlarını UTC zaman damgası ile konsola yazar.
        /// </summary>
        public void Warn(string message)
        {
            if (message == null) return;
            Console.WriteLine($"[WARN ] {DateTime.UtcNow:O} {message}");
        }
        /// <summary>
        /// Uyarı seviyesindeki log mesajlarını UTC zaman damgası ile konsola yazar.
        /// </summary>
        public void Error(string message)
        {
            if (message == null) return;
            Console.WriteLine($"[ERROR ] {DateTime.UtcNow:O} {message}");
        }
    }
}