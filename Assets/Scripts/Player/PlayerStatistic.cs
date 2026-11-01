using System;
using UnityEngine;

/// <summary>
/// Statistik Player: poin ayam dan hitung mundur waktu.
/// Pasang di Player. Semua nilai dipantau lewat variabel public di Inspector.
/// Game dimulai saat StartGame() dipanggil (oleh GameHUD setelah READY / GO!!).
/// </summary>
public class PlayerStatistic : MonoBehaviour
{
    public enum GameState { Ready, Playing, Won, TimeUp }

    [Header("Referensi")]
    [Tooltip("Kosongkan untuk dicari otomatis di scene.")]
    [SerializeField] private ChickenPen pen;

    [Header("Pengaturan")]
    [Tooltip("Batas waktu permainan (detik).")]
    [SerializeField] private float timeLimit = 120f;
    [Tooltip("Aktifkan hanya jika scene TIDAK memakai GameHUD (tanpa READY / GO!!): game langsung mulai.")]
    [SerializeField] private bool autoStart = false;

    [Header("Poin (dipantau di Inspector, jangan diedit manual)")]
    public int point;                // ayam yang sudah masuk kandang
    public int totalChickens;        // total ayam di scene
    public string progress = "0/0";  // contoh "3/10"
    [Range(0f, 1f)]
    public float progressPercent;    // 0 sampai 1

    [Header("Waktu")]
    public float timeRemaining;      // sisa waktu (detik)
    public string timeDisplay = "00:00"; // format mm:ss

    [Header("Status")]
    public GameState state = GameState.Ready;

    public event Action OnGameStarted;
    public event Action<int, int> OnPointChanged;      // (point, totalChickens)
    public event Action<GameState> OnGameEnded;        // Won atau TimeUp

    private int lastDisplayedSecond = -1;

    private void Start()
    {
        timeRemaining = timeLimit;
        UpdateTimeDisplay();

        if (pen == null) pen = FindFirstObjectByType<ChickenPen>();
        if (pen != null)
        {
            pen.OnScoreChanged += HandleScoreChanged;
            HandleScoreChanged(pen.PennedCount, pen.TotalCount); // sinkron nilai awal
        }
        else
        {
            Debug.LogWarning("PlayerStatistic: ChickenPen tidak ditemukan di scene.", this);
        }

        if (autoStart) StartGame();
    }

    private void OnDestroy()
    {
        if (pen != null) pen.OnScoreChanged -= HandleScoreChanged;
    }

    private void Update()
    {
        if (state != GameState.Playing) return; // waktu hanya jalan saat Playing

        timeRemaining = Mathf.Max(0f, timeRemaining - Time.deltaTime);
        UpdateTimeDisplay();

        if (timeRemaining <= 0f)
            EndGame(GameState.TimeUp);
    }

    /// <summary>Mulai permainan: waktu mulai berjalan dan poin bisa menyelesaikan game.</summary>
    public void StartGame()
    {
        if (state != GameState.Ready) return;
        state = GameState.Playing;
        OnGameStarted?.Invoke();
    }

    private void HandleScoreChanged(int penned, int total)
    {
        if (state == GameState.Won || state == GameState.TimeUp) return; // setelah game selesai, statistik dibekukan

        point = penned;
        totalChickens = total;
        progress = $"{penned}/{total}";
        progressPercent = total > 0 ? (float)penned / total : 0f;

        OnPointChanged?.Invoke(point, totalChickens);

        if (state == GameState.Playing && total > 0 && penned >= total)
            EndGame(GameState.Won);
    }

    private void EndGame(GameState result)
    {
        state = result;
        OnGameEnded?.Invoke(result);
    }

    /// <summary>Mulai ulang waktu dan status (langsung Playing). Poin disinkronkan lagi dari kandang.</summary>
    [ContextMenu("Reset Statistic")]
    public void ResetStatistic()
    {
        timeRemaining = timeLimit;
        lastDisplayedSecond = -1;
        UpdateTimeDisplay();

        point = 0;
        state = GameState.Playing;

        if (pen != null) HandleScoreChanged(pen.PennedCount, pen.TotalCount);
    }

    private void UpdateTimeDisplay()
    {
        int sec = Mathf.CeilToInt(timeRemaining);
        if (sec == lastDisplayedSecond) return;
        lastDisplayedSecond = sec;
        timeDisplay = $"{sec / 60:00}:{sec % 60:00}";
    }
}