using Ow.Game.Objects.Players.Managers;
using Ow.Net.netty.commands;
using System;
using System.Collections.Generic;

namespace Ow.Game.Objects.Players.Skills
{
    class Travel : Skill
    {
        public override string LootId { get => SkillManager.CITADEL_TRAVEL; }

        public override int Duration { get => TimeManager.CITADEL_TRAVEL_DURATION; }
        public override int Cooldown { get => TimeManager.CITADEL_TRAVEL_COOLDOWN; }

        public Travel(Player player) : base(player) { }

        public override void Tick()
        {
            if (Active && cooldown.AddMilliseconds(Duration) < DateTime.Now)
                Disable();
        }

        public override void Send()
        {
            var citadelIds = new List<int> { Ship.CITADEL, Ship.CITADEL_ELITE, Ship.CITADEL_VETERAN };

            if (citadelIds.Contains(Player.Ship.Id) && (cooldown.AddMilliseconds(Duration + Cooldown) < DateTime.Now || Player.Storage.GodMode))
            {
                Player.Storage.CitadelTravel = true;
                Player.AddVisualModifier(VisualModifierCommand.TRAVEL_MODE, 0, "", 0, true);

                Player.SendCommand(SetSpeedCommand.write(Player.Speed, Player.Speed));
                Player.SendCooldown(LootId, Duration, true);
                Active = true;
                cooldown = DateTime.Now;
            }
        }

        public override void Disable()
        {
            Player.Storage.CitadelTravel = false;
            Player.RemoveVisualModifier(VisualModifierCommand.TRAVEL_MODE);
            Player.SendCommand(SetSpeedCommand.write(Player.Speed, Player.Speed));

            Player.SendCooldown(LootId, Cooldown);
            Active = false;
        }
    }
}
