using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Animasi Player tanpa Animator Controller (pakai Playables), sama polanya dengan ChickenAnimation.
/// - Idle / Run / Jump : layer bawah, otomatis dari Rigidbody + PlayerController.IsGrounded
/// - Catch Down / Mid / Up : layer atas (pakai Avatar Mask supaya hanya badan atas), dipicu ChickenCarrier.OnCatch
///
/// PASANG di GameObject model yang punya Animator (child dari Player). Controller Animator dikosongkan.
/// </summary>
[RequireComponent(typeof(Animator))]
public class PlayerAnimation : MonoBehaviour
{
    [Header("Clips (drag dari FBX player)")]
    [SerializeField] private AnimationClip idleClip;
    [SerializeField] private AnimationClip runClip;
    [SerializeField] private AnimationClip jumpClip;
    [SerializeField] private AnimationClip catchDownClip;
    [SerializeField] private AnimationClip catchMidClip;
    [SerializeField] private AnimationClip catchUpClip;

    [Header("Catch (badan atas)")]
    [Tooltip("Opsional tapi disarankan: Avatar Mask dengan Hips + kaki (UpperLeg/LowerLeg/Foot) dimatikan. " +
             "Kosong = catch menimpa seluruh badan.")]
    [SerializeField] private AvatarMask upperBodyMask;
    [SerializeField] private float catchSpeed = 1f;
    [SerializeField] private float catchFadeTime = 0.1f;

    [Header("Locomotion")]
    [SerializeField] private float fadeTime = 0.12f;
    [Tooltip("Kecepatan datar (m/s) di atas nilai ini = Run.")]
    [SerializeField] private float runThreshold = 0.5f;
    [Tooltip("Kecepatan player (m/s) di mana animasi Run diputar 1x. Kalau kaki terlihat meluncur, ubah nilai ini " +
             "(makin kecil = langkah makin cepat).")]
    [SerializeField] private float runReferenceSpeed = 6f;
    [SerializeField] private Vector2 runSpeedClamp = new Vector2(0.5f, 2.5f);
    [Tooltip("Toleransi singkat agar tidak 'berkedip' ke Jump saat melewati tonjolan kecil.")]
    [SerializeField] private float groundedGrace = 0.08f;

    private const int Idle = 0, Run = 1, Jump = 2;

    private PlayableGraph graph;
    private AnimationMixerPlayable locoMixer, catchMixer;
    private AnimationLayerMixerPlayable layerMixer;
    private AnimationClipPlayable idleP, runP, jumpP;
    private AnimationClipPlayable[] catchP = new AnimationClipPlayable[3];
    private AnimationClip[] catchClips;

    private Rigidbody rb;
    private PlayerController controller;
    private ChickenCarrier carrier;

    private readonly float[] locoWeight = new float[3];
    private float idleTime, runTime, jumpTime;
    private bool wasJumping;
    private float lastGroundedTime;

    private bool catchActive;
    private int catchIndex;
    private float catchTime;
    private bool ready;

    private void Awake()
    {
        if (idleClip == null || runClip == null || jumpClip == null ||
            catchDownClip == null || catchMidClip == null || catchUpClip == null)
        {
            Debug.LogWarning($"{name}: Ada clip yang belum diisi di PlayerAnimation.", this);
            enabled = false;
            return;
        }

        rb = GetComponentInParent<Rigidbody>();
        controller = GetComponentInParent<PlayerController>();
        carrier = GetComponentInParent<ChickenCarrier>();

        var animator = GetComponent<Animator>();
        animator.applyRootMotion = false; // gerak dikendalikan Rigidbody

        catchClips = new[] { catchDownClip, catchMidClip, catchUpClip };

        graph = PlayableGraph.Create("PlayerAnimation");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

        // Layer bawah: Idle / Run / Jump
        locoMixer = AnimationMixerPlayable.Create(graph, 3);
        idleP = AnimationClipPlayable.Create(graph, idleClip);
        runP  = AnimationClipPlayable.Create(graph, runClip);
        jumpP = AnimationClipPlayable.Create(graph, jumpClip);
        ConnectManual(idleP, locoMixer, Idle);
        ConnectManual(runP,  locoMixer, Run);
        ConnectManual(jumpP, locoMixer, Jump);
        locoWeight[Idle] = 1f;
        ApplyLocoWeights();

        // Layer atas: Catch Down / Mid / Up
        catchMixer = AnimationMixerPlayable.Create(graph, 3);
        for (int i = 0; i < 3; i++)
        {
            catchP[i] = AnimationClipPlayable.Create(graph, catchClips[i]);
            ConnectManual(catchP[i], catchMixer, i);
        }

        // Gabungkan: layer 0 = locomotion, layer 1 = catch (override, bisa pakai mask)
        layerMixer = AnimationLayerMixerPlayable.Create(graph, 2);
        graph.Connect(locoMixer, 0, layerMixer, 0);
        graph.Connect(catchMixer, 0, layerMixer, 1);
        layerMixer.SetInputWeight(0, 1f);
        layerMixer.SetInputWeight(1, 0f);
        if (upperBodyMask != null) layerMixer.SetLayerMaskFromAvatarMask(1, upperBodyMask);

        var output = AnimationPlayableOutput.Create(graph, "Player", animator);
        output.SetSourcePlayable(layerMixer);

        graph.Play();
        ready = true;
    }

