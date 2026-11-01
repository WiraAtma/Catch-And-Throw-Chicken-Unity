using System;
using UnityEngine;

/// <summary>
/// Kandang ayam. Pasang di GameObject kandang.
/// Ayam yang DILEMPAR lalu mendarat dan berhenti di dalam Zone dianggap masuk kandang (+1 poin).
/// Ayam yang sudah masuk tidak bisa diangkat lagi dan tetap berkeliaran di dalam kandang.
/// Skor = ayam di kandang / total ayam (misal 3/10).
/// </summary>
public class ChickenPen : MonoBehaviour
{
    [Header("Zone (meter, relatif terhadap pivot object ini; scale object diabaikan)")]
    [SerializeField] private Vector3 zoneCenter = new Vector3(0f, 1f, 0f);
    [SerializeField] private Vector3 zoneSize = new Vector3(4f, 2f, 4f);
    [Tooltip("Ayam harus berada sejauh ini di dalam tepi zone (X/Z) agar dihitung.")]
    [SerializeField] private float scorePadding = 0.2f;

    [Header("Rules")]
    [Tooltip("Aktif: hanya ayam yang dilempar yang dihitung. Nonaktif: ayam yang berjalan masuk sendiri juga dihitung.")]
    [SerializeField] private bool requireThrown = true;

    [Header("HUD")]
    [SerializeField] private bool showHud = false;

    /// <summary>Dipanggil saat skor berubah: (jumlah di kandang, total ayam). Bisa dihubungkan ke UI TextMeshPro.</summary>
    public event Action<int, int> OnScoreChanged;

    public int PennedCount { get; private set; }
    public int TotalCount { get; private set; }
    public bool IsComplete => TotalCount > 0 && PennedCount >= TotalCount;
    public Vector3 Center => transform.position + transform.rotation * zoneCenter;

    private GUIStyle hudStyle;

    private void Update()
    {
        var all = ChickenWander.All;
        int penned = 0;

        for (int i = 0; i < all.Count; i++)
        {
            ChickenWander chicken = all[i];

            if (chicken.Pen == this) { penned++; continue; }

            // Hanya ayam yang sudah mendarat & berhenti (state Wander), belum di kandang
            if (!chicken.CanBePickedUp) continue;
            if (requireThrown && !chicken.WasThrown) continue;

            if (Contains(chicken.transform.position, scorePadding))
            {
                chicken.EnterPen(this);
                penned++;
            }
        }

        if (penned != PennedCount || all.Count != TotalCount)
        {
            PennedCount = penned;
            TotalCount = all.Count;
            OnScoreChanged?.Invoke(PennedCount, TotalCount);
        }
    }

    /// <summary>Apakah titik dunia berada di dalam zone. padding mengecilkan zone pada sumbu X/Z.</summary>
    public bool Contains(Vector3 worldPoint, float padding = 0f)
    {
        Vector3 local = Quaternion.Inverse(transform.rotation) * (worldPoint - transform.position) - zoneCenter;
        Vector3 half = zoneSize * 0.5f;
        return Mathf.Abs(local.x) <= half.x - padding
            && Mathf.Abs(local.z) <= half.z - padding
            && Mathf.Abs(local.y) <= half.y;
    }

    public bool ContainsForScoring(Vector3 worldPoint) => Contains(worldPoint, scorePadding);

    private void OnGUI()
    {
        if (!showHud) return;

        if (hudStyle == null)
        {
            hudStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 32,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter
            };
            hudStyle.normal.textColor = Color.white;
        }

    }

    // Klik kanan judul komponen > "Fit Zone X/Z To Renderer Bounds" untuk mencocokkan lebar zone dengan model kandang
    [ContextMenu("Fit Zone X/Z To Renderer Bounds")]
    private void FitZoneToBounds()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogWarning("ChickenPen: tidak ada Renderer untuk dijadikan acuan ukuran.", this);
            return;
        }

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

        Vector3 localCenter = Quaternion.Inverse(transform.rotation) * (b.center - transform.position);
        zoneCenter = new Vector3(localCenter.x, zoneCenter.y, localCenter.z);
        zoneSize = new Vector3(b.size.x, zoneSize.y, b.size.z);

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    private void OnDrawGizmos()
    {
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.15f);
        Gizmos.DrawCube(zoneCenter, zoneSize);
        Gizmos.color = new Color(0.2f, 1f, 0.3f, 1f);
        Gizmos.DrawWireCube(zoneCenter, zoneSize);
    }
}