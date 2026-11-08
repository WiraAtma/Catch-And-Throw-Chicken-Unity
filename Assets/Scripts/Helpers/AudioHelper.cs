using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AudioHelper - satu pintu untuk semua audio di game.
/// Clip di-drag langsung di Inspector pada script masing-masing (Player, Enemy, GameManager, dll).
///
/// CONTOH DI SCRIPT LAIN:
///
///   public class Player : MonoBehaviour
///   {
///       public AudioHelper.Sound footstep;   // bisa drag beberapa clip + atur volume/pitch
///       public AudioClip jumpSfx;            // atau cukup satu clip biasa
///       public AudioClip winJingle;
///       public AudioClip rainLoop;
///       public AudioClip bgm;
///
///       void Start()  { AudioHelper.Instance.PlayMusic(bgm); }
///       void Update() { AudioHelper.Instance.SetRepeating(footstep, isMoving, 0.4f); }
///       void Jump()   { AudioHelper.Instance.PlaySFX(jumpSfx); }
///       void Win()    { AudioHelper.Instance.PlayStinger(winJingle); }
///   }
///
/// SETUP: TIDAK WAJIB. Kalau AudioHelper belum ada di scene, otomatis dibuat saat pertama dipanggil.
/// Taruh manual di GameObject kosong hanya jika memakai Sound Library (string id) atau mau atur pool/ducking.
/// Semua fungsi (PlaySFX, PlayStinger, StartRepeating, StopRepeating, SetRepeating,
/// PlayLoop, StopLoop, IsLoopPlaying) menerima: AudioClip, AudioHelper.Sound, atau string id (opsional).
/// </summary>
[DisallowMultipleComponent]
public class AudioHelper : MonoBehaviour
{
    private static AudioHelper instance;
    private static bool quitting;

    /// <summary>
    /// Akses global. Kalau belum ada di scene, otomatis dibuat sendiri (tanpa Sound Library),
    /// jadi tidak wajib menaruh AudioHelper manual selama clip di-drag di script masing-masing.
    /// </summary>
    public static AudioHelper Instance
    {
        get
        {
            if (quitting) return null;

            if (instance == null)
            {
                instance = FindFirstObjectByType<AudioHelper>();

                if (instance == null)
                {
                    GameObject go = new GameObject("AudioHelper (auto)");
                    instance = go.AddComponent<AudioHelper>(); // Awake langsung jalan
                }
            }

            instance.Initialize(); // aman kalau Awake-nya belum sempat dipanggil Unity
            return instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        quitting = false;
    }

    private bool initialized;

    // ---------------------------------------------------------------- DATA

    [System.Serializable]
    public class Sound
    {
        [Tooltip("Opsional. Hanya dipakai kalau sound ini didaftarkan di Sound Library dan dipanggil lewat string.")]
        public string id;
        [Tooltip("Boleh lebih dari satu clip, nanti dipilih acak (bagus untuk footstep).")]
        public AudioClip[] clips;
        [Range(0f, 1f)] public float volume = 1f;
        [Range(0.5f, 2f)] public float minPitch = 1f;
        [Range(0.5f, 2f)] public float maxPitch = 1f;

        [System.NonSerialized] private int lastIndex = -1;

        public AudioClip GetClip()
        {
            if (clips == null || clips.Length == 0) return null;
            if (clips.Length == 1) return clips[0];

            // hindari clip yang sama dua kali berturut-turut
            int index;
            do { index = Random.Range(0, clips.Length); } while (index == lastIndex);
            lastIndex = index;
            return clips[index];
        }

        public float GetPitch()
        {
            return Mathf.Approximately(minPitch, maxPitch) ? minPitch : Random.Range(minPitch, maxPitch);
        }
    }

    private class MusicChannel
    {
        public AudioSource source;
        public float level;       // 0..1 untuk fade
        public float baseVolume = 1f;
    }

    private class LoopInfo
    {
        public AudioSource source;
        public float level;       // 0..1 untuk fade
        public float baseVolume = 1f;
        public Coroutine fade;
    }

    // ------------------------------------------------------------ INSPECTOR

    [Header("Sound Library (OPSIONAL - hanya jika mau panggil pakai string id)")]
    [SerializeField] private List<Sound> sounds = new List<Sound>();

    [Header("SFX Pool")]
    [SerializeField] private int initialSfxSources = 8;
    [SerializeField] private int maxSfxSources = 24;

    [Header("Ducking (untuk Stinger)")]
    [SerializeField, Range(0f, 1f)] private float duckedMusicMultiplier = 0.3f;

    // ---------------------------------------------------------------- STATE

    private const string KeyMaster = "audio_master";
    private const string KeyMusic = "audio_music";
    private const string KeySfx = "audio_sfx";

    private float masterVolume = 1f;
    private float musicVolume = 1f;
    private float sfxVolume = 1f;

    private readonly Dictionary<string, Sound> library = new Dictionary<string, Sound>();
    private readonly Dictionary<AudioClip, Sound> clipSounds = new Dictionary<AudioClip, Sound>();
    private readonly List<AudioSource> sfxPool = new List<AudioSource>();
    private readonly Dictionary<Sound, LoopInfo> activeLoops = new Dictionary<Sound, LoopInfo>();
    private readonly Dictionary<Sound, Coroutine> activeRepeaters = new Dictionary<Sound, Coroutine>();

    private MusicChannel channelA, channelB, activeMusic;
    private Coroutine musicFadeRoutine;
    private int nextStealIndex;
    private int duckCount;
    private float duckMultiplier = 1f;

    // ------------------------------------------------------------ PROPERTIES

    public float MasterVolume
    {
        get => masterVolume;
        set { masterVolume = Mathf.Clamp01(value); PlayerPrefs.SetFloat(KeyMaster, masterVolume); RefreshVolumes(); }
    }

    public float MusicVolume
    {
        get => musicVolume;
        set { musicVolume = Mathf.Clamp01(value); PlayerPrefs.SetFloat(KeyMusic, musicVolume); RefreshVolumes(); }
    }

    public float SFXVolume
    {
        get => sfxVolume;
        set { sfxVolume = Mathf.Clamp01(value); PlayerPrefs.SetFloat(KeySfx, sfxVolume); RefreshVolumes(); }
    }

    public bool IsMusicPlaying => activeMusic != null && activeMusic.source.isPlaying;

    // ---------------------------------------------------------------- SETUP

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        Initialize();
    }