    private void ConnectManual(AnimationClipPlayable p, AnimationMixerPlayable mixer, int port)
    {
        p.SetSpeed(0); // waktu diatur manual di Update
        graph.Connect(p, 0, mixer, port);
    }

    private void OnEnable()  { if (carrier != null) carrier.OnCatch += PlayCatch; }
    private void OnDisable() { if (carrier != null) carrier.OnCatch -= PlayCatch; }

    /// <summary>0 = Down, 1 = Mid, 2 = Up. Dipanggil otomatis lewat ChickenCarrier.OnCatch.</summary>
    public void PlayCatch(int type)
    {
        if (!ready) return;
        catchIndex = Mathf.Clamp(type, 0, 2);
        catchTime = 0f;
        catchActive = true;
        for (int i = 0; i < 3; i++)
            catchMixer.SetInputWeight(i, i == catchIndex ? 1f : 0f);
    }

    private void Update()
    {
        if (!ready) return;
        float dt = Time.deltaTime;

        // ---------- Status ----------
        float flatSpeed = 0f;
        if (rb != null)
        {
            Vector3 v = rb.linearVelocity;
            flatSpeed = new Vector2(v.x, v.z).magnitude;
        }
        if (controller == null || controller.IsGrounded) lastGroundedTime = Time.time;
        bool airborne = Time.time - lastGroundedTime > groundedGrace;

        // ---------- Pilih target locomotion ----------
        int target = airborne ? Jump : (flatSpeed > runThreshold ? Run : Idle);
        bool jumping = target == Jump;
        if (jumping && !wasJumping) jumpTime = 0f; // lompat mulai dari frame awal
        wasJumping = jumping;

        // Crossfade (dinormalisasi supaya total bobot = 1)
        float step = dt / Mathf.Max(0.01f, fadeTime);
        float sum = 0f;
        for (int i = 0; i < 3; i++)
        {
            locoWeight[i] = Mathf.MoveTowards(locoWeight[i], i == target ? 1f : 0f, step);
            sum += locoWeight[i];
        }
        if (sum > 0.0001f) for (int i = 0; i < 3; i++) locoWeight[i] /= sum;
        ApplyLocoWeights();

        // ---------- Waktu clip ----------
        idleTime = Mathf.Repeat(idleTime + dt, idleClip.length);

        float runMul = Mathf.Clamp(flatSpeed / Mathf.Max(0.01f, runReferenceSpeed), runSpeedClamp.x, runSpeedClamp.y);
        runTime = Mathf.Repeat(runTime + dt * runMul, runClip.length);

        jumpTime = Mathf.Min(jumpTime + dt, jumpClip.length); // berhenti di frame terakhir sampai mendarat

        idleP.SetTime(idleTime);
        runP.SetTime(runTime);
        jumpP.SetTime(jumpTime);

        // ---------- Catch (layer atas) ----------
        float catchW = 0f;
        if (catchActive)
        {
            var clip = catchClips[catchIndex];
            catchTime += dt * catchSpeed;
            float fade = Mathf.Max(0.01f, catchFadeTime);
            if (catchTime >= clip.length)
            {
                catchActive = false;
            }
            else
            {
                catchW = Mathf.Clamp01(Mathf.Min(catchTime / fade, (clip.length - catchTime) / fade));
                catchP[catchIndex].SetTime(catchTime);
            }
        }
        layerMixer.SetInputWeight(1, catchW);

        graph.Evaluate(0f);
    }

    private void ApplyLocoWeights()
    {
        for (int i = 0; i < 3; i++) locoMixer.SetInputWeight(i, locoWeight[i]);
    }

    private void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }
}