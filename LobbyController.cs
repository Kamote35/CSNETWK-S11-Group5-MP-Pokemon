using UnityEngine;
using UnityEngine.UI;
using TMPro; // Needed for TextMeshPro
using UnityEngine.SceneManagement;

public class LobbyController : MonoBehaviour
{
    public TMP_InputField joinIPInput;
    public TMP_InputField spectateIPInput;
    public TextMeshProUGUI hostIPText;

    public void OnHostClicked()
    {
        // 1. Tell the NetworkManager to start as Host
        // We pick a port, e.g., 5000
        NetworkManager.Instance.InitializeHost(5000);
        
        // 2. Show the user their local IP (simple check)
        string localIP = GetLocalIPAddress();
        hostIPText.text = "Hosting on: " + localIP;

        // 3. Load the Battle Scene
        // Note: In a real game, we might wait for a handshake first, 
        // but for this assignment, we load the scene and wait there.
        SceneManager.LoadScene("BattleScene");
    }

    public void OnJoinClicked()
    {
        string ip = joinIPInput.text.Trim();
        if (string.IsNullOrEmpty(ip)) return;

        // 1. Start NetworkManager as Joiner
        NetworkManager.Instance.InitializeJoiner(ip, 5000);

        // 2. Load Battle Scene
        SceneManager.LoadScene("BattleScene");
    }
    
    public void OnSpectateClicked()
    {
        string ip = spectateIPInput.text;
        if (string.IsNullOrEmpty(ip)) return;

        // 1. Start NetworkManager as Joiner (Spectator Mode)
        // We will need to add a flag for "IsSpectator" to NetworkManager later
        NetworkManager.Instance.InitializeSpectator(ip, 5000);

        // 2. Load Battle Scene
        SceneManager.LoadScene("BattleScene");
    }

    public void OnBackClicked()
    {
        SceneManager.LoadScene("MainMenu");
    }

    // Helper to find local IP
    private string GetLocalIPAddress()
    {
        var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return ip.ToString();
            }
        }
        return "127.0.0.1";
    }
}