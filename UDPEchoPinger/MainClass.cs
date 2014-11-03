using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace UDPEchoPinger
{
    public class MainClass
    {
        private static IPEndPoint remoteHost;
        private static UdpClient udpClient = new UdpClient();
        private static ConcurrentQueue<string> fileLog = new ConcurrentQueue<string>();
        private static Thread sendThread;
        private static Thread receiveThread;
        private static Thread loggerThread;


        public static void Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("Error: Please call this program with a remote address");
                return;
            }

            IPAddress destinationAddress;
            int destinationPort;
            if (!IPAddress.TryParse(args[0], out destinationAddress))
            {
                IPAddress[] hostAddresses = Dns.GetHostAddresses(args[0]);
                if (hostAddresses.Length == 0)
                {
                    Console.WriteLine("Not a valid remote host");
                    return;
                }
                destinationAddress = hostAddresses[0];
            }

            if (!Int32.TryParse(args[1], out destinationPort))
            {
                Console.WriteLine("Not a valid port");
                return;
            }

            remoteHost = new IPEndPoint(destinationAddress, destinationPort);
            udpClient.Connect(remoteHost);
            StartLoggerThread();
            StartReceiveThread();
            StartSendLoop();
            //Blocker
            bool running = true;
            while (running)
            {
                string command = Console.ReadLine();
                if (command == "/quit")
                {
                    running = false;
                }
            }

            sendThread.Abort();
            receiveThread.Abort();
            loggerThread.Abort();

            Console.WriteLine("Goodbye!");
        }

        private static void StartSendLoop()
        {
            sendThread = new Thread(new ThreadStart(SendLoop));
            sendThread.Start();
        }

        private static void SendLoop()
        {
            int currentSecond = DateTime.UtcNow.Second;
            while (true)
            {
                while (DateTime.UtcNow.Second == currentSecond)
                {
                    Thread.Sleep(50);
                }
                currentSecond = DateTime.UtcNow.Second;
                long currentTime = DateTime.UtcNow.Ticks;
                byte[] messageBytes = BitConverter.GetBytes(currentTime);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(messageBytes);
                }
                udpClient.Send(messageBytes, messageBytes.Length);
            }
        }

        private static void StartLoggerThread()
        {
            loggerThread = new Thread(new ThreadStart(LoggerLoop));
            loggerThread.Start();
        }

        private static void LoggerLoop()
        {
            string currentPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string loggerFile = Path.Combine(currentPath, "pinger.txt");
            Console.WriteLine("Logging to: " + loggerFile);
            string newLine;
            using (StreamWriter sw = new StreamWriter(loggerFile, true))
            {
                sw.AutoFlush = true;
                while (true)
                {
                    while (fileLog.TryDequeue(out newLine))
                    {
                        sw.WriteLine(newLine);
                    }
                    Thread.Sleep(50);
                }
            }
        }

        private static void StartReceiveThread()
        {
            receiveThread = new Thread(new ThreadStart(ReceiveLoop));
            receiveThread.Start();
        }

        private static void ReceiveLoop()
        {
            while (true)
            {
                IPEndPoint fromEndpoint = new IPEndPoint(IPAddress.Any, 9010);
                byte[] receiveBytes = udpClient.Receive(ref fromEndpoint);
                if (receiveBytes.Length == 8)
                {
                    //Read our time
                    long currentTime = DateTime.UtcNow.Ticks;

                    //Read the send time
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(receiveBytes);
                    }
                    long sendTime = BitConverter.ToInt64(receiveBytes, 0);

                    //Calcualte the difference and log it
                    long timeDiff = currentTime - sendTime;
                    long msDiff = timeDiff / 10000;
                    Console.WriteLine("Got time from " + fromEndpoint.ToString() + ", diff: " + msDiff);

                    //Calculate unix time
                    long unixTime = (long)(new DateTime(sendTime, DateTimeKind.Utc) - new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc)).TotalSeconds;
                    fileLog.Enqueue(unixTime + " " + msDiff);

                }
                else
                {
                    Console.WriteLine("Got invalid message from " + fromEndpoint.ToString());
                }
            }
        }
    }
}

