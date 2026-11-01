using System;
using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// HUD permainan. Pasang di GameObject Canvas / HUD, lalu drag TextMeshPro ke field-nya.
/// - Start Game Text: "READY" -> "GO!!" saat game dimulai (waktu dan permainan baru mulai di GO!!).
/// - Chicken Count / Max Count / Timer: hanya ANGKA (label dibuat sendiri di UI).
/// - End Game Text: muncul 1 detik setelah game berakhir, tampil 3 detik, lalu hilang.
///   Setelah hilang, event OnEndSequenceFinished dipanggil (ModalAlert memakainya untuk menampilkan hasil).
/// - Gerakan Player dikunci (PlayerController.isMoving = false) selama Start Text, End Text, dan Modal.
/// Data diambil dari PlayerStatistic.
/// </summary>
public class GameHUD : MonoBehaviour
{
    [Header("Referensi Data")]
    [Tooltip("Kosongkan untuk dicari otomatis di scene (PlayerStatistic di Player).")]
    [SerializeField] private PlayerStatistic statistic;

    [Header("Start Game Text (default AKTIF di Hierarchy, hanya 1 TextMeshPro)")]
    [SerializeField] private TMP_Text startGameText;
    [SerializeField] private string readyMessage = "READY";
    [SerializeField] private string goMessage = "GO!!";
    [Tooltip("Lama teks READY (detik).")]
    [SerializeField] private float readyDuration = 3f;
    [Tooltip("Lama teks GO!! (detik). Game sudah berjalan selama teks ini tampil.")]
    [SerializeField] private float goDuration = 2f;
    [Tooltip("Aktif: dunia game (Player, ayam, fisika) dibekukan selama READY.")]
    [SerializeField] private bool freezeWorldDuringReady = true;

    [Header("Player Input Lock")]
    [Tooltip("Aktif: Player tetap terkunci selama GO!! tampil. Nonaktif: Player bisa bergerak begitu GO!! muncul (saat timer mulai).")]
    [SerializeField] private bool lockMovementDuringGo = true;

    [Header("Chicken Counter (TextMeshPro)")]
    [SerializeField] private TMP_Text chickenCountText;     // jumlah ayam di kandang, misal "3"
    [SerializeField] private TMP_Text chickenMaxCountText;  // total ayam, misal "10"

    [Header("Timer (TextMeshPro)")]
    [SerializeField] private TMP_Text timerText;            // sisa waktu
    [Tooltip("Aktif: tampil 01:59.  Nonaktif: tampil detik saja, misal 119.")]
    [SerializeField] private bool minutesAndSeconds = true;

    [Header("End Game Text (default NONAKTIF di Hierarchy)")]
    [SerializeField] private TMP_Text endGameText;
    [SerializeField] private string timeUpMessage = "Times Up!";
    [SerializeField] private string finishedMessage = "Game Finished";
    [Tooltip("Jeda setelah game berakhir sebelum teks muncul (detik).")]
    [SerializeField] private float delayBeforeShow = 1f;
    [Tooltip("Lama teks tampil sebelum disembunyikan (detik).")]
    [SerializeField] private float showDuration = 3f;

    /// <summary>Dipanggil setelah End Game Text disembunyikan. Parameter: Won atau TimeUp.</summary>
    public event Action<PlayerStatistic.GameState> OnEndSequenceFinished;

    private int lastSecond = -1;
    private Coroutine endSequence;
    private bool worldFrozen;
    private PlayerController player;

    private void Awake()
    {
        // Awake berjalan sebelum Update milik script mana pun, jadi Player terkunci sejak frame pertama
        player = FindFirstObjectByType<PlayerController>();
        if (player == null)
            Debug.LogWarning("GameHUD: PlayerController tidak ditemukan, gerakan Player tidak bisa dikunci.", this);

        SetPlayerMovement(false);
    }

    private void Start()
    {
        if (endGameText != null) endGameText.gameObject.SetActive(false);

        if (statistic == null) statistic = FindFirstObjectByType<PlayerStatistic>();

        if (statistic == null)
        {
            Debug.LogWarning("GameHUD: PlayerStatistic tidak ditemukan di scene.", this);
        }
        else
        {
            statistic.OnPointChanged += HandlePointChanged;
            statistic.OnGameEnded += HandleGameEnded;

            // Tampilkan nilai awal
            HandlePointChanged(statistic.point, statistic.totalChickens);
            UpdateTimer(force: true);
        }

        StartCoroutine(StartSequence());
    }

    private void OnDestroy()
    {
        if (worldFrozen) Time.timeScale = 1f; // jaga-jaga kalau HUD hancur saat READY

        if (statistic == null) return;
        statistic.OnPointChanged -= HandlePointChanged;
        statistic.OnGameEnded -= HandleGameEnded;
    }

    private void Update()
    {
        if (statistic == null) return;
        UpdateTimer(force: false);
    }

    // ---------------------------------------------------------------- Kunci gerakan Player

    private void SetPlayerMovement(bool allowed)
    {
        if (player != null) player.isMoving = allowed;
    }

    // Buka kunci hanya kalau game memang sedang berjalan (jangan membuka kunci setelah game berakhir)
    private void UnlockPlayerIfPlaying()
    {
        if (statistic == null || statistic.state == PlayerStatistic.GameState.Playing)
            SetPlayerMovement(true);
    }

    // ---------------------------------------------------------------- Alur awal game

    // READY (1 dtk, dunia beku) -> GO!! (game mulai, 1 dtk) -> teks disembunyikan -> Player bisa bergerak
    private IEnumerator StartSequence()
    {
        if (freezeWorldDuringReady)
        {
            Time.timeScale = 0f;
            worldFrozen = true;
        }

        if (startGameText != null)
        {
            startGameText.gameObject.SetActive(true);
            startGameText.text = readyMessage;
        }

        yield return new WaitForSecondsRealtime(readyDuration); // realtime, karena timeScale bisa 0

        if (worldFrozen)
        {
            Time.timeScale = 1f;
            worldFrozen = false;
        }

        if (statistic != null) statistic.StartGame(); // waktu mulai berjalan

        if (startGameText != null) startGameText.text = goMessage;
        if (!lockMovementDuringGo) UnlockPlayerIfPlaying();

        yield return new WaitForSecondsRealtime(goDuration);

        if (startGameText != null) startGameText.gameObject.SetActive(false);
        UnlockPlayerIfPlaying();
    }

    // ---------------------------------------------------------------- Counter & timer

    private void HandlePointChanged(int point, int total)
    {
        if (chickenCountText != null) chickenCountText.text = point.ToString();
        if (chickenMaxCountText != null) chickenMaxCountText.text = total.ToString();
    }

    private void UpdateTimer(bool force)
    {
        if (timerText == null) return;

        // Update teks hanya saat detik berganti (hemat alokasi string)
        int sec = Mathf.CeilToInt(statistic.timeRemaining);
        if (!force && sec == lastSecond) return;
        lastSecond = sec;

        timerText.text = minutesAndSeconds
            ? $"{sec / 60:00}:{sec % 60:00}"
            : sec.ToString();
    }

    // ---------------------------------------------------------------- Alur akhir game

    private void HandleGameEnded(PlayerStatistic.GameState result)
    {
        SetPlayerMovement(false); // terkunci begitu game berakhir, selama End Text sampai Modal

        if (endSequence != null) StopCoroutine(endSequence);
        endSequence = StartCoroutine(EndSequence(result));
    }

    // Game berakhir -> tunggu 1 detik -> tampilkan teks -> 3 detik -> sembunyikan -> beri tahu ModalAlert
    private IEnumerator EndSequence(PlayerStatistic.GameState result)
    {
        yield return new WaitForSeconds(delayBeforeShow);

        if (endGameText != null)
        {
            endGameText.text = result == PlayerStatistic.GameState.Won ? finishedMessage : timeUpMessage;
            endGameText.gameObject.SetActive(true);

            yield return new WaitForSeconds(showDuration);

            endGameText.gameObject.SetActive(false);
        }

        endSequence = null;
        OnEndSequenceFinished?.Invoke(result);
    }
}