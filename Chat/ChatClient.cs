//TODO ide belerakni egy punkció hívást majd a Battle Royal kapuhoz

using Ow.Game.Events;
using Ow.Game.Objects;
using Ow.Game.Movements;
using Ow.Managers;
using Ow.Net;
using Ow.Net.netty;
using Ow.Net.netty.commands;
using Ow.Utils;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Ow.Game;
using static Ow.Game.GameSession;
using System.Collections.Concurrent;
using Ow.Managers.MySQLManager;
using Ow.Game.Objects.Collectables;
using Ow.Game.Objects.Stations;

namespace Ow.Chat
{
    public enum Permissions
    {
        NORMAL = 0,
        ADMINISTRATOR = 1,
        CHAT_MODERATOR = 2
    }

    class ChatClient
    {
        public Socket Socket { get; set; }
        public int UserId { get; set; }
        public Permissions Permission { get; set; }
        public List<Int32> ChatsJoined = new List<Int32>();

        public static List<string> Filter = new List<string>
        {
            "orospu",
            "çocuğu",
            "karını",
            "sülaleni",
            "dinini",
            "imanını",
            "kitabını",
            "siker",
            "lavuk",
            "ananı",
            "gavat",
            "oneultimate",
            "http",
            "bitch",
            "fuck",
            "restart",
            "amına",
            "piç",
            "yavşak",
            "siktir",
            "anne",
            "bacı",
            "sikerim",
            "pezevenk",
            ".ovh",
            "puto",
            "maldito",
            "perro"
        };

        public ChatClient(Socket handler)
        {
            Socket = handler;

            StateObject state = new StateObject();
            state.workSocket = handler;
            handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                new AsyncCallback(ReadCallback), state);
        }

        public static void LoadChatRooms()
        {
            var rooms = new List<Room> {
                new Chat.Room(1, "Global", 0, -1),
                new Chat.Room(2, "MMO", 1, 1),
                new Chat.Room(3, "EIC", 2, 2),
                new Chat.Room(4, "VRU", 3, 3),
                new Chat.Room(5, "Clan Search", 5, -1)
            };

            foreach (var room in rooms)
                Chat.Room.Rooms.Add(room.Id, room);
        }

        public void Execute(string message)
        {
            try
            {
                string[] packet = message.Split(ChatConstants.MSG_SEPERATOR);
                switch (packet[0])
                {
                    case ChatConstants.CMD_USER_LOGIN:
                        var loginPacket = message.Replace("@", "%").Split('%');
                        UserId = Convert.ToInt32(loginPacket[3]);

                        if (!QueryManager.CheckSessionId(UserId, loginPacket[4]))
                        {
                            Close();
                            return;
                        }

                        var gameSession = GameManager.GetGameSession(UserId);
                        if (gameSession == null) return;

                        Permission = (Permissions)QueryManager.GetChatPermission(gameSession.Player.Id);

                        if (GameManager.ChatClients.ContainsKey(UserId))
                            GameManager.ChatClients[gameSession.Player.Id]?.Close();

                        GameManager.ChatClients.TryAdd(gameSession.Player.Id, this);

                        Send("bv%" + gameSession.Player.Id + "#");
                        var servers = Room.Rooms.Aggregate(String.Empty, (current, chat) => current + chat.Value.ToString());
                        servers = servers.Remove(servers.Length - 1);
                        Send("by%" + servers + "#");
                        Send($"dq%Use '/duel name' for invite someone to duel.#");
                        ChatsJoined.Add(Room.Rooms.FirstOrDefault().Value.Id);

                        if (QueryManager.ChatFunctions.Banned(UserId))
                        {
                            Send($"{ChatConstants.CMD_BANN_USER}%#");
                            Close();
                            return;
                        }
                        break;
                    case ChatConstants.CMD_USER_MSG:
                        SendMessage(message);
                        break;
                    case ChatConstants.CMD_USER_JOIN:
                        var roomId = Convert.ToInt32(message.Split('%')[2].Split('@')[0]);
                        gameSession = GameManager.GetGameSession(UserId);

                        if (Room.Rooms.ContainsKey(roomId))
                        {
                            if (!ChatsJoined.Contains(roomId))
                                ChatsJoined.Add(roomId);
                        }
                        else
                        {
                            if (gameSession.Player.Storage.DuelInvites.ContainsKey(roomId))
                                AcceptDuel(gameSession.Player.Storage.DuelInvites[roomId], roomId);
                        }
                        break;
                }
            }
            catch (Exception e)
            {
                Out.WriteLine("Exception: " + e, "ChatClient.cs");
                Logger.Log("error_log", $"- [ChatClient.cs] Execute void exception: {e}");
            }
        }

        public void AcceptDuel(Player inviterPlayer, int duelId)
        {          
            var gameSession = GameManager.GetGameSession(UserId);

            if (!gameSession.Player.Storage.DuelInvites.ContainsKey(duelId))
            {
                Send($"dq%This invite is no longer available.#");
                return;
            }

            if (Duel.InDuel(gameSession.Player))
            {
                Send($"dq%You can't accept duels while you're in a duel.#");
                return;
            }

            if (Duel.InDuel(inviterPlayer))
            {
                Send($"dq%Your opponent is already fighting on duel with another player.#");
                return;
            }

            if (inviterPlayer != null && gameSession.Player != null)
            {
                if (gameSession.Player.Storage.IsInEquipZone && inviterPlayer.Storage.IsInEquipZone)
                {
                    gameSession.Player.Storage.DuelInvites.TryRemove(duelId, out inviterPlayer);
                    var players = new ConcurrentDictionary<int, Player>();
                    players.TryAdd(gameSession.Player.Id, gameSession.Player);
                    players.TryAdd(inviterPlayer.Id, inviterPlayer);

                    new Duel(players);
                }
            }          
        }

