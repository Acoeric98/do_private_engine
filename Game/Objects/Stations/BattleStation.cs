using Ow.Game;
using Ow.Game.Movements;
using Ow.Game.Ticks;
using Ow.Managers;
using Ow.Net.netty.commands;
using Ow.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ow.Game.Objects.Stations
{
    public class EquippedModuleBase
    {
        public int ClanId { get; set; }
        public List<SatelliteBase> Modules { get; set; }

        public EquippedModuleBase(int clanId, List<SatelliteBase> satellites)
        {
            ClanId = clanId;
            Modules = satellites;
        }
    }

    class BattleStation : Activatable
    {
        public const int DEFLECTOR_MINUTES_MIN = 5;
        public const int DEFLECTOR_MINUTES_MAX = 60;
        public const int DEFLECTOR_MINUTES_INCREMENT = 5;
        public const int DEFLECTOR_DEFAULT_SECONDS = DEFLECTOR_MINUTES_MAX * 60;
        public const int DEFLECTOR_INACTIVE_AFTER_BUILD_MINUTES = 30;

        public Dictionary<int, List<Satellite>> EquippedStationModule = new Dictionary<int, List<Satellite>>();

        public bool InBuildingState = false;
        public int BuildTimeInMinutes = 0;

        public bool DeflectorActive = false;
        public int DeflectorSecondsLeft = 0;
        public int DeflectorSecondsMax = 0;

        public string AsteroidName { get; set; }
        private DateTime lastShieldRegeneration = new DateTime();

        public BattleStation(string name, Spacemap spacemap, Position position, Clan clan, 
            List<EquippedModuleBase> modules, bool inBuildingState, int buildTimeInMinutes, DateTime buildTime,
            bool deflectorActive, int deflectorSecondsLeft, DateTime deflectorTime,
            List<int> visualModifiers) : base(spacemap, (clan.Id != 0 ? clan.FactionId : 0), position, clan, (clan.Id == 0 || inBuildingState ? AssetTypeModule.ASTEROID : AssetTypeModule.BATTLESTATION))
        {
            ShieldAbsorption = 0.8;

            MaxHitPoints = 7000000;
            CurrentHitPoints = MaxHitPoints;
            CurrentShieldPoints = 3500000;
            MaxShieldPoints = 3500000;

            InBuildingState = inBuildingState;
            BuildTimeInMinutes = buildTimeInMinutes;

            DeflectorActive = deflectorActive;
            this.deflectorTime = deflectorTime == default ? DateTime.Now : deflectorTime;
            DeflectorSecondsLeft = deflectorSecondsLeft;
            DeflectorSecondsMax = deflectorSecondsLeft > 0 ? deflectorSecondsLeft : DEFLECTOR_DEFAULT_SECONDS;

            AsteroidName = name;

            Name = Clan.Id != 0 ? Clan.Name : name;
            LastCombatTime = DateTime.Now;

            if (DeflectorActive)
            {
                DeflectorSecondsLeft = Math.Max(0, DeflectorSecondsLeft - (int)DateTime.Now.Subtract(this.deflectorTime).TotalSeconds);
                this.deflectorTime = DateTime.Now;
                Invincible = true;
                AddVisualModifier(VisualModifierCommand.BATTLESTATION_DEFLECTOR, DeflectorSecondsLeft, "", 0, true);
                Program.TickManager.AddTick(this);
            }

            foreach (var modifier in visualModifiers)
                AddVisualModifier((short)modifier, 0, "", 0, true);

            foreach (var module in modules)
            {
                var satellite = new List<Satellite>();

                foreach (var m in module.Modules)
                {
                    var s = new Satellite(this, m.OwnerId, Satellite.GetName(m.Type), m.DesignId, m.ItemId, m.SlotId, m.Type, Satellite.GetPosition(Position, m.SlotId));
                    s.InstallationSecondsLeft = m.InstallationSecondsLeft;
                    s.Installed = m.Installed;
                    s.CurrentHitPoints = m.CurrentHitPoints;
                    s.MaxHitPoints = m.MaxHitPoints;
                    s.CurrentShieldPoints = m.CurrentShieldPoints;
                    s.MaxShieldPoints = m.MaxShieldPoints;

                    if (DeflectorActive)
                        s.AddVisualModifier(VisualModifierCommand.BATTLESTATION_DEFLECTOR, 0, "", 0, true);
                    satellite.Add(s);
                }

                EquippedStationModule.Add(module.ClanId, satellite);
            }

            if (Clan.Id != 0 && InBuildingState)
            {
                BuildTimeInMinutes = BuildTimeInMinutes - (int)DateTime.Now.Subtract(buildTime).TotalMinutes;
                this.buildTime = DateTime.Now;
                Program.TickManager.AddTick(this);
            }
            else if (Clan.Id != 0 && !InBuildingState)
                Build();
        }

        public DateTime buildTime = new DateTime();
        public DateTime deflectorTime = new DateTime();
        public new void Tick()
        {
            if (InBuildingState && buildTime.AddMinutes(BuildTimeInMinutes) < DateTime.Now)
            {
                Build(true);
                QueryManager.BattleStations.BattleStation(this);
            }

            if (DeflectorActive && !HasDeflectorModuleInstalled())
                DeactiveDeflector();

            if (DeflectorActive && GetDeflectorSecondsLeft() <= 0)
                DeactiveDeflector();

            if (!InBuildingState && AssetTypeId == AssetTypeModule.BATTLESTATION && !Destroyed)
            {
                if (LastCombatTime.AddSeconds(Satellite.BATTLE_STATION_PEACE_TIME) < DateTime.Now && CurrentShieldPoints < MaxShieldPoints)
                {
                    if (lastShieldRegeneration.AddSeconds(1) < DateTime.Now)
                    {
                        Heal(Satellite.GetShieldRegenerationAmount(MaxShieldPoints), 0, HealType.SHIELD);
                        lastShieldRegeneration = DateTime.Now;
                    }
                }
                else
                    lastShieldRegeneration = DateTime.Now;
            }
        }

        public bool HasDeflectorModuleInstalled()
        {
            return EquippedStationModule.ContainsKey(Clan.Id) && EquippedStationModule[Clan.Id].Any(x => x.Type == StationModuleModule.DEFLECTOR && x.Installed && !x.Destroyed);
        }

        public int GetDeflectorSecondsLeft()
        {
            if (!DeflectorActive) return 0;

            var secondsLeft = DeflectorSecondsLeft - (int)DateTime.Now.Subtract(deflectorTime).TotalSeconds;
            return secondsLeft > 0 ? secondsLeft : 0;
        }

        public bool IsDeflectorOnCooldown()
        {
            return !DeflectorActive && deflectorTime > DateTime.Now;
        }

        public int GetDeflectorCooldownSecondsLeft()
        {
            if (!IsDeflectorOnCooldown()) return 0;

            return Math.Max(0, (int)deflectorTime.Subtract(DateTime.Now).TotalSeconds);
        }

        public void ActivateDeflector(int seconds)
        {
            if (DeflectorActive || Destroyed || InBuildingState || !HasDeflectorModuleInstalled() || IsDeflectorOnCooldown()) return;

            DeflectorSecondsMax = seconds > 0 ? seconds : DEFLECTOR_DEFAULT_SECONDS;
            DeflectorSecondsLeft = DeflectorSecondsMax;
            deflectorTime = DateTime.Now;

            Invincible = true;
            DeflectorActive = true;

            AddVisualModifier(VisualModifierCommand.BATTLESTATION_DEFLECTOR, DeflectorSecondsLeft, "", 0, true);

            foreach (var modules in EquippedStationModule.Values)
                foreach (var satellite in modules)
                    satellite.AddVisualModifier(VisualModifierCommand.BATTLESTATION_DEFLECTOR, 0, "", 0, true);

            Program.TickManager.AddTick(this);
        }

        public void DeactiveDeflector()
        {
            RemoveVisualModifier(VisualModifierCommand.BATTLESTATION_DEFLECTOR);

            foreach (var modules in EquippedStationModule.Values)
                foreach (var satellite in modules)
                    satellite.RemoveVisualModifier(VisualModifierCommand.BATTLESTATION_DEFLECTOR);

            Invincible = false;
            DeflectorActive = false;
            DeflectorSecondsLeft = 0;
            deflectorTime = DateTime.Now;
            QueryManager.BattleStations.BattleStation(this);
        }

        public void Build(bool applyDeflectorCooldown = false)
        {
            AssetTypeId = AssetTypeModule.BATTLESTATION;

            if (applyDeflectorCooldown)
            {
                DeflectorActive = false;
                Invincible = false;
                DeflectorSecondsLeft = 0;
                DeflectorSecondsMax = DeflectorSecondsMax > 0 ? DeflectorSecondsMax : DEFLECTOR_DEFAULT_SECONDS;
                deflectorTime = DateTime.Now.AddMinutes(DEFLECTOR_INACTIVE_AFTER_BUILD_MINUTES);
            }

            RemoveVisualModifier(VisualModifierCommand.BATTLESTATION_CONSTRUCTING);
            //Visuals.Add(new VisualModifierCommand(Id, VisualModifierCommand.BATTLESTATION_DOWNTIME_TIMER, 1800, "", 0, true));
            PrepareSatellites();

            GameManager.SendCommandToMap(Spacemap.Id, AssetRemoveCommand.write(GetAssetType(), Id));

            foreach (var character in Spacemap.Characters.Values)
            {
                if (character is Player player)
                {
                    short relationType = character.Clan.Id != 0 && Clan.Id != 0 ? Clan.GetRelation(character.Clan) : (short)0;
                    player.SendCommand(GetAssetCreateCommand(relationType));
                }
            }

            BuildTimeInMinutes = 0;
            InBuildingState = false;
            Program.TickManager.AddTick(this);
        }

        public void PrepareSatellites()
        {
            foreach (var satellite in EquippedStationModule[Clan.Id])
            {
                if (satellite.Type != StationModuleModule.DEFLECTOR && satellite.Type != StationModuleModule.HULL)
                {
                    Spacemap.Activatables.TryAdd(satellite.Id, satellite);

                    foreach (var character in satellite.Spacemap.Characters.Values)
                    {
                        if (character is Player player)
                        {                   
                            short relationType = character.Clan.Id != 0 && satellite.Clan.Id != 0 ? satellite.Clan.GetRelation(character.Clan) : (short)0;
                            player.SendCommand(satellite.GetAssetCreateCommand(relationType));
                        }
                    }
                }
            }
        }

        public override void Click(GameSession gameSession)
        {
            var player = gameSession.Player;

            int secondsLeft = (int)(TimeSpan.FromMinutes(BuildTimeInMinutes).TotalSeconds - (DateTime.Now - buildTime).TotalSeconds);

            if (InBuildingState)
                player.SendCommand(BattleStationBuildingStateCommand.write(Id, Id, Name, secondsLeft, 0, Clan.Name, new FactionModule((short)FactionId)));
            else
            {
                if (player.Clan.Id == 0)
                    player.SendCommand(BattleStationNoClanUiInitializationCommand.write(Id));
                else
                {
                    var stationModuleModule = new List<StationModuleModule>();

                    if (EquippedStationModule.ContainsKey(player.Clan.Id))
                    {
                        foreach (var mm in EquippedStationModule[player.Clan.Id])
                        {
                            if (mm.Type == StationModuleModule.HULL || mm.Type == StationModuleModule.DEFLECTOR)
                            {
                                mm.CurrentHitPoints = CurrentHitPoints;
                                mm.MaxHitPoints = MaxHitPoints;
                                mm.CurrentShieldPoints = CurrentShieldPoints;
                                mm.MaxShieldPoints = MaxShieldPoints;
                            }

                            stationModuleModule.Add(new StationModuleModule(Id, mm.ItemId, mm.SlotId, mm.Type, mm.CurrentHitPoints,
                                    mm.MaxHitPoints, mm.CurrentShieldPoints, mm.MaxShieldPoints, 16, QueryManager.GetUserPilotName(mm.OwnerId), 0, mm.InstallationSecondsLeft, 0, 0, 500));
                        }
                    }

                    var playerModules = new List<StationModuleModule>();

                    for (var i = 0; i < player.Storage.BattleStationModules.Count; i++)
                    {
                        if (!player.Storage.BattleStationModules[i].InUse)
                            playerModules.Add(new StationModuleModule(Id, player.Storage.BattleStationModules[i].Id, i, player.Storage.BattleStationModules[i].Type, 1, 1, 1, 1, 16, QueryManager.GetUserPilotName(player.Id), 0, 0, 0, 0, 500));
                    }

                    if (Clan.Id != 0 && player.Clan.Id == Clan.Id)
                    {
                        var hasDeflector = HasDeflectorModuleInstalled();
                        var deflectorActive = hasDeflector && DeflectorActive;
                        var deflectorSecondsMax = hasDeflector ? (DeflectorSecondsMax > 0 ? DeflectorSecondsMax : DEFLECTOR_DEFAULT_SECONDS) : 0;
                        var deflectorSecondsLeft = deflectorActive ? GetDeflectorSecondsLeft() : 0;

                        player.SendCommand(BattleStationManagementUiInitializationCommand.write(
                            Id, 
                            Id, 
                            Name, 
                            Clan.Name, 
                            new FactionModule((short)FactionId),
                            new BattleStationStatusCommand(Id, Id, Name, deflectorActive, deflectorSecondsLeft, deflectorSecondsMax, 0, 0, 0, 0, 0, 0, 0, 0, new EquippedModulesModule(stationModuleModule)),
                            new AvailableModulesCommand(playerModules),
                            hasDeflector ? DEFLECTOR_MINUTES_MIN : 0,
                            hasDeflector ? DEFLECTOR_MINUTES_MAX : 0,
                            hasDeflector ? DEFLECTOR_MINUTES_INCREMENT : 0,
                            deflectorActive));
                    }
                    else
                    {
                        var bestClan = EquippedStationModule.Values.Where(x => x.Where(y => y.Installed).ToList().Count() > 0).ToList().Count > 0 ? EquippedStationModule.OrderByDescending(x => x.Value.Count) : null;
                        player.SendCommand(BattleStationBuildingUiInitializationCommand.write(Id, Id, Name,
                                          new AsteroidProgressCommand(
                                                  Id,
                                                  (float)(EquippedStationModule.ContainsKey(player.Clan.Id) ? EquippedStationModule[player.Clan.Id].Where(x => x.Installed).ToList().Count : 0) / 10,
                                                  (float)(bestClan != null ? bestClan.FirstOrDefault().Value.Where(x => x.Installed).ToList().Count : 0) / 10,
                                                  player.Clan.Name,
                                                  bestClan != null ? GameManager.GetClan(bestClan.FirstOrDefault().Key)?.Name : "Leading clan's progress",
                                                  new EquippedModulesModule(EquippedStationModule.ContainsKey(player.Clan.Id) ? stationModuleModule : new List<StationModuleModule>()),
                                                  (EquippedStationModule.ContainsKey(player.Clan.Id) ? EquippedStationModule[player.Clan.Id].Where(x => x.Installed).ToList().Count : 0) == 10),
                                          new AvailableModulesCommand(playerModules),
                                          1,
                                          60,
                                          0));
                    }
                }
            }
        }

        public override byte[] GetAssetCreateCommand(short clanRelationModule = ClanRelationModule.NONE)
        {
            return AssetCreateCommand.write(GetAssetType(), Name,
                                          FactionId, Clan.Tag, Id, 0, 0,
                                          Position.X, Position.Y, Clan.Id, true, true, true, true,
                                          new ClanRelationModule(clanRelationModule),
                                          VisualModifiers.Values.ToList());
        }
    }
}
