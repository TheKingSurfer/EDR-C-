using System;
using System.Diagnostics;

class EventLogWatcher
{
    

    /// <summary>
    /// Monitors and displays specific event log entries based on the provided event IDs.
    /// </summary>
    /// <param name="logName">The name of the event log to monitor.</param>
    /// <param name="eventIds">An array of event IDs to filter and display.</param>
    /// <remarks>
    /// This method sets up an event log listener for the specified log name and filters the entries based on the provided event IDs.
    /// When an event matching one of the IDs is written to the log, relevant information such as the
    /// event ID, time generated, and message are displayed.
    /// The method will continue to monitor the log until the user presses Enter to exit.
    /// </remarks>
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