        public void SendMessage(string content)
        {
            var gameSession = GameManager.GetGameSession(UserId);
            if (gameSession == null) return;

            gameSession.LastActiveTime = DateTime.Now;
            string messagePacket = "";

            var packet = content.Replace("@", "%").Split('%');
            var roomId = packet[1];
            var message = packet[2];

            var cmd = message.Split(' ')[0];
            if (cmd == "/ahelp")
            {
                var helpMessages = new List<string>
                {
                    "/ahelp - Parancs lista megjelenítése.",
                    "/reconnect - Chat kapcsolat újraindítása.",
                    "/w <játékos> <üzenet> - Privát üzenet küldése.",
                    "/duel <játékos> - Párbaj meghívás küldése.",
                    "/users - Online játékosok listázása.",
                    "/ausers - Összes online játékos és ID listázása."
                };

                if (Permission == Permissions.ADMINISTRATOR || Permission == Permissions.CHAT_MODERATOR)
                {
                    helpMessages.AddRange(new[]
                    {
                        "/kick <userId> - Felhasználó kirúgása a chatről.",
                        "/ban <userId> <typeId> <óra> <indok> - Tiltás (0=chat, 1=játék; moderátor csak 0).",
                        "/unban <userId> <typeId> - Tiltás feloldása (0=chat, 1=játék)."
                    });
                }

                if (Permission == Permissions.ADMINISTRATOR)
                {
                    helpMessages.AddRange(new[]
                    {
                        "/msg <szöveg> - Rendszerüzenet küldése mindenkinek.",
                        "/destroy <userId> - Hajó elpusztítása.",
                        "/ship <shipId> - Saját hajó típusának cseréje.",
                        "/spawn <shipId> <mapId> <x> <y> - NPC lehívása a megadott pozícióra.",
                        "/spawn_booty <típus> - Booty láda létrehozása a jelenlegi pozíciódon (1=zöld, 2=piros, 3=kék, 4=arany).",
                        "/jump <mapId> - Ugrás egy másik pályára (0,0 pozíció).",
                        "/tp <mapId> <x> <y> - Ugrás adott pályára koordinátákkal.",
                        "/ptp <userId> <mapId> <x> <y> - Játékos áthelyezése adott pályára és pozícióra.",
                        "/set_portal <mapId> <x> <y> <targetMapId> <targetX> <targetY> - Portál létrehozása a pályán.",
                        "/set_cbs <name> <mapId> <clanId> <x> <y> <inBuildingState> <buildTimeInMinutes> <buildTime yyyy-MM-dd HH:mm:ss> <deflectorActive> <deflectorSecondsLeft> <deflectorTime yyyy-MM-dd HH:mm:ss> <visualModifiers json> <modules json> <active (0|1)> - Clan Battle Station sor beszúrása/frissítése.",
                        "/move <userId> <mapId> - Játékos mozgatása pálya 0,0 koordinátára.",
                        "/teleport <userId> - Saját hajó áthelyezése a megadott játékoshoz.",
                        "/pos - Jelenlegi pozíciód kijelzése.",
                        "/pull <userId> - Játékos behívása a pozíciódra.",
                        "/speed+ <érték> - Sebesség bónusz beállítása.",
                        "/damage+ <érték> - Sebzés bónusz beállítása.",
                        "/hp+ <érték> - Életerő növelése mindkét konfiguráción.",
                        "/shield+ <érték> - Pajzs növelése mindkét konfiguráción.",
                        "/god <on|off> - Isten mód ki- vagy bekapcsolása.",
                        "/start_spaceball [limit] - Spaceball esemény indítása (opcionális limitekkel).",
                        "/stop_spaceball - Spaceball esemény leállítása.",
                        "/start_jpb - Jackpot Battle indítása.",
                        "/give_booster <userId> <boosterType> [óra] - Booster adása (típus: 0,1,2,3,8,9,10,11,12,5,6,15,16,7,4).",
                        "/system <szöveg> - Rendszerüzenet küldése a chatre.",
                        "/title <userId> <title> <permanent (0|1)> - Egyedi cím beállítása.",
                        "/rmtitle <userId> <permanent (0|1)> - Cím eltávolítása.",
                        "/id <pilotName> - Játékos azonosítójának lekérdezése.",
                        "/reward <userId> <typeId> <mennyiség> - Jutalom adása (1=uridium, 2=credits, 3=honor, 4=experience).",
                        "/cd0 - Az összes játékos cooldownjának 5 másodpercre állítása.",
                        "/restart <másodperc> - Szerver újraindításának időzítése."
                    });
                }

                foreach (var help in helpMessages)
                    Send($"dq%{help}#");
            }
            else if (message.StartsWith("/reconnect"))
            {
                Close();
            }
            else if (cmd == "/w")
            {
                if (message.Split(' ').Length < 2)
                {
                    Send($"{ChatConstants.CMD_NO_WHISPER_MESSAGE}%#");
                    return;
                }

                var player = GameManager.GetPlayerByName(message.Split(' ')[1]);

                if (player == null || !GameManager.ChatClients.ContainsKey(player.Id))
                {
                    Send($"{ChatConstants.CMD_USER_NOT_EXIST}%#");
                    return;
                }

                if (player.Name == gameSession.Player.Name)
                {
                    Send($"dq%You can't whisper to yourself.#");
                    //Send($"{ChatConstants.CMD_CANNOT_WHISPER_YOURSELF}%#");
                    return;
                }

                message = message.Remove(0, player.Name.Length + 3);
                GameManager.ChatClients[player.Id].Send("cv%" + gameSession.Player.Name + "@" + message + "#");
                Send("cw%" + player.Name + "@" + message + "#");

                foreach (var client in GameManager.ChatClients.Values)
                {
                    if (gameSession.Player.Id != client.UserId && client.Permission == Permissions.ADMINISTRATOR && GameManager.ChatClients[player.Id].Permission != Permissions.ADMINISTRATOR)
                        client.Send($"dq%{gameSession.Player.Name} whispering to {player.Name}:{message}#");
                }

                Logger.Log("chat_log", $"{gameSession.Player.Name} ({gameSession.Player.Id}) whispering to {player.Name} ({player.Id}):{message}");
            }
            else if (cmd == "/kick" && (Permission == Permissions.ADMINISTRATOR || Permission == Permissions.CHAT_MODERATOR))
            {
                if (message.Split(' ').Length < 2) return;

                var userId = Convert.ToInt32(message.Split(' ')[1]);
                var player = GameManager.GetPlayerById(userId);

                if (player != null && player.Name != gameSession.Player.Name)
                {
                    if (GameManager.ChatClients.ContainsKey(player.Id))
                    {
                        var client = GameManager.ChatClients[player.Id];
                        client.Send($"{ChatConstants.CMD_KICK_USER}%#");
                        client.Close();

                        GameManager.SendChatSystemMessage($"{player.Name} has kicked.");
                    }
                }
            }
            else if (cmd == "/duel")
            {
                if (message.Split(' ').Length < 2)
                {
                    Send($"dq%Use '/duel name' for invite someone to duel.#");
                    return;
                }

                var userName = message.Split(' ')[1];
                var duelPlayer = GameManager.GetPlayerByName(userName);

                if (duelPlayer == null || !GameManager.ChatClients.ContainsKey(duelPlayer.Id))
                {
                    Send($"{ChatConstants.CMD_USER_NOT_EXIST}%#");
                    return;
                }

                if (duelPlayer.Name == gameSession.Player.Name)
                {
                    Send($"{ChatConstants.CMD_CANNOT_INVITE_YOURSELF}%#");
                    return;
                }

                if (duelPlayer == null || duelPlayer == gameSession.Player || !GameManager.ChatClients.ContainsKey(duelPlayer.Id)) return;
                if (duelPlayer.Storage.DuelInvites.Any(x => x.Value == gameSession.Player))
                {
                    Send($"dq%{userName} already invited from you before.#");
                    return;
                }

                var duelId = Randoms.CreateRandomID();

                Send($"cr%{duelPlayer.Name}#");
                duelPlayer.Storage.DuelInvites.TryAdd(duelId, gameSession.Player);

                GameManager.ChatClients[duelPlayer.Id].Send("cj%" + duelId + "@" + "Duel" + "@" + 0 + "@" + gameSession.Player.Name + "#");
            }
            else if (cmd == "/msg" && Permission == Permissions.ADMINISTRATOR)
            {
                var msg = message.Remove(0, 4);
                GameManager.SendPacketToAll($"0|A|STD|{msg}");
            }
            else if (cmd == "/destroy" && Permission == Permissions.ADMINISTRATOR)
            {
                if (message.Split(' ').Length < 2) return;

                var userId = Convert.ToInt32(message.Split(' ')[1]);
                var player = GameManager.GetPlayerById(userId);

                if (player == null)
                {
                    Send($"{ChatConstants.CMD_USER_NOT_EXIST}%#");
                    return;
                }

                player.Destroy(gameSession.Player, Game.DestructionType.PLAYER);
            }
            else if (cmd == "/ship" && Permission == Permissions.ADMINISTRATOR)
            {
                if (message.Split(' ').Length < 2) return;

                var shipId = Convert.ToInt32(message.Split(' ')[1]);
                var ship = GameManager.GetShip(shipId);

                if (ship == null)
                {
                    Send($"dq%The ship that with entered doesn't exists.#");
                    return;
                }

                gameSession.Player.ChangeShip(shipId);
            }
            else if (cmd == "/spawn" && Permission == Permissions.ADMINISTRATOR)
            {
                if (message.Split(' ').Length < 5) return;

                var shipId = Convert.ToInt32(message.Split(' ')[1]);
                var mapId = Convert.ToInt32(message.Split(' ')[2]);
                var x = Convert.ToInt32(message.Split(' ')[3]);
                var y = Convert.ToInt32(message.Split(' ')[4]);

                var spacemap = GameManager.GetSpacemap(mapId);
                var ship = GameManager.GetShip(shipId);

                if (ship == null)
                {
                    Send($"dq%The npc with entered id doesn't exist in database.#");
                    return;
                }

                if (spacemap == null)
                {
                    Send($"dq%The map that with entered doesn't exists.#");
                    return;
                }

                var position = new Position(x, y);
                new Npc(Randoms.CreateRandomID(), ship, spacemap, position);

                Send($"dq%Npc spawned on map {mapId} at X: {x}, Y: {y}.#");
            }
            else if (cmd == "/spawn_booty" && Permission == Permissions.ADMINISTRATOR)
            {
                var args = message.Split(' ');
                if (args.Length < 2)
                {
                    Send($"dq%Usage: /spawn_booty <type (1=green, 2=red, 3=blue, 4=gold)>.#");
                    return;
                }

                if (!int.TryParse(args[1], out var bootyType))
                {
                    Send($"dq%Invalid booty type. Use 1=green, 2=red, 3=blue, 4=gold.#");
                    return;
                }

                var player = gameSession.Player;
                var spacemap = player?.Spacemap;
                if (player == null || spacemap == null)
                {
                    Send($"dq%Player or map not available for spawning.#");
                    return;
                }

                Collectable booty = null;
                var bootyName = string.Empty;
                var position = new Position(player.Position.X, player.Position.Y);

                switch (bootyType)
                {
                    case 1:
                        booty = new GreenBooty(position, spacemap, true);
                        bootyName = "green";
                        break;
                    case 2:
                        booty = new RedBooty(position, spacemap, true);
                        bootyName = "red";
                        break;
                    case 3:
                        booty = new BlueBooty(position, spacemap, true);
                        bootyName = "blue";
                        break;
                    case 4:
                        booty = new GoldBooty(position, spacemap, true);
                        bootyName = "gold";
                        break;
                    default:
                        Send($"dq%Invalid booty type. Use 1=green, 2=red, 3=blue, 4=gold.#");
                        return;
                }

                if (booty != null)
                    Send($"dq%Spawned {bootyName} booty at your position (map {spacemap.Id}, X: {position.X}, Y: {position.Y}).#");
            }
            else if (cmd == "/jump" && Permission == Permissions.ADMINISTRATOR)
            {
                if (message.Split(' ').Length < 2) return;

                var mapId = Convert.ToInt32(message.Split(' ')[1]);
                gameSession.Player.Jump(mapId, new Position(0, 0));
            }
            else if (cmd == "/tp" && Permission == Permissions.ADMINISTRATOR)
            {
                if (message.Split(' ').Length < 4) return;

                var mapId = Convert.ToInt32(message.Split(' ')[1]);
                var x = Convert.ToInt32(message.Split(' ')[2]);
                var y = Convert.ToInt32(message.Split(' ')[3]);

                var map = GameManager.GetSpacemap(mapId);

                if (map == null)
                {
                    Send($"dq%The map that with entered doesn't exists.#");
                    return;
                }

                gameSession.Player.Jump(map.Id, new Position(x, y));
            }
            else if (cmd == "/ptp" && Permission == Permissions.ADMINISTRATOR)
            {
                if (message.Split(' ').Length < 5) return;

                var userId = Convert.ToInt32(message.Split(' ')[1]);
                var mapId = Convert.ToInt32(message.Split(' ')[2]);
                var x = Convert.ToInt32(message.Split(' ')[3]);
                var y = Convert.ToInt32(message.Split(' ')[4]);

                var player = GameManager.GetPlayerById(userId);
                var map = GameManager.GetSpacemap(mapId);

                if (player == null)
                {
                    Send($"{ChatConstants.CMD_USER_NOT_EXIST}%#");
                    return;
                }

                if (map == null)
                {
                    Send($"dq%The map that with entered doesn't exists.#");
                    return;
                }

                player.Jump(map.Id, new Position(x, y));
            }
            else if (cmd == "/set_portal" && Permission == Permissions.ADMINISTRATOR)
            {
                var args = message.Split(' ');
                if (args.Length < 7) return;

                var mapId = Convert.ToInt32(args[1]);
                var mapX = Convert.ToInt32(args[2]);
                var mapY = Convert.ToInt32(args[3]);
                var targetMapId = Convert.ToInt32(args[4]);
                var targetX = Convert.ToInt32(args[5]);
                var targetY = Convert.ToInt32(args[6]);

                var map = GameManager.GetSpacemap(mapId);
                var targetMap = GameManager.GetSpacemap(targetMapId);

                if (map == null || targetMap == null)
                {
                    Send($"dq%The map that with entered doesn't exists.#");
                    return;
                }

                var portalBase = new PortalBase
                {
                    TargetSpaceMapId = targetMapId,
                    Position = new List<int> { mapX, mapY },
                    TargetPosition = new List<int> { targetX, targetY },
                    GraphicId = 1,
                    FactionId = 1,
                    Visible = true,
                    Working = true
                };

                if (!QueryManager.AddPortal(mapId, portalBase))
                {
                    Send($"dq%Failed to add portal to map {mapId}.#");
                    return;
                }

                var portalPosition = new Position(mapX, mapY);
                var portalTargetPosition = new Position(targetX, targetY);
                new Portal(map, portalPosition, portalTargetPosition, targetMapId, portalBase.GraphicId, portalBase.FactionId, portalBase.Visible, portalBase.Working);

                Send($"dq%Portal added on map {mapId} at X: {mapX}, Y: {mapY} -> map {targetMapId} ({targetX}, {targetY}).#");
            }
            else if (cmd == "/set_cbs" && Permission == Permissions.ADMINISTRATOR)
            {
                var args = message.Split(' ');
                if (args.Length < 17)
                {
                    Send($"dq%Usage: /set_cbs <name> <mapId> <clanId> <x> <y> <inBuildingState> <buildTimeInMinutes> <buildTime yyyy-MM-dd HH:mm:ss> <deflectorActive> <deflectorSecondsLeft> <deflectorTime yyyy-MM-dd HH:mm:ss> <visualModifiers json> <modules json> <active (0|1)>#");
                    return;
                }

                var name = args[1];
                if (!int.TryParse(args[2], out var mapId) ||
                    !int.TryParse(args[3], out var clanId) ||
                    !int.TryParse(args[4], out var positionX) ||
                    !int.TryParse(args[5], out var positionY) ||
                    !int.TryParse(args[6], out var inBuildingState) ||
                    !int.TryParse(args[7], out var buildTimeInMinutes) ||
                    !int.TryParse(args[10], out var deflectorActive) ||
                    !int.TryParse(args[11], out var deflectorSecondsLeft) ||
                    !int.TryParse(args[16], out var active))
                {
                    Send($"dq%Invalid numeric value. Please check the parameters and try again.#");
                    return;
                }

                if (!DateTime.TryParse($"{args[8]} {args[9]}", out var buildTime) ||
                    !DateTime.TryParse($"{args[12]} {args[13]}", out var deflectorTime))
                {
                    Send($"dq%Invalid date format. Use yyyy-MM-dd HH:mm:ss.#");
                    return;
                }

                List<int> visualModifiers;
                try
                {
                    visualModifiers = JsonConvert.DeserializeObject<List<int>>(args[14]) ?? new List<int>();
                }
                catch
                {
                    Send($"dq%Invalid visual modifier JSON. Use a JSON array, e.g. [].#");
                    return;
                }

                List<EquippedModuleBase> modules;
                try
                {
                    modules = JsonConvert.DeserializeObject<List<EquippedModuleBase>>(args[15]) ?? new List<EquippedModuleBase>();
                }
                catch
                {
                    Send($"dq%Invalid modules JSON. Use a JSON array, e.g. [].#");
                    return;
                }

                var spacemap = GameManager.GetSpacemap(mapId);
                if (spacemap == null)
                {
                    Send($"dq%The map with id {mapId} does not exist.#");
                    return;
                }

                var clan = GameManager.GetClan(clanId);
                if (clan == null)
                {
                    Send($"dq%The clan with id {clanId} does not exist.#");
                    return;
                }

                var visualJson = JsonConvert.SerializeObject(visualModifiers).Replace("'", "\\'");
                var modulesJson = JsonConvert.SerializeObject(modules).Replace("'", "\\'");
                var escapedName = name.Replace("'", "\\'");

                using (var mySqlClient = SqlDatabaseManager.GetClient())
                {
                    mySqlClient.ExecuteNonQuery($"INSERT INTO server_battlestations (name, mapId, clanId, positionX, positionY, inBuildingState, buildTimeInMinutes, buildTime, deflectorActive, deflectorSecondsLeft, deflectorTime, visualModifiers, modules, active) " +
                        $"VALUES ('{escapedName}', {mapId}, {clanId}, {positionX}, {positionY}, {inBuildingState}, {buildTimeInMinutes}, '{buildTime:yyyy-MM-dd HH:mm:ss}', {deflectorActive}, {deflectorSecondsLeft}, '{deflectorTime:yyyy-MM-dd HH:mm:ss}', '{visualJson}', '{modulesJson}', {active}) " +
                        $"ON DUPLICATE KEY UPDATE mapId = VALUES(mapId), clanId = VALUES(clanId), positionX = VALUES(positionX), positionY = VALUES(positionY), inBuildingState = VALUES(inBuildingState), buildTimeInMinutes = VALUES(buildTimeInMinutes), buildTime = VALUES(buildTime), deflectorActive = VALUES(deflectorActive), deflectorSecondsLeft = VALUES(deflectorSecondsLeft), deflectorTime = VALUES(deflectorTime), visualModifiers = VALUES(visualModifiers), modules = VALUES(modules), active = VALUES(active)");
                }

                if (active != 0)
                {
                    if (GameManager.BattleStations.TryGetValue(name, out var existing))
                        existing.Spacemap.Activatables.TryRemove(existing.Id, out _);

                    var battleStation = new BattleStation(name, spacemap, new Position(positionX, positionY), clan, modules, Convert.ToBoolean(inBuildingState), buildTimeInMinutes, buildTime, Convert.ToBoolean(deflectorActive), deflectorSecondsLeft, deflectorTime, visualModifiers);
                    GameManager.BattleStations.AddOrUpdate(name, battleStation, (key, value) => battleStation);

                    foreach (var session in GameManager.GameSessions.Values)
                    {
                        var player = session.Player;
                        if (player != null && player.Spacemap != null && player.Spacemap.Id == spacemap.Id)
                        {
                            short relationType = player.Clan.Id != 0 && battleStation.Clan.Id != 0 ? battleStation.Clan.GetRelation(player.Clan) : (short)0;
                            player.SendCommand(battleStation.GetAssetCreateCommand(relationType));
                        }
                    }
                }
                else if (GameManager.BattleStations.TryRemove(name, out var inactiveStation))
                {
                    inactiveStation.Spacemap.Activatables.TryRemove(inactiveStation.Id, out _);
                }

                Send($"dq%CBS entry for '{name}' saved (active={(active != 0 ? "yes" : "no")}).#");
            }
            else if (cmd == "/move" && Permission == Permissions.ADMINISTRATOR)
            {
                if (message.Split(' ').Length < 3) return;

                var player = GameManager.GetPlayerById(Convert.ToInt32(message.Split(' ')[1]));
                var map = GameManager.GetSpacemap(Convert.ToInt32(message.Split(' ')[2]));

                if (player == null)
                {
                    Send($"{ChatConstants.CMD_USER_NOT_EXIST}%#");
                    return;
                }

                if (map == null)
                {
                    Send($"dq%The map that with entered doesn't exists.#");
                    return;
                }

                GameManager.GetPlayerById(player.Id)?.Jump(map.Id, new Position(0, 0));
            }
            else if (cmd == "/teleport" && Permission == Permissions.ADMINISTRATOR)
            {
                if (message.Split(' ').Length < 3) return;

                var player = GameManager.GetPlayerById(Convert.ToInt32(message.Split(' ')[1]));

                if (player == null)
                {
                    Send($"{ChatConstants.CMD_USER_NOT_EXIST}%#");
                    return;
                }

                gameSession.Player?.Jump(player.Spacemap.Id, player.Position);
            }
            else if (cmd == "/pos" && Permission == Permissions.ADMINISTRATOR)
            {
                var position = gameSession.Player.Position;
                var mapId = gameSession.Player.Spacemap.Id;

                Send($"dq%Your current position is X: {position.X}, Y: {position.Y} on map {mapId}.#");
            }
            else if (cmd == "/pull" && Permission == Permissions.ADMINISTRATOR)
            {
                if (message.Split(' ').Length < 3) return;

                var player = GameManager.GetPlayerById(Convert.ToInt32(message.Split(' ')[1]));

                if (player == null)
                {
                    Send($"{ChatConstants.CMD_USER_NOT_EXIST}%#");
                    return;
                }

                player?.Jump(gameSession.Player.Spacemap.Id, gameSession.Player.Position);
            }
            else if (cmd == "/speed+" && Permission == Permissions.ADMINISTRATOR)
            {
                if (message.Split(' ').Length < 2) return;

                var speed = Convert.ToInt32(message.Split(' ')[1]);
                gameSession.Player.SetSpeedBoost(speed);
            }
            else if (cmd == "/damage+" && Permission == Permissions.ADMINISTRATOR)
            {
                if (message.Split(' ').Length < 2) return;

                var damage = Convert.ToInt32(message.Split(' ')[1]);
                gameSession.Player.Storage.DamageBoost = damage;
            }
            else if (cmd == "/hp+" && Permission == Permissions.ADMINISTRATOR)
            {
                if (message.Split(' ').Length < 2) return;

                var hitpoints = Convert.ToInt32(message.Split(' ')[1]);
                gameSession.Player.Equipment.Configs.Config1Hitpoints += hitpoints;
                gameSession.Player.Equipment.Configs.Config2Hitpoints += hitpoints;
                gameSession.Player.Heal(hitpoints);
                gameSession.Player.UpdateStatus();
                gameSession.Player.Heal(hitpoints);
            }
            else if (cmd == "/shield+" && Permission == Permissions.ADMINISTRATOR)
            {
                if (message.Split(' ').Length < 2) return;

                var shield = Convert.ToInt32(message.Split(' ')[1]);
                gameSession.Player.Equipment.Configs.Config1Shield += shield;
                gameSession.Player.Equipment.Configs.Config2Shield += shield;
                gameSession.Player.Heal(shield, gameSession.Player.Id, HealType.SHIELD);
                gameSession.Player.UpdateStatus();
                gameSession.Player.Heal(shield, gameSession.Player.Id, HealType.SHIELD);
            }
            else if (cmd == "/god" && Permission == Permissions.ADMINISTRATOR)
            {
                if (message.Split(' ').Length < 2) return;

                var mod = message.Split(' ')[1];
                gameSession.Player.Storage.GodMode = mod == "on" ? true : mod == "off" ? false : false;
            }

            else if (cmd == "/start_spaceball" && Permission == Permissions.ADMINISTRATOR)
            {
                if (message.Split(' ').Length >= 2)
                {
                    var limit = Convert.ToInt32(message.Split(' ')[1]);
                    EventManager.Spaceball.Limit = limit;
                }

                EventManager.Spaceball.Start();
            }
            else if (cmd == "/stop_spaceball" && Permission == Permissions.ADMINISTRATOR)
            {
                EventManager.Spaceball.Stop();
            }
            else if (cmd == "/start_jpb" && Permission == Permissions.ADMINISTRATOR)
            {
                EventManager.JackpotBattle.Start();
            }
            else if (cmd == "/give_booster" && Permission == Permissions.ADMINISTRATOR)
            {
                if (message.Split(' ').Length < 3) return;

                var userId = Convert.ToInt32(message.Split(' ')[1]);
                var boosterType = Convert.ToInt32(message.Split(' ')[2]);
                var hours = message.Split(' ').Length == 4 ? Convert.ToInt32(message.Split(' ')[3]) : 10;

                if (!new int[] { 0, 1, 2, 3, 8, 9, 10, 11, 12, 5, 6, 15, 16, 7, 4 }.Contains(boosterType)) return;

                var player = GameManager.GetPlayerById(userId);

                if (player != null)
                    player.BoosterManager.Add((BoosterType)boosterType, hours);
            }
            else if (cmd == "/ban" && (Permission == Permissions.ADMINISTRATOR || Permission == Permissions.CHAT_MODERATOR))
            {
                /*
                0 CHAT BAN
                1 OYUN BANI
                */
                if (message.Split(' ').Length < 4) return;

                var userId = Convert.ToInt32(message.Split(' ')[1]);
                var typeId = Convert.ToInt32(message.Split(' ')[2]);
                var hours = Convert.ToInt32(message.Split(' ')[3]);
                var reason = message.Remove(0, (userId.ToString().Length + typeId.ToString().Length + hours.ToString().Length) + 7);

                if (typeId == 1 && Permission == Permissions.CHAT_MODERATOR) return;

                if (typeId == 0 || typeId == 1)
                {
                    QueryManager.ChatFunctions.AddBan(userId, gameSession.Player.Id, reason, typeId, (DateTime.Now.AddHours(hours)).ToString("yyyy-MM-dd HH:mm:ss"));

                    var player = GameManager.GetPlayerById(userId);

                    if (player != null)
                    {
                        if (GameManager.ChatClients.ContainsKey(player.Id))
                        {
                            var client = GameManager.ChatClients[player.Id];

                            if (client != null)
                            {
                                client.Send($"{ChatConstants.CMD_BANN_USER}%#");
                                client.Close();
                            }
                        }

                        if (typeId == 1)
                        {
                            player.Destroy(null, DestructionType.MISC);
                            player.GameSession.Disconnect(DisconnectionType.NORMAL);
                        }
                    }
                }
            }
            else if (cmd == "/unban" && (Permission == Permissions.ADMINISTRATOR || Permission == Permissions.CHAT_MODERATOR))
            {
                /*
                0 CHAT BAN
                1 OYUN BANI
                */
                if (message.Split(' ').Length < 3) return;

                var userId = Convert.ToInt32(message.Split(' ')[1]);
                var typeId = Convert.ToInt32(message.Split(' ')[2]);

                if (typeId == 1 && Permission == Permissions.CHAT_MODERATOR) return;

                if (typeId == 0 || typeId == 1)
                    QueryManager.ChatFunctions.UnBan(userId, gameSession.Player.Id, typeId);
            }
            else if (cmd == "/restart" && Permission == Permissions.ADMINISTRATOR)
            {
                if (message.Split(' ').Length < 2) return;

                var seconds = Convert.ToInt32(message.Split(' ')[1]);
                GameManager.Restart(seconds);
            }
            else if (cmd == "/users")
            {
                var users = GameManager.GameSessions.Values.Where(x => x.Player.RankId != 21).Aggregate(String.Empty, (current, user) => current + user.Player.Name + ", ");
                users = users.Remove(users.Length - 2);

                Send($"dq%Users online {GameManager.GameSessions.Values.Where(x => x.Player.RankId != 21).Count()}: {users}#");
            }
            else if (cmd == "/ausers")
            {
                var sessions = GameManager.GameSessions.Values.ToList();

                if (!sessions.Any())
                {
                    Send("dq%Users online 0: #");
                    return;
                }

                var users = string.Join(", ", sessions.Select(session => $"{session.Player.Name} ({session.Player.Id})"));

                Send($"dq%Users online {sessions.Count}: {users}#");
            }
            else if (cmd == "/system" && Permission == Permissions.ADMINISTRATOR)
            {
                message = message.Remove(0, 8);
                GameManager.SendChatSystemMessage(message);
            }
            else if (cmd == "/title" && Permission == Permissions.ADMINISTRATOR)
            {
                if (message.Split(' ').Length < 3) return;
                var userId = Convert.ToInt32(message.Split(' ')[1]);
                var title = message.Split(' ')[2];
                var permanent = Convert.ToBoolean(Convert.ToInt32(message.Split(' ')[3]));

                var player = GameManager.GetPlayerById(userId);
                if (player == null || !GameManager.ChatClients.ContainsKey(player.Id))
                {
                    Send($"{ChatConstants.CMD_USER_NOT_EXIST}%#");
                    return;
                }

                player.SetTitle(title, permanent);
            }
            else if (cmd == "/rmtitle" && Permission == Permissions.ADMINISTRATOR)
            {
                if (message.Split(' ').Length < 3) return;
                var userId = Convert.ToInt32(message.Split(' ')[1]);
                var permanent = Convert.ToBoolean(Convert.ToInt32(message.Split(' ')[2]));

                var player = GameManager.GetPlayerById(userId);
                if (player == null || !GameManager.ChatClients.ContainsKey(player.Id))
                {
                    Send($"{ChatConstants.CMD_USER_NOT_EXIST}%#");
                    return;
                }

                player.SetTitle("", permanent);
            }
            else if (cmd == "/id" && Permission == Permissions.ADMINISTRATOR)
            {
                if (message.Split(' ').Length < 2) return;

                var userName = message.Split(' ')[1];

                using (var mySqlClient = SqlDatabaseManager.GetClient())
                {
                    var query = $"SELECT userId FROM player_accounts WHERE pilotName = '{userName}'";

                    var result = (DataTable)mySqlClient.ExecuteQueryTable(query);
                    if (result.Rows.Count >= 1)
                    {
                        var userId = mySqlClient.ExecuteQueryRow(query)["userId"].ToString();

                        Send($"dq%{userName} id is: {userId}#");
                    }
                }
            }
            
            else if (cmd == "/reward" && Permission == Permissions.ADMINISTRATOR)
            {
                if (message.Split(' ').Length < 4) return;
                var userId = Convert.ToInt32(message.Split(' ')[1]);
                var typeId = Convert.ToInt32(message.Split(' ')[2]); //1 uridium / 2 credits / 3 honor / 4 experience
                var amount = Convert.ToInt32(message.Split(' ')[3]);

                var player = GameManager.GetPlayerById(userId);
                
                if (player != null && new[] {1,2,3,4}.Contains(typeId))
                {
                    var rewardName = "";
                    switch (typeId)
                    {
                        case 1:
                            player.ChangeData(DataType.URIDIUM, amount);
                            rewardName = "uridium";
                            break;
                        case 2:
                            player.ChangeData(DataType.CREDITS, amount);
                            rewardName = "credits";
                            break;
                        case 3:
                            player.ChangeData(DataType.HONOR, amount);
                            rewardName = "honor";
                            break;
                        case 4:
                            player.ChangeData(DataType.EXPERIENCE, amount);
                            rewardName = "experience";
                            break;
                    }
                    player.SendPacket($"0|A|STD|You got {amount} {rewardName} from {gameSession.Player.Name}.");
                    Send($"dq%{player.Name} has got {amount} {rewardName} from you.#");
                    GameManager.ChatClients[player.Id].Send($"dq%You got {amount} {rewardName} from {gameSession.Player.Name}.#");
                }
            }
            else if (cmd == "/cd0" && Permission == Permissions.ADMINISTRATOR)
            {
                const int targetSeconds = 5;
                foreach (var session in GameManager.GameSessions.Values)
                    session.Player?.ReduceCooldownsToSeconds(targetSeconds);

                GameManager.SendChatSystemMessage($"All player cooldowns were set to {targetSeconds} seconds by {gameSession.Player.Name}.");
            }

            else
            {
                if (!cmd.StartsWith("/"))
                {
                    foreach (var m in Filter)
                    {
                        if (message.ToLower().Contains(m.ToLower()) && Permission == Permissions.NORMAL)
                        {
                            Send($"{ChatConstants.CMD_KICK_BY_SYSTEM}%#");
                            Close();
                            return;
                        }
                    }

                    foreach (var pair in GameManager.ChatClients.Values)
                    {
                        if (pair.ChatsJoined.Contains(Convert.ToInt32(roomId)))
                        {
                            var name = gameSession.Player.Name + (pair.Permission == Permissions.ADMINISTRATOR || pair.Permission == Permissions.CHAT_MODERATOR ? $" ({gameSession.Player.Id})" : "");
                            var color = (Permission == Permissions.ADMINISTRATOR || Permission == Permissions.CHAT_MODERATOR) ? "j" : "a";
                            messagePacket = $"{color}%" + roomId + "@" + name + "@" + message;

                            if (gameSession.Player.Clan.Tag != "")
                                messagePacket += "@" + gameSession.Player.Clan.Tag;

                            pair.Send(messagePacket + "#");
                        }
                    }

                    Logger.Log("chat_log", $"{gameSession.Player.Name} ({gameSession.Player.Id}): {message}");
                }
            }
        }

        public void Close()
        {
            try
            {
                Socket.Shutdown(SocketShutdown.Both);
                Socket.Close();

                GameManager.ChatClients.TryRemove(UserId, out var value);
            }
            catch (Exception)
            {
                //ignore
                //Logger.Log("error_log", $"- [ChatClient.cs] Close void exception: {e}");
            }
        }

        private void ReadCallback(IAsyncResult ar)
        {
            try
            {
                if (Socket == null || !Socket.IsBound || !Socket.Connected) return;

                String content = String.Empty;

                StateObject state = (StateObject)ar.AsyncState;
                Socket handler = state.workSocket;

                int bytesRead = handler.EndReceive(ar);

                if (bytesRead > 0)
                {
                    content = Encoding.UTF8.GetString(
                        state.buffer, 0, bytesRead);

                    if (content.Trim() != "")
                    {
                        Execute(content);

                        handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                        new AsyncCallback(ReadCallback), state);
                    }
                }
                else
                {
                    Close();
                }
            }
            catch
            {
                Close();
            }
        }

        public void Send(String data)
        {
            try
            {
                if (Socket == null || !Socket.IsBound || !Socket.Connected) return;

                byte[] byteData = Encoding.UTF8.GetBytes(data);

                Socket.BeginSend(byteData, 0, byteData.Length, 0,
                    new AsyncCallback(SendCallback), Socket);
            }
            catch (Exception e)
            {
                Logger.Log("error_log", $"- [ChatClient.cs] Send void exception: {e}");
            }
        }

        private static void SendCallback(IAsyncResult ar)
        {
            try
            {
                Socket handler = (Socket)ar.AsyncState;

                handler.EndSend(ar);
            }
            catch (Exception)
            {
                //Logger.Log("error_log", $"- [ChatClient.cs] SendCallback void exception: {e}");
            }
        }
    }
}
