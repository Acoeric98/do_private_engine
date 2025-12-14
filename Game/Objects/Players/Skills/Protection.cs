using Ow.Game.Objects.Players.Managers;
using Ow.Net.netty.commands;
using System;
using System.Collections.Generic;

namespace Ow.Game.Objects.Players.Skills
{
    class Protection : Skill
    {
        private const int RANGE = 700;

        public override string LootId { get => SkillManager.CITADEL_PROTECTION; }

        public override int Duration { get => TimeManager.CITADEL_PROTECTION_DURATION; }
        public override int Cooldown { get => TimeManager.CITADEL_PROTECTION_COOLDOWN; }

        private readonly List<Player> _protectedPlayers = new List<Player>();

        public Protection(Player player) : base(player) { }

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
                Player.Storage.CitadelProtection = true;

                Player.AddVisualModifier(VisualModifierCommand.PROTECT_OWNER, 0, "", 0, true);

                if (Player.Group != null)
                {
                    foreach (var member in Player.Group.Members.Values)
                    {
                        if (member == null || member == Player) continue;
                        if (member.Spacemap != Player.Spacemap) continue;

                        if (member.Position.DistanceTo(Player.Position) <= RANGE)
                        {
                            member.AddVisualModifier(VisualModifierCommand.PROTECT_TARGET, 0, "", 0, true);
                            _protectedPlayers.Add(member);
                        }
                    }
                }

                Player.SendCooldown(LootId, Duration, true);
                Active = true;
                cooldown = DateTime.Now;
            }
        }

        public override void Disable()
        {
            Player.Storage.CitadelProtection = false;

            Player.RemoveVisualModifier(VisualModifierCommand.PROTECT_OWNER);

            foreach (var member in _protectedPlayers)
                member?.RemoveVisualModifier(VisualModifierCommand.PROTECT_TARGET);

            _protectedPlayers.Clear();

            Player.SendCooldown(LootId, Cooldown);
            Active = false;
        }
    }
}
