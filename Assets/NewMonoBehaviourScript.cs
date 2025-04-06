using UnityEngine;
using UnityEngine.UI;
using Renci.SshNet;
using System.Threading;
using System.Text;
using System.IO;
using System.Collections.Generic;
using Renci.SshNet.Common;
using TMPro;
using System.Text.RegularExpressions;


public class SshTerminal : MonoBehaviour
{
    public TMP_InputField host;
    public TMP_InputField username;
    public TMP_InputField password;
    public TextMeshProUGUI outputText;
    public ScrollRect scrollRect;
    // UI Text for terminal output
    public TMP_InputField inputField;  // UI InputField for entering commands

    private SshClient client;
    private ShellStream shell;
    private Thread readThread;
    private Queue<string> outputQueue = new Queue<string>();

    void Start()
    {
        inputField.onEndEdit.AddListener(OnInputSubmitted);
    }

    // Called from the Connect button's OnClick event
    public void OnConnectButtonPressed()
    {
        if (client != null && client.IsConnected)
        {
            Debug.Log("Disconnecting existing SSH session...");
            Disconnect();
        }

        Debug.Log("Attempting SSH connection...");
        new Thread(() =>
        {
            try
            {
                client = new SshClient(host.text.Trim(), username.text.Trim(), password.text);
                client.Connect();

                var modes = new Dictionary<TerminalModes, uint> { { TerminalModes.ECHO, 0 } };
                shell = client.CreateShellStream("xterm", 80, 24, 800, 600, 1024, modes);

                readThread = new Thread(ReadShellOutput) { IsBackground = true };
                readThread.Start();

                lock (outputQueue)
                {
                    outputQueue.Enqueue($"\n[Connected to {host.text}]\n");
                }
            }
            catch (System.Exception ex)
            {
                lock (outputQueue)
                {
                    outputQueue.Enqueue($"\n[Connection Error] {ex.Message}\n");
                }
                Debug.LogError("SSH connection failed: " + ex);
            }
        }).Start();
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
    }
    void Disconnect()
    {
        try
        {
            shell?.Close();
            client?.Disconnect();
            client?.Dispose();
            shell = null;
            client = null;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("Error while disconnecting: " + ex.Message);
        }

        if (readThread != null && readThread.IsAlive)
        {
            readThread.Abort(); // Optional: use safer threading for production
            readThread = null;
        }

        lock (outputQueue)
        {
            outputQueue.Enqueue("\n[Disconnected]\n");
        }
    }

    private static string StripAnsiCodes(string input)
    {
        // Removes ANSI escape sequences like \x1B[31m or \u001b[0K
        // return Regex.Replace(input, @"\x1B\[[0-9;]*[a-zA-Z]", "");
        return Regex.Replace(input, @"\x1B\[[0-9;]*[mGKHF]", "");
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
                outputText.text = StripAnsiCodes(outputText.text);

                // Autoscroll
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
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
