using Newtonsoft.Json;
using Ow.Game.Objects;
using Ow.Managers;
using Ow.Net.netty.commands;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ow.Game.Objects.Players.Managers
{
    public class BoosterBase
    {
        public short Type { get; set; }
        public int Seconds { get; set; }

        public BoosterBase(short type, int seconds)
        {
            Type = type;
            Seconds = seconds;
        }
    }

    class BoosterManager : AbstractManager
    {
        public Dictionary<short, List<BoosterBase>> Boosters = new Dictionary<short, List<BoosterBase>>();
        private Dictionary<BoostedAttributeType, int> ExternalBoosts = new Dictionary<BoostedAttributeType, int>();

        public BoosterManager(Player player) : base(player) { }

        private DateTime boosterTime = new DateTime();
        public void Tick()
        {
            if (boosterTime.AddSeconds(5) < DateTime.Now)
            {
                for (short i = 0; i < Boosters.ToList().Count; i++)
                {
                    var boosters = Boosters.ToList()[i].Value;

                    for (short k = 0; k < boosters.Count; k++)
                    {
                        boosters[k].Seconds -= 5;

                        if (boosters[k].Seconds <= 0)
                            Remove((BoosterType)boosters[k].Type);
                    }
                }
                boosterTime = DateTime.Now;
            }
        }

        public void Add(BoosterType boosterType, int hours)
        {
            Player.SendPacket($"0|A|STM|booster_found|%BOOSTERNAME%|{boosterType.ToString()}|%HOURS%|{hours}");

            var seconds = (int)TimeSpan.FromHours(hours).TotalSeconds;
            short boostedAttributeType = GetBoosterType((short)boosterType);

            if (boostedAttributeType != 0)
            {
                if (!Boosters.ContainsKey((short)boostedAttributeType))
                    Boosters[boostedAttributeType] = new List<BoosterBase>();

                if (Boosters[boostedAttributeType].Where(x => x.Type == (short)boosterType).Count() <= 0)
                    Boosters[boostedAttributeType].Add(new BoosterBase((short)boosterType, seconds));
                else
                    Boosters[boostedAttributeType].Where(x => x.Type == (short)boosterType).FirstOrDefault().Seconds += seconds;

                Update();
                QueryManager.SavePlayer.Boosters(Player);
            }
        }

        public void Remove(BoosterType boosterType)
        {
            short boostedAttributeType = GetBoosterType((short)boosterType);

            if (boostedAttributeType != 0)
            {
                if (Boosters.ContainsKey(boostedAttributeType))
                    Boosters[boostedAttributeType].Remove(Boosters[boostedAttributeType].Where(x => x.Type == (short)boosterType).FirstOrDefault());

                if (Boosters[boostedAttributeType].Count == 0)
                    Boosters.Remove(boostedAttributeType);

                Update();
                QueryManager.SavePlayer.Boosters(Player);
            }
        }

        public void SetExternalBoosts(Dictionary<BoostedAttributeType, int> boosts)
        {
            var normalizedBoosts = boosts ?? new Dictionary<BoostedAttributeType, int>();

            if (normalizedBoosts.Count == ExternalBoosts.Count && !normalizedBoosts.Except(ExternalBoosts).Any() && !ExternalBoosts.Except(normalizedBoosts).Any())
                return;

            ExternalBoosts = normalizedBoosts;
            Update();
        }

        public void Update()
        {
            var boostedAttributes = new List<BoosterUpdateModule>();

            AddBoostedAttribute(boostedAttributes, BoostedAttributeType.DAMAGE, BoostedAttributeTypeModule.DAMAGE);
            AddBoostedAttribute(boostedAttributes, BoostedAttributeType.SHIELD, BoostedAttributeTypeModule.SHIELD);
            AddBoostedAttribute(boostedAttributes, BoostedAttributeType.MAXHP, BoostedAttributeTypeModule.MAXHP);
            AddBoostedAttribute(boostedAttributes, BoostedAttributeType.REPAIR, BoostedAttributeTypeModule.REPAIR);
            AddBoostedAttribute(boostedAttributes, BoostedAttributeType.HONOUR, BoostedAttributeTypeModule.HONOUR);
            AddBoostedAttribute(boostedAttributes, BoostedAttributeType.EP, BoostedAttributeTypeModule.EP);

            Player.SendCommand(AttributeBoosterUpdateCommand.write(boostedAttributes));
            Player.SendCommand(AttributeHitpointUpdateCommand.write(Player.CurrentHitPoints, Player.MaxHitPoints, Player.CurrentNanoHull, Player.MaxNanoHull));
            Player.SendCommand(AttributeShieldUpdateCommand.write(Player.CurrentShieldPoints, Player.MaxShieldPoints));

            //TODO dont need every time
            Player.SettingsManager.SendMenuBarsCommand();
        }

        private void AddBoostedAttribute(List<BoosterUpdateModule> boostedAttributes, BoostedAttributeType boostedAttributeType, short boostedAttributeTypeModule)
        {
            var percentage = GetPercentage(boostedAttributeType);

            if (percentage <= 0)
                return;

            var boosterList = Boosters.ContainsKey((short)boostedAttributeType)
                ? Boosters[(short)boostedAttributeType].Select(x => new BoosterTypeModule(x.Type)).ToList()
                : new List<BoosterTypeModule>();

            boostedAttributes.Add(new BoosterUpdateModule(new BoostedAttributeTypeModule(boostedAttributeTypeModule), percentage, boosterList));
        }

        public int GetPercentage(BoostedAttributeType boostedAttributeType)
        {
            var percentage = 0;

            if (Boosters.ContainsKey((short)boostedAttributeType))
                foreach (var booster in Boosters[(short)boostedAttributeType])
                    percentage += GetBoosterPercentage(booster.Type);

            percentage += GetExternalPercentage(boostedAttributeType);

            return percentage;
        }

        private int GetExternalPercentage(BoostedAttributeType boostedAttributeType)
        {
            return ExternalBoosts.TryGetValue(boostedAttributeType, out var percentage) ? percentage : 0;
        }

        private short GetBoosterType(short boosterType)
        {
            short boostedAttributeType = 0;

            switch (boosterType)
            {
                case BoosterTypeModule.DMG_B01:
                case BoosterTypeModule.DMG_B02:
                    boostedAttributeType = (short)BoostedAttributeType.DAMAGE;
                    break;
                case BoosterTypeModule.SHD_B01:
                case BoosterTypeModule.SHD_B02:
                    boostedAttributeType = (short)BoostedAttributeType.SHIELD;
                    break;
                case BoosterTypeModule.HP_B01:
                case BoosterTypeModule.HP_B02:
                    boostedAttributeType = (short)BoostedAttributeType.MAXHP;
                    break;
                case BoosterTypeModule.REP_B01:
                case BoosterTypeModule.REP_B02:
                case BoosterTypeModule.REP_S01:
                    boostedAttributeType = (short)BoostedAttributeType.REPAIR;
                    break;
                case BoosterTypeModule.HON_B01:
                case BoosterTypeModule.HON_B02:
                case BoosterTypeModule.HON50:
                    boostedAttributeType = (short)BoostedAttributeType.HONOUR;
                    break;
                case BoosterTypeModule.EP_B01:
                case BoosterTypeModule.EP_B02:
                case BoosterTypeModule.EP50:
                    boostedAttributeType = (short)BoostedAttributeType.EP;
                    break;
            }

            return boostedAttributeType;
        }

        private int GetBoosterPercentage(short boosterTypeModule)
        {
            var percentage = 0;

            switch (boosterTypeModule)
            {
                case BoosterTypeModule.DMG_B01:
                case BoosterTypeModule.DMG_B02:
                case BoosterTypeModule.HP_B01:
                case BoosterTypeModule.HP_B02:
                case BoosterTypeModule.REP_B01:
                case BoosterTypeModule.REP_B02:
                case BoosterTypeModule.REP_S01:
                case BoosterTypeModule.HON_B01:
                case BoosterTypeModule.HON_B02:
                case BoosterTypeModule.EP_B01:
                case BoosterTypeModule.EP_B02:
                    percentage = 10;
                    break;
                case BoosterTypeModule.SHD_B01:
                case BoosterTypeModule.SHD_B02:
                    percentage = 25;
                    break;
                case BoosterTypeModule.HON50:
                case BoosterTypeModule.EP50:
                    percentage = 50;
                    break;
            }

            return percentage;
        }
    }
}
