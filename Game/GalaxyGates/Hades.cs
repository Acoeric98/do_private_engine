using Ow.Game.Movements;
using Ow.Game.Objects;
using Ow.Game.Objects.Players.Managers;
using Ow.Managers;
using Ow.Net.netty.commands;
using Ow.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Ow.Game.GalaxyGates
{
    class Hades
    {
        public const int HadesMapId = 71;
        public const int ExitMapId = 16;
        public const int MinimumPlayers = 2;
        private const bool DebugAllowSoloEntry = true;
        public const int MaximumPlayers = 8;
        private const int WaveTwoTriggerRemainingNpcs = 10;
        private const int FinalHonorReward = 16000000;
        private const int RewardKeysPerType = 3;
        private const int PortalJumpCountdownSeconds = 15;
        private const int FirstWaveStartDelaySeconds = 15;
        private const int NpcSpawnCircleRadius = 3000;
        private const int NpcCountMessageIntervalSeconds = 45;
        private const int RandomBoosterRewardHours = 5;
        private const int FinalUridiumReward = 50000;
        private const int CompletionExitDelaySeconds = 20;
        private const int EventPortalGraphicId = 1;
        private const int RestPortalGraphicId = 1;
        private const int ExitPortalGraphicId = 1;

        private static readonly Position HadesCenter = new Position(10400, 6400);
        private static readonly Position RestPortalPosition = new Position(10100, 6400);
        private static readonly Position ExitPortalPosition = new Position(10700, 6400);
        private static readonly Position ExitTargetPosition = new Position(21000, 13000);
        private static readonly Position ExitMapCenter = new Position(21200, 13300);

        public static bool Active { get; private set; }
        public static int EntryMapId { get; private set; }
        public static Portal EventPortal { get; private set; }

        private static readonly object SyncRoot = new object();
        private static readonly List<HadesRun> Runs = new List<HadesRun>();

        public static bool StartEvent(int entryMapId, Position entryPosition, out string message)
        {
            lock (SyncRoot)
            {
                if (Active)
                {
                    message = "Hades event is already active.";
                    return false;
                }

                var entryMap = GameManager.GetSpacemap(entryMapId);
                if (entryMap == null || GameManager.GetSpacemap(HadesMapId) == null || GameManager.GetSpacemap(ExitMapId) == null)
                {
                    message = "Entry map, Hades map 71, or exit map 16 doesn't exist.";
                    return false;
                }

                EntryMapId = entryMapId;
                EventPortal = new Portal(entryMap, entryPosition, HadesCenter, HadesMapId, EventPortalGraphicId, 0, true, true);
                GameManager.SendCommandToMap(entryMap.Id, EventPortal.GetAssetCreateCommand());

                Active = true;
                message = $"Hades event started on map {entryMapId} at X: {entryPosition.X}, Y: {entryPosition.Y}. Target map is fixed to {HadesMapId}.";
                GameManager.SendPacketToAll("0|A|STD|Hades event started! Minimum 2, maximum 8 players can enter together.");
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
                GameManager.SendPacketToAll("0|A|STD|Hades event ended!");
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
                player.SendPacket("0|A|STD|Hades event is not active.");
                return false;
            }

            var group = player.Group;
            var debugSoloEntry = DebugAllowSoloEntry && group == null;
            if (!debugSoloEntry)
            {
                if (group == null)
                {
                    player.SendPacket("0|A|STD|Hades kapuhoz csoport kell.");
                    return false;
                }

                if (group.Leader != player)
                {
                    player.SendPacket("0|A|STD|A Hades kaput csak a csoport vezetője indíthatja.");
                    return false;
                }
            }

            var eligiblePlayers = debugSoloEntry
                ? new List<Player> { player }
                : group.Members.Values
                    .Where(member => member != null && member.GameSession != null && !member.Destroyed && member.Spacemap != null && member.Spacemap.Id == EntryMapId && member.Position.DistanceTo(EventPortal.Position) <= Portal.SECURE_ZONE_RANGE)
                    .ToList();

            if (eligiblePlayers.Count < MinimumPlayers && !debugSoloEntry)
            {
                SendToGroup(player, $"Hades kapuhoz minimum {MinimumPlayers} csoporttag kell a kapunál.");
                return false;
            }

            if (eligiblePlayers.Count > MaximumPlayers)
            {
                SendToGroup(player, $"Hades kapuba egyszerre maximum {MaximumPlayers} játékos mehet be.");
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

                if (Runs.Any(activeRun => activeRun.ContainsAnyPlayer(eligiblePlayers)))
                {
                    SendToGroup(player, "Van olyan csoporttag, akinek már fut Hades kapuja.");
                    return false;
                }

                var run = new HadesRun(debugSoloEntry ? player.Id : group.Id, eligiblePlayers);
                Runs.Add(run);
                run.Start();
            }

            return true;
        }


        public static bool IsPlayerInActiveRun(Player player)
        {
            if (player == null)
                return false;

            lock (SyncRoot)
                return Runs.Any(run => run.ContainsPlayer(player.Id));
        }

        public static void ResetForPlayer(Player player)
        {
            if (player == null)
                return;

            HadesRun run;
            lock (SyncRoot)
                run = Runs.FirstOrDefault(candidate => candidate.ContainsPlayer(player.Id));

            run?.ResetBecausePlayerLeftGroup(player);
        }

        private static void SendToGroup(Player player, string message)
        {
            if (player.Group == null) return;

            foreach (var member in player.Group.Members.Values)
                member.SendPacket($"0|A|STD|{message}");
        }

        private static void RemoveRun(HadesRun run)
        {
            lock (SyncRoot)
                Runs.Remove(run);
        }

        private class HadesRun
        {
            private readonly int GroupId;
            private readonly Spacemap Spacemap;
            private readonly List<int> PlayerIds;
            private readonly List<int> NpcIds = new List<int>();
            private readonly List<int> PortalIds = new List<int>();
            private Portal RestPortal;
            private Portal ExitPortal;
            private bool WaveTwoSpawned;
            private bool BossSpawned;
            private bool WaitingForNextStage;
            private bool Completed;
            private bool Disposed;
            private bool PortalJumpInProgress;
            private bool SoloReturnGraceInProgress;

            private readonly HadesWaveDefinition[] Waves = new[]
            {
                new HadesWaveDefinition("Sibelon", 74, 46, 114, 124),
                new HadesWaveDefinition("Lordakium", 77, 28, 107, 123),
                new HadesWaveDefinition("Kristallon", 79, 35, 45, 122)
            };

            private int CurrentWaveIndex;

            public HadesRun(int groupId, List<Player> players)
            {
                GroupId = groupId;
                PlayerIds = players.Select(player => player.Id).ToList();
                Spacemap = new Spacemap(HadesMapId, $"Hades-{groupId}-{DateTime.Now.Ticks}", 0, null, null, null, new OptionsBase { RangeDisabled = true, DeathLocationRepair = true, LogoutBlocked = true });
                Spacemap.GroupId = groupId;
            }

            public bool ContainsAnyPlayer(List<Player> players)
            {
                return players.Any(player => PlayerIds.Contains(player.Id));
            }

            public bool ContainsPlayer(int playerId)
            {
                return PlayerIds.Contains(playerId);
            }

            public void ReturnPlayer(Player player)
            {
                if (player == null || !PlayerIds.Contains(player.Id))
                    return;

                JumpPlayer(player, HadesCenter, Spacemap);
                player.SendPacket($"0|A|STD|Visszaengedve a saját Hades csoport mapodra ({Spacemap.Name}).");
            }

            public bool ContainsPortal(int portalId)
            {
                return PortalIds.Contains(portalId);
            }

            public async void Start()
            {
                Spacemap.CharacterRemoved += OnCharacterRemoved;

                foreach (var playerId in PlayerIds)
                {
                    var player = GameManager.GetPlayerById(playerId);
                    if (player == null) continue;
                    JumpPlayer(player, HadesCenter, Spacemap);
                }

                await WaitUntilAllPlayersInside();

                if (Disposed || Completed)
                    return;

                SendMessage($"Minden csoporttag bent van a Hades kapuban. Wave 1 {FirstWaveStartDelaySeconds} másodperc múlva indul, várakozás a csapat összerendeződésére.");
                await Countdown("Wave 1", FirstWaveStartDelaySeconds);

                if (Disposed || Completed)
                    return;

                MonitorNpcCount();
                SendMessage("A Hades Wave 1 indul.");
                SpawnWaveOne();
            }

            public void Dispose()
            {
                if (Disposed) return;
                Disposed = true;

                Spacemap.CharacterRemoved -= OnCharacterRemoved;
                RemoveRestPortals();

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


            public async void ResetBecausePlayerLeftGroup(Player player)
            {
                if (Disposed || player == null || !PlayerIds.Contains(player.Id))
                    return;

                var hadesMap = Spacemap;
                var oldPosition = new Position(player.Position.X, player.Position.Y);
                var wasInside = player.Spacemap == hadesMap;

                SendMessage($"{player.Name} kilépett vagy ki lett dobva a csoportból, ezért a Hades kapu resetelődik.");
                Dispose();
                RemoveRun(this);

                if (!wasInside)
                    return;

                await Task.Delay(20000);

                var currentPlayer = GameManager.GetPlayerById(player.Id);
                var exitMap = GameManager.GetSpacemap(ExitMapId);
                if (currentPlayer != null && exitMap != null && currentPlayer.Spacemap == hadesMap && currentPlayer.Position.DistanceTo(oldPosition) <= 1)
                    JumpPlayer(currentPlayer, ExitMapCenter, exitMap);
            }

            private async void StartSoloReturnGrace(Player player)
            {
                if (SoloReturnGraceInProgress)
                    return;

                SoloReturnGraceInProgress = true;
                player.SendPacket("0|A|STD|Egyedül haltál meg a Hades kapuban. 5 perced van visszatérni a kapuba.");
                await Task.Delay(5 * 60 * 1000);

                if (Disposed || Completed)
                    return;

                if (!PlayerIds.Any(playerId => GameManager.GetPlayerById(playerId)?.Spacemap == Spacemap))
                {
                    Dispose();
                    RemoveRun(this);
                }
            }

            public void UsePortal(Player player, Portal portal)
            {
                if (player == null || portal == null || !PlayerIds.Contains(player.Id))
                    return;

                if (RestPortal != null && portal.Id == RestPortal.Id)
                {
                    StartPortalCountdown(HadesCenter, Spacemap, () =>
                    {
                        RemoveRestPortals();
                        WaitingForNextStage = false;
                        SendMessage("Hades pihenő vége, a következő wave indul.");
                        SpawnWaveOne();
                    });
                    return;
                }

                if (ExitPortal != null && portal.Id == ExitPortal.Id)
                    StartPortalCountdown(ExitTargetPosition, GameManager.GetSpacemap(ExitMapId), null);
            }


            private async Task WaitUntilAllPlayersInside()
            {
                while (!Disposed && !Completed && !AreAllPlayersInside())
                {
                    SendMessage("Várakozás: a Wave 1 csak akkor indul, ha a teljes csoport bent van a Hades kapuban.");
                    await Task.Delay(1000);
                }
            }

            private bool AreAllPlayersInside()
            {
                return PlayerIds.All(playerId => GameManager.GetPlayerById(playerId)?.Spacemap == Spacemap);
            }

            private async void MonitorNpcCount()
            {
                while (!Disposed && !Completed)
                {
                    await Task.Delay(NpcCountMessageIntervalSeconds * 1000);
                    if (Disposed || Completed || BossSpawned)
                        continue;

                    SendMessage($"Hades NPC-k a mapon: {NpcIds.Count} db.");
                }
            }

            private async void StartPortalCountdown(Position position, Spacemap targetMap, Action afterJump)
            {
                if (PortalJumpInProgress || targetMap == null)
                    return;

                PortalJumpInProgress = true;
                await Countdown("Kapu ugrás", PortalJumpCountdownSeconds);
                if (Disposed || Completed)
                {
                    PortalJumpInProgress = false;
                    return;
                }

                foreach (var playerId in PlayerIds)
                {
                    var groupPlayer = GameManager.GetPlayerById(playerId);
                    if (groupPlayer != null && groupPlayer.Spacemap == Spacemap)
                        JumpPlayer(groupPlayer, position, targetMap);
                }

                PortalJumpInProgress = false;
                afterJump?.Invoke();
            }

            private async Task Countdown(string label, int secondsTotal)
            {
                for (var seconds = secondsTotal; seconds > 0; seconds--)
                {
                    SendMessage($"{label} {seconds} másodperc múlva. Várakozás, hogy a teljes csoport együtt legyen.");
                    await Task.Delay(1000);
                    if (Disposed || Completed)
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

            private void SpawnWaveOne()
            {
                WaveTwoSpawned = false;
                BossSpawned = false;
                WaitingForNextStage = false;
                var wave = Waves[CurrentWaveIndex];
                var amount = 50;
                SpawnNpcGroupOnCircle(new List<HadesNpcSpawn> { new HadesNpcSpawn(wave.WaveOneShipId, amount) });
                SendMessage($"Wave 1 következik: {GetShipName(wave.WaveOneShipId)} - {amount} db.");
            }

            private void SpawnWaveTwo()
            {
                WaveTwoSpawned = true;
                var wave = Waves[CurrentWaveIndex];
                var bossAmount = 20;
                var uberAmount = 10;
                SpawnNpcGroupOnCircle(new List<HadesNpcSpawn>
                {
                    new HadesNpcSpawn(wave.BossShipId, bossAmount),
                    new HadesNpcSpawn(wave.UberShipId, uberAmount)
                });
                SendMessage($"Wave 2 következik: {GetShipName(wave.BossShipId)} - {bossAmount} db, {GetShipName(wave.UberShipId)} - {uberAmount} db.");
            }

            private void SpawnBoss()
            {
                BossSpawned = true;
                var wave = Waves[CurrentWaveIndex];
                SendMessage($"Emperor {wave.Name} érkezik!");
                SpawnNpc(wave.EmperorShipId, HadesCenter);
            }

            private void SpawnNpc(int shipId, Position position)
            {
                var ship = GameManager.GetShip(shipId);
                if (ship == null)
                {
                    SendMessage($"Hades hiba: hiányzó ship id {shipId}.");
                    return;
                }

                var npc = new Npc(Randoms.CreateRandomID(), ship, Spacemap, position, false);
                npc.NpcAI.AttackRandomPlayersAggressively = true;
                NpcIds.Add(npc.Id);
            }

            private void SpawnNpcGroupOnCircle(List<HadesNpcSpawn> spawns)
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
                        var x = HadesCenter.X + (int)Math.Round(Math.Cos(angle) * NpcSpawnCircleRadius);
                        var y = HadesCenter.Y + (int)Math.Round(Math.Sin(angle) * NpcSpawnCircleRadius);
                        SpawnNpc(spawn.ShipId, new Position(x, y));
                        spawnedCount++;
                    }
                }
            }

            private string GetShipName(int shipId)
            {
                return GameManager.GetShip(shipId)?.Name ?? $"NPC {shipId}";
            }

            private void OnCharacterRemoved(object sender, Spacemap.CharacterArgs e)
            {
                if (Disposed || Completed)
                    return;

                if (e.Character is Player removedPlayer)
                {
                    if (!PlayerIds.Any(playerId => GameManager.GetPlayerById(playerId)?.Spacemap == Spacemap))
                    {
                        if (PlayerIds.Count == 1 && removedPlayer.Destroyed)
                        {
                            StartSoloReturnGrace(removedPlayer);
                            return;
                        }

                        SendMessage("Hades kapu megszakadt, nincs bent csoporttag.");
                        Dispose();
                        RemoveRun(this);
                    }
                    return;
                }

                var npc = e.Character as Npc;
                if (npc == null || !NpcIds.Remove(npc.Id) || WaitingForNextStage)
                    return;

                if (!WaveTwoSpawned && NpcIds.Count <= WaveTwoTriggerRemainingNpcs)
                {
                    SpawnWaveTwo();
                    return;
                }

                if (WaveTwoSpawned && !BossSpawned && NpcIds.Count == 0)
                {
                    SpawnBoss();
                    return;
                }

                if (BossSpawned && NpcIds.Count == 0)
                    CompleteCurrentStage();
            }

            private void CompleteCurrentStage()
            {
                var wave = Waves[CurrentWaveIndex];
                SendMessage($"Hades {wave.Name} boss lement.");
                CurrentWaveIndex++;

                if (CurrentWaveIndex >= Waves.Length)
                {
                    CompleteRunAfterDelay();
                    return;
                }

                WaitingForNextStage = true;
                SpawnRestPortals();
                SendMessage("Pihenő: a bal oldali kapuval tovább lehet menni, a jobb oldali kapuval kiugrasz a 4-4 mapra.");
            }

            private async void CompleteRunAfterDelay()
            {
                if (Completed)
                    return;

                Completed = true;
                RewardPlayers();
                SendMessage($"Hades kapu teljesítve! {CompletionExitDelaySeconds} másodperc múlva kidob a 16-os map közepére.");
                await Task.Delay(CompletionExitDelaySeconds * 1000);

                var exitMap = GameManager.GetSpacemap(ExitMapId);
                if (exitMap != null)
                {
                    foreach (var playerId in PlayerIds)
                    {
                        var player = GameManager.GetPlayerById(playerId);
                        if (player != null && player.Spacemap == Spacemap)
                            JumpPlayer(player, ExitMapCenter, exitMap);
                    }
                }

                Dispose();
                RemoveRun(this);
            }

            private void SpawnRestPortals()
            {
                RemoveRestPortals();

                RestPortal = new Portal(Spacemap, RestPortalPosition, HadesCenter, HadesMapId, RestPortalGraphicId, 0, true, true);
                ExitPortal = new Portal(Spacemap, ExitPortalPosition, ExitTargetPosition, ExitMapId, ExitPortalGraphicId, 0, true, true);
                PortalIds.Add(RestPortal.Id);
                PortalIds.Add(ExitPortal.Id);

                foreach (var playerId in PlayerIds)
                {
                    var player = GameManager.GetPlayerById(playerId);
                    if (player?.Spacemap == Spacemap)
                    {
                        player.SendCommand(RestPortal.GetAssetCreateCommand());
                        player.SendCommand(ExitPortal.GetAssetCreateCommand());
                    }
                }
            }

            private void RemoveRestPortals()
            {
                RemoveRunPortal(RestPortal);
                RemoveRunPortal(ExitPortal);
                RestPortal = null;
                ExitPortal = null;
            }

            private void RemoveRunPortal(Portal portal)
            {
                if (portal == null)
                    return;

                PortalIds.Remove(portal.Id);
                Activatable activatable;
                Spacemap.Activatables.TryRemove(portal.Id, out activatable);

                foreach (var playerId in PlayerIds)
                {
                    var player = GameManager.GetPlayerById(playerId);
                    if (player?.Spacemap == Spacemap)
                        player.SendCommand(RemovePortalCommand.write(portal.Id));
                }
            }

            private void RewardPlayers()
            {
                var rewardPlayers = PlayerIds
                    .Select(playerId => GameManager.GetPlayerById(playerId))
                    .Where(player => player != null && player.Spacemap == Spacemap)
                    .ToList();

                if (rewardPlayers.Count == 0)
                    return;

                var honorReward = FinalHonorReward / PlayerIds.Count;
                foreach (var player in rewardPlayers)
                {
                    player.ChangeData(DataType.HONOR, honorReward);
                    player.ChangeData(DataType.URIDIUM, FinalUridiumReward);
                    AddBootyKeys(player);
                    var boosterType = AddRandomBooster(player);
                    var boosterName = GetBoosterRewardName(boosterType);
                    player.SendPacket($"0|A|STD|Hades reward: {honorReward} becsület, {FinalUridiumReward} uridium, minden booty kulcsból {RewardKeysPerType} db és {RandomBoosterRewardHours} óra {boosterName} booster.");
                }
            }

            private void AddBootyKeys(Player player)
            {
                if (player?.Equipment?.Items?.BootyKeys == null)
                    return;

                var bootyKeys = player.Equipment.Items.BootyKeys;
                bootyKeys.GreenKeys += RewardKeysPerType;
                bootyKeys.RedKeys += RewardKeysPerType;
                bootyKeys.BlueKeys += RewardKeysPerType;
                bootyKeys.SilverKeys += RewardKeysPerType;
                bootyKeys.GoldKeys += RewardKeysPerType;
                QueryManager.SavePlayer.BootyKeys(player);
                player.SendPacket($"0|A|BK|{bootyKeys.TotalKeys}");
            }


            private BoosterType AddRandomBooster(Player player)
            {
                var boosterTypes = new[]
                {
                    BoosterType.DMG_B01, BoosterType.DMG_B02, BoosterType.EP_B01, BoosterType.EP_B02,
                    BoosterType.HON_B01, BoosterType.HON_B02, BoosterType.HP_B01, BoosterType.HP_B02,
                    BoosterType.REP_B01, BoosterType.REP_B02, BoosterType.SHD_B01, BoosterType.SHD_B02
                };

                var boosterType = boosterTypes[Randoms.random.Next(boosterTypes.Length)];
                player.BoosterManager.Add(boosterType, RandomBoosterRewardHours);
                return boosterType;
            }

            private string GetBoosterRewardName(BoosterType boosterType)
            {
                switch (boosterType)
                {
                    case BoosterType.DMG_B01:
                        return "sebzés B01";
                    case BoosterType.DMG_B02:
                        return "sebzés B02";
                    case BoosterType.EP_B01:
                        return "tapasztalat B01";
                    case BoosterType.EP_B02:
                        return "tapasztalat B02";
                    case BoosterType.HON_B01:
                        return "becsület B01";
                    case BoosterType.HON_B02:
                        return "becsület B02";
                    case BoosterType.HP_B01:
                        return "életerő B01";
                    case BoosterType.HP_B02:
                        return "életerő B02";
                    case BoosterType.REP_B01:
                        return "javítás B01";
                    case BoosterType.REP_B02:
                        return "javítás B02";
                    case BoosterType.SHD_B01:
                        return "pajzs B01";
                    case BoosterType.SHD_B02:
                        return "pajzs B02";
                    default:
                        return boosterType.ToString();
                }
            }

            private void SendMessage(string message)
            {
                foreach (var playerId in PlayerIds)
                {
                    var player = GameManager.GetPlayerById(playerId);
                    if (player != null && player.Spacemap == Spacemap)
                        player.SendPacket($"0|A|STD|{message}");
                }
            }
        }

        private class HadesNpcSpawn
        {
            public int ShipId { get; }
            public int Amount { get; }

            public HadesNpcSpawn(int shipId, int amount)
            {
                ShipId = shipId;
                Amount = amount;
            }
        }

        private class HadesWaveDefinition
        {
            public string Name { get; }
            public int WaveOneShipId { get; }
            public int BossShipId { get; }
            public int UberShipId { get; }
            public int EmperorShipId { get; }

            public HadesWaveDefinition(string name, int waveOneShipId, int bossShipId, int uberShipId, int emperorShipId)
            {
                Name = name;
                WaveOneShipId = waveOneShipId;
                BossShipId = bossShipId;
                UberShipId = uberShipId;
                EmperorShipId = emperorShipId;
            }
        }
    }
}
