using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Memutar animasi ayam tanpa Animator Controller (pakai Playables).
/// - Run : saat ayam berjalan (kecepatan animasi mengikuti kecepatan ayam)
/// - Fly : saat dibawa / dilempar (loop kepakan sayap selama masih di udara / dibawa)
/// - Idle: saat berhenti (pose Run frame pertama dibekukan), atau Fly kalau Idle Uses Fly dicentang
///
/// PASANG di GameObject yang punya Animator (instance model FBX ayam, bisa root prefab atau child).
/// ChickenWander akan menemukannya otomatis lewat GetComponentInChildren.
/// </summary>
[RequireComponent(typeof(Animator))]
public class ChickenAnimation : MonoBehaviour
{
    public enum Mode { Idle, Run, Fly }

    [Header("Clips (drag dari FBX LowPolyChicken)")]
    [SerializeField] private AnimationClip runClip;
    [SerializeField] private AnimationClip flyClip;

    [Header("Blend")]
    [SerializeField] private float fadeTime = 0.12f;

    [Header("Run")]
    [Tooltip("Kecepatan ayam (m/s) di mana animasi Run diputar 1x. Kaki menapak bergerak ~1.7 m/s pada 1x, " +
             "jadi nilai 1.7 membuat kaki 'menempel' di tanah. Makin kecil = langkah makin cepat.")]
    [SerializeField] private float runReferenceSpeed = 1.7f;
    [SerializeField] private Vector2 runSpeedClamp = new Vector2(0.6f, 4f);
    [Tooltip("Centang kalau gerak kaki masih terlihat seperti berjalan mundur (memutar Run terbalik).")]
    [SerializeField] private bool reverseRun = false;

    [Header("Fly")]
    [Tooltip("Frame 6 s/d ~35 pada Thrown_Fly = kepakan sayap yang bisa di-loop mulus. Jangan diubah kecuali klip diganti.")]
    [SerializeField] private float flyLoopStart = 0.2f;
    [SerializeField] private float flyLoopLength = 0.983f;

    [Header("Idle")]
    [Tooltip("Kalau dicentang, ayam yang sedang berhenti ikut memainkan Fly. Default: pose berdiri (Run dibekukan).")]
    [SerializeField] private bool idleUsesFly = false;

    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationClipPlayable runPlayable, flyPlayable;

    private Mode mode = Mode.Idle;
    private float runSpeed = 1f;
    private float runTime, flyTime, flyWeight;
    private bool wasFlying;
    private bool ready;

    private void Awake()
    {
        if (runClip == null || flyClip == null)
        {
            Debug.LogWarning($"{name}: Run Clip / Fly Clip belum diisi di ChickenAnimation.", this);
            enabled = false;
            return;
        }

        var animator = GetComponent<Animator>();
        animator.applyRootMotion = false; // gerak dikendalikan Rigidbody, bukan animasi

        graph = PlayableGraph.Create("ChickenAnimation");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

        mixer = AnimationMixerPlayable.Create(graph, 2);
        runPlayable = AnimationClipPlayable.Create(graph, runClip);
        flyPlayable = AnimationClipPlayable.Create(graph, flyClip);
        runPlayable.SetSpeed(0); // waktu diatur manual di Update
        flyPlayable.SetSpeed(0);

        graph.Connect(runPlayable, 0, mixer, 0);
        graph.Connect(flyPlayable, 0, mixer, 1);
        mixer.SetInputWeight(0, 1f);
        mixer.SetInputWeight(1, 0f);

        var output = AnimationPlayableOutput.Create(graph, "Chicken", animator);
        output.SetSourcePlayable(mixer);

        graph.Play();
        ready = true;
    }

    /// <summary>Dipanggil tiap frame oleh ChickenWander. moveSpeed hanya dipakai untuk Run.</summary>
    public void Play(Mode newMode, float moveSpeed = 0f)
    {
        mode = newMode;
        if (newMode == Mode.Run)
            runSpeed = Mathf.Clamp(moveSpeed / Mathf.Max(0.01f, runReferenceSpeed), runSpeedClamp.x, runSpeedClamp.y);
    }

    private void Update()
    {
        if (!ready) return;
        float dt = Time.deltaTime;

        bool flying = mode == Mode.Fly || (mode == Mode.Idle && idleUsesFly);
        if (flying && !wasFlying) flyTime = 0f; // mulai Fly dari awal (sayap membuka)
        wasFlying = flying;

        // Crossfade Run <-> Fly
        flyWeight = Mathf.MoveTowards(flyWeight, flying ? 1f : 0f, dt / Mathf.Max(0.01f, fadeTime));
        mixer.SetInputWeight(0, 1f - flyWeight);
        mixer.SetInputWeight(1, flyWeight);

        // Waktu Run
        float runLen = runClip.length;
        if (mode == Mode.Run)
        {
            runTime = Mathf.Repeat(runTime + dt * runSpeed * (reverseRun ? -1f : 1f), runLen);
        }
        else
        {
            // berhenti: ayunkan ke pose berdiri (frame awal/akhir loop) lalu diam
            float target = runTime > runLen * 0.5f ? runLen : 0f;
            runTime = Mathf.MoveTowards(runTime, target, dt * runLen * 3f);
            if (Mathf.Approximately(runTime, runLen)) runTime = 0f;
        }

        // Waktu Fly: mainkan dari awal, lalu loop bagian kepakan selama masih terbang/dibawa
        if (flying)
        {
            flyTime += dt;
            float loopEnd = flyLoopStart + flyLoopLength;
            if (flyTime >= loopEnd) flyTime -= flyLoopLength;
            flyTime = Mathf.Min(flyTime, flyClip.length);
        }

        runPlayable.SetTime(runTime);
        flyPlayable.SetTime(flyTime);
        graph.Evaluate(0f);
    }

    private void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }
}