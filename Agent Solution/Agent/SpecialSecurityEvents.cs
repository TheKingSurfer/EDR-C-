using System;
using System.Diagnostics;

class EventLogWatcher
{
    public static void SpecialEvents(string logName, int[] eventIds)
    {
        // Create an EventLog instance
        using (EventLog eventLog = new EventLog(logName))
        {
            // Set up an EventLogWatcher
            eventLog.EntryWritten += (sender, e) =>
            {
                EventLogEntry entry = e.Entry;

                // Check if the event ID matches any of the ones we are interested in
                if (Array.Exists(eventIds, id => entry.InstanceId == id))
                {
                    // Output relevant information about the event
                    Console.WriteLine($"Event ID: {entry.InstanceId}");
                    Console.WriteLine($"Time: {entry.TimeGenerated}");
                    Console.WriteLine($"Message: {entry.Message}");
                    Console.WriteLine();
                }
            };

            // Enable the event log to raise events
            eventLog.EnableRaisingEvents = true;

            // Keep the application running
            Console.WriteLine("Press Enter to exit.");
            Console.ReadLine();
        }
    }
}