    private void Initialize()
    {
        if (initialized) return;
        initialized = true;

        DontDestroyOnLoad(gameObject);

        masterVolume = PlayerPrefs.GetFloat(KeyMaster, 1f);
        musicVolume = PlayerPrefs.GetFloat(KeyMusic, 1f);
        sfxVolume = PlayerPrefs.GetFloat(KeySfx, 1f);

        BuildLibrary();

        channelA = CreateMusicChannel();
        channelB = CreateMusicChannel();
        activeMusic = channelA;

        for (int i = 0; i < initialSfxSources; i++) CreateSfxSource();
    }

    private void OnApplicationQuit()
    {
        quitting = true;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void BuildLibrary()
    {
        library.Clear();
        foreach (Sound s in sounds)
        {
            if (string.IsNullOrEmpty(s.id)) continue;
            if (library.ContainsKey(s.id))
            {
                Debug.LogWarning($"[AudioHelper] ID duplikat: \"{s.id}\" (yang kedua diabaikan).");
                continue;
            }
            library.Add(s.id, s);
        }
    }

    private MusicChannel CreateMusicChannel()
    {
        AudioSource src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = true;
        return new MusicChannel { source = src };
    }

    private AudioSource CreateSfxSource()
    {
        AudioSource src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        sfxPool.Add(src);
        return src;
    }

    // ------------------------------------------------------ RESOLVER HELPER

    /// <summary>Bungkus AudioClip jadi Sound (di-cache supaya reference-nya konsisten untuk loop/repeat).</summary>
    private Sound Wrap(AudioClip clip, float volume = 1f)
    {
        if (clip == null) return null;

        if (!clipSounds.TryGetValue(clip, out Sound s))
        {
            s = new Sound { clips = new[] { clip }, volume = Mathf.Clamp01(volume) };
            clipSounds.Add(clip, s);
        }
        else
        {
            s.volume = Mathf.Clamp01(volume);
        }
        return s;
    }

    private Sound Find(string id)
    {
        if (library.TryGetValue(id, out Sound s)) return s;
        Debug.LogWarning($"[AudioHelper] Sound dengan id \"{id}\" tidak ada di Sound Library.");
        return null;
    }

    // ================================================================
    //  1) SEKALI SAJA (ONE-SHOT)
    // ================================================================

    public void PlaySFX(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (clip == null)
        {
            Debug.LogWarning("[AudioHelper] PlaySFX dipanggil dengan clip kosong (lupa drag clip di Inspector?).");
            return;
        }
        AudioSource src = GetFreeSfxSource();
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume) * sfxVolume * masterVolume;
        src.pitch = pitch;
        src.Play();
    }

