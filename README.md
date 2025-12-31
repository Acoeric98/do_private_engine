# DarkOrbit Private Engine

Rövid útmutató a kód felépítéséhez és ahhoz, hogy fejlesztéskor hol érdemes keresni a fontos elemeket.

## Projektfa (részlet)
```
.
├── Program.cs                    # Belépési pont: szerver indítás, MySQL-check, tick ciklus
├── Managers/
│   ├── GameManager.cs            # Futó sessionök, játékosok, klánok, pályák, broadcast segédmetódusok
│   ├── EventManager.cs           # Időzített/automatikus események indítása
│   ├── QueryManager.cs           # Adatbázis-lekérdezések és mentések (játékos, chat, stb.)
│   └── MySQLManager/
│       ├── SqlDatabaseClient.cs
│       └── SqlDatabaseManager.cs # MySQL inicializálás és kapcsolatkezelés
├── Game/
│   ├── GameSession.cs            # Játékoshoz kötött session-állapot
│   ├── Spacemap.cs, Ship.cs, Clan.cs
│   ├── Objects/                  # Játékbeli entitások (pl. Player, NPC, Collectable)
│   ├── Movements/                # Mozgás- és pozíciókezelés
│   ├── Events/                   # Játékbeli event logika
│   ├── GalaxyGates/              # Kapu-specifikus működés
│   └── Ticks/                    # Tick interfészek és TickManager segéd
├── Net/
│   ├── GameServer.cs, GameClient.cs
│   ├── ChatServer.cs, SocketServer.cs
│   ├── netty/                    # Csomag-parserek, kérések és parancsok (Handler, commands, handlers, requests)
│   └── mysql/                    # Login/session MySQL kérések a hálózati rétegben
├── Chat/
│   ├── ChatClient.cs             # Chat kapcsolat kezelése
│   ├── ChatConstants.cs
│   └── Room.cs                   # Chat-szobák definíciója és regisztrálása
├── Utils/
│   ├── Out.cs, Logger.cs         # Logolás és konzolos kimenet
│   ├── Maths.cs, Randoms.cs, Bytes.cs
├── App.config                    # .NET futtatási beállítások, assembly binding
└── DarkOrbit.sln / DarkOrbit.csproj
```

## Fő összefüggések fejlesztőknek
- **Indítási folyamat**: A `Program.cs` állítja be az UTF-8 kimenetet, ellenőrzi a MySQL kapcsolatot (`SqlDatabaseManager.Initialize()`), betölti az alap adatokat (`QueryManager.Load...` hívások), majd elindítja a szervereket (`GameServer`, `ChatServer`, `SocketServer`) és a tick ciklust (`TickManager.Tick()`).
- **Hálózati réteg**: A `Net/` mappa tartalmazza a TCP szervereket és klienseket. A `netty/Handler.cs` regisztrálja a parancsokat (`AddCommands`), míg a `netty/requests` és `netty/commands` könyvtárakban vannak az egyes csomagok/parancsok osztályai. A `GameClient` és `ChatClient` osztályok kapcsolják össze a socketet a játékmenettel/chat modulokkal.
- **Adatbázis és perzisztencia**: A `Managers/MySQLManager/` biztosítja a MySQL kapcsolatot; a `QueryManager` végzi a mentéseket (pl. játékos állapot, boosterek, modulok) és lekéréseket (session ellenőrzés, chat bannok). Fejlesztéskor új mentéseket/lekérdezéseket érdemes itt bővíteni, hogy minden DB-hívás egy helyen maradjon.
- **Játéklogika**: A `Game/` alatti osztályok a domain-modellt alkotják (`Spacemap`, `Ship`, `Clan`, különböző `Objects` és `Events`). A `GameSession` kapcsolja össze a játékost a hálózati klienssel; a `TickManager` és `Ticks/` interfészeken keresztül fut a folyamatos játékmenet (pl. mozgás, spawn, időzített események).
- **Chat modul**: A `Chat/` mappa kezeli a chat socket kapcsolatot (`ChatClient`), a szobákat (`Room.AddRooms()` hívódik indításkor) és a chat specifikus konstansokat/packeteket.
- **Segédosztályok**: A `Utils/` gyűjti a logolást (`Logger`, `Out`), matematikai segédfüggvényeket és bitekkel való műveleteket, amelyeket a hálózati és játék réteg is használ.

Fejlesztéskor érdemes a fenti területeket követni: új játékelemekhez a `Game/` alá, új csomag/parancs kezeléshez a `Net/netty/` alá, adatbázis-műveletekhez a `QueryManager` és `MySQLManager` rétegekhez, míg közös segédfunkciókhoz a `Utils/` modulba tegyél kódot.



BOOSTER ID:

ID	Kódbeli név
0	DMG_B01
1	DMG_B02
2	EP_B01
3	EP_B02
4	EP50
5	HON_B01
6	HON_B02
7	HON50
8	HP_B01
9	HP_B02
10	REP_B01
11	REP_B02
12	REP_S01
13	RES_B01
14	RES_B02
15	SHD_B01
16	SHD_B02
17	SREG_B01
18	SREG_B02
19	BB_01
20	QR_01
21	CD_B01
22	CD_B02
23	KAPPA_B01
24	HONM_1
25	XPM_1
26	DMGM_1

MODUL ID:

ID	Kódbeli név	Rövid azonosító (GetName)
0	NONE	(nincs megjelenített kód)
1	DESTROYED	(nincs megjelenített kód)
2	HULL	(nincs megjelenített kód)
3	DEFLECTOR	(nincs megjelenített kód)
4	REPAIR	REPM-1
5	LASER_HIGH_RANGE	LTM-HR
6	LASER_MID_RANGE	LTM-MR
7	LASER_LOW_RANGE	LTM-LR
8	ROCKET_MID_ACCURACY	RAM-MA
9	ROCKET_LOW_ACCURACY	RAM-LA
10	HONOR_BOOSTER	HONM-1
11	DAMAGE_BOOSTER	DMGM-1
12	EXPERIENCE_BOOSTER	XPM-1
