using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using Mirror;

public class EndGame : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text winnerText;
    [SerializeField] private Button returnToMenuButton;
    
    private void Start()
    {
        string winnerName = NetworkManagerRawr.lastWinnerName;
        
        if (winnerText != null)
            winnerText.text = string.IsNullOrEmpty(winnerName) ? "Game Ended" : $"Winner: {winnerName}";
        
        returnToMenuButton?.onClick.AddListener(ReturnToMainMenu);
        
        NetworkManagerRawr.lastWinnerName = "";
        NetworkManagerRawr.lastWinnerId = -1;
    }
    
    private void StopNetwork(NetworkManagerRawr nm)
    {
        if (NetworkServer.active && NetworkClient.isConnected) nm.StopHost();
        else if (NetworkClient.isConnected) nm.StopClient();
        else if (NetworkServer.active) nm.StopServer();
    }
    
    private void ReturnToMainMenu()
    {
        var nm = NetworkManager.singleton as NetworkManagerRawr;
        
        if (nm != null)
        {
            StopNetwork(nm);
            Destroy(nm.gameObject);
        }
        
        StartCoroutine(LoadMainMenuAfterCleanup());
    }
    
    private System.Collections.IEnumerator LoadMainMenuAfterCleanup()
    {
        yield return null;
        SceneManager.LoadScene("MainMenu");
    }
}
