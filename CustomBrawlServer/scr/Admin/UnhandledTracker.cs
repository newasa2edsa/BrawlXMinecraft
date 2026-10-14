using System;
using System.Collections.Concurrent;
using System.Linq;

namespace CustomBrawlServer.Admin;

/// <summary>
/// Remembers every client message / turn command the server has no handler for, with a sample payload.
/// Type <c>unhandled</c> in the server console to see what the client is actually sending, which is the
/// fastest way to find out which feature (a gadget, a super, a menu button...) is still missing.
/// </summary>
public static class UnhandledTracker
{
    private sealed class Entry
    {
        public int Count;
        public string SampleHex = string.Empty;
        public DateTime LastSeen;
    }

    private static readonly ConcurrentDictionary<string, Entry> Seen = new();

    public static void RecordMessage(int messageType, byte[] payload) =>
        Record($"message {messageType}", payload, 0);

    public static void RecordCommand(int commandType, byte[] buffer, int offset) =>
        Record($"command {commandType}", buffer, offset);

    private static void Record(string key, byte[] buffer, int offset)
    {
        var entry = Seen.GetOrAdd(key, _ => new Entry());
        lock (entry)
        {
            entry.Count++;
            entry.LastSeen = DateTime.UtcNow;

            if (entry.SampleHex.Length == 0 && buffer.Length > offset)
            {
                var take = Math.Min(48, buffer.Length - offset);
                entry.SampleHex = Convert.ToHexString(buffer, offset, take) + (buffer.Length - offset > take ? "..." : "");
            }
        }
    }

    public static void Print()
    {
        if (Seen.IsEmpty)
        {
            Console.WriteLine("[Admin] Nothing unhandled so far. Everything the client sent has a handler.");
            return;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n--- Unhandled client traffic (most frequent first) ---");
        foreach (var (key, entry) in Seen.OrderByDescending(kv => kv.Value.Count))
        {
            lock (entry)
            {
                Console.WriteLine($"  {key,-16} x{entry.Count,-5} last {entry.LastSeen:HH:mm:ss}  sample: {(entry.SampleHex.Length == 0 ? "(empty)" : entry.SampleHex)}");
            }
        }
        Console.WriteLine();
        Console.ResetColor();
    }
}
