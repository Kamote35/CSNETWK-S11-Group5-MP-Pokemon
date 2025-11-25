using UnityEngine;
using UnityEngine.SceneManagement; // Needed to change scenes

public class MainMenuController : MonoBehaviour
{
    public void OnPlayClicked()
    {
        // Loads the next scene (which will be the Lobby)
        SceneManager.LoadScene("LobbyScene");
    }

    public void OnPokedexClicked()
    {
        Debug.Log("Pokedex not implemented yet!");
        // Optional: Load PokedexScene if you have time later
    }

    public void OnExitClicked()
    {
        Debug.Log("Exiting Game...");
        Application.Quit();
    }
}