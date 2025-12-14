using Ow.Game.Objects.Players.Managers;
using Ow.Net.netty.commands;
using System;
using System.Collections.Generic;

namespace Ow.Game.Objects.Players.Skills
{
    class Fortify : Skill
    {
        public override string LootId { get => SkillManager.CITADEL_FORTIFY; }

        public override int Duration { get => TimeManager.CITADEL_FORTIFY_DURATION; }
        public override int Cooldown { get => TimeManager.CITADEL_FORTIFY_COOLDOWN; }

        public Fortify(Player player) : base(player) { }

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
                Player.SkillManager.DisableAllSkills();

                Player.Storage.CitadelFortify = true;
                Player.AddVisualModifier(VisualModifierCommand.FORTIFY, 0, "", 0, true);

                Player.SendCommand(SetSpeedCommand.write(Player.Speed, Player.Speed));
                Player.SendCooldown(LootId, Duration, true);
                Active = true;
                cooldown = DateTime.Now;
            }
        }

        public override void Disable()
        {
            Player.Storage.CitadelFortify = false;
            Player.RemoveVisualModifier(VisualModifierCommand.FORTIFY);
            Player.SendCommand(SetSpeedCommand.write(Player.Speed, Player.Speed));

            Player.SendCooldown(LootId, Cooldown);
            Active = false;
        }
    }
}
