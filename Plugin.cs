namespace AllRegions
{
    // The usings sit inside the namespace so the engine's names win over game classes with the same
    // name in the global namespace (many games have their own SceneManager, Logger, Player...).
    using System;
    using System.Collections.Generic;
    using BepInEx;
    using BepInEx.Configuration;
    using BepInEx.Logging;
    using BepInEx.Unity.IL2CPP;
    using HarmonyLib;
    using UnityEngine;
    using Game;
    using RegionNo = Game.QuantumRegion.No;

    /// <summary>
    /// All Regions: PICO PARK 2's public lobby list shows the lobbies of every region at once, each with its
    /// region and ping, instead of one region at a time.
    ///
    /// How it works:
    /// - While the list sits open, a scanner visits the other regions one after another with the game's own
    ///   GameConnection (a few regions at a time, each connection closed and cleaned up once its list has arrived), and
    ///   turns their rooms into list entries with the game's own RoomConnector code.
    /// - Those entries are added to the list the game builds for your region; each row gets a region/ping label.
    /// - Picking a lobby from another region does what you would do by hand: change the region (the same
    ///   setting the region window writes), let the game reload the list for that region, then press the
    ///   lobby's button, so the game joins it through its normal path.
    /// - The scanner steps aside the moment you pick a lobby or leave the list.
    /// </summary>
    [BepInPlugin(Guid, PluginName, Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "modwright.picopark2.allregions";
        public const string PluginName = "AllRegions";
        public const string Version = "0.4.1";

        internal static ManualLogSource Logger;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> RescanSeconds;
        internal static ConfigEntry<int> ParallelRegions;
        internal static ConfigEntry<bool> DetailedLog;

        public override void Load()
        {
            Logger = Log;
            Enabled = Config.Bind("General", "Enabled", true, "Show the public lobbies of all regions together.");
            RescanSeconds = Config.Bind("General", "RescanSeconds", 10, "While the list is open, search all regions again this many seconds after the last search ended.");
            ParallelRegions = Config.Bind("General", "ParallelRegions", 3, "How many other regions to search at the same time.");
            DetailedLog = Config.Bind("Troubleshooting", "DetailedLog", true,
                "Write every step (searches, joins, list refreshes) to BepInEx/AllRegions.log. Send that file with a bug report.");
            Diag.Open();
            try
            {
                new Harmony(Guid).PatchAll();
                AddComponent<Scanner>();
                Diag.Info(PluginName + " " + Version + " loaded");
            }
            catch (Exception e)
            {
                Diag.Error("loading failed, the mod is off", e);
            }
        }

        internal static string RegionLabel(RegionNo no) => no switch
        {
            RegionNo.Asia => "Asia",
            RegionNo.Australia => "Australia",
            RegionNo.Europe => "Europe",
            RegionNo.HongKong => "Hong Kong",
            RegionNo.India => "India",
            RegionNo.Japan => "Japan",
            RegionNo.Russia => "Russia",
            RegionNo.SouthAfrica => "S. Africa",
            RegionNo.SouthAmerica => "S. America",
            RegionNo.SouthKorea => "S. Korea",
            RegionNo.UsaWest => "US West",
            RegionNo.UsaEast => "US East",
            _ => no.ToString(),
        };
    }

    /// <summary>
    /// The troubleshooting log, BepInEx/AllRegions.log: what the mod did and why, with times, so a player can
    /// send one file with a bug report. The previous session's log is kept as AllRegions.prev.log. Important
    /// lines also go to BepInEx's own log and console.
    /// </summary>
    internal static class Diag
    {
        private const long MaxBytes = 4 * 1024 * 1024;
        private static System.IO.StreamWriter file;
        private static long written;
        private static readonly Dictionary<string, float> lastSaid = new();

        internal static string FilePath => System.IO.Path.Combine(Paths.BepInExRootPath, "AllRegions.log");

        internal static void Open()
        {
            try
            {
                var path = FilePath;
                var prev = System.IO.Path.Combine(Paths.BepInExRootPath, "AllRegions.prev.log");
                if (System.IO.File.Exists(path)) System.IO.File.Copy(path, prev, true);
                // Flushed on every line, so the file is complete even if the game crashes.
                file = new System.IO.StreamWriter(path, false, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
                Write("INFO", $"AllRegions {Plugin.Version}, started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                Write("INFO", $"game {Application.version}, Unity {Application.unityVersion}, BepInEx {typeof(Paths).Assembly.GetName().Version}, {Environment.OSVersion}");
                Write("INFO", $"settings: Enabled={Plugin.Enabled.Value} RescanSeconds={Plugin.RescanSeconds.Value} ParallelRegions={Plugin.ParallelRegions.Value} DetailedLog={Plugin.DetailedLog.Value}");
            }
            catch (Exception e) { Plugin.Logger.LogWarning("could not open AllRegions.log: " + e.Message); file = null; }
        }

        /// <summary>A step worth knowing when something goes wrong; only in the file, and only with DetailedLog.</summary>
        internal static void Debug(string msg)
        {
            if (Plugin.DetailedLog.Value) Write("DEBUG", msg);
        }

        internal static void Info(string msg)
        {
            Plugin.Logger.LogInfo(msg);
            Write("INFO", msg);
        }

        internal static void Warn(string msg)
        {
            Plugin.Logger.LogWarning(msg);
            Write("WARN", msg);
        }

        internal static void Error(string what, Exception e)
        {
            Plugin.Logger.LogError(what + ": " + e.Message);
            Write("ERROR", what + ": " + e);
        }

        /// <summary>An error that could repeat every frame: written at most once every 10 seconds per kind.</summary>
        internal static void ErrorOnce(string what, Exception e)
        {
            float now = Time.unscaledTime;
            if (lastSaid.TryGetValue(what, out var t) && now - t < 10f) return;
            lastSaid[what] = now;
            Error(what, e);
        }

        private static void Write(string level, string msg)
        {
            if (file == null || written > MaxBytes) return;
            try
            {
                var line = $"{DateTime.Now:HH:mm:ss.fff} {level,-5} {msg}";
                written += line.Length + 2;
                file.WriteLine(line);
                if (written > MaxBytes) file.WriteLine("(log is full; restart the game to start a new one)");
            }
            catch { file = null; }
        }
    }

    /// <summary>A lobby found in a region, and where it came from.</summary>
    internal sealed class FoundRoom
    {
        public PublicRoomInfo Info;
        public RegionNo Region;
        public int Ping;
        public float Seen;   // when a list last had it
    }

    /// <summary>Scans the other regions while the list is idle, and carries out cross-region joins.</summary>
    public class Scanner : MonoBehaviour
    {
        public Scanner(IntPtr ptr) : base(ptr) { }

        // Every room the list should show, by room name, with its region and ping (your region's too).
        internal static readonly Dictionary<string, FoundRoom> Rooms = new();
        internal static RoomConnector Connector;
        internal static bool Converting;   // while the scanner borrows the game's list builder

        // The public list's routine (RoomConnector.func_PublicGuest) waits for a pick in this state.
        private const int ListIdleState = 3;
        // Lobby buttons are the menu's choices from ListTop (6) on: the first row is 6, the next 7...
        internal const int ListTopChoice = 6;

        private static readonly RegionNo[] AllNos =
        {
            RegionNo.Asia, RegionNo.Australia, RegionNo.Europe, RegionNo.HongKong, RegionNo.India, RegionNo.Japan,
            RegionNo.Russia, RegionNo.SouthAfrica, RegionNo.SouthAmerica, RegionNo.SouthKorea, RegionNo.UsaWest, RegionNo.UsaEast,
        };

        // One region being scanned: its own connection, closed as soon as the list has arrived.
        private sealed class Job
        {
            public GameConnection Conn;
            public RegionNo No;
            public float Started;
            public bool Requested;
        }

        private readonly List<RegionNo> todo = new();
        private readonly List<Job> jobs = new();
        private float lastScanEnd = -1000f, idleSince = -1f, rescanAskedAt = -1000f;
        private bool rescanNow;
        private int scanTotal, scanDone;

        // A cross-region join in progress: the room, its region and when it started.
        internal static string PendingRoom;
        internal static RegionNo PendingRegion;
        private static float pendingSince;

        // Connections being closed: Photon finishes a disconnect only while the client is serviced, so they are
        // updated until they are gone, then the game's own cleanup (destroyClient) releases them.
        private readonly List<(GameConnection Conn, float Since)> closing = new();

        internal static Scanner I;
        private void Awake() => I = this;

        private static bool quitting;

        // Unity calls this before the game exits. Extra connections still open (a search in progress, or ones
        // being closed) have their own network threads, and the game hangs on exit if they keep running:
        // stop them at once, without the polite disconnect a normal close does.
        private void OnApplicationQuit() => Shutdown("the game is closing");
        private void OnDestroy() => Shutdown("the mod was unloaded");

        private void Shutdown(string why)
        {
            if (quitting) return;
            quitting = true;
            int n = jobs.Count + closing.Count;
            todo.Clear();
            foreach (var j in jobs) Kill(j.Conn);
            foreach (var (c, _) in closing) Kill(c);
            jobs.Clear();
            closing.Clear();
            scanTotal = 0;
            PendingRoom = null;
            Diag.Info($"shut down ({why}), {n} extra connections stopped");
        }

        private static void Kill(GameConnection c)
        {
            try { c.m_Client?.LoadBalancingPeer?.StopThread(); } catch { }
            try { c.destroyClient(); } catch { }
        }

        private void Update()
        {
            if (quitting) return;
            ServiceClosing();
            try
            {
                FindMenu();
                WatchUpdateButton();
                if (PendingRoom != null) StepJoin();
                else StepScan();
            }
            catch (Exception e)
            {
                Diag.ErrorOnce(PendingRoom != null ? "join" : "scan", e);
                if (PendingRoom != null) { PendingRoom = null; CloseInfoWindow(); }
                Stop("error");
            }
        }

        private void ServiceClosing()
        {
            for (int i = closing.Count - 1; i >= 0; i--)
            {
                var (c, since) = closing[i];
                bool done;
                try
                {
                    c.Update();
                    done = c.GetClientState() is GameConnection.ClientState.Disconnected or GameConnection.ClientState.None or GameConnection.ClientState.Error;
                }
                catch { done = true; }
                if (!done && Time.unscaledTime - since < 5f) continue;
                if (!done) Diag.Debug("a search connection did not finish disconnecting in 5 s; releasing it anyway");
                try { c.destroyClient(); } catch { }
                closing.RemoveAt(i);
            }
        }

        /// <summary>The list is open and waiting for a pick, and the game's connection is up.</summary>
        private static bool ListIdle()
        {
            try
            {
                var rc = Connector;
                if (rc == null || rc.m_FuncType != RoomConnector.FuncType.PublicGuest || rc.m_FuncState != ListIdleState) return false;
                var c = rc.m_Connection;
                return c != null && c.m_Client != null && c.GetClientState() == GameConnection.ClientState.Connected;
            }
            catch { return false; }
        }

        /// <summary>The scan is running or waiting to go on: what the loading indicator shows.</summary>
        internal bool Searching => scanTotal > 0 && (todo.Count > 0 || jobs.Count > 0);
        internal int ScanDone => scanDone;
        internal int ScanTotal => scanTotal;

        private void StepScan()
        {
            if (!Plugin.Enabled.Value || Menu() == null)
            {
                // The list is gone: drop the scan. Rooms already found stay for when it opens again.
                idleSince = -1f;
                if (jobs.Count > 0 || todo.Count > 0) Stop("the list was closed");
                return;
            }
            if (!ListIdle())
            {
                // The list is busy for a moment (the game refreshing it, a page change): pause, and carry on
                // with the same regions afterwards instead of starting over.
                idleSince = -1f;
                if (jobs.Count > 0) Diag.Debug($"list busy: pausing the search ({scanDone}/{scanTotal} done)");
                foreach (var j in jobs) todo.Insert(0, j.No);
                CloseAll();
                return;
            }
            if (idleSince < 0) idleSince = Time.unscaledTime;
            if (Time.unscaledTime - idleSince < 0.5f) return;   // let the game settle first

            if (todo.Count == 0 && jobs.Count == 0 && (rescanNow || Time.unscaledTime - lastScanEnd >= Plugin.RescanSeconds.Value))
            {
                rescanNow = false;
                var home = HomeRegion();
                foreach (var no in AllNos) if (no != home) todo.Add(no);
                scanTotal = todo.Count;
                scanDone = 0;
                Diag.Debug($"searching {todo.Count} other regions (your region: {Plugin.RegionLabel(home)})");
                RefreshList();   // your own region's rows, from the game's connection, at the same pace
            }

            // A few regions at once; connections still closing count against the limit.
            int max = System.Math.Max(1, Plugin.ParallelRegions.Value);
            while (todo.Count > 0 && jobs.Count < max && jobs.Count + closing.Count < max + 2)
            {
                var no = todo[0];
                todo.RemoveAt(0);
                StartRegion(no);
            }

            for (int i = jobs.Count - 1; i >= 0; i--)
            {
                var j = jobs[i];
                j.Conn.Update();
                var state = j.Conn.GetClientState();
                if (state == GameConnection.ClientState.Connected && !j.Requested)
                {
                    j.Conn.requestRoomList();
                    j.Requested = true;
                }
                bool done = false;
                if (j.Conn.GetRoomState() == GameConnection.RoomState.LoadedRoomList) { Collect(j); done = true; }
                else if (state is GameConnection.ClientState.Error or GameConnection.ClientState.Disconnected || Time.unscaledTime - j.Started > 10f)
                {
                    // Its lobbies can't be confirmed: drop them rather than show lobbies that may be gone.
                    int dropped = Forget(j.No);
                    Diag.Debug($"{Plugin.RegionLabel(j.No)}: no answer ({state}, {Time.unscaledTime - j.Started:0.0} s){(dropped > 0 ? $", hid its {dropped} lobbies" : "")}");
                    if (dropped > 0) RefreshList();
                    done = true;
                }
                if (!done) continue;
                jobs.RemoveAt(i);
                CloseConn(j.Conn);
                scanDone++;
            }
            if (scanTotal > 0 && todo.Count == 0 && jobs.Count == 0)
            {
                lastScanEnd = Time.unscaledTime;
                scanTotal = 0;
                Diag.Debug($"search done: {Rooms.Count} lobbies listed in all");
            }
        }

        /// <summary>Removes a region's lobbies from the list; returns how many there were.</summary>
        private static int Forget(RegionNo no)
        {
            var stale = new List<string>();
            foreach (var kv in Rooms) if (kv.Value.Region == no) stale.Add(kv.Key);
            foreach (var k in stale) Rooms.Remove(k);
            return stale.Count;
        }

        private float lastUpdateTimer = float.NaN;

        /// <summary>
        /// The Update button starts a cooldown (m_UpdateDisableTimer). Its timer jumps when the button is pressed,
        /// where it otherwise moves by a frame's worth: that jump is the press. Watching it needs no patch on the
        /// game's click handler.
        /// </summary>
        private void WatchUpdateButton()
        {
            var m = Menu();
            // During a join the list restarts and its timer resets; that is not a press.
            if (m == null || !Plugin.Enabled.Value || PendingRoom != null) { lastUpdateTimer = float.NaN; return; }
            float t = m.m_UpdateDisableTimer;
            if (!float.IsNaN(lastUpdateTimer) && Mathf.Abs(t - lastUpdateTimer) > 0.5f) RescanNow();
            lastUpdateTimer = t;
        }

        /// <summary>The list's Update button: scan every region again now, not at the next timed scan.</summary>
        internal void RescanNow()
        {
            if (Time.unscaledTime - rescanAskedAt < 2f) return;
            rescanAskedAt = Time.unscaledTime;
            Diag.Debug("Update pressed: searching all regions again");
            todo.Clear();
            CloseAll();
            scanTotal = 0;
            rescanNow = true;
        }

        private static RegionNo HomeRegion() => RegionOf(Connector?.m_Connection);

        /// <summary>
        /// The region a connection is really on: Photon's region code ("jp", "eu"...). The game's own
        /// m_RegionNo lags behind for a moment while it switches region.
        /// </summary>
        internal static RegionNo RegionOf(GameConnection c)
        {
            if (c == null) return RegionNo.None;
            try
            {
                var token = c.m_Client?.CloudRegion;
                if (!string.IsNullOrEmpty(token))
                {
                    var no = QuantumRegion.GetNo(token.Split('/')[0]);
                    if (no != RegionNo.None) return no;
                }
            }
            catch { }
            return c.m_RegionNo;
        }

        private void StartRegion(RegionNo no)
        {
            var main = Connector.m_ConnectionParameter;
            var p = new ConnectionParameter
            {
                nickName = main.nickName,
                platformUserId = main.platformUserId,
                hostColorIndex = main.hostColorIndex,
                isHost = false,
                regionNo = no,
                roomType = main.roomType,
                gameMode = main.gameMode,
                lobbyTitleType = main.lobbyTitleType,
                roomListType = main.roomListType,
                keepUpdateRoomList = false,
                roomName = "",
                localPlayerCount = main.localPlayerCount,
                blockUserIdList = main.blockUserIdList,
                muteUserIdList = main.muteUserIdList,
            };
            var conn = new GameConnection();
            conn.SetAppVersion(Connector.m_Connection.m_AppVersion ?? "");
            if (!conn.Connect(p))
            {
                Diag.Debug($"{Plugin.RegionLabel(no)}: could not start a connection");
                CloseConn(conn);
                scanDone++;
                return;
            }
            jobs.Add(new Job { Conn = conn, No = no, Started = Time.unscaledTime });
        }

        /// <summary>Turns the region's rooms into the game's list entries, with the game's own code.</summary>
        private void Collect(Job j)
        {
            var conn = j.Conn;
            var current = j.No;
            int ping = 0;
            try { ping = conn.m_Client.LoadBalancingPeer.RoundTripTime; } catch { }
            var rc = Connector;
            var savedConn = rc.m_Connection;
            var savedList = rc.m_PublicRoomInfoList;
            var found = new List<PublicRoomInfo>();
            Converting = true;
            try
            {
                rc.m_Connection = conn;
                rc.m_PublicRoomInfoList = new Il2CppSystem.Collections.Generic.List<PublicRoomInfo>();
                rc.createPublicRoomInfoList();
                foreach (var r in rc.m_PublicRoomInfoList) found.Add(r);
            }
            finally
            {
                rc.m_Connection = savedConn;
                rc.m_PublicRoomInfoList = savedList;
                Converting = false;
            }
            int stale = Forget(current);
            float now = Time.unscaledTime;
            foreach (var r in found) Rooms[r.RoomName] = new FoundRoom { Info = r, Region = current, Ping = ping, Seen = now };
            Diag.Debug($"{Plugin.RegionLabel(current)}: {found.Count} lobbies, {ping} ms, {now - j.Started:0.0} s");
            if (found.Count > 0 || stale > 0) RefreshList();
        }

        private void CloseConn(GameConnection c)
        {
            try { c.Disconnect(); } catch { }
            closing.Add((c, Time.unscaledTime));
        }

        private void CloseAll()
        {
            foreach (var j in jobs) CloseConn(j.Conn);
            jobs.Clear();
        }

        /// <summary>Abandons the scan at once (the rest of the regions wait for the next scan).</summary>
        internal void Stop(string why)
        {
            if (jobs.Count > 0 || todo.Count > 0) Diag.Debug($"stopping the search ({scanDone}/{scanTotal} done): {why}");
            todo.Clear();
            CloseAll();
            scanTotal = 0;
        }

        // ---------------- "searching" indicator ----------------

        private static Texture2D pixel;
        private static GUIStyle style;
        private static int styleSize;
        private static readonly Color Green = new(0.36f, 0.72f, 0.16f);

        /// <summary>
        /// While other regions are being searched, a row of blinking squares and "SEARCHING 4/11" sit above the
        /// REGION label in the corner of the list.
        /// </summary>
        private void OnGUI()
        {
            if (quitting) return;
            try { DrawSearching(); }
            catch (Exception e) { Diag.ErrorOnce("indicator", e); }
        }

        private void DrawSearching()
        {
            if (!Plugin.Enabled.Value || !Searching || PendingRoom != null || Menu() == null) return;
            if (pixel == null) { pixel = new Texture2D(1, 1); pixel.SetPixel(0, 0, Color.white); pixel.Apply(); }
            float s = Screen.height / 1080f;
            int size = Mathf.RoundToInt(30 * s);
            if (style == null || styleSize != size)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
                style.normal.textColor = Green;
                styleSize = size;
            }
            var green = Green;
            var label = $"SEARCHING {ScanDone}/{ScanTotal}";
            float right = Screen.width - 40 * s, y = 860 * s;
            GUI.Label(new Rect(right - 500 * s, y, 500 * s, 40 * s), label, style);
            // Three squares lighting up in turn, left of the text.
            float textW = style.CalcSize(new GUIContent(label)).x;
            float box = 14 * s, gap = 8 * s, x0 = right - textW - 20 * s - 3 * box - 2 * gap;
            int lit = (int)(Time.unscaledTime * 4f) % 3;
            var old = GUI.color;
            for (int i = 0; i < 3; i++)
            {
                GUI.color = i == lit ? green : new Color(green.r, green.g, green.b, 0.3f);
                GUI.DrawTexture(new Rect(x0 + i * (box + gap), y + 20 * s - box / 2, box, box), pixel);
            }
            GUI.color = old;
        }

        /// <summary>Rebuilds the game's list so new lobbies show without waiting for its own refresh.</summary>
        private static void RefreshList()
        {
            try
            {
                if (!ListIdle()) return;
                Connector.createPublicRoomInfoList();
                var menu = Menu();
                if (menu == null) return;
                // Same steps the menu takes for a new list: filter by the ticked game modes, page, draw.
                menu.SetPublicRoomInfoList(Connector.m_PublicRoomInfoList);
                menu.createSelectedRoomInfoList();
                menu.preparePage();
                menu.showPage();
            }
            catch (Exception e) { Diag.ErrorOnce("list refresh", e); }
        }

        // The public list's menu while it is on screen, looked up once a frame.
        private static UISelectPublicMenu menu;

        private static void FindMenu()
        {
            var m = UnityEngine.Object.FindObjectOfType<UISelectPublicMenu>();
            menu = m != null && m.gameObject.activeInHierarchy ? m : null;
        }

        internal static UISelectPublicMenu Menu() => menu != null && menu.gameObject.activeInHierarchy ? menu : null;

        // ---------------- joining a lobby in another region ----------------

        // The select screen's step for the public list (SelectSceneManager.Func_PublicMenu) goes through these states.
        private const int MenuPrepare = 0, MenuState1 = 6, MenuState2 = 7;
        private static int restartIn;
        internal static float RowsDrawnAt = -1f;
        private static float readySince = -1f;

        /// <summary>
        /// Starts a cross-region join the way a player would: set the region the region window sets, then let the
        /// select screen run its public-list step again from the start, as it does after the region window's OK.
        /// It reconnects to that region and shows its list, and the lobby's button is pressed once it is up.
        /// </summary>
        /// <returns>False if the switch could not start; the click is then left to the game.</returns>
        internal static bool BeginJoin(FoundRoom f)
        {
            var gm = Connector.m_GameManager;
            if (gm == null) gm = UnityEngine.Object.FindObjectOfType<GameManager>();
            if (gm == null || gm.m_SaveParameter == null)
            {
                Diag.Warn($"can't switch to {Plugin.RegionLabel(f.Region)} for {f.Info.RoomName}: the game's settings were not found");
                return false;
            }
            I?.Stop("joining a lobby");
            PendingRoom = f.Info.RoomName;
            PendingRegion = f.Region;
            pendingSince = Time.unscaledTime;
            Diag.Info($"joining {PendingRoom} in {Plugin.RegionLabel(f.Region)}: switching region from {Plugin.RegionLabel(HomeRegion())} first");
            gm.m_SaveParameter.regionNo = f.Region;
            try { Connector.m_Connection.Disconnect(); } catch { }
            // The select screen may still be acting on the click this frame: restart its step on the next one.
            restartIn = 2;
            return true;
        }

        private static string joinStateSaid;

        /// <summary>Logs the join's progress when it changes, so a stuck join shows where it stopped.</summary>
        private static void JoinState(string s)
        {
            if (s == joinStateSaid) return;
            joinStateSaid = s;
            Diag.Debug($"join {PendingRoom}: {s}");
        }

        private void StepJoin()
        {
            var ssm = UnityEngine.Object.FindObjectOfType<SelectSceneManager>();
            if (restartIn > 0)
            {
                if (--restartIn == 0)
                {
                    if (ssm == null)
                    {
                        Diag.Warn($"join {PendingRoom}: the select screen is gone; cancelled");
                        PendingRoom = null;
                        return;
                    }
                    Menu()?.SetActive(false);
                    RowsDrawnAt = -1f;
                    readySince = -1f;
                    joinStateSaid = null;
                    ssm.m_FuncState = MenuPrepare;
                    JoinState("restarting the public list in the new region");
                }
                return;
            }
            if (TimeoutCheck(ssm)) return;
            var m = Menu();
            bool ready = m != null && ssm != null && (ssm.m_FuncState == MenuState1 || ssm.m_FuncState == MenuState2)
                         && ListIdle() && HomeRegion() == PendingRegion;
            if (!ready) { readySince = -1f; return; }
            if (readySince < 0f) readySince = Time.unscaledTime;
            CloseInfoWindow();
            RefreshRegionLabel();
            // The menu opens once while it connects and draws the new region's rows when the list arrives; a press
            // before that lands on the old rows and is dropped. Wait for the rows and for the menu to take input.
            if (RowsDrawnAt < 0f || Time.unscaledTime - RowsDrawnAt < 0.3f || !m.m_IsEnableInputInternal || !m.m_IsEnableInputExternal)
            {
                JoinState($"list is up, waiting for its rows (rows drawn: {RowsDrawnAt >= 0f}, input: {m.m_IsEnableInputInternal}/{m.m_IsEnableInputExternal})");
                if (Time.unscaledTime - readySince > 5f)
                {
                    Diag.Info($"{PendingRoom}: {Plugin.RegionLabel(PendingRegion)} shows no lobbies; the list is yours");
                    PendingRoom = null;
                }
                return;
            }
            var page = m.m_PageRoomInfoList;
            for (int i = 0; page != null && i < page.Count; i++)
            {
                if (page[i].RoomName != PendingRoom) continue;
                Diag.Info($"{PendingRoom}: on the list in {Plugin.RegionLabel(PendingRegion)}, pressing it");
                PendingRoom = null;
                m.onClick_LobbyInfoButton(ListTopChoice + i);
                return;
            }
            JoinState($"rows drawn, the lobby is not on the first page ({page?.Count ?? 0} rows)");
            if (Time.unscaledTime - RowsDrawnAt > 3f)
            {
                Diag.Info($"{PendingRoom}: not on the first page of {Plugin.RegionLabel(PendingRegion)} (it may have closed or filled up); the list is yours");
                PendingRoom = null;
            }
        }

        private static bool TimeoutCheck(SelectSceneManager ssm)
        {
            if (Time.unscaledTime - pendingSince <= 25f) return false;
            Diag.Warn($"{PendingRoom}: the list for {Plugin.RegionLabel(PendingRegion)} did not come up in 25 s " +
                      $"(screen step {ssm?.m_FuncState.ToString() ?? "gone"}, list state {Connector?.m_FuncState.ToString() ?? "?"}, connection {Connector?.m_Connection?.GetClientState().ToString() ?? "?"})");
            PendingRoom = null;
            CloseInfoWindow();
            return true;
        }

        /// <summary>The "Connecting..." window the select screen opened for the click it never got to finish.</summary>
        private static void CloseInfoWindow()
        {
            try
            {
                var w = UnityEngine.Object.FindObjectOfType<UIInfoWindow>();
                if (w != null && w.gameObject.activeInHierarchy) w.SetActive(false);
            }
            catch { }
        }

        /// <summary>The REGION label in the corner shows the saved region when it is (re)shown.</summary>
        private static void RefreshRegionLabel()
        {
            try
            {
                var info = UnityEngine.Object.FindObjectOfType<UIRegionInfo>();
                if (info != null && info.gameObject.activeInHierarchy) { info.SetActive(false); info.SetActive(true); }
            }
            catch { }
        }
    }

    /// <summary>
    /// The game builds the public list from your region's rooms: remember them with your region and ping,
    /// and add the other regions' rooms the scanner found.
    /// </summary>
    [HarmonyPatch(typeof(RoomConnector), nameof(RoomConnector.createPublicRoomInfoList))]
    static class CreatePublicRoomInfoListPatch
    {
        // Other regions' lobbies not seen by a search for this long are left out: after the list was closed for
        // a while they may be gone, and the next search brings back the ones that are still there.
        private const float MaxAge = 90f;

        static void Postfix(RoomConnector __instance)
        {
            // Every patch body is guarded: an exception escaping into the game's code could break or crash it.
            try
            {
                if (Scanner.Converting || !Plugin.Enabled.Value) return;
                Scanner.Connector = __instance;
                var list = __instance.m_PublicRoomInfoList;
                if (list == null) return;
                var conn = __instance.m_Connection;
                var home = Scanner.RegionOf(conn);
                int ping = 0;
                try { ping = conn.m_Client.LoadBalancingPeer.RoundTripTime; } catch { }
                float now = Time.unscaledTime;
                var names = new HashSet<string>();
                foreach (var r in list)
                {
                    if (r == null || r.RoomName == null) continue;
                    names.Add(r.RoomName);
                    Scanner.Rooms[r.RoomName] = new FoundRoom { Info = r, Region = home, Ping = ping, Seen = now };
                }
                // Rooms remembered for this region that the fresh list no longer has are gone.
                var gone = new List<string>();
                foreach (var kv in Scanner.Rooms) if (kv.Value.Region == home && !names.Contains(kv.Key)) gone.Add(kv.Key);
                foreach (var k in gone) Scanner.Rooms.Remove(k);
                int added = 0;
                foreach (var kv in Scanner.Rooms)
                    if (kv.Value.Region != home && now - kv.Value.Seen < MaxAge) { list.Add(kv.Value.Info); added++; }
                Diag.Debug($"list built: {names.Count} in {Plugin.RegionLabel(home)} + {added} from other regions");
            }
            catch (Exception e) { Diag.ErrorOnce("merging the list", e); }
        }
    }

    /// <summary>Picking a lobby from another region: switch region first, then the scanner presses it again.</summary>
    [HarmonyPatch(typeof(UISelectPublicMenu), nameof(UISelectPublicMenu.onClick_LobbyInfoButton))]
    static class LobbyClickPatch
    {
        static bool Prefix(UISelectPublicMenu __instance, int index)
        {
            try
            {
                if (!Plugin.Enabled.Value) return true;
                Scanner.I?.Stop("a lobby was picked");
                var page = __instance.m_PageRoomInfoList;
                int row = index - Scanner.ListTopChoice;
                if (page == null || row < 0 || row >= page.Count || Scanner.Connector == null) return true;
                var info = page[row];
                if (info?.RoomName == null || !Scanner.Rooms.TryGetValue(info.RoomName, out var f)) return true;
                var home = Scanner.RegionOf(Scanner.Connector.m_Connection);
                Diag.Debug($"picked {info.RoomName} ({Plugin.RegionLabel(f.Region)}, your region {Plugin.RegionLabel(home)})");
                if (f.Region == home) return true;   // your region: the game joins as usual
                return !Scanner.BeginJoin(f);
            }
            catch (Exception e)
            {
                Diag.ErrorOnce("lobby click", e);
                return true;
            }
        }
    }

    /// <summary>Puts "region, ping" after the host's name on each row.</summary>
    [HarmonyPatch(typeof(UILobbyListInfo), nameof(UILobbyListInfo.Set))]
    static class LobbyRowPatch
    {
        static void Postfix(UILobbyListInfo __instance, PublicRoomInfo publicRoomInfo)
        {
            try
            {
                Scanner.RowsDrawnAt = Time.unscaledTime;   // a pending join waits for the new region's rows
                if (publicRoomInfo?.RoomName == null || !Plugin.Enabled.Value) return;
                if (!Scanner.Rooms.TryGetValue(publicRoomInfo.RoomName, out var f)) return;
                var text = __instance.nameTextController;
                if (text == null) return;
                var color = f.Ping <= 0 ? "#cccccc" : f.Ping < 120 ? "#7CFC7C" : f.Ping < 220 ? "#FFD84D" : "#FF7A5C";
                var ping = f.Ping > 0 ? f.Ping + " ms" : "?";
                // The row's text box has rich text off; turn it on for the colored label, or fall back to plain text.
                bool rich = false;
                try { var tmp = text.GetTextMeshProUGUI(); if (tmp != null) { tmp.richText = true; rich = true; } } catch { }
                text.SetText(rich
                    ? $"{Escape(publicRoomInfo.HostName)} <size=65%><color={color}>{Plugin.RegionLabel(f.Region)} {ping}</color></size>"
                    : $"{publicRoomInfo.HostName} ({Plugin.RegionLabel(f.Region)} {ping})");
            }
            catch (Exception e) { Diag.ErrorOnce("row label", e); }
        }

        /// <summary>
        /// Host names are typed by other players: with rich text on, a name like "&lt;size=500&gt;" would be drawn
        /// as a tag. TextMeshPro shows everything inside noparse as typed; a name can't close it early because its
        /// own closing tag is broken up.
        /// </summary>
        private static string Escape(string s) => string.IsNullOrEmpty(s)
            ? ""
            : "<noparse>" + System.Text.RegularExpressions.Regex.Replace(s, "</noparse>", "</ noparse>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase) + "</noparse>";
    }
}
