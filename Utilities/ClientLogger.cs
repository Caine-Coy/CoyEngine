using CoyEngine.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoyEngine.Utilities
{
    public static class ClientLogger
    {
        private static readonly object _lock = new object();
        private static readonly LinkedList<string> _lines = new LinkedList<string>();
        private const int MaxLines = 500;
        private static readonly string _logFilePath = Path.Combine(
            Environment.CurrentDirectory, "birth_of_dog.log");

        public static void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            lock (_lock)
            {
                _lines.AddLast(line);
                if (_lines.Count > MaxLines) _lines.RemoveFirst();
            }
            // Always write to stdout so terminal captures it
            Console.WriteLine(line);
            System.Diagnostics.Debug.WriteLine(line);
        }

        public static void LogError(string message, Exception? ex = null)
        {
            var line = ex != null
                ? $"[{DateTime.Now:HH:mm:ss.fff}] ERROR: {message} — {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"
                : $"[{DateTime.Now:HH:mm:ss.fff}] ERROR: {message}";
            lock (_lock)
            {
                _lines.AddLast(line);
                if (_lines.Count > MaxLines) _lines.RemoveFirst();
            }
            Console.Error.WriteLine(line);
            System.Diagnostics.Debug.WriteLine(line);
        }

        public static string[] GetRecent(int count = 10)
        {
            lock (_lock)
            {
                return _lines.Reverse().Take(count).ToArray();
            }
        }

        /// <summary>
        /// Flush all log lines to a file on disk (call on crash for post-mortem)
        /// </summary>
        public static void FlushToFile()
        {
            try
            {
                lock (_lock)
                {
                    File.WriteAllLines(_logFilePath, _lines);
                }
                Console.Error.WriteLine($"Log flushed to {_logFilePath}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to flush log: {ex.Message}");
            }
        }

        public static void Clear()
        {
            lock (_lock) { _lines.Clear(); }
        }
    }
}