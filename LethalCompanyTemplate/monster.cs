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
            Logger.LogInfo("[Monster-Overdose-Company] Mod chargé !");
            harmony.PatchAll();
        }
    }

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
            }
        }
    }

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
            }
            else if (!___isEntranceToBuilding && ChaosManager.hasPlayerEntered && !RobotManager.hasSequenceStarted)
            {
                RobotManager.hasSequenceStarted = true;
                Plugin.Instance.StartCoroutine(RobotManager.WakeUpRobotsSequence());
            }
        }
    }

    public class RobotManager
    {
        public static List<EnemyAI> spawnedRobots = new List<EnemyAI>();
        public static bool hasSequenceStarted = false;

        public static void InitRobots(RoundManager manager)
        {
            spawnedRobots.Clear();
            hasSequenceStarted = false;

            if (manager.currentLevel.OutsideEnemies == null) return;
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
                Vector3 randomPoint = shipPosition + (Random.insideUnitSphere * 40f);
                NavMeshHit hit;

                if (NavMesh.SamplePosition(randomPoint, out hit, 25f, NavMesh.AllAreas))
                {
                    if (Vector3.Distance(hit.position, shipPosition) < 20f) continue;

                    int enemyIndex = manager.currentLevel.OutsideEnemies.IndexOf(robotEnemy);
                    if (enemyIndex != -1)
                    {
                        manager.SpawnOutsideEnemy(hit.position, 0f, enemyIndex);
                        
                        EnemyAI[] allActiveEnemies = Object.FindObjectsOfType<EnemyAI>();
                        foreach (EnemyAI robot in allActiveEnemies)
                        {
                            if (robot != null && !spawnedRobots.Contains(robot) && Vector3.Distance(robot.transform.position, hit.position) < 3f)
                            {
                                spawnedRobots.Add(robot);
                                spawnedCount++;
                                break;
                            }
                        }
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
                }
                yield return new WaitForSeconds(10f);
            }
        }
    }

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

            if (spawnIntervalTimer >= 15f)
            {
                spawnIntervalTimer = 0f;
                TrySpawnChaosEnemy(__instance);
            }
        }

        static void TrySpawnChaosEnemy(RoundManager manager)
        {
            if (StartOfRound.Instance.allPlayerScripts.Length == 0) return;
            PlayerControllerB targetPlayer = StartOfRound.Instance.allPlayerScripts[Random.Range(0, StartOfRound.Instance.allPlayerScripts.Length)];
            if (targetPlayer == null || !targetPlayer.isPlayerControlled || targetPlayer.isPlayerDead) return;

            if (manager.currentLevel.Enemies == null || manager.currentLevel.Enemies.Count == 0) return;

            SpawnableEnemyWithRarity selectedEnemy = manager.currentLevel.Enemies[Random.Range(0, manager.currentLevel.Enemies.Count)];
            Vector3 spawnPos = targetPlayer.transform.position + (Random.insideUnitSphere * Random.Range(5f, 25f));

            NavMeshHit hit;
            if (NavMesh.SamplePosition(spawnPos, out hit, 20f, NavMesh.AllAreas))
            {
                int enemyIndex = manager.currentLevel.Enemies.IndexOf(selectedEnemy);
                if (enemyIndex != -1)
                {
                    manager.SpawnEnemyOnServer(hit.position, 0f, enemyIndex);

                    EnemyAI[] allActiveEnemies = Object.FindObjectsOfType<EnemyAI>();
                    if (allActiveEnemies.Length > 0)
                    {
                        foreach (EnemyAI enemyAI in allActiveEnemies)
                        {
                            if (enemyAI != null && !enemyAI.isOutside && Vector3.Distance(enemyAI.transform.position, hit.position) < 3f)
                            {
                                enemyAI.isOutside = true;
                                enemyAI.allAINodes = GameObject.FindGameObjectsWithTag("OutsideAINode");
                                enemyAI.SwitchToBehaviourState(1);
                                
                                if (enemyAI.agent != null)
                                {
                                    enemyAI.agent.Warp(hit.position);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
