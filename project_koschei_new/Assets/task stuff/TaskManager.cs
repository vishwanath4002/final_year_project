using System;
using Unity.Netcode;
using UnityEngine;

public enum GamePhase
{
    Intro,
    Briefing,
    Task1_Field,
    ReturnBriefing,
    Task2_ProtectScientist,
    BossFight,
    Victory,
    GameOver
}

public class TaskManager : MonoBehaviour
{
    public static TaskManager Instance { get; private set; }

    [Header("Task References")]
    [SerializeField] Task1_FieldObjectives fieldTask;
    [SerializeField] ScavengerRaidTask scavengerRaidTask;

    [Header("Debug -- Read Only")]
    [SerializeField] GamePhase currentPhase = GamePhase.Intro;

    public event Action<GamePhase> OnPhaseChanged;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Debug.Log("[TaskManager] Initialized.");
    }

    void Start()
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.Log("[TaskManager] Running on client -- task flow managed by server only.");
            return;
        }

        Debug.Log("[TaskManager] Running on SERVER -- starting game flow.");

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnAllPlayersDead += HandleGameOver;
            Debug.Log("[TaskManager] Subscribed to GameManager.OnAllPlayersDead.");
        }
        else
        {
            Debug.LogWarning("[TaskManager] GameManager.Instance is null -- player death won't trigger game over!");
        }

        if (fieldTask == null)
            Debug.LogError("[TaskManager] Field Task is not assigned in the Inspector!");
        if (scavengerRaidTask == null)
            Debug.LogError("[TaskManager] Scavenger Raid Task is not assigned in the Inspector!");

        BeginIntro();
    }

    // ================================================================
    // Phase 1 -- Intro
    // ================================================================
    void BeginIntro()
    {
        SetPhase(GamePhase.Intro);
        Debug.Log("[TaskManager] INTRO phase started -- waiting for NPC1 dialogue to complete.");
        Debug.Log("[TaskManager] (TEST) Call TaskManager.Instance.CompleteIntro() to advance.");
    }

    public void CompleteIntro()
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[TaskManager] CompleteIntro called on client -- ignored.");
            return;
        }
        if (currentPhase != GamePhase.Intro)
        {
            Debug.LogWarning($"[TaskManager] CompleteIntro called but current phase is {currentPhase} -- ignored.");
            return;
        }
        Debug.Log("[TaskManager] Intro complete -- moving to Briefing.");
        BeginBriefing();
    }

    // ================================================================
    // Phase 2 -- Briefing
    // ================================================================
    void BeginBriefing()
    {
        SetPhase(GamePhase.Briefing);
        Debug.Log("[TaskManager] BRIEFING phase started -- waiting for Scientist NPC1 dialogue.");
        Debug.Log("[TaskManager] (TEST) Call TaskManager.Instance.CompleteBriefing() to advance.");
    }

    public void CompleteBriefing()
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[TaskManager] CompleteBriefing called on client -- ignored.");
            return;
        }
        if (currentPhase != GamePhase.Briefing)
        {
            Debug.LogWarning($"[TaskManager] CompleteBriefing called but current phase is {currentPhase} -- ignored.");
            return;
        }
        Debug.Log("[TaskManager] Briefing complete -- starting Task 1.");
        BeginTask1();
    }

    // ================================================================
    // Phase 3 -- Task 1
    // ================================================================
    void BeginTask1()
    {
        SetPhase(GamePhase.Task1_Field);
        Debug.Log("[TaskManager] TASK 1 started -- players must burn mushrooms and deliver cans.");

        fieldTask.OnTaskCompleted += OnTask1Completed;
        fieldTask.OnTaskFailed += OnTask1TimerFailed;
        fieldTask.StartTask();
    }

    void OnTask1Completed()
    {
        Debug.Log("[TaskManager] Task 1 reported COMPLETE -- unsubscribing and moving on.");
        fieldTask.OnTaskCompleted -= OnTask1Completed;
        fieldTask.OnTaskFailed -= OnTask1TimerFailed;
        BeginReturnBriefing();
    }

    void OnTask1TimerFailed()
    {
        Debug.Log("[TaskManager] Task 1 timer FAILED -- penalty scavengers are now spawning. Task still in progress.");
        Debug.Log("[TaskManager] Players must still complete all objectives to advance.");
    }

    // ================================================================
    // Phase 4 -- Return Briefing
    // ================================================================
    void BeginReturnBriefing()
    {
        SetPhase(GamePhase.ReturnBriefing);
        Debug.Log("[TaskManager] RETURN BRIEFING phase -- waiting for Scientist NPC1 second dialogue.");
        Debug.Log("[TaskManager] (TEST) Call TaskManager.Instance.CompleteReturnBriefing() to advance.");
    }

    public void CompleteReturnBriefing()
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[TaskManager] CompleteReturnBriefing called on client -- ignored.");
            return;
        }
        if (currentPhase != GamePhase.ReturnBriefing)
        {
            Debug.LogWarning($"[TaskManager] CompleteReturnBriefing called but phase is {currentPhase} -- ignored.");
            return;
        }
        Debug.Log("[TaskManager] Return briefing complete -- starting Task 2.");
        BeginTask2();
    }

    // ================================================================
    // Phase 5 -- Task 2
    // ================================================================
    void BeginTask2()
    {
        SetPhase(GamePhase.Task2_ProtectScientist);
        Debug.Log("[TaskManager] TASK 2 started -- protect the scientist from scavengers!");

        scavengerRaidTask.OnTaskCompleted += OnTask2Completed;
        scavengerRaidTask.OnTaskFailed += OnTask2Failed;
        scavengerRaidTask.StartTask();
    }

    void OnTask2Completed()
    {
        Debug.Log("[TaskManager] Task 2 reported COMPLETE -- all aliens defeated, scientist safe!");
        scavengerRaidTask.OnTaskCompleted -= OnTask2Completed;
        scavengerRaidTask.OnTaskFailed -= OnTask2Failed;
        BeginBossFight();
    }

    void OnTask2Failed()
    {
        Debug.Log("[TaskManager] Task 2 FAILED -- scientist was killed. Triggering game over.");
        scavengerRaidTask.OnTaskCompleted -= OnTask2Completed;
        scavengerRaidTask.OnTaskFailed -= OnTask2Failed;
        HandleGameOver();
    }

    // ================================================================
    // Phase 6 -- Boss Fight
    // ================================================================
    void BeginBossFight()
    {
        SetPhase(GamePhase.BossFight);
        Debug.Log("[TaskManager] BOSS FIGHT phase started (placeholder).");
        Debug.Log("[TaskManager] (TEST) Call TaskManager.Instance.OnBossDefeated() to trigger victory.");
    }

    public void OnBossDefeated()
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[TaskManager] OnBossDefeated called on client -- ignored.");
            return;
        }
        if (currentPhase != GamePhase.BossFight)
        {
            Debug.LogWarning($"[TaskManager] OnBossDefeated called but phase is {currentPhase} -- ignored.");
            return;
        }
        Debug.Log("[TaskManager] Boss defeated -- triggering VICTORY!");
        SetPhase(GamePhase.Victory);
        GameManager.Instance?.TriggerVictory();
    }

    // ================================================================
    // Game Over
    // ================================================================
    void HandleGameOver()
    {
        if (currentPhase == GamePhase.GameOver)
        {
            Debug.LogWarning("[TaskManager] HandleGameOver called but already in GameOver phase.");
            return;
        }

        Debug.Log($"[TaskManager] Game over triggered during phase: {currentPhase}");

        if (currentPhase == GamePhase.Task1_Field)
        {
            Debug.Log("[TaskManager] Cleaning up Task 1...");
            fieldTask.OnTaskCompleted -= OnTask1Completed;
            fieldTask.OnTaskFailed -= OnTask1TimerFailed;
            fieldTask.EndTask();
        }
        if (currentPhase == GamePhase.Task2_ProtectScientist)
        {
            Debug.Log("[TaskManager] Cleaning up Task 2...");
            scavengerRaidTask.OnTaskCompleted -= OnTask2Completed;
            scavengerRaidTask.OnTaskFailed -= OnTask2Failed;
            scavengerRaidTask.EndTask();
        }

        SetPhase(GamePhase.GameOver);
        GameManager.Instance?.TriggerGameOver();
    }

    void SetPhase(GamePhase phase)
    {
        Debug.Log($"[TaskManager] ========== PHASE: {phase} ==========");
        currentPhase = phase;
        OnPhaseChanged?.Invoke(phase);
    }

    void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnAllPlayersDead -= HandleGameOver;
    }
}