    public void PlaySFX(Sound sound) { PlaySound(sound); }

    public void PlaySFX(string id) { PlaySound(Find(id)); }

    /// <summary>
    /// Untuk jingle penting (Game Win, Game Over, Level Up).
    /// Musik latar otomatis mengecil selama jingle berbunyi.
    /// </summary>
    public void PlayStinger(AudioClip clip, float volume = 1f, bool duckMusic = true)
    {
        PlayStinger(Wrap(clip, volume), duckMusic);
    }

    public void PlayStinger(string id, bool duckMusic = true)
    {
        PlayStinger(Find(id), duckMusic);
    }

    public void PlayStinger(Sound sound, bool duckMusic = true)
    {
        AudioSource src = PlaySound(sound);
        if (src == null || !duckMusic) return;

        float duration = src.clip.length / Mathf.Max(0.01f, src.pitch);
        StartCoroutine(DuckRoutine(duration));
    }

    private AudioSource PlaySound(Sound s)
    {
        if (s == null) return null;

        AudioClip clip = s.GetClip();
        if (clip == null)
        {
            Debug.LogWarning("[AudioHelper] Sound belum punya clip (cek Inspector, mungkin lupa drag).");
            return null;
        }

        AudioSource src = GetFreeSfxSource();
        src.clip = clip;
        src.volume = s.volume * sfxVolume * masterVolume;
        src.pitch = s.GetPitch();
        src.Play();
        return src;
    }

    private AudioSource GetFreeSfxSource()
    {
        foreach (AudioSource src in sfxPool)
            if (!src.isPlaying) return src;

        if (sfxPool.Count < maxSfxSources) return CreateSfxSource();

        // pool penuh: ambil alih source secara bergiliran
        AudioSource stolen = sfxPool[nextStealIndex % sfxPool.Count];
        nextStealIndex++;
        stolen.Stop();
        return stolen;
    }

    private IEnumerator DuckRoutine(float duration)
    {
        duckCount++;
        duckMultiplier = duckedMusicMultiplier;
        RefreshVolumes();

        yield return new WaitForSecondsRealtime(duration);

        duckCount = Mathf.Max(0, duckCount - 1);
        if (duckCount == 0)
        {
            duckMultiplier = 1f;
            RefreshVolumes();
        }
    }

    // ================================================================
    //  2) BERULANG (REPEATING) - langkah kaki, tembakan otomatis
    // ================================================================

    /// <summary>randomInterval = variasi acak +/- supaya tidak terdengar mekanis.</summary>
    public void StartRepeating(Sound sound, float interval, float randomInterval = 0f)
    {
        if (sound == null || activeRepeaters.ContainsKey(sound)) return;
        activeRepeaters.Add(sound, StartCoroutine(RepeatRoutine(sound, interval, randomInterval)));
    }

    public void StartRepeating(AudioClip clip, float interval, float randomInterval = 0f, float volume = 1f)
    {
        StartRepeating(Wrap(clip, volume), interval, randomInterval);
    }

    public void StartRepeating(string id, float interval, float randomInterval = 0f)
    {
        StartRepeating(Find(id), interval, randomInterval);
    }

    public void StopRepeating(Sound sound)
    {
        if (sound == null || !activeRepeaters.TryGetValue(sound, out Coroutine routine)) return;
        StopCoroutine(routine);
        activeRepeaters.Remove(sound);
    }

    public void StopRepeating(AudioClip clip)
    {
        if (clip != null && clipSounds.TryGetValue(clip, out Sound s)) StopRepeating(s);
    }

    public void StopRepeating(string id)
    {
        if (library.TryGetValue(id, out Sound s)) StopRepeating(s);
    }

    /// <summary>
    /// Praktis dipanggil tiap frame dari Update():
    /// AudioHelper.Instance.SetRepeating(footstep, isMoving &amp;&amp; isGrounded, 0.4f);
    /// </summary>
    public void SetRepeating(Sound sound, bool active, float interval, float randomInterval = 0.05f)
    {
        if (active) StartRepeating(sound, interval, randomInterval);
        else StopRepeating(sound);
    }

    public void SetRepeating(AudioClip clip, bool active, float interval, float randomInterval = 0.05f)
    {
        if (active) StartRepeating(clip, interval, randomInterval);
        else StopRepeating(clip);
    }

    public void StopAllRepeating()
    {
        foreach (Coroutine routine in activeRepeaters.Values) StopCoroutine(routine);
        activeRepeaters.Clear();
    }

    private IEnumerator RepeatRoutine(Sound s, float interval, float randomInterval)
    {
        while (true)
        {
            PlaySound(s);
            float wait = interval + Random.Range(-randomInterval, randomInterval);
            yield return new WaitForSeconds(Mathf.Max(0.05f, wait));
        }
    }

    // ================================================================
    //  3) LOOP SFX (ambience, mesin, api, hujan)
    // ================================================================

    public void PlayLoop(AudioClip clip, float volume = 1f, float fadeIn = 0.25f)
    {
        if (clip == null)
        {
            Debug.LogWarning("[AudioHelper] PlayLoop dipanggil dengan clip kosong (lupa drag clip di Inspector?).");
            return;
        }
        PlayLoop(Wrap(clip, volume), fadeIn);
    }

    public void PlayLoop(string id, float fadeIn = 0.25f)
    {
        PlayLoop(Find(id), fadeIn);
    }

    /// <summary>Mulai loop. Kalau sound yang sama sudah berjalan, diabaikan.</summary>
    public void PlayLoop(Sound sound, float fadeIn = 0.25f)
    {
        if (sound == null || activeLoops.ContainsKey(sound)) return;

        AudioClip clip = sound.GetClip();
        if (clip == null) return;

        AudioSource src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = true;
        src.clip = clip;
        src.pitch = sound.GetPitch();

        LoopInfo info = new LoopInfo
        {
            source = src,
            baseVolume = sound.volume,
            level = fadeIn > 0f ? 0f : 1f
        };

        ApplyLoopVolume(info);
        src.Play();
        activeLoops.Add(sound, info);

        if (fadeIn > 0f) info.fade = StartCoroutine(FadeLoop(info, 1f, fadeIn, false));
    }

    public void StopLoop(Sound sound, float fadeOut = 0.25f)
    {
        if (sound == null || !activeLoops.TryGetValue(sound, out LoopInfo info)) return;
        activeLoops.Remove(sound);

        if (info.fade != null) StopCoroutine(info.fade);

        if (fadeOut > 0f) info.fade = StartCoroutine(FadeLoop(info, 0f, fadeOut, true));
        else Destroy(info.source);
    }

    public void StopLoop(AudioClip clip, float fadeOut = 0.25f)
    {
        if (clip != null && clipSounds.TryGetValue(clip, out Sound s)) StopLoop(s, fadeOut);
    }

    public void StopLoop(string id, float fadeOut = 0.25f)
    {
        if (library.TryGetValue(id, out Sound s)) StopLoop(s, fadeOut);
    }

    public bool IsLoopPlaying(Sound sound) => sound != null && activeLoops.ContainsKey(sound);

    public bool IsLoopPlaying(AudioClip clip) =>
        clip != null && clipSounds.TryGetValue(clip, out Sound s) && activeLoops.ContainsKey(s);

    public void StopAllLoops(float fadeOut = 0.25f)
    {
        List<Sound> keys = new List<Sound>(activeLoops.Keys);
        foreach (Sound s in keys) StopLoop(s, fadeOut);
    }

    private IEnumerator FadeLoop(LoopInfo info, float target, float duration, bool destroyAtEnd)
    {
        float start = info.level;
        float t = 0f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            info.level = Mathf.Lerp(start, target, t / duration);
            ApplyLoopVolume(info);
            yield return null;
        }

