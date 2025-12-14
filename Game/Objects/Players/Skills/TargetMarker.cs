using Ow.Game.Objects.Players.Managers;
using Ow.Net.netty.commands;
using System;
using System.Collections.Generic;

namespace Ow.Game.Objects.Players.Skills
{
    class TargetMarker : Skill
    {
        public override string LootId { get => SkillManager.SPEARHEAD_TARGET_MARKER; }

        public override int Duration { get => TimeManager.SPEARHEAD_TARGET_MARKER_DURATION; }
        public override int Cooldown { get => TimeManager.SPEARHEAD_TARGET_MARKER_COOLDOWN; }

        private Attackable _target;

        public TargetMarker(Player player) : base(player) { }

        public override void Tick()
        {
            if (!Active) return;

            if (cooldown.AddMilliseconds(Duration) < DateTime.Now || _target == null || _target.Destroyed || _target.Spacemap.Id != Player.Spacemap.Id)
                Disable();
        }

        public override void Send()
        {
            var spearheadIds = new List<int> { Ship.SPEARHEAD, Ship.SPEARHEAD_ELITE, Ship.SPEARHEAD_VETERAN };

            if (spearheadIds.Contains(Player.Ship.Id) && (cooldown.AddMilliseconds(Duration + Cooldown) < DateTime.Now || Player.Storage.GodMode))
            {
                var target = Player.Selected;

                if (target != null && Player.TargetDefinition(target, false))
                {
                    _target = target;

                    if (target is Player targetPlayer)
                    {
                        targetPlayer.Storage.underTargetMarker = true;
                        targetPlayer.Storage.underTargetMarkerTime = DateTime.Now;
                        targetPlayer.Storage.targetMarkerOwner = Player;
                    }

                    target.AddVisualModifier(VisualModifierCommand.DAMAGE_ICON, 0, "", 0, true);

                    Player.SendCooldown(LootId, Duration, true);
                    Active = true;
                    cooldown = DateTime.Now;
                }
            }
        }

        public override void Disable()
        {
            if (_target is Player targetPlayer)
                targetPlayer.Storage.DeactiveTargetMarkerEffect();
            else
                _target?.RemoveVisualModifier(VisualModifierCommand.DAMAGE_ICON);

            Player.SendCooldown(LootId, Cooldown);
            Active = false;
            _target = null;
        }
    }
}
