using UnityEngine;
using UnityEngine.InputSystem; // New Input System

/// <summary>
/// Pasang di Player.
/// - E (tidak membawa ayam)  : angkat ayam terdekat dalam jarak Pickup Radius -> ayam di atas kepala player
/// - E (sedang membawa ayam) : lempar melengkung (parabola) ke arah kamera
/// Selama membawa ayam, tidak bisa mengambil ayam lain.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class ChickenCarrier : MonoBehaviour
{
    [Header("Pickup")]
    [SerializeField] private float pickupRadius = 1.8f;
    [Tooltip("Posisi ayam saat dibawa, relatif terhadap Player. Y=1.5 = tepat di atas kepala kapsul default.")]
    [SerializeField] private Vector3 holdOffset = new Vector3(0f, 1.5f, 0f);

    [Header("Throw")]
    [Tooltip("Kecepatan awal lemparan (m/s). Makin besar = makin jauh.")]
    [SerializeField] private float throwSpeed = 10f;
    [Tooltip("Sudut lemparan ke atas (derajat). 45 = paling jauh, 30-40 = seperti tendangan bola.")]
    [Range(5f, 80f)]
    [SerializeField] private float throwAngle = 40f;

    [Header("Audio (drag clip di sini)")]
    [Tooltip("Dimainkan 1x setiap melempar ayam.")]
    public AudioClip throwClip;
    [Range(0f, 1f)] public float throwVolume = 1f;

    [Header("Catch Animation")]
    [Tooltip("Tinggi ayam dari kaki player: di bawah nilai ini = Down.")]
    [SerializeField] private float catchDownMaxHeight = 0.6f;
    [Tooltip("Di bawah nilai ini = Mid, di atasnya = Up.")]
    [SerializeField] private float catchMidMaxHeight = 1.4f;

    public event System.Action<int> OnCatch; 

    [Header("Debug")]
    [SerializeField] private bool showDebug = true;

    private Transform cam;
    private Transform holdPoint;
    private ChickenWander carried;
    private readonly Collider[] overlapBuffer = new Collider[32];

    public bool IsCarrying => carried != null;

    private void Awake()
    {
        var go = new GameObject("ChickenHoldPoint");
        holdPoint = go.transform;
        holdPoint.SetParent(transform, false);
        holdPoint.localPosition = holdOffset;
    }

    private void Start()
    {
        cam = Camera.main != null ? Camera.main.transform : null;
    }

    private void Update()
    {
        holdPoint.localPosition = holdOffset; // supaya perubahan di Inspector langsung terlihat

        var kb = Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;

        if (IsCarrying) ThrowChicken();
        else TryPickUp();
    }

    private void TryPickUp()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, pickupRadius, overlapBuffer,
                                                  Physics.AllLayers, QueryTriggerInteraction.Ignore);

        ChickenWander best = null;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            var chicken = overlapBuffer[i].GetComponentInParent<ChickenWander>();
            if (chicken == null || !chicken.CanBePickedUp) continue;

            float sqr = (chicken.transform.position - transform.position).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = chicken; }
        }

        if (best == null) return;

        // Hitung tipe catch dari tinggi ayam relatif terhadap kaki player
        float feetY = GetComponent<Collider>().bounds.min.y;
        float rel = best.transform.position.y - feetY;
        int type = rel < catchDownMaxHeight ? 0 : (rel < catchMidMaxHeight ? 1 : 2);

        best.PickUp(holdPoint);
        carried = best;
        OnCatch?.Invoke(type);
    }

    private void ThrowChicken()
    {
        // Arah lempar = arah kamera (diratakan), seperti arah "maju" saat WASD
        Vector3 forward = cam != null ? cam.forward : transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = transform.forward;
        forward.Normalize();

        float rad = throwAngle * Mathf.Deg2Rad;
        Vector3 dir = forward * Mathf.Cos(rad) + Vector3.up * Mathf.Sin(rad);
        // Jarak lemparan TETAP: tidak dipengaruhi kecepatan lari Player
        Vector3 velocity = dir * throwSpeed;

        carried.Throw(velocity);
        carried = null;

        // Audio: lempar 1x
        if (AudioHelper.Instance != null)
            AudioHelper.Instance.PlaySFX(throwClip, throwVolume);
    }

    private void OnGUI()
    {
        if (!showDebug) return;
        GUI.Label(new Rect(10, 32, 400, 25), IsCarrying ? "Membawa ayam  -  [E] Lempar" : "[E] Ambil ayam terdekat");
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, pickupRadius);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.TransformPoint(holdOffset), 0.25f);
    }
}