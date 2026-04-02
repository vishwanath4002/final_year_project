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

    // Synced to all clients
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

            // -- HUD update on ALL clients via NetworkVariable callback --
            ApplyHUDForPhase(newPhase);
        };

        // Apply immediately for late-joining clients
        currentPhaseDisplay = (GamePhase)_networkPhase.Value;
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
    // HUD helper -- runs on every client from the NetworkVariable callback
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
                // Zone markers are activated server-side in BeginTask1.
                // HUD shows the first sub-task; the field task script updates it further
                // via the ClientRpcs below as sub-objectives complete.
                PlayerHUD.Local?.ShowTask("Collect firewood and bring it to the fire pit.");
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

        if (stageIdx == 0 && CurrentPhase == GamePhase.Intro)
            CompleteIntro();
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

    void BeginIntro()
    {
        SetPhase(GamePhase.Intro);
    }

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
    // Phase 2 -- Briefing (NPC2 stage 0)
    // ================================================================

    void BeginBriefing()
    {
        SetPhase(GamePhase.Briefing);
    }

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
    // ================================================================

    void BeginTask1()
    {
        SetPhase(GamePhase.Task1_Field);

        // Activate firewood zone -- marker appears, players can start depositing
        firewoodZone?.ActivateTask();

        // Subscribe to sub-objective events to drive mid-task HUD updates
        if (firewoodZone != null)
        {
            firewoodZone.OnFireLit += OnFireLit;
            firewoodZone.OnMushroomsComplete += OnMushroomsComplete;
        }

        fieldTask.OnTaskCompleted += OnTask1Completed;
        fieldTask.OnTaskFailed += OnTask1TimerFailed;
        fieldTask.StartTask();
    }

    void OnFireLit()
    {
        // Fire is lit -- tell all clients to update HUD to mushroom sub-task
        FireLitClientRpc();
    }

    void OnMushroomsComplete()
    {
        // Mushrooms done -- activate can delivery zone and update HUD
        canDeliveryZone?.ActivateTask();
        MushroomsDoneClientRpc();
    }

    void OnTask1Completed()
    {
        Debug.Log("[TaskManager] Task 1 COMPLETE.");
        fieldTask.OnTaskCompleted -= OnTask1Completed;
        fieldTask.OnTaskFailed -= OnTask1TimerFailed;

        if (firewoodZone != null)
        {
            firewoodZone.OnFireLit -= OnFireLit;
            firewoodZone.OnMushroomsComplete -= OnMushroomsComplete;
        }

        Task1CompleteClientRpc();
        BeginReturnBriefing();
    }

    void OnTask1TimerFailed()
    {
        Debug.Log("[TaskManager] Task 1 timer expired -- penalty scavengers spawning. Task still active.");
    }

    // ================================================================
    // Phase 4 -- Return Briefing (NPC2 stage 1)
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

    // ================================================================
    // Phase 6 -- Petrov Debrief (NPC3 stage 1)
    // ================================================================

    void BeginPetrovDebrief()
    {
        SetPhase(GamePhase.PetrovDebrief);
    }

    // ================================================================
    // Phase 7 -- Return to Voss (NPC2 stage 2)
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

            if (firewoodZone != null)
            {
                firewoodZone.OnFireLit -= OnFireLit;
                firewoodZone.OnMushroomsComplete -= OnMushroomsComplete;
            }
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
    // CLIENT RPCS -- mid-task HUD updates (sub-objectives inside Task1)
    // ================================================================

    [ClientRpc]
    void FireLitClientRpc()
    {
        PlayerHUD.Local?.ShowTask("The fire is lit. Collect mushrooms and burn them in the flames.");
    }

    [ClientRpc]
    void MushroomsDoneClientRpc()
    {
        PlayerHUD.Local?.CompleteCurrentTask("Mushrooms burned");
        StartCoroutine(DelayedShowTask("Gather food cans scattered around and deliver them to the church.", 1.5f));
    }

    [ClientRpc]
    void Task1CompleteClientRpc()
    {
        PlayerHUD.Local?.CompleteCurrentTask("Field tasks complete");
    }

    [ClientRpc]
    void Task2CompleteClientRpc()
    {
        PlayerHUD.Local?.CompleteCurrentTask("Petrov is safe");
    }

    System.Collections.IEnumerator DelayedShowTask(string desc, float delay)
    {
        yield return new WaitForSeconds(delay);
        PlayerHUD.Local?.ShowTask(desc);
    }

    // ================================================================
    // TESTING HELPERS
    // ================================================================

    public void ForceCompleteMushroomTask()
    {
        if (!IsServer) { Debug.LogWarning("[TaskManager] ForceCompleteMushroomTask can only be called on server!"); return; }
        if (CurrentPhase != GamePhase.Task1_Field) { Debug.LogWarning($"[TaskManager] ForceCompleteMushroomTask ignored -- not in Task1_Field (current: {CurrentPhase})"); return; }

        if (firewoodZone != null) { firewoodZone.ForceCompleteMushroomBurning(); Debug.Log("[TaskManager] Mushroom task force completed."); }
        else Debug.LogError("[TaskManager] firewoodZone is null!");
    }

    public void ForceCompleteFoodCanTask()
    {
        if (!IsServer) { Debug.LogWarning("[TaskManager] ForceCompleteFoodCanTask can only be called on server!"); return; }
        if (CurrentPhase != GamePhase.Task1_Field) { Debug.LogWarning($"[TaskManager] ForceCompleteFoodCanTask ignored -- not in Task1_Field (current: {CurrentPhase})"); return; }

        if (canDeliveryZone != null) { canDeliveryZone.ForceCompleteCanDelivery(); Debug.Log("[TaskManager] Food can task force completed."); }
        else Debug.LogError("[TaskManager] canDeliveryZone is null!");
    }

    public void ForceCompleteScavengerRaid()
    {
        if (!IsServer) { Debug.LogWarning("[TaskManager] ForceCompleteScavengerRaid can only be called on server!"); return; }
        if (CurrentPhase != GamePhase.Task2_ScavengerRaid) { Debug.LogWarning($"[TaskManager] ForceCompleteScavengerRaid ignored -- not in Task2_ScavengerRaid (current: {CurrentPhase})"); return; }

        if (scavengerRaidTask != null) { scavengerRaidTask.ForceCompleteTask(); Debug.Log("[TaskManager] Scavenger raid force completed."); }
        else Debug.LogError("[TaskManager] scavengerRaidTask is null!");
    }

    public void ForceStartBossFight()
    {
        if (!IsServer) { Debug.LogWarning("[TaskManager] ForceStartBossFight can only be called on server!"); return; }

        if (CurrentPhase == GamePhase.PetrovDebrief || CurrentPhase == GamePhase.ReturnToVoss)
        { Debug.Log("[TaskManager] Skipping dialogues, going straight to boss fight."); BeginBossFight(); }
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

    string GetPhaseDescription(GamePhase phase)
    {
        return phase switch
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
    }

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