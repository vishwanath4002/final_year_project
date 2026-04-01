using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative task sequencer for Task 1.
/// Activates zones in order, pushes HUD updates to all clients via ClientRpc.
/// </summary>
public class Task1Manager : NetworkBehaviour
{
    [Header("Zone References")]
    [SerializeField] private FirewoodDeliveryZone firewoodZone;
    [SerializeField] private CanDeliveryZone canZone;

    // ================================================================
    // Task descriptions shown on every player's HUD
    // ================================================================
    private const string DESC_FIREWOOD = "Collect firewood and bring it to the fire pit.";
    private const string DESC_LIGHT_FIRE = "All logs placed. Stand near the pit and press [E] to light the fire.";
    private const string DESC_MUSHROOMS = "The fire is lit! Collect mushrooms and burn them in the flames.";
    private const string DESC_CANS = "Gather food cans scattered around and deliver them to the church.";

    private const string COMPLETE_FIREWOOD = "Fire lit";
    private const string COMPLETE_MUSHROOMS = "Mushrooms burned";
    private const string COMPLETE_CANS = "Food cans delivered";

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer) return;

        // Subscribe to zone events — all fire server-side only
        if (firewoodZone != null)
        {
            firewoodZone.OnFireLit += HandleFireLit;
            firewoodZone.OnMushroomsComplete += HandleMushroomsComplete;
        }

        if (canZone != null)
        {
            canZone.OnCansComplete += HandleCansComplete;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (!IsServer) return;

        if (firewoodZone != null)
        {
            firewoodZone.OnFireLit -= HandleFireLit;
            firewoodZone.OnMushroomsComplete -= HandleMushroomsComplete;
        }

        if (canZone != null)
        {
            canZone.OnCansComplete -= HandleCansComplete;
        }
    }

    // ================================================================
    // CALL THIS to begin the task sequence (from your round/game manager)
    // ================================================================

    public void BeginTask1()
    {
        if (!IsServer)
        {
            Debug.LogWarning("[Task1Manager] BeginTask1 must be called on the server.");
            return;
        }

        Debug.Log("[Task1Manager] Task 1 started — activating firewood zone.");

        if (firewoodZone != null)
            firewoodZone.ActivateTask();

        // Tell all clients to show the first task description
        ShowTaskClientRpc(DESC_FIREWOOD);
    }

    [ServerRpc(RequireOwnership = false)]
    public void BeginTask1ServerRpc()
    {
        BeginTask1();
    }

    // ================================================================
    // SERVER-SIDE EVENT HANDLERS
    // ================================================================

    private void HandleFireLit()
    {
        Debug.Log("[Task1Manager] Fire lit — starting mushroom phase.");

        // Task description updates: clear the firewood step, show mushroom step
        // No completion banner for firewood specifically — the fire lighting IS the firewood completion
        ShowTaskClientRpc(DESC_MUSHROOMS);
    }

    private void HandleMushroomsComplete()
    {
        Debug.Log("[Task1Manager] Mushrooms done — activating can delivery zone.");

        // Complete mushroom step on HUD, then activate can delivery
        CompleteTaskClientRpc(COMPLETE_MUSHROOMS);

        if (canZone != null)
            canZone.ActivateTask();

        // Brief delay before showing the next task description so the
        // completion banner has time to appear. We show it after 1 frame.
        StartCoroutine(ShowNextTaskAfterDelay(DESC_CANS, completeLingerSeconds: 1f));
    }

    private void HandleCansComplete()
    {
        Debug.Log("[Task1Manager] All cans delivered — Task 1 complete!");
        CompleteTaskClientRpc(COMPLETE_CANS);
    }

    private System.Collections.IEnumerator ShowNextTaskAfterDelay(string description, float completeLingerSeconds)
    {
        yield return new WaitForSeconds(completeLingerSeconds);
        ShowTaskClientRpc(description);
    }

    // ================================================================
    // CLIENT RPC — pushes HUD updates to every player instance
    // ================================================================

    [ClientRpc]
    private void ShowTaskClientRpc(string description)
    {
        PlayerHUD.Local?.ShowTask(description);
    }

    [ClientRpc]
    private void CompleteTaskClientRpc(string taskName)
    {
        PlayerHUD.Local?.CompleteCurrentTask(taskName);
    }

    // ================================================================
    // TESTING HELPERS
    // ================================================================

    public void ForceSkipToMushrooms()
    {
        if (!IsServer) return;
        firewoodZone?.ForceActivateFire();
    }

    public void ForceSkipMushrooms()
    {
        if (!IsServer) return;
        firewoodZone?.ForceCompleteMushroomBurning();
    }

    public void ForceSkipCans()
    {
        if (!IsServer) return;
        canZone?.ForceCompleteCanDelivery();
    }
}