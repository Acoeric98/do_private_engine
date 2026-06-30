using Ow.Game.Movements;
using Ow.Game.Objects;
using Ow.Managers;
using Ow.Net.netty.commands;
using Ow.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Ow.Game.GalaxyGates
{
    /// <summary>
    /// Egyedül teljesíthető, könnyen testreszabható kapu sablon a Hades logikája alapján.
    /// </summary>
    class SoloGateTemplate
    {
        // =====================================================================
        // ===================== ITT ÁLLÍTSD A KAPU ALAPJAIT ===================
        // =====================================================================
        public const int GateMapId = 71;                 // <<< IDE ÍRD: melyik meglévő map kinézetét használja a kapu (Hades minta: 71)
        public const int ExitMapId = 16;                 // <<< IDE ÍRD: teljesítés után melyik mapra dobjon ki
        private const int EventPortalGraphicId = 1;      // <<< IDE ÍRD: belépő event portál kinézete
        private const int ExitPortalGraphicId = 1;       // <<< IDE ÍRD: kapun belüli kilépő portál kinézete
        private const int FirstWaveDelaySeconds = 10;    // <<< IDE ÍRD: belépés után hány mp múlva induljon az első wave
        private const int NextWaveDelaySeconds = 8;      // <<< IDE ÍRD: wave-ek között hány mp szünet legyen
        private const int CompletionExitDelaySeconds = 15;// <<< IDE ÍRD: teljesítés után hány mp múlva dobjon ki
        private const int NpcSpawnCircleRadius = 180;    // <<< IDE ÍRD: NPC-k milyen távol spawnoljanak a középponttól
        private const int FinalUridiumReward = 10000;    // <<< IDE ÍRD: végső uridium jutalom
        private const int FinalHonorReward = 250000;     // <<< IDE ÍRD: végső honor jutalom

        // =====================================================================
        // ===================== ITT ÁLLÍTSD A POZÍCIÓKAT =======================
        // =====================================================================
        private static readonly Position GateCenter = new Position(10400, 6400);       // <<< IDE ÍRD: kapun belüli kezdő/spawn közép
        private static readonly Position ExitPortalPosition = new Position(10700, 6400);// <<< IDE ÍRD: belső kilépő portál helye
        private static readonly Position ExitTargetPosition = new Position(21000, 13000);// <<< IDE ÍRD: hova vigyen a belső kilépő portál
        private static readonly Position CompletionExitPosition = new Position(10400, 6400);// <<< IDE ÍRD: teljesítés utáni kidobási pozíció

        // =====================================================================
        // ===================== ITT ÁLLÍTSD A WAVE-EKET / NPC-KET ==============
        // =====================================================================
        // Formátum: new SoloGateWave("Wave neve", new SoloGateNpcSpawn(NPC_SHIP_ID, DARAB), ...)
        // Példa NPC ID-k Hadesből: 74 Sibelon, 46 Boss Sibelon, 114 Uber Sibelon, 124 Emperor Sibelon.
        private static readonly SoloGateWave[] GateWaves = new[]
        {
            new SoloGateWave("Alap wave 1", new SoloGateNpcSpawn(74, 10)),                  // <<< IDE ÍRD: 1. wave NPC id + darab
            new SoloGateWave("Alap wave 2", new SoloGateNpcSpawn(46, 5), new SoloGateNpcSpawn(114, 2)), // <<< IDE ÍRD: 2. wave NPC-k
            new SoloGateWave("Alap boss", new SoloGateNpcSpawn(124, 1))                    // <<< IDE ÍRD: boss wave NPC id + darab
        };

        public static bool Active { get; private set; }
        public static int EntryMapId { get; private set; }
        public static Portal EventPortal { get; private set; }

        private static readonly object SyncRoot = new object();
        private static readonly List<SoloGateRun> Runs = new List<SoloGateRun>();

        public static bool StartEvent(int entryMapId, Position entryPosition, out string message)
        {
            lock (SyncRoot)
            {
                if (Active)
                {
                    message = "Solo gate template event is already active.";
                    return false;
                }

                var entryMap = GameManager.GetSpacemap(entryMapId);
                if (entryMap == null || GameManager.GetSpacemap(GateMapId) == null || GameManager.GetSpacemap(ExitMapId) == null)
                {
                    message = $"Entry map, gate map {GateMapId}, or exit map {ExitMapId} doesn't exist.";
                    return false;
                }

                EntryMapId = entryMapId;
                EventPortal = new Portal(entryMap, entryPosition, GateCenter, GateMapId, EventPortalGraphicId, 0, true, true);
                GameManager.SendCommandToMap(entryMap.Id, EventPortal.GetAssetCreateCommand());

                Active = true;
                message = $"Solo gate template event started on map {entryMapId} at X: {entryPosition.X}, Y: {entryPosition.Y}.";
                GameManager.SendPacketToAll("0|A|STD|Solo gate template event started! Egyedül is indítható, csoport nem kell.");
                return true;
            }
        }

        public static void StopEvent()
        {
            lock (SyncRoot)
            {
                Active = false;

                if (EventPortal != null)
                {
                    EventPortal.Remove();
                    EventPortal = null;
                }

                foreach (var run in Runs.ToList())
                    run.Dispose();

                Runs.Clear();
                GameManager.SendPacketToAll("0|A|STD|Solo gate template event ended!");
            }
        }

        public static bool IsEventPortal(Portal portal)
        {
            return Active && EventPortal != null && portal != null && portal.Id == EventPortal.Id;
        }

        public static bool TryUseRunPortal(Player player, Portal portal)
        {
            if (player == null || portal == null)
                return false;

            lock (SyncRoot)
            {
                var run = Runs.FirstOrDefault(candidate => candidate.ContainsPortal(portal.Id));
                if (run == null)
                    return false;

                run.UsePortal(player, portal);
                return true;
            }
        }

        public static bool TryEnter(Player player)
        {
            if (!Active || EventPortal == null)
            {
                player.SendPacket("0|A|STD|Solo gate template event is not active.");
                return false;
            }

            lock (SyncRoot)
            {
                var existingRun = Runs.FirstOrDefault(activeRun => activeRun.ContainsPlayer(player.Id));
                if (existingRun != null)
                {
                    existingRun.ReturnPlayer(player);
                    return true;
                }

                var run = new SoloGateRun(player);
                Runs.Add(run);
                run.Start();
            }

            return true;
        }

        private static void RemoveRun(SoloGateRun run)
        {
            lock (SyncRoot)
                Runs.Remove(run);
        }

        private class SoloGateRun
        {
            private readonly int PlayerId;
            private readonly Spacemap Spacemap;
            private readonly List<int> NpcIds = new List<int>();
            private readonly List<int> PortalIds = new List<int>();
            private Portal ExitPortal;
            private int CurrentWaveIndex;
            private bool Completed;
            private bool Disposed;
            private bool PortalJumpInProgress;

            public SoloGateRun(Player player)
            {
                PlayerId = player.Id;
                Spacemap = new Spacemap(GateMapId, $"SoloGate-{player.Id}-{DateTime.Now.Ticks}", 0, null, null, null, new OptionsBase { RangeDisabled = true, DeathLocationRepair = false, LogoutBlocked = true });
            }

            public bool ContainsPlayer(int playerId)
            {
                return PlayerId == playerId;
            }

            public bool ContainsPortal(int portalId)
            {
                return PortalIds.Contains(portalId);
            }

            public void ReturnPlayer(Player player)
            {
                if (player == null || player.Id != PlayerId)
                    return;

                JumpPlayer(player, GateCenter, Spacemap);
                player.SendPacket($"0|A|STD|Visszaengedve a saját solo gate mapodra ({Spacemap.Name}).");
            }

            public async void Start()
            {
                Spacemap.CharacterRemoved += OnCharacterRemoved;
                var player = GameManager.GetPlayerById(PlayerId);
                if (player == null)
                {
                    Dispose();
                    RemoveRun(this);
                    return;
                }

                JumpPlayer(player, GateCenter, Spacemap);
                SendMessage($"Solo gate indul {FirstWaveDelaySeconds} másodperc múlva.");
                await Countdown("Első wave", FirstWaveDelaySeconds);

                if (!Disposed && !Completed)
                    SpawnCurrentWave();
            }

            public void Dispose()
            {
                if (Disposed) return;
                Disposed = true;

                Spacemap.CharacterRemoved -= OnCharacterRemoved;
                RemoveExitPortal();

                foreach (var npcId in NpcIds.ToList())
                {
                    var npc = Spacemap.Characters.Values.OfType<Npc>().FirstOrDefault(character => character.Id == npcId);
                    if (npc != null && !npc.Destroyed)
                        npc.Destroy(null, DestructionType.MISC);
                }

                NpcIds.Clear();
                PortalIds.Clear();
                global::Ow.Program.TickManager.RemoveTick(Spacemap);
            }

            public void UsePortal(Player player, Portal portal)
            {
                if (player == null || portal == null || player.Id != PlayerId)
                    return;

                if (ExitPortal != null && portal.Id == ExitPortal.Id)
                    StartPortalCountdown(ExitTargetPosition, GameManager.GetSpacemap(ExitMapId), null);
            }

            private void SpawnCurrentWave()
            {
                if (CurrentWaveIndex >= GateWaves.Length)
                {
                    CompleteRunAfterDelay();
                    return;
                }

                var wave = GateWaves[CurrentWaveIndex];
                SpawnNpcGroupOnCircle(wave.Spawns.ToList());
                SendMessage($"Solo gate wave: {wave.Name}. NPC-k: {string.Join(", ", wave.Spawns.Select(spawn => $"{GetShipName(spawn.ShipId)} x{spawn.Amount}"))}.");
            }

            private void SpawnNpc(int shipId, Position position)
            {
                var ship = GameManager.GetShip(shipId);
                if (ship == null)
                {
                    SendMessage($"Solo gate hiba: hiányzó ship id {shipId}.");
                    return;
                }

                var npc = new Npc(Randoms.CreateRandomID(), ship, Spacemap, position, false);
                npc.NpcAI.AttackRandomPlayersAggressively = true;
                NpcIds.Add(npc.Id);
            }

            private void SpawnNpcGroupOnCircle(List<SoloGateNpcSpawn> spawns)
            {
                var totalAmount = spawns.Sum(spawn => spawn.Amount);
                if (totalAmount <= 0)
                    return;

                var spawnedCount = 0;
                foreach (var spawn in spawns)
                {
                    for (var i = 0; i < spawn.Amount; i++)
                    {
                        var angle = 2 * Math.PI * spawnedCount / totalAmount;
                        var x = GateCenter.X + (int)Math.Round(Math.Cos(angle) * NpcSpawnCircleRadius);
                        var y = GateCenter.Y + (int)Math.Round(Math.Sin(angle) * NpcSpawnCircleRadius);
                        SpawnNpc(spawn.ShipId, new Position(x, y));
                        spawnedCount++;
                    }
                }
            }

            private void OnCharacterRemoved(object sender, Spacemap.CharacterArgs e)
            {
                if (Disposed || Completed)
                    return;

                if (e.Character is Player)
                {
                    SendMessage("Solo gate megszakadt, a játékos kiment a kapuból.");
                    Dispose();
                    RemoveRun(this);
                    return;
                }

                var npc = e.Character as Npc;
                if (npc == null || !NpcIds.Remove(npc.Id))
                    return;

                if (NpcIds.Count != 0)
                    return;

                CurrentWaveIndex++;
                if (CurrentWaveIndex >= GateWaves.Length)
                {
                    CompleteRunAfterDelay();
                    return;
                }

                StartNextWaveAfterDelay();
            }

            private async void StartNextWaveAfterDelay()
            {
                SendMessage($"Következő wave {NextWaveDelaySeconds} másodperc múlva.");
                await Countdown("Következő wave", NextWaveDelaySeconds);

                if (!Disposed && !Completed)
                    SpawnCurrentWave();
            }

            private async void CompleteRunAfterDelay()
            {
                if (Completed)
                    return;

                Completed = true;
                RewardPlayer();
                SpawnExitPortal();
                SendMessage($"Solo gate teljesítve! {CompletionExitDelaySeconds} másodperc múlva kidob, vagy használd a megjelent kijárat portált.");
                await Task.Delay(CompletionExitDelaySeconds * 1000);

                var player = GameManager.GetPlayerById(PlayerId);
                var exitMap = GameManager.GetSpacemap(ExitMapId);
                if (player != null && player.Spacemap == Spacemap && exitMap != null)
                    JumpPlayer(player, CompletionExitPosition, exitMap);

                Dispose();
                RemoveRun(this);
            }

            private void SpawnExitPortal()
            {
                RemoveExitPortal();
                ExitPortal = new Portal(Spacemap, ExitPortalPosition, ExitTargetPosition, ExitMapId, ExitPortalGraphicId, 0, true, true);
                PortalIds.Add(ExitPortal.Id);

                var player = GameManager.GetPlayerById(PlayerId);
                if (player?.Spacemap == Spacemap)
                    player.SendCommand(ExitPortal.GetAssetCreateCommand());
            }

            private void RemoveExitPortal()
            {
                if (ExitPortal == null)
                    return;

                PortalIds.Remove(ExitPortal.Id);
                Activatable activatable;
                Spacemap.Activatables.TryRemove(ExitPortal.Id, out activatable);

                var player = GameManager.GetPlayerById(PlayerId);
                if (player?.Spacemap == Spacemap)
                    player.SendCommand(RemovePortalCommand.write(ExitPortal.Id));

                ExitPortal = null;
            }

            private async void StartPortalCountdown(Position position, Spacemap targetMap, Action afterJump)
            {
                if (PortalJumpInProgress || targetMap == null)
                    return;

                PortalJumpInProgress = true;
                await Countdown("Kapu ugrás", 3);
                if (Disposed)
                {
                    PortalJumpInProgress = false;
                    return;
                }

                var player = GameManager.GetPlayerById(PlayerId);
                if (player != null && player.Spacemap == Spacemap)
                    JumpPlayer(player, position, targetMap);

                PortalJumpInProgress = false;
                afterJump?.Invoke();
                Dispose();
                RemoveRun(this);
            }

            private async Task Countdown(string label, int secondsTotal)
            {
                for (var seconds = secondsTotal; seconds > 0; seconds--)
                {
                    SendMessage($"{label}: {seconds} mp.");
                    await Task.Delay(1000);
                    if (Disposed)
                        return;
                }
            }

            private void JumpPlayer(Player player, Position position, Spacemap targetMap)
            {
                if (targetMap == null)
                    return;

                if (player.Spacemap != null)
                    player.Spacemap.RemoveCharacter(player);

                player.LastCombatTime = DateTime.Now.AddSeconds(-999);
                player.CurrentInRangePortalId = -1;
                player.Deselection();
                player.Storage.InRangeAssets.Clear();
                player.InRangeCharacters.Clear();
                player.SetPosition(new Position(position.X, position.Y));
                player.Spacemap = targetMap;
                player.Spacemap.AddAndInitPlayer(player);
            }

            private void RewardPlayer()
            {
                var player = GameManager.GetPlayerById(PlayerId);
                if (player == null || player.Spacemap != Spacemap)
                    return;

                player.ChangeData(DataType.HONOR, FinalHonorReward);
                player.ChangeData(DataType.URIDIUM, FinalUridiumReward);
                player.SendPacket($"0|A|STD|Solo gate reward: {FinalHonorReward} becsület és {FinalUridiumReward} uridium.");
            }

            private string GetShipName(int shipId)
            {
                return GameManager.GetShip(shipId)?.Name ?? $"NPC {shipId}";
            }

            private void SendMessage(string message)
            {
                var player = GameManager.GetPlayerById(PlayerId);
                if (player != null)
                    player.SendPacket($"0|A|STD|{message}");
            }
        }

        private class SoloGateNpcSpawn
        {
            public int ShipId { get; }
            public int Amount { get; }

            public SoloGateNpcSpawn(int shipId, int amount)
            {
                ShipId = shipId;
                Amount = amount;
            }
        }

        private class SoloGateWave
        {
            public string Name { get; }
            public IEnumerable<SoloGateNpcSpawn> Spawns { get; }

            public SoloGateWave(string name, params SoloGateNpcSpawn[] spawns)
            {
                Name = name;
                Spawns = spawns;
            }
        }
    }
}