        info.level = target;
        ApplyLoopVolume(info);
        info.fade = null;

        if (destroyAtEnd) Destroy(info.source);
    }

    private void ApplyLoopVolume(LoopInfo info)
    {
        if (info.source != null)
            info.source.volume = info.level * info.baseVolume * sfxVolume * masterVolume;
    }

    // ================================================================
    //  4) MUSIK (panjang + loop + crossfade)
    // ================================================================

    /// <summary>Mainkan musik. Musik sebelumnya di-crossfade. Lagu yang sama diabaikan.</summary>
    public void PlayMusic(AudioClip clip, float fade = 1f, float volume = 1f)
    {
        if (clip == null) return;

        if (activeMusic.source.clip == clip && activeMusic.source.isPlaying) return;

        if (musicFadeRoutine != null) StopCoroutine(musicFadeRoutine);

        MusicChannel outgoing = activeMusic;
        MusicChannel incoming = (activeMusic == channelA) ? channelB : channelA;

        // channel yang akan dipakai dibersihkan dulu (kalau tadi masih fade-out)
        incoming.source.Stop();
        incoming.source.clip = clip;
        incoming.source.loop = true;
        incoming.baseVolume = Mathf.Clamp01(volume);
        incoming.level = fade > 0f ? 0f : 1f;
        ApplyMusicVolume(incoming);
        incoming.source.Play();

        activeMusic = incoming;

        if (fade > 0f)
        {
            musicFadeRoutine = StartCoroutine(CrossFadeRoutine(outgoing, incoming, fade));
        }
        else
        {
            outgoing.level = 0f;
            outgoing.source.Stop();
            outgoing.source.clip = null;
        }
    }

    public void PlayMusic(Sound sound, float fade = 1f)
    {
        if (sound == null) return;
        PlayMusic(sound.GetClip(), fade, sound.volume);
    }

    public void PlayMusic(string id, float fade = 1f)
    {
        PlayMusic(Find(id), fade);
    }

    public void StopMusic(float fade = 1f)
    {
        if (musicFadeRoutine != null) StopCoroutine(musicFadeRoutine);

        if (fade > 0f) musicFadeRoutine = StartCoroutine(FadeOutRoutine(activeMusic, fade));
        else
        {
            activeMusic.level = 0f;
            activeMusic.source.Stop();
        }
    }

    public void PauseMusic() => activeMusic.source.Pause();
    public void ResumeMusic() => activeMusic.source.UnPause();

    private IEnumerator CrossFadeRoutine(MusicChannel from, MusicChannel to, float duration)
    {
        float fromStart = from.level;
        float t = 0f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime; // tetap jalan walau Time.timeScale = 0
            float k = Mathf.Clamp01(t / duration);
            to.level = k;
            from.level = fromStart * (1f - k);
            ApplyMusicVolume(to);
            ApplyMusicVolume(from);
            yield return null;
        }

        to.level = 1f;
        from.level = 0f;
        ApplyMusicVolume(to);
        from.source.Stop();
        from.source.clip = null;
        musicFadeRoutine = null;
    }

    private IEnumerator FadeOutRoutine(MusicChannel ch, float duration)
    {
        float start = ch.level;
        float t = 0f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            ch.level = Mathf.Lerp(start, 0f, t / duration);
            ApplyMusicVolume(ch);
            yield return null;
        }

        ch.level = 0f;
        ch.source.Stop();
        musicFadeRoutine = null;
    }

    private void ApplyMusicVolume(MusicChannel ch)
    {
        ch.source.volume = ch.level * ch.baseVolume * musicVolume * masterVolume * duckMultiplier;
    }

    // ================================================================
    //  UTIL
    // ================================================================

    private void RefreshVolumes()
    {
        if (channelA != null) ApplyMusicVolume(channelA);
        if (channelB != null) ApplyMusicVolume(channelB);
        foreach (LoopInfo info in activeLoops.Values) ApplyLoopVolume(info);
    }

    /// <summary>Hentikan semua suara (berguna saat pindah scene / kembali ke menu).</summary>
    public void StopAll(float fade = 0.25f)
    {
        StopAllRepeating();
        StopAllLoops(fade);
        StopMusic(fade);
        foreach (AudioSource src in sfxPool) src.Stop();
    }
}