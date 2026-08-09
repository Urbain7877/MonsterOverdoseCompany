using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using System.Collections;
using GameNetcodeStuff;

namespace MonsterOverdoseCompany
{
    [BepInPlugin("com.votre_pseudo.monsteroverdosecompany", "Monster-Overdose-Company", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private readonly Harmony harmony = new Harmony("com.votre_pseudo.monsteroverdosecompany");
        public static Plugin Instance;

        void Awake()
        {
            Instance = this;
            Logger.LogInfo("[Monster-Overdose-Company] Mod chargé avec succès ! Préparez-vous au chaos.");
            harmony.PatchAll();
        }
    }

    // ==========================================
    // 1. RÈGLE : BONUS DE +20% DE SCRAP
    // ==========================================
    [HarmonyPatch(typeof(RoundManager))]
    public class ScrapBonusPatch
    {
        [HarmonyPatch("SpawnScrapInLevel")]
        [HarmonyPrefix]
        static void BoostScrapAmount(RoundManager __instance)
        {
            if (__instance.currentLevel != null)
            {
                __instance.currentLevel.minScrap = Mathf.RoundToInt(__instance.currentLevel.minScrap * 1.20f);
                __instance.currentLevel.maxScrap = Mathf.RoundToInt(__instance.currentLevel.maxScrap * 1.20f);
                Debug.Log($"[Monster-Overdose-Company] Bonus de scrap (+20%) appliqué ! Max scrap: {__instance.currentLevel.maxScrap}");
            }
        }
    }

    // ==========================================
    // 2. DÉCLENCHEURS (ENTRÉE ET SORTIE COMPLEXE)
    // ==========================================
    [HarmonyPatch(typeof(EntranceTeleport))]
    public class EntrancePatch
    {
        [HarmonyPatch("TeleportPlayer")]
        [HarmonyPostfix]
        static void Postfix(bool ___isEntranceToBuilding)
        {
            if (___isEntranceToBuilding && !ChaosManager.hasPlayerEntered)
            {
                ChaosManager.hasPlayerEntered = true;
                Debug.Log("[Monster-Overdose-Company] Joueur entre ! Chrono activé.");
            }
            else if (!___isEntranceToBuilding && ChaosManager.hasPlayerEntered && !RobotManager.hasSequenceStarted)
            {
                RobotManager.hasSequenceStarted = true;
                Plugin.Instance.StartCoroutine(RobotManager.WakeUpRobotsSequence());
                Debug.Log("[Monster-Overdose-Company] Joueur sort ! Lancement du réveil progressif des robots !");
            }
        }
    }

    // ==========================================
    // 3. GESTION DES ROBOTS
    // ==========================================
    public class RobotManager
    {
        public static List<EnemyAI> spawnedRobots = new List<EnemyAI>();
        public static bool hasSequenceStarted = false;

        public static void InitRobots(RoundManager manager)
        {
            spawnedRobots.Clear();
            hasSequenceStarted = false;

            SpawnableEnemyWithRarity robotEnemy = manager.currentLevel.OutsideEnemies.Find(e => e.enemyType.enemyName.ToLower().Contains("radmech"));
            if (robotEnemy == null) return;

            Vector3 shipPosition = Vector3.zero;
            GameObject shipObj = GameObject.FindWithTag("Ship");
            if (shipObj != null)
            {
                shipPosition = shipObj.transform.position;
            }

            int spawnedCount = 0;
            int attempts = 0;

            while (spawnedCount < 15 && attempts < 150)
            {
                attempts++;
                Vector3 randomPoint = Random.insideUnitSphere * 40f;
                NavMeshHit hit;

                if (NavMesh.SamplePosition(randomPoint, out hit, 25f, NavMesh.AllAreas))
                {
                    if (Vector3.Distance(hit.position, shipPosition) < 20f) continue;

                    GameObject obj = Object.Instantiate(robotEnemy.enemyType.enemyPrefab, hit.position, Quaternion.identity);
                    EnemyAI robot = obj.GetComponent<EnemyAI>();
                    if (robot != null)
                    {
                        spawnedRobots.Add(robot);
                        spawnedCount++;
                    }
                }
            }
            Debug.Log($"[Monster-Overdose-Company] {spawnedCount} robots générés dehors.");
        }

        public static IEnumerator WakeUpRobotsSequence()
        {
            foreach (EnemyAI robot in spawnedRobots)
            {
                if (robot != null && !robot.isEnemyDead)
                {
                    robot.SwitchToBehaviourState(1);
                    Debug.Log("[Monster-Overdose-Company] Un robot se réveille !");
                }
                yield return new WaitForSeconds(10f);
            }
        }
    }

    // ==========================================
    // 4. GESTION DU CHAOS ET DES SPAWNS MONSTRES
    // ==========================================
    [HarmonyPatch(typeof(RoundManager))]
    public class ChaosManager
    {
        public static bool hasPlayerEntered = false;
        public static float gameTimer = 0f;
        public static float spawnIntervalTimer = 0f;

        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        static void ResetOnStart(RoundManager __instance)
        {
            hasPlayerEntered = false;
            gameTimer = 0f;
            spawnIntervalTimer = 0f;
            RobotManager.InitRobots(__instance);
        }

        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        static void UpdateChaos(RoundManager __instance)
        {
            if (!hasPlayerEntered || __instance.currentLevel == null) return;

            gameTimer += Time.deltaTime;
            spawnIntervalTimer += Time.deltaTime;

            int currentMaxEnemies = 10 + (int)(gameTimer / 120f) * 10;
            if (currentMaxEnemies > 50) currentMaxEnemies = 50;

            __instance.currentLevel.maxEnemyPowerCount = currentMaxEnemies;
            __instance.currentLevel.maxOutsideEnemyPowerCount = currentMaxEnemies;

            if (spawnIntervalTimer >= 15f)
            {
                spawnIntervalTimer = 0f;
                TrySpawnChaosEnemy(__instance);
            }

            if (gameTimer >= 300f)
            {
                MakeAllEnemiesHostile();
            }
        }

        static void TrySpawnChaosEnemy(RoundManager manager)
        {
            if (StartOfRound.Instance.allPlayerScripts.Length == 0) return;
            PlayerControllerB targetPlayer = StartOfRound.Instance.allPlayerScripts[Random.Range(0, StartOfRound.Instance.allPlayerScripts.Length)];
            if (targetPlayer == null || !targetPlayer.isPlayerControlled || targetPlayer.isPlayerDead) return;

            List<SpawnableEnemyWithRarity> allEnemies = new List<SpawnableEnemyWithRarity>();
            if (manager.currentLevel.Enemies != null) allEnemies.AddRange(manager.currentLevel.Enemies);

            if (allEnemies.Count == 0) return;

            SpawnableEnemyWithRarity selectedEnemy = allEnemies[Random.Range(0, allEnemies.Count)];
            Vector3 spawnPos = targetPlayer.transform.position + (Random.insideUnitSphere * Random.Range(5f, 25f));

            NavMeshHit hit;
            if (NavMesh.SamplePosition(spawnPos, out hit, 20f, NavMesh.AllAreas))
            {
                int enemyIndex = manager.currentLevel.Enemies.IndexOf(selectedEnemy);
                if (enemyIndex != -1)
                {
                    manager.SpawnEnemyOnServer(hit.position, 0f, enemyIndex);
                }
            }
        }

        static void MakeAllEnemiesHostile()
        {
            EnemyAI[] enemies = Object.FindObjectsOfType<EnemyAI>();
            foreach (EnemyAI enemy in enemies)
            {
                if (enemy.isEnemyDead) continue;
                enemy.SwitchToBehaviourState(1);
            }
        }
    }

    // ==========================================
    // 5. RÈGLE LÉVIATHAN
    // ==========================================
    [HarmonyPatch(typeof(SandWormAI))]
    public class LeviathanIndoorPatch
    {
        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        static void CustomLeviathanMovement(SandWormAI __instance)
        {
            if (__instance.targetPlayer != null && ChaosManager.gameTimer >= 420f)
            {
                if (__instance.agent == null)
                {
                    __instance.agent = __instance.gameObject.GetComponent<NavMeshAgent>();
                }

                if (__instance.agent != null && __instance.agent.isOnNavMesh)
                {
                    float distance = Vector3.Distance(__instance.transform.position, __instance.targetPlayer.transform.position);
                    if (distance > 20f)
                    {
                        __instance.agent.speed = 20f;
                        __instance.SetDestinationToPosition(__instance.targetPlayer.transform.position);
                    }
                }
            }
        }
    }
}
