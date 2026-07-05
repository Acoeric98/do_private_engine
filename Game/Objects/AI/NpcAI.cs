using Ow.Game.Movements;
using Ow.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ow.Game.Objects.AI
{
    class NpcAI
    {
        public Npc Npc { get; set; }

        public NpcAIOption AIOption = NpcAIOption.SEARCH_FOR_ENEMIES;
        public bool AttackRandomPlayersAggressively { get; set; }
        private static int ALIEN_DISTANCE_TO_USER = 300;
        private static int AGGRESSIVE_BUMP_RANGE = 1200;

        public NpcAI(Npc npc) { Npc = npc; }

        public DateTime lastMovement = new DateTime();

        public void TickAI()
        {
            if(lastMovement.AddSeconds(1) < DateTime.Now)
            {
                switch (AIOption)
                {
                    case NpcAIOption.SEARCH_FOR_ENEMIES:
                        if (AttackRandomPlayersAggressively)
                        {
                            var target = GetRandomAttackablePlayerOnMap();
                            if (target != null)
                            {
                                Npc.Selected = target;
                                Npc.Attacking = true;
                                AIOption = NpcAIOption.FLY_TO_ENEMY;
                                break;
                            }
                        }

                        foreach (var players in Npc.InRangeCharacters.Values)
                        {
                            if (players is Player)
                            {
                                var player = players as Player;

                                if (Npc.Ship.Aggressive && Npc.Position.DistanceTo(player.Position) > AGGRESSIVE_BUMP_RANGE)
                                    continue;

                                var inDefenseZone = Npc.Spacemap?.IsInNpcDefenseZone(player.Position) == true;

                                if (player.Storage.IsInDemilitarizedZone || player.Invisible || inDefenseZone || Npc.Position.DistanceTo(player.Position) > Npc.RenderRange)
                                {
                                    Npc.Attacking = false;
                                    Npc.Selected = null;
                                    AIOption = NpcAIOption.SEARCH_FOR_ENEMIES;
                                }
                                else
                                {
                                    if (Npc.Ship.Aggressive)
                                        Npc.Attacking = true;

                                    Npc.Selected = player;
                                    AIOption = NpcAIOption.FLY_TO_ENEMY;
                                }
                            }
                        }

                        if (!Npc.Moving && Npc.Selected == null)
                        {
                            int nextPosX = Randoms.random.Next(20000);
                            int nextPosY = Randoms.random.Next(12800);

                            Movement.Move(Npc, new Position(nextPosX, nextPosY));
                        }
                        break;
                    case NpcAIOption.FLY_TO_ENEMY:
                        if (IsValidTarget(Npc.Selected as Player))
                        {
                            var player = Npc.Selected as Player;

                            Npc.Attacking = Npc.Ship.Aggressive || AttackRandomPlayersAggressively;
                            Movement.Move(Npc, Position.GetPosOnCircle(player.Position, ALIEN_DISTANCE_TO_USER));
                            AIOption = NpcAIOption.WAIT_PLAYER_MOVE;
                        } 
                        else
                        {
                            Npc.Attacking = false;
                            Npc.Selected = null;
                            AIOption = NpcAIOption.SEARCH_FOR_ENEMIES;
                        }
                        break;
                    case NpcAIOption.WAIT_PLAYER_MOVE:
                        if (IsValidTarget(Npc.Selected as Player))
                        {
                            var player = Npc.Selected as Player;

                            if (AttackRandomPlayersAggressively && !Npc.Moving)
                                AIOption = NpcAIOption.FLY_TO_ENEMY;
                            else if (player.Moving)
                                AIOption = NpcAIOption.FLY_TO_ENEMY;
                        }
                        else
                        {
                            Npc.Attacking = false;
                            Npc.Selected = null;
                            AIOption = NpcAIOption.SEARCH_FOR_ENEMIES;
                        }
                        break;
                }

                lastMovement = DateTime.Now;
            }
        }

        private Player GetRandomAttackablePlayerOnMap()
        {
            var players = Npc.Spacemap?.Characters.Values
                .OfType<Player>()
                .Where(IsValidTarget)
                .OrderBy(_ => Randoms.random.Next())
                .ToList();

            return players != null && players.Count > 0 ? players[0] : null;
        }

        private bool IsValidTarget(Player player)
        {
            if (player == null || player.Destroyed || player.Spacemap != Npc.Spacemap)
                return false;

            if (player.Storage.IsInDemilitarizedZone || player.Invisible)
                return false;

            if (Npc.Spacemap?.IsInNpcDefenseZone(player.Position) == true)
                return false;

            return AttackRandomPlayersAggressively || Npc.Position.DistanceTo(player.Position) < Npc.RenderRange;
        }

        private double DegreeToRadian(double angle)
        {
            return Math.PI * angle / 180.0;
        }
    }
}
