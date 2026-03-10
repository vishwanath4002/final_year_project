using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Server-only keyboard shortcuts to manually advance game phases for testing.
///
/// Press 1-8 to skip through the game flow:
/// 1 = Complete Intro (NPC1 intro done)
/// 2 = Complete Dr. Voss Briefing (Start Task 1)
/// 3 = Complete Mushroom Task (burn all mushrooms)
/// 4 = Complete Food Can Task (deliver all cans)
/// 5 = Complete Return to Voss (after Task 1)
/// 6 = Complete Rescue Task (protect Petrov from scavengers)
/// 7 = Spawn Boss (trigger boss fight)
/// 8 = Kill Boss (victory)
/// 
/// 0 = Print current phase
/// 9 = Simulate player death
/// </summary>
public class GameFlowTester : MonoBehaviour
{
    [Header("Debug Info")]
    [SerializeField] private bool showDebugMessages = true;

    private void Update()
    {
        // Only server can advance phases
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;

        if (TaskManager.Instance == null)
        {
            if (Input.anyKeyDown)
                Debug.LogError("[GameFlowTester] TaskManager.Instance is null!");
            return;
        }

        // 0 = Print current phase
        if (Input.GetKeyDown(KeyCode.Alpha0))
        {
            PrintCurrentPhase();
        }

        // 1 = Complete Intro
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            Log("1 - Completing Intro (NPC1)");
            TaskManager.Instance.CompleteIntro();
        }

        // 2 = Complete Dr. Voss Briefing
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            Log("2 - Completing Dr. Voss Briefing (Start Task 1)");
            TaskManager.Instance.CompleteBriefing();
        }

        // 3 = Complete Mushroom Task
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            Log("3 - Force completing Mushroom Task");
            TaskManager.Instance.ForceCompleteMushroomTask();
        }

        // 4 = Complete Food Can Task
        if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            Log("4 - Force completing Food Can Task");
            TaskManager.Instance.ForceCompleteFoodCanTask();
        }

        // 5 = Complete Return to Voss (after Task 1)
        if (Input.GetKeyDown(KeyCode.Alpha5))
        {
            Log("5 - Completing Return to Dr. Voss");
            TaskManager.Instance.CompleteReturnBriefing();
        }

        // 6 = Complete Rescue Task (Scavenger Raid)
        if (Input.GetKeyDown(KeyCode.Alpha6))
        {
            Log("6 - Force completing Scavenger Raid Task");
            TaskManager.Instance.ForceCompleteScavengerRaid();
        }

        // 7 = Spawn Boss
        if (Input.GetKeyDown(KeyCode.Alpha7))
        {
            Log("7 - Spawning Boss (skip Petrov debrief + Return to Voss)");
            TaskManager.Instance.ForceStartBossFight();
        }

        // 8 = Kill Boss (Victory)
        if (Input.GetKeyDown(KeyCode.Alpha8))
        {
            Log("8 - Boss Defeated (Victory)");
            TaskManager.Instance.OnBossDefeated();
        }

        // 9 = Simulate Player Death
        if (Input.GetKeyDown(KeyCode.Alpha9))
        {
            Log("9 - Simulating local player death");
            if (GameManager.Instance != null)
            {
                ulong localClientId = NetworkManager.Singleton.LocalClientId;
                GameManager.Instance.RegisterPlayerDeath(localClientId);
            }
            else
            {
                Debug.LogError("[GameFlowTester] GameManager.Instance is null!");
            }
        }
    }

    private void PrintCurrentPhase()
    {
        if (TaskManager.Instance == null)
        {
            Debug.LogError("[GameFlowTester] TaskManager.Instance is null!");
            return;
        }

        GamePhase currentPhase = TaskManager.Instance.CurrentPhase;
        Debug.Log("========================================");
        Debug.Log($"[GameFlowTester] CURRENT PHASE: {currentPhase}");
        Debug.Log($"[GameFlowTester] {GetPhaseHelp(currentPhase)}");
        Debug.Log("========================================");
    }

    private string GetPhaseHelp(GamePhase phase)
    {
        return phase switch
        {
            GamePhase.Intro => "Press 1 to complete intro",
            GamePhase.Briefing => "Press 2 to complete briefing",
            GamePhase.Task1_Field => "Press 3 for mushrooms, 4 for cans (both needed)",
            GamePhase.ReturnBriefing => "Press 5 to complete return briefing",
            GamePhase.Task2_ScavengerRaid => "Press 6 to complete scavenger raid",
            GamePhase.PetrovDebrief => "Press 7 to spawn boss (skips debrief)",
            GamePhase.ReturnToVoss => "Press 7 to spawn boss",
            GamePhase.BossFight => "Press 8 to defeat boss",
            GamePhase.Victory => "GAME WON",
            GamePhase.GameOver => "GAME OVER",
            _ => "Unknown phase"
        };
    }

    private void Log(string message)
    {
        if (showDebugMessages)
        {
            Debug.Log($"[GameFlowTester] {message}");
        }
    }
}