using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Modal hasil akhir. Pasang di GameObject AlertModal (HARUS aktif), bukan di Content.
/// Struktur:  AlertModal (script ini) > Content (default NONAKTIF) > Background + ResultModal (StarText, tombol)
/// Alur: GameHUD selesai menampilkan End Game Text -> Content diaktifkan -> bintang dihitung dan ditampilkan.
/// </summary>
public class ModalAlert : MonoBehaviour
{
    private const int MaxStars = 3;

    [Header("Referensi (kosongkan untuk dicari otomatis)")]
    [SerializeField] private GameHUD hud;
    [SerializeField] private PlayerStatistic statistic;

    [Header("Modal")]
    [Tooltip("Objek Content (anak AlertModal) yang berisi Background dan ResultModal. Default nonaktif.")]
    [SerializeField] private GameObject content;
    [SerializeField] private TMP_Text starText;
    [Tooltip("{0} = jumlah bintang, {1} = bintang maksimal.")]
    [SerializeField] private string starFormat = "Star: {0}/{1}";

    [Header("Syarat Bintang (persen ayam masuk kandang)")]
    [Range(0f, 1f)] [SerializeField] private float oneStarPercent = 0.5f;   // >= 50%  -> 1 bintang
    [Range(0f, 1f)] [SerializeField] private float twoStarPercent = 0.8f;   // >= 80%  -> 2 bintang
    // Semua ayam masuk (100%) -> 3 bintang

    [Header("Scene")]
    [SerializeField] private string menuSceneName = "LevelSelect";

    [Header("Hasil (dipantau di Inspector)")]
    public int starCount;
    [Range(0f, 1f)] public float resultPercent;

    private void Start()
    {
        if (content != null) content.SetActive(false);

        if (hud == null) hud = FindFirstObjectByType<GameHUD>();
        if (statistic == null) statistic = FindFirstObjectByType<PlayerStatistic>();

        if (hud == null || statistic == null)
        {
            Debug.LogWarning("ModalAlert: GameHUD atau PlayerStatistic tidak ditemukan di scene.", this);
            return;
        }

        hud.OnEndSequenceFinished += HandleEndSequenceFinished;
    }

    private void OnDestroy()
    {
        if (hud != null) hud.OnEndSequenceFinished -= HandleEndSequenceFinished;
    }

    private void HandleEndSequenceFinished(PlayerStatistic.GameState result)
    {
        Show();
    }

    public void Show()
    {
        starCount = CalculateStars(statistic.point, statistic.totalChickens, out resultPercent);

        if (starText != null)
            starText.text = string.Format(starFormat, starCount, MaxStars);

        if (content != null) content.SetActive(true);

        // Gerakan Player tetap terkunci selama modal tampil
        var player = FindFirstObjectByType<PlayerController>();
        if (player != null) player.isMoving = false;

        // Kursor dibutuhkan untuk klik tombol. Kamera dimatikan supaya tidak mengunci kursor lagi saat klik.
        var cam = FindFirstObjectByType<ThirdPersonCamera>();
        if (cam != null) cam.enabled = false;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private int CalculateStars(int point, int total, out float percent)
    {
        percent = total > 0 ? (float)point / total : 0f;

        if (total > 0 && point >= total) return 3;   // sempurna
        if (percent >= twoStarPercent) return 2;
        if (percent >= oneStarPercent) return 1;
        return 0;
    }

    // ---------------------------------------------------------------- Dipanggil dari Button > OnClick()

    public void BackToMenu()
    {
        SceneManager.LoadScene(menuSceneName);
    }

    public void RestartGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}