using Ow.Game.Objects.Players.Managers;
using System;
using System.Collections.Generic;

namespace Ow.Game.Objects.Players.Skills
{
    class Jamx : Skill
    {
        public override string LootId { get => SkillManager.SPEARHEAD_JAM_X; }

        public override int Duration { get => 0; }
        public override int Cooldown { get => TimeManager.SPEARHEAD_JAMX_COOLDOWN; }

        public Jamx(Player player) : base(player) { }

        public override void Tick() { }

        public override void Send()
        {
            var spearheadIds = new List<int> { Ship.SPEARHEAD, Ship.SPEARHEAD_ELITE, Ship.SPEARHEAD_VETERAN };
            var jamxLockout = 15000;

            if (spearheadIds.Contains(Player.Ship.Id) && (cooldown.AddMilliseconds(Cooldown) < DateTime.Now || Player.Storage.GodMode))
            {
                var target = Player.Selected;

                if (target is Player targetPlayer && Player.TargetDefinition(targetPlayer, false))
                {
                    targetPlayer.SkillManager.ApplyJamxCooldown(jamxLockout);
                    targetPlayer.CpuManager.DisableCloak();
                }

                Player.SendCooldown(LootId, Cooldown);
                Active = true;
                cooldown = DateTime.Now;
            }
        }

        public override void Disable()
        {
            Active = false;
        }
    }
}
