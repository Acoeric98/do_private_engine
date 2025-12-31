using Ow.Game.Movements;
using Ow.Game.Objects;
using Ow.Game.Objects.Stations;
using Ow.Managers;
using Ow.Net.netty.commands;
using Ow.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ow.Game.Objects.Collectables
{
    class RedBooty : Collectable
    {
        public RedBooty(Position position, Spacemap spacemap, bool respawnable, Player toPlayer = null) : base(AssetTypeModule.BOXTYPE_PIRATE_BOOTY, position, spacemap, respawnable, toPlayer) { }

        public override void Reward(Player player)
        {
            GrantBattleStationModule(player);

            player.Equipment.Items.BootyKeys--;
            QueryManager.SavePlayer.Information(player);
            player.SendPacket($"0|A|BK|{player.Equipment.Items.BootyKeys}");
        }

        public override byte[] GetCollectableCreateCommand()
        {
            return CreateBoxCommand.write("PIRATE_BOOTY_RED", Hash, Position.Y, Position.X);
        }

        private void GrantBattleStationModule(Player player)
        {
            var moduleTypes = new short[]
            {
                StationModuleModule.HULL,
                StationModuleModule.DEFLECTOR,
                StationModuleModule.REPAIR,
                StationModuleModule.LASER_HIGH_RANGE,
                StationModuleModule.LASER_MID_RANGE,
                StationModuleModule.LASER_LOW_RANGE,
                StationModuleModule.ROCKET_MID_ACCURACY,
                StationModuleModule.ROCKET_LOW_ACCURACY,
                StationModuleModule.HONOR_BOOSTER,
                StationModuleModule.DAMAGE_BOOSTER,
                StationModuleModule.EXPERIENCE_BOOSTER
            };

            var selectedType = moduleTypes[Randoms.random.Next(moduleTypes.Length)];
            var moduleId = Randoms.CreateRandomID();

            player.Storage.BattleStationModules.Add(new ModuleBase(moduleId, selectedType, false));
            QueryManager.SavePlayer.Modules(player);

            player.SendPacket($"0|A|STD|Battlestation modul nyitás: {GetModuleName(selectedType)}");
        }

        private string GetModuleName(short moduleType)
        {
            switch (moduleType)
            {
                case StationModuleModule.DAMAGE_BOOSTER:
                    return "DMG";
                case StationModuleModule.EXPERIENCE_BOOSTER:
                    return "EXP";
                case StationModuleModule.HONOR_BOOSTER:
                    return "HON";
                case StationModuleModule.REPAIR:
                    return "REP";
                case StationModuleModule.LASER_HIGH_RANGE:
                    return "Lézer (HR)";
                case StationModuleModule.LASER_MID_RANGE:
                    return "Lézer (MR)";
                case StationModuleModule.LASER_LOW_RANGE:
                    return "Lézer (LR)";
                case StationModuleModule.ROCKET_MID_ACCURACY:
                    return "Rakéta (MA)";
                case StationModuleModule.ROCKET_LOW_ACCURACY:
                    return "Rakéta (LA)";
                case StationModuleModule.DEFLECTOR:
                    return "Deflektor";
                case StationModuleModule.HULL:
                    return "Alapmodul";
                default:
                    return moduleType.ToString();
            }
        }
    }
}
