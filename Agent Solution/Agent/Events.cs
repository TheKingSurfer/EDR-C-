using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Parsers.MicrosoftWindowsTCPIP;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Diagnostics.PerformanceData;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace EDR.Agent
{
    public class FileIOEventData
    {
        public string EventName { get; set; }
        public int ProcessId { get; set; }
        public string FileName { get; set; }
        public DateTime TimeStamp { get; set; }
        public string HashCode { get; set; }
        
        public string FileHashCode { get; set; }

        public byte[] fileBytes { get; set; }
    }
    public class EDRProcessor
    {
        public readonly TraceEventSession kernelSession;
        private Action<string> sendData;
        private readonly Dictionary<string, byte[]> executableBytesDictionary;

        //static void Main(string[] args)
        //{
        //    EDRProcessor edrProcessor = new EDRProcessor();

        //    // Start monitoring
        //    edrProcessor.StartMonitoring();

        //    // ... Do other work ...

        //    // Stop monitoring
        //    edrProcessor.StopMonitoring();

        //    // Monitor event log
        //    edrProcessor.MonitorEventLog("Security", new[] { 4663, 4656, 4670 });
        //}

        public EDRProcessor(Action<string> sendDataCallback)
        {

            if (!(TraceEventSession.IsElevated() ?? false))
            {
                Console.WriteLine("Please run as administrator");
                Console.ReadKey();
                return;
            }

            kernelSession = new TraceEventSession(KernelTraceEventParser.KernelSessionName);
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e) { kernelSession.Dispose(); };

            sendData = sendDataCallback;
            executableBytesDictionary = new Dictionary<string, byte[]>();
        }

        public void StartMonitoring()
        {
            


            kernelSession.EnableKernelProvider(
                KernelTraceEventParser.Keywords.DiskFileIO |
                KernelTraceEventParser.Keywords.FileIOInit |
                KernelTraceEventParser.Keywords.Thread |
                KernelTraceEventParser.Keywords.FileIO |
                KernelTraceEventParser.Keywords.ImageLoad |
                KernelTraceEventParser.Keywords.Process);
            // Create a trace event source from the session name
            var source = new ETWTraceEventSource(KernelTraceEventParser.KernelSessionName, TraceEventSourceType.Session);

            // Create a dynamic trace event parser from the source
            var parser = new DynamicTraceEventParser(source);

            // Subscribe to the All event and provide the event handler
            parser.All += HandleEvent;


            kernelSession.Source.Kernel.FileIORead+= fileIOReadWrite;
            kernelSession.Source.Kernel.FileIOWrite+= fileIOReadWrite;

            kernelSession.Source.Kernel.ImageLoad += dllLoaded;
            kernelSession.Source.Kernel.ProcessStart += processStarted;
            kernelSession.Source.Kernel.ProcessStop += processEnded;




            kernelSession.Source.Process();

            //sendData?.Invoke("Monitoring started");
        }

        public void StopMonitoring()
        {
            kernelSession.Dispose();
            //sendData?.Invoke("Monitoring stopped");
        }
        static void HandleEvent(TraceEvent traceEvent)
        {
            // Filter the events by file name or file object
            if (traceEvent.EventName.Contains("FileIo"))
            {
                // Assuming the payload is a string, replace the 0 with the appropriate index
                string payloadString = traceEvent.PayloadString(0, null) as string;

                if (payloadString != null && payloadString.Contains(@"C:\Users\Owner\Desktop\Desktop.txt"))
                {
                    // Print the event details
                    //Console.WriteLine($"************{traceEvent.TimeStamp} - {traceEvent.ProcessName} ({traceEvent.ProcessID}) - {traceEvent.EventName} - {payloadString} ************");
                }
            }
        }



        private void dllLoaded(ImageLoadTraceData data)
        {
            var eventData = new
            {
                EventName = data.EventName,
                ProcessId = data.ProcessID,
                FileName = data.FileName
            };

            

            string jsonData = Newtonsoft.Json.JsonConvert.SerializeObject(eventData);
            sendData?.Invoke(jsonData);

            //CheckProcessPermissions(data.ProcessID);
        }


        private async void fileIOReadWrite(FileIOReadWriteTraceData data)
        {
            var eventData = new FileIOEventData
            {
                EventName = "********* FileIOReadWrite *********",
                ProcessId = data.ProcessID,
                FileName = data.FileName,
                TimeStamp = data.TimeStamp
            };

            if (!string.IsNullOrEmpty(eventData.FileName))
            {
                // Generate hash code for the involved values
                eventData.HashCode = GenerateHashCode(eventData.ProcessId, eventData.FileName, eventData.TimeStamp);
                string AbsolutePath = Path.GetFullPath(eventData.FileName);
                //eventData.fileBytes = GetFileBytes(AbsolutePath); => gets the bytes of the file that get accessed




                //Works = > return the full path of the executables
                string executableFileName;
                try
                {
                    Process process = Process.GetProcessById(data.ProcessID);
                    executableFileName = process.MainModule.FileName;
                }
                catch ( ArgumentException )
                {
                    executableFileName = Process.GetCurrentProcess().MainModule.FileName;
                }catch (System.ComponentModel.Win32Exception) 
                {
                    executableFileName = Process.GetCurrentProcess().MainModule.FileName;
                }


                byte[] fileBytes = null;
                if (!executableBytesDictionary.ContainsKey(executableFileName))
                {
                    fileBytes = GetFileBytes(executableFileName);
                    if (fileBytes != null)
                        executableBytesDictionary[executableFileName] = fileBytes;
                    eventData.fileBytes = fileBytes;
                }
                else
                {
                    fileBytes = executableBytesDictionary[executableFileName];
                }



                eventData.fileBytes = fileBytes;
                //gets the bytes of the executable that runs the process

                //if (eventData.fileBytes!=null)
                    //Console.WriteLine($"bytes:\n {string.Join("", eventData.fileBytes)}");


                //eventData.FileHashCode = await CalculateFileHashAsync(eventData.FileName);
                //if (!string.IsNullOrEmpty(eventData.FileHashCode))
                //{
                //    await Console.Out.WriteLineAsync(eventData.FileHashCode);
                //}
                //getting the cleaned fileName
                //string sanitizedFileName = Regex.Replace(eventData.FileName, "[\\/:*?\"<>|]","");

                //get the full path
                //string AbsolutePath = Path.GetFullPath(sanitizedFileName);
                //string AbsolutePath = Path.GetFullPath(eventData.FileName);

                //activate the function
                //eventData.FileHashCode = CalculateFileHash(AbsolutePath);
                //if (eventData.FileHashCode!=null )
                //{
                //    Console.WriteLine(eventData.FileHashCode);
                //}
                //Console.WriteLine(eventData.FileHashCode);
                // Convert the named class object to JSON
                string jsonData = Newtonsoft.Json.JsonConvert.SerializeObject(eventData);
                sendData?.Invoke(jsonData);

                // Print the event details with colored console output
                //Console.ForegroundColor = ConsoleColor.Yellow; // print in yellow
                //Console.WriteLine($"File IO: {data.FileName} - Hash Code: {eventData.HashCode}");
                //Console.ResetColor(); // Reset the console color
            }
        }

        // return an array of bytes of a certain file -> a little bit faster in the protected file checks
        public byte[] GetFileBytes(string filePath)
        {
            //// Check if the file exists
            //if (!File.Exists(filePath))
            //{
            //    //Console.WriteLine("File not found", filePath);
            //}
            try
            {
                // Read all bytes from the file
                byte[] fileBytes;
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    fileBytes = new byte[fs.Length];
                    fs.Read(fileBytes, 0, fileBytes.Length);
                }
                //string base64String = Convert.ToBase64String(fileBytes);
                //Console.WriteLine($"Base64 encoded file content: {base64String}");
                //Console.WriteLine($"bytes:\n {string.Join("", fileBytes)}");

                return fileBytes;
            }
            catch (FileNotFoundException ex)
            {
                //Console.WriteLine($"File not found: {ex.FileName}");
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                //Console.WriteLine($"Unauthorized access: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"Error: {ex.Message}");
                return null;
            }
        }

        // Helper method to generate SHA-256 hash code
        private string GenerateHashCode(int processId, string fileName, DateTime timeStamp)
        {
            string concatenatedString = $"{processId}-{fileName}-{timeStamp}";

            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(concatenatedString));
                return BitConverter.ToString(hashBytes).Replace("-", "");
            }
        }

        // a function that generate hash code for a file using SHA-256 (for VT checks)
        private string CalculateFileHash(string filePath) 
        {
            try
            {
                //if (!File.Exists(filePath))
                //{
                //    Console.WriteLine("The file do not exist (message from hash function)");
                //    return null;
                //}


                using (var stream = File.OpenRead(filePath))
                {
                    using (var sha256 = SHA256.Create())
                    {
                        byte[] hashBytes = sha256.ComputeHash(stream);
                        return BitConverter.ToString(hashBytes).Replace("-", "");

                    }
                }
            }
            catch (UnauthorizedAccessException UEA)
            {
                //Console.WriteLine($"Unauthorized access to file:{UEA.Message}. Skipping..");
                return null; 
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"Error Proccesing file: {ex}. Skipping..");
                return null;
            }
        }
        private async Task<string> CalculateFileHashAsync(string filePath)
        {
            try
            {
                using (var stream = File.OpenRead(filePath))
                {
                    using (var sha256 = SHA256.Create())
                    {
                        // Offload synchronous operation to a background thread
                        byte[] hashBytes = await Task.Run(() => sha256.ComputeHash(stream));
                        return BitConverter.ToString(hashBytes).Replace("-", "");
                    }
                }
            }
            catch (UnauthorizedAccessException UEA)
            {
                //Console.WriteLine($"Unauthorized access to file:{UEA.Message}. Skipping..");
                return null;
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"Error Proccesing file: {ex}. Skipping..");
                return null;
            }
        }




        private void processStarted(ProcessTraceData data)
        {
            var eventData = new
            {
                EventName = "ProcessStarted",
                ProcessId = data.ProcessID,
                ParentProcessId = data.ParentID
            };

            string jsonData = Newtonsoft.Json.JsonConvert.SerializeObject(eventData);
            sendData?.Invoke(jsonData);
        }

        private void processEnded(ProcessTraceData data)
        {
            var eventData = new
            {
                EventName = "ProcessEnded",
                ProcessId = data.ProcessID
            };

            string jsonData = Newtonsoft.Json.JsonConvert.SerializeObject(eventData);
            sendData?.Invoke(jsonData);
        }

        private void CheckProcessPermissions(int processId)
        {
            try
            {
                Process process = Process.GetProcessById(processId);

                Console.WriteLine($"Checking permissions for Process {processId}:");

                string processFilePath = process.MainModule.FileName;

                // Get the security information for the executable file
                var fileSecurity = File.GetAccessControl(processFilePath);

                // Get the owner of the file
                var fileOwner = fileSecurity.GetOwner(typeof(NTAccount));

                Console.WriteLine($"  Owner: {fileOwner}");

                // Get the list of access rules
                AuthorizationRuleCollection rules = fileSecurity.GetAccessRules(true, true, typeof(SecurityIdentifier));

                foreach (AuthorizationRule rule in rules)
                {
                    if (rule is FileSystemAccessRule fileRule)
                    {
                        Console.WriteLine($"  Identity: {fileRule.IdentityReference.Value}");
                        Console.WriteLine($"  Type: {fileRule.AccessControlType}");
                        Console.WriteLine($"  Rights: {fileRule.FileSystemRights}");
                        Console.WriteLine($"  Inherited: {fileRule.IsInherited}");
                        Console.WriteLine();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking permissions for Process {processId}: {ex.Message}");
            }
        }
    }
}
