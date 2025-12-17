using UnityEngine;
using UnityEngine.SceneManagement;

public class LobbyDiscovery : MonoBehaviour
{
    public void GoToMainMenu()
    {
        SceneManager.LoadScene("MainMenu");
    }
}
