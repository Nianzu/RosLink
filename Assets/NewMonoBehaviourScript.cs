using UnityEngine;
using UnityEngine.UI;
using Renci.SshNet;
using System.Threading;
using System.Text;
using System.IO;
using System.Collections.Generic;
using Renci.SshNet.Common;
using TMPro;



public class SshTerminal : MonoBehaviour
{
    public string host;
    public string username;
    public string password;
    public TextMeshProUGUI outputText;
    // UI Text for terminal output
    public InputField inputField;  // UI InputField for entering commands

    private SshClient client;
    private ShellStream shell;
    private Thread readThread;
    private Queue<string> outputQueue = new Queue<string>();

    void Start()
    {
        // Connect in a background thread to avoid freezing
        Debug.Log("SSH Terminal Starting...");
        new Thread(() =>
        {
            Debug.Log("SSH Thread Starting...");
            client = new SshClient(host, username, password);
            try
            {
                client.Connect();
                var modes = new Dictionary<TerminalModes, uint>();
                modes[TerminalModes.ECHO] = 0;  // we handle echo manually
                shell = client.CreateShellStream("xterm", 80, 24, 800, 600, 1024, modes);

                // Start reader thread
                readThread = new Thread(ReadShellOutput) { IsBackground = true };
                readThread.Start();

                Debug.Log("SSH Thread Started");
            }
            catch (System.Exception ex)
            {
                lock (outputQueue) { outputQueue.Enqueue($"\n[Connection Error] {ex.Message}\n"); }
                Debug.Log("SSH Thread Error: " + ex.Message);
            }
        }).Start();

        // Setup InputField submit handler
        inputField.onEndEdit.AddListener(OnInputSubmitted);
    }

    void ReadShellOutput()
    {
        var reader = new StreamReader(shell, Encoding.UTF8);
        while (client != null && client.IsConnected)
        {
            string text = null;
            try
            {
                // ReadLine can block, so check DataAvailable first
                if (shell != null && shell.DataAvailable)
                {
                    text = shell.Read();  // read whatever is available
                }
            }
            catch { /* handle exceptions if needed */ }
            if (!string.IsNullOrEmpty(text))
            {
                lock (outputQueue) { outputQueue.Enqueue(text); }
            }
            Thread.Sleep(50); // small delay
        }
    }

    void OnInputSubmitted(string userInput)
    {
        if (!string.IsNullOrEmpty(userInput) && client != null && client.IsConnected)
        {
            shell.WriteLine(userInput);
            // Append the command to output (with a newline and prompt maybe)
            lock (outputQueue) { outputQueue.Enqueue($"> {userInput}\n"); }
        }
        inputField.text = "";  // clear input field
        inputField.ActivateInputField();  // focus back to input for next command
    }

    void Update()
    {
        lock (outputQueue)
        {
            if (outputQueue.Count > 0)
            {
                // Append all queued output to the UI text
                while (outputQueue.Count > 0)
                {
                    outputText.text += outputQueue.Dequeue();
                }
                // You might want to auto-scroll the ScrollRect here if needed
            }
        }
    }

    void OnDestroy()
    {
        // Clean up on exit
        if (client != null && client.IsConnected)
        {
            shell.Close();  // close shell stream
            client.Disconnect();
            client.Dispose();
        }
        if (readThread != null && readThread.IsAlive)
        {
            readThread.Abort();
        }
    }
}
