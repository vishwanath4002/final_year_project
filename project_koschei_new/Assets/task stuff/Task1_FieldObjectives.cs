using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class Task1_FieldObjectives : NetworkBehaviour, IGameTask
{
    [Header("Zone References")]
    public FirewoodDeliveryZone firewoodZone;
    public CanDeliveryZone canZone;

    [Header("Timer")]
    public float taskDuration = 300f;

    [Header("Penalty")]
    public PenaltyScavengerSpawner penaltySpawner;

    public event Action OnTaskCompleted;
    public event Action OnTaskFailed;

    private bool mushroomsComplete = false;
    private bool cansComplete = false;
    private bool taskActive = false;
    private Coroutine timerCoroutine;

    // ================================================================

    public void StartTask()
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[Task1] StartTask called on client -- ignored.");
            return;
        }
        if (taskActive)
        {
            Debug.LogWarning("[Task1] Already running.");
            return;
        }

        if (firewoodZone == null) Debug.LogError("[Task1] FirewoodZone not assigned!");
        if (canZone == null) Debug.LogError("[Task1] CanZone not assigned!");

        taskActive = true;
        mushroomsComplete = false;
        cansComplete = false;

        // Subscribe to zone events
        firewoodZone.OnWoodProgressChanged += HandleWoodProgress;
        firewoodZone.OnFireLit += HandleFireLit;
        firewoodZone.OnMushroomProgressChanged += HandleMushroomProgress;
        firewoodZone.OnMushroomsComplete += HandleMushroomsComplete;
        canZone.OnCanProgressChanged += HandleCanProgress;
        canZone.OnCansComplete += HandleCansComplete;

        // Activate zone markers
        firewoodZone.ActivateTask();
        canZone.ActivateTask();

        // Show initial HUD on all clients
        ShowHudClientRpc(
            firewoodZone.GetRequiredWood(),
            firewoodZone.GetRequiredMushrooms(),
            canZone.GetRequiredCans()
        );

        timerCoroutine = StartCoroutine(TaskTimerRoutine());

        Debug.Log($"[Task1] Started. Timer: {taskDuration}s");
    }

    public void EndTask()
    {
        if (!taskActive) return;
        CleanUp();
        Debug.Log("[Task1] Force-ended.");
    }

    // ================================================================
    // Timer
    // ================================================================

    IEnumerator TaskTimerRoutine()
    {
        float remaining = taskDuration;
        while (remaining > 0f && taskActive)
        {
            if (Mathf.FloorToInt(remaining) % 60 == 0)
                Debug.Log($"[Task1] {remaining}s remaining.");
            yield return new WaitForSeconds(1f);
            remaining -= 1f;
        }

        if (!taskActive) yield break;

        Debug.Log("[Task1] TIMER EXPIRED -- penalty scavengers incoming!");
        penaltySpawner?.StartSpawning();
        OnTaskFailed?.Invoke();
    }

    // ================================================================
    // Zone callbacks (server-side) -- broadcast to all clients
    // ================================================================

    void HandleWoodProgress(int current, int required)
    {
        UpdateWoodHudClientRpc(current, required);

        if (current >= required)
            FirewoodDepositCompleteClientRpc(); // remove deposit line, show "light fire" line
    }

    void HandleFireLit()
        => FireLitClientRpc(firewoodZone.GetRequiredMushrooms());

    void HandleMushroomProgress(int current, int required)
        => UpdateMushroomHudClientRpc(current, required);

    void HandleMushroomsComplete()
    {
        if (!taskActive) return;
        Debug.Log("[Task1] Mushrooms COMPLETE.");
        MushroomBurnCompleteClientRpc();
        mushroomsComplete = true;
        LogObjectiveStatus();
        CheckAllComplete();
    }

    void HandleCanProgress(int current, int required)
        => UpdateCanHudClientRpc(current, required);

    void HandleCansComplete()
    {
        if (!taskActive) return;
        Debug.Log("[Task1] Cans COMPLETE.");
        CanDeliveryCompleteClientRpc();
        cansComplete = true;
        LogObjectiveStatus();
        CheckAllComplete();
    }

    void LogObjectiveStatus()
        => Debug.Log($"[Task1] Mushrooms: {(mushroomsComplete ? "DONE" : "pending")} | Cans: {(cansComplete ? "DONE" : "pending")}");

    void CheckAllComplete()
    {
        if (!mushroomsComplete || !cansComplete) return;
        Debug.Log("[Task1] ALL objectives complete!");
        AllCompleteClientRpc();
        CleanUp();
        OnTaskCompleted?.Invoke();
    }

    void CleanUp()
    {
        taskActive = false;
        if (timerCoroutine != null) { StopCoroutine(timerCoroutine); timerCoroutine = null; }

        if (firewoodZone != null)
        {
            firewoodZone.OnWoodProgressChanged -= HandleWoodProgress;
            firewoodZone.OnFireLit -= HandleFireLit;
            firewoodZone.OnMushroomProgressChanged -= HandleMushroomProgress;
            firewoodZone.OnMushroomsComplete -= HandleMushroomsComplete;
        }
        if (canZone != null)
        {
            canZone.OnCanProgressChanged -= HandleCanProgress;
            canZone.OnCansComplete -= HandleCansComplete;
        }

        penaltySpawner?.StopSpawning();
        Debug.Log("[Task1] Cleaned up.");
    }

    // ================================================================
    // ClientRpcs
    // ================================================================

    [ClientRpc]
    void ShowHudClientRpc(int reqWood, int reqMushrooms, int reqCans)
        => PlayerHUD.Local?.ShowTask1(reqWood, reqMushrooms, reqCans);

    [ClientRpc]
    void UpdateWoodHudClientRpc(int current, int required)
        => PlayerHUD.Local?.SetFirewoodProgress(current, required);

    [ClientRpc]
    void FirewoodDepositCompleteClientRpc()
        => PlayerHUD.Local?.OnFirewoodDepositComplete();

    [ClientRpc]
    void FireLitClientRpc(int requiredMushrooms)
        => PlayerHUD.Local?.OnFireLit(requiredMushrooms);

    [ClientRpc]
    void UpdateMushroomHudClientRpc(int current, int required)
        => PlayerHUD.Local?.SetMushroomProgress(current, required);

    [ClientRpc]
    void MushroomBurnCompleteClientRpc()
        => PlayerHUD.Local?.OnMushroomBurnComplete();

    [ClientRpc]
    void UpdateCanHudClientRpc(int current, int required)
        => PlayerHUD.Local?.SetCanProgress(current, required);

    [ClientRpc]
    void CanDeliveryCompleteClientRpc()
        => PlayerHUD.Local?.OnCanDeliveryComplete();

    [ClientRpc]
    void AllCompleteClientRpc()
        => PlayerHUD.Local?.CompleteCurrentTask("Field tasks complete");
}