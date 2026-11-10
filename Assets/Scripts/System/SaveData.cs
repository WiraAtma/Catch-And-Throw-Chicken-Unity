using UnityEngine;

/// <summary>
/// Save data game: hanya menyimpan skor (bintang) terbaik per level.
/// Skor hanya diperbarui jika lebih besar dari yang tersimpan.
/// </summary>
public static class SaveData
{
    private const string ScoreKeyFormat = "Score_Level_{0}";

    public static int GetScore(int levelId)
    {
        return PlayerPrefs.GetInt(string.Format(ScoreKeyFormat, levelId), 0);
    }

    /// <returns>true jika skor baru lebih tinggi dan tersimpan.</returns>
    public static bool SaveScore(int levelId, int score)
    {
        if (score <= GetScore(levelId)) return false;

        PlayerPrefs.SetInt(string.Format(ScoreKeyFormat, levelId), score);
        PlayerPrefs.Save();
        return true;
    }

    public static void ResetAll()
    {
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
    }
}