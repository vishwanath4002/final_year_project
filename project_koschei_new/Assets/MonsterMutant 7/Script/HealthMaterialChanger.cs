using UnityEngine;

public class HealthMaterialChanger : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Health health;

    [Header("Materials (Stage1 = Full HP → Stage4 = Critical)")]
    [SerializeField] private Material stage1Material;
    [SerializeField] private Material stage2Material;
    [SerializeField] private Material stage3Material;
    [SerializeField] private Material stage4Material;

    [Header("Health Thresholds")]
    [Range(0f, 1f)] public float stage2Threshold = 0.75f;
    [Range(0f, 1f)] public float stage3Threshold = 0.5f;
    [Range(0f, 1f)] public float stage4Threshold = 0.25f;

    private Renderer[] childRenderers;
    private int currentStage = 1;

    private void Awake()
    {
        childRenderers = GetComponentsInChildren<Renderer>();
    }

    private void Start()
    {
        if (health != null)
        {
            UpdateMaterial();
        }

        HealthMaterialChanger changer = GetComponent<HealthMaterialChanger>();
        if (changer != null)
        {
            changer.UpdateMaterial();
        }
    }

    public void UpdateMaterial()
    {
        if (health == null) return;

        float healthPercent = health.GetCurrentHealth() / health.GetMaxHealth();

        int newStage = GetStageFromHealth(healthPercent);

        if (newStage == currentStage) return; // Avoid unnecessary changes

        currentStage = newStage;

        Material targetMat = GetMaterialFromStage(newStage);

        foreach (Renderer rend in childRenderers)
        {
            rend.material = targetMat;
        }
    }

    private int GetStageFromHealth(float percent)
    {
        if (percent >= stage2Threshold)
            return 1;   // 75–100%

        if (percent >= stage3Threshold)
            return 2;   // 50–75%

        if (percent >= stage4Threshold)
            return 3;   // 25–50%

        return 4;       // 0–25%
    }

    private Material GetMaterialFromStage(int stage)
    {
        switch (stage)
        {   
            case 1: return stage1Material; // Healthy
            case 2: return stage2Material; // Slight damage
            case 3: return stage3Material; // Heavy damage
            case 4: return stage4Material; // Critical
            default: return stage1Material;
        }
    }
}