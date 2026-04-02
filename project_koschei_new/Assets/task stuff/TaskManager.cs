using System;
using Unity.Netcode;
using UnityEngine;

public enum GamePhase
{
    Intro,
    Briefing,
    Task1_Field,
    ReturnBriefing,
    Task2_ScavengerRaid,
    PetrovDebrief,
    ReturnToVoss,
    BossFight,
    Victory,
    GameOver
}

public class TaskManager : NetworkBehaviour
{
    public static TaskManager Instance { get; private set; }

    [Header("Tasks")]
    [SerializeField] Task1_FieldObjectives fieldTask;
    [SerializeField] ScavengerRaidTask scavengerRaidTask;

    [Header("Delivery Zones (for testing)")]
    [SerializeField] FirewoodDeliveryZone firewoodZone;
    [SerializeField] CanDeliveryZone canDeliveryZone;

    [Header("NPC1 -- Intro Scientist (dies at boss fight)")]
    [SerializeField] ScientistNPCDialogue npc1Dialogue;
    [SerializeField] ScientistNPCController npc1Controller;

    [Header("NPC2 -- Dr. Voss")]
    [SerializeField] ScientistNPCDialogue npc2Dialogue;

    [Header("NPC3 -- Dr. Petrov")]
    [SerializeField] ScientistNPCDialogue npc3Dialogue;
    [SerializeField] ScientistNPCController npc3Controller;

    [Header("Boss Fight")]
    [SerializeField] GameObject bossPrefab;
    [SerializeField] Transform bossSpawnPoint;

    [Header("Impostor")]
    [SerializeField] ImpostorBackendConnector impostorConnector;

    [Header("Debug -- Read Only")]
    [SerializeField] private GamePhase currentPhaseDisplay;

    private NetworkVariable<int> _networkPhase = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public event Action<GamePhase> OnPhaseChanged;
    public GamePhase CurrentPhase => (GamePhase)_networkPhase.Value;

    // ================================================================

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _networkPhase.OnValueChanged += (oldVal, newVal) =>
        {
            GamePhase oldPhase = (GamePhase)oldVal;
            GamePhase newPhase = (GamePhase)newVal;
            currentPhaseDisplay = newPhase;
            Debug.Log($"[TaskManager] ========== PHASE CHANGED: {oldPhase} -> {newPhase} ==========");
            Debug.Log($"[TaskManager] {GetPhaseDescription(newPhase)}");
            ApplyHUDForPhase(newPhase);
        };

        currentPhaseDisplay = CurrentPhase;
        ApplyHUDForPhase(CurrentPhase);

        if (!IsServer) return;

        ValidateReferences();

        if (GameManager.Instance != null)
            GameManager.Instance.OnAllPlayersDead += HandleGameOver;

        if (npc1Dialogue != null) npc1Dialogue.OnStageCompleted += OnNpc1StageCompleted;
        if (npc2Dialogue != null) npc2Dialogue.OnStageCompleted += OnNpc2StageCompleted;
        if (npc3Dialogue != null) npc3Dialogue.OnStageCompleted += OnNpc3StageCompleted;

        BeginIntro();
    }

    // ================================================================
    // HUD -- runs on every client via the NetworkVariable callback.
    // Task1 progress HUD is handled entirely by Task1_FieldObjectives
    // via its own ClientRpcs, so we leave that case empty here.
    // ================================================================

    void ApplyHUDForPhase(GamePhase phase)
    {
        switch (phase)
        {
            case GamePhase.Intro:
                PlayerHUD.Local?.ShowTask("Find the contact near the camp and speak with them.");
                break;

            case GamePhase.Briefing:
                PlayerHUD.Local?.ShowTask("Head to Dr. Voss and receive your briefing.");
                break;

            case GamePhase.Task1_Field:
                // Task1_FieldObjectives.StartTask() sends ShowHudClientRpc to all clients
                // which calls PlayerHUD.ShowTask1() with live progress tracking.
                // Nothing to do here.
                break;

            case GamePhase.ReturnBriefing:
                PlayerHUD.Local?.ShowTask("Return to Dr. Voss for your next orders.");
                break;

            case GamePhase.Task2_ScavengerRaid:
                PlayerHUD.Local?.ShowTask("Head to Dr. Petrov's location and protect him from the scavengers.");
                break;

            case GamePhase.PetrovDebrief:
                PlayerHUD.Local?.ShowTask("Speak with Dr. Petrov.");
                break;

            case GamePhase.ReturnToVoss:
                PlayerHUD.Local?.ShowTask("Return to Dr. Voss and report back.");
                break;

            case GamePhase.BossFight:
                PlayerHUD.Local?.ClearTask();
                break;

            case GamePhase.Victory:
                PlayerHUD.Local?.CompleteCurrentTask("Mission complete");
                break;

            case GamePhase.GameOver:
                PlayerHUD.Local?.ClearTask();
                break;
        }
    }

    // ================================================================
    // NPC Dialogue Callbacks
    // ================================================================

    void OnNpc1StageCompleted(int stageIdx)
    {
        Debug.Log($"[TaskManager] NPC1 stage {stageIdx} complete (phase: {CurrentPhase})");
        if (stageIdx == 0 && CurrentPhase == GamePhase.Intro) CompleteIntro();
    }

    void OnNpc2StageCompleted(int stageIdx)
    {
        Debug.Log($"[TaskManager] NPC2 (Dr. Voss) stage {stageIdx} complete (phase: {CurrentPhase})");
        if (stageIdx == 0 && CurrentPhase == GamePhase.Briefing) CompleteBriefing();
        else if (stageIdx == 1 && CurrentPhase == GamePhase.ReturnBriefing) CompleteReturnBriefing();
        else if (stageIdx == 2 && CurrentPhase == GamePhase.ReturnToVoss) CompleteReturnToVoss();
    }

    void OnNpc3StageCompleted(int stageIdx)
    {
        Debug.Log($"[TaskManager] NPC3 (Dr. Petrov) stage {stageIdx} complete (phase: {CurrentPhase})");
        if (stageIdx == 0)
            Debug.Log("[TaskManager] NPC3 combat lines done -- scavengers still active.");
        else if (stageIdx == 1 && CurrentPhase == GamePhase.PetrovDebrief)
            BeginReturnToVoss();
    }

    // ================================================================
    // Phase 1 -- Intro
    // ================================================================

    void BeginIntro() => SetPhase(GamePhase.Intro);

    public void CompleteIntro()
    {
        if (!IsServer) return;
        if (CurrentPhase != GamePhase.Intro)
        {
            Debug.LogWarning($"[TaskManager] CompleteIntro ignored -- phase is {CurrentPhase}");
            return;
        }
        BeginBriefing();
    }

    // ================================================================
    // Phase 2 -- Briefing
    // ================================================================

    void BeginBriefing() => SetPhase(GamePhase.Briefing);

    public void CompleteBriefing()
    {
        if (!IsServer) return;
        if (CurrentPhase != GamePhase.Briefing)
        {
            Debug.LogWarning($"[TaskManager] CompleteBriefing ignored -- phase is {CurrentPhase}");
            return;
        }
        impostorConnector?.EnableImpostorSpawning();
        BeginTask1();
    }

    // ================================================================
    // Phase 3 -- Task 1
    // Task1_FieldObjectives.StartTask() activates both zone markers and
    // handles all HUD progress updates itself via ClientRpcs.
    // ================================================================

    void BeginTask1()
    {
        SetPhase(GamePhase.Task1_Field);

        fieldTask.OnTaskCompleted += OnTask1Completed;
        fieldTask.OnTaskFailed += OnTask1TimerFailed;
        fieldTask.StartTask();
    }

    void OnTask1Completed()
    {
        Debug.Log("[TaskManager] Task 1 COMPLETE.");
        fieldTask.OnTaskCompleted -= OnTask1Completed;
        fieldTask.OnTaskFailed -= OnTask1TimerFailed;

        Task1CompleteClientRpc();
        BeginReturnBriefing();
    }

    void OnTask1TimerFailed()
    {
        Debug.Log("[TaskManager] Task 1 timer expired -- penalty scavengers spawning. Task still active.");
    }

    [ClientRpc]
    void Task1CompleteClientRpc()
        => PlayerHUD.Local?.CompleteCurrentTask("Field tasks complete");

    // ================================================================
    // Phase 4 -- Return Briefing
    // ================================================================

    void BeginReturnBriefing()
    {
        SetPhase(GamePhase.ReturnBriefing);
        npc2Dialogue?.UnlockNextStage();
    }

    public void CompleteReturnBriefing()
    {
        if (!IsServer) return;
        if (CurrentPhase != GamePhase.ReturnBriefing)
        {
            Debug.LogWarning($"[TaskManager] CompleteReturnBriefing ignored -- phase is {CurrentPhase}");
            return;
        }
        BeginTask2();
    }

    // ================================================================
    // Phase 5 -- Task 2 (Scavenger Raid)
    // ================================================================

    void BeginTask2()
    {
        SetPhase(GamePhase.Task2_ScavengerRaid);

        scavengerRaidTask.OnTaskCompleted += OnTask2Completed;
        scavengerRaidTask.OnTaskFailed += OnTask2Failed;
        scavengerRaidTask.StartTask();
    }

    void OnTask2Completed()
    {
        Debug.Log("[TaskManager] Task 2 COMPLETE -- Petrov is safe.");
        scavengerRaidTask.OnTaskCompleted -= OnTask2Completed;
        scavengerRaidTask.OnTaskFailed -= OnTask2Failed;

        npc3Dialogue?.UnlockNextStage();

        Task2CompleteClientRpc();
        BeginPetrovDebrief();
    }

    void OnTask2Failed()
    {
        Debug.Log("[TaskManager] Task 2 FAILED -- Petrov was killed.");
        scavengerRaidTask.OnTaskCompleted -= OnTask2Completed;
        scavengerRaidTask.OnTaskFailed -= OnTask2Failed;
        HandleGameOver();
    }

    [ClientRpc]
    void Task2CompleteClientRpc()
        => PlayerHUD.Local?.CompleteCurrentTask("Petrov is safe");

    // ================================================================
    // Phase 6 -- Petrov Debrief
    // ================================================================

    void BeginPetrovDebrief() => SetPhase(GamePhase.PetrovDebrief);

    // ================================================================
    // Phase 7 -- Return to Voss
    // ================================================================

    void BeginReturnToVoss()
    {
        SetPhase(GamePhase.ReturnToVoss);
        npc2Dialogue?.UnlockNextStage();
    }

    public void CompleteReturnToVoss()
    {
        if (!IsServer) return;
        if (CurrentPhase != GamePhase.ReturnToVoss)
        {
            Debug.LogWarning($"[TaskManager] CompleteReturnToVoss ignored -- phase is {CurrentPhase}");
            return;
        }
        BeginBossFight();
    }

    // ================================================================
    // Phase 8 -- Boss Fight
    // ================================================================

    void BeginBossFight()
    {
        SetPhase(GamePhase.BossFight);

        impostorConnector?.DisableImpostorSpawning();

        if (npc1Dialogue != null)
            npc1Dialogue.SyncKillNPC();
        else
            Debug.LogWarning("[TaskManager] npc1Dialogue not assigned -- NPC1 won't die.");

        if (bossPrefab != null && bossSpawnPoint != null)
        {
            GameObject boss = Instantiate(bossPrefab, bossSpawnPoint.position, bossSpawnPoint.rotation);
            NetworkObject netObj = boss.GetComponent<NetworkObject>();
            if (netObj != null) netObj.Spawn(true);
            Debug.Log($"[TaskManager] Boss spawned at {bossSpawnPoint.position}.");
        }
        else
        {
            Debug.LogWarning("[TaskManager] bossPrefab or bossSpawnPoint not assigned -- boss not spawned.");
        }
    }

    public void OnBossDefeated()
    {
        if (!IsServer) return;
        if (CurrentPhase != GamePhase.BossFight)
        {
            Debug.LogWarning($"[TaskManager] OnBossDefeated ignored -- phase is {CurrentPhase}");
            return;
        }
        SetPhase(GamePhase.Victory);
        GameManager.Instance?.TriggerVictory();
    }

    // ================================================================
    // Game Over
    // ================================================================

    void HandleGameOver()
    {
        if (CurrentPhase == GamePhase.GameOver) return;

        if (CurrentPhase == GamePhase.Task1_Field)
        {
            fieldTask.OnTaskCompleted -= OnTask1Completed;
            fieldTask.OnTaskFailed -= OnTask1TimerFailed;
            fieldTask.EndTask();
        }

        if (CurrentPhase == GamePhase.Task2_ScavengerRaid)
        {
            scavengerRaidTask.OnTaskCompleted -= OnTask2Completed;
            scavengerRaidTask.OnTaskFailed -= OnTask2Failed;
            scavengerRaidTask.EndTask();
        }

        impostorConnector?.DisableImpostorSpawning();
        SetPhase(GamePhase.GameOver);
        GameManager.Instance?.TriggerGameOver();
    }

    // ================================================================
    // Testing Helpers
    // ================================================================

    public void ForceCompleteMushroomTask()
    {
        if (!IsServer) { Debug.LogWarning("[TaskManager] Server only."); return; }
        if (CurrentPhase != GamePhase.Task1_Field) { Debug.LogWarning($"[TaskManager] Not in Task1_Field."); return; }
        if (firewoodZone != null) firewoodZone.ForceCompleteMushroomBurning();
        else Debug.LogError("[TaskManager] firewoodZone is null!");
    }

    public void ForceCompleteFoodCanTask()
    {
        if (!IsServer) { Debug.LogWarning("[TaskManager] Server only."); return; }
        if (CurrentPhase != GamePhase.Task1_Field) { Debug.LogWarning($"[TaskManager] Not in Task1_Field."); return; }
        if (canDeliveryZone != null) canDeliveryZone.ForceCompleteCanDelivery();
        else Debug.LogError("[TaskManager] canDeliveryZone is null!");
    }

    public void ForceCompleteScavengerRaid()
    {
        if (!IsServer) { Debug.LogWarning("[TaskManager] Server only."); return; }
        if (CurrentPhase != GamePhase.Task2_ScavengerRaid) { Debug.LogWarning($"[TaskManager] Not in Task2_ScavengerRaid."); return; }
        if (scavengerRaidTask != null) scavengerRaidTask.ForceCompleteTask();
        else Debug.LogError("[TaskManager] scavengerRaidTask is null!");
    }

    public void ForceStartBossFight()
    {
        if (!IsServer) { Debug.LogWarning("[TaskManager] Server only."); return; }
        if (CurrentPhase == GamePhase.PetrovDebrief || CurrentPhase == GamePhase.ReturnToVoss)
            BeginBossFight();
        else
            Debug.LogWarning($"[TaskManager] ForceStartBossFight ignored -- phase is {CurrentPhase}.");
    }

    // ================================================================
    // Helpers
    // ================================================================

    void SetPhase(GamePhase phase)
    {
        GamePhase previous = CurrentPhase;
        _networkPhase.Value = (int)phase;
        currentPhaseDisplay = phase;

        Debug.Log($"[TaskManager] ========== PHASE CHANGED: {previous} -> {phase} ==========");
        Debug.Log($"[TaskManager] {GetPhaseDescription(phase)}");

        OnPhaseChanged?.Invoke(phase);
    }

    string GetPhaseDescription(GamePhase phase) => phase switch
    {
        GamePhase.Intro => "Players should approach NPC1 and press E.",
        GamePhase.Briefing => "Players should find Dr. Voss (NPC2) and press E.",
        GamePhase.Task1_Field => "Players must burn mushrooms AND deliver food cans.",
        GamePhase.ReturnBriefing => "NPC2 stage 1 unlocked. Players return to Dr. Voss.",
        GamePhase.Task2_ScavengerRaid => "Players must protect Dr. Petrov from scavenger waves.",
        GamePhase.PetrovDebrief => "NPC3 stage 1 unlocked. Players talk to Dr. Petrov for lore.",
        GamePhase.ReturnToVoss => "NPC2 stage 2 unlocked. Players return to Dr. Voss.",
        GamePhase.BossFight => "NPC1 is dead. Boss has spawned. Impostor disabled.",
        GamePhase.Victory => "Boss defeated. All objectives complete.",
        GamePhase.GameOver => "All players dead or objective failed.",
        _ => "Unknown phase."
    };

    void ValidateReferences()
    {
        if (fieldTask == null) Debug.LogError("[TaskManager] fieldTask not assigned!");
        if (scavengerRaidTask == null) Debug.LogError("[TaskManager] scavengerRaidTask not assigned!");
        if (firewoodZone == null) Debug.LogWarning("[TaskManager] firewoodZone not assigned - testing helpers won't work!");
        if (canDeliveryZone == null) Debug.LogWarning("[TaskManager] canDeliveryZone not assigned - testing helpers won't work!");
        if (npc1Dialogue == null) Debug.LogError("[TaskManager] npc1Dialogue not assigned!");
        if (npc1Controller == null) Debug.LogWarning("[TaskManager] npc1Controller not assigned.");
        if (npc2Dialogue == null) Debug.LogError("[TaskManager] npc2Dialogue not assigned!");
        if (npc3Dialogue == null) Debug.LogError("[TaskManager] npc3Dialogue not assigned!");
        if (npc3Controller == null) Debug.LogWarning("[TaskManager] npc3Controller not assigned.");
        if (bossPrefab == null) Debug.LogWarning("[TaskManager] bossPrefab not assigned!");
        if (bossSpawnPoint == null) Debug.LogWarning("[TaskManager] bossSpawnPoint not assigned!");
        if (impostorConnector == null) Debug.LogWarning("[TaskManager] impostorConnector not assigned.");
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (npc1Dialogue != null) npc1Dialogue.OnStageCompleted -= OnNpc1StageCompleted;
        if (npc2Dialogue != null) npc2Dialogue.OnStageCompleted -= OnNpc2StageCompleted;
        if (npc3Dialogue != null) npc3Dialogue.OnStageCompleted -= OnNpc3StageCompleted;
        if (GameManager.Instance != null) GameManager.Instance.OnAllPlayersDead -= HandleGameOver;
    }
}