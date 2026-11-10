using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// UI pilih level (2 level hardcode). Membaca bintang tersimpan dan menampilkannya di teks tiap button.
/// </summary>
public class LevelSelectUI : MonoBehaviour
{
    private const int MaxStars = 3;

    [Header("Teks bintang di dalam button")]
    [SerializeField] private TMP_Text level1StarText;
    [SerializeField] private TMP_Text level2StarText;

    [SerializeField] private string starFormat = "Star: {0}/{1}";

    [Header("Nama scene")]
    [SerializeField] private string level1SceneName = "Level1";
    [SerializeField] private string level2SceneName = "Level2";

    private void Start()
    {
        Refresh();
    }

    private void Refresh()
    {
        if (level1StarText != null)
            level1StarText.text = string.Format(starFormat, SaveData.GetScore(1), MaxStars);

        if (level2StarText != null)
            level2StarText.text = string.Format(starFormat, SaveData.GetScore(2), MaxStars);
    }
    // Dipanggil dari Button > OnClick()
    public void PlayLevel1() => SceneManager.LoadScene(level1SceneName);
    public void PlayLevel2() => SceneManager.LoadScene(level2SceneName);

    [ContextMenu("Reset Semua Bintang")]
    private void ResetStars()
    {
        SaveData.ResetAll();
        Refresh();
    }
}