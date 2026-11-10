using UnityEngine;
using UnityEngine.SceneManagement;

public class LevelNavigation : MonoBehaviour
{
    public void GoToLevel1()
    {
        SceneManager.LoadScene("Level1");
    }
    public void GoToLevel2()
    {
        SceneManager.LoadScene("Level2");
    }
}