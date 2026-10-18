using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AE2ForTerraria.Content.Items;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using AE2ForTerraria.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoMod.Cil;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Creative;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.UI;

namespace AE2ForTerraria.Systems;

/// <summary>
/// Explicit real-client, single-player journey. The coordinator supplies a copy of the old
/// test world in validation/player-journey/RUN/sandbox.wld and sandbox.twld. This fixture
/// never selects an entry from the user's world/player lists. Input is injected into the
/// actual game update loop; this is deliberately not advertised as an OS mouse macro.
/// </summary>
public sealed class PlayerJourneyRegression : ModSystem
{
    private const string Root = @"C:\Users\ROG\Documents\ChatGPT\ae2 for tr\validation\player-journey";
    private const string FixtureWorld = "AE2ValidationCore20261003";
    private const string FixturePlayer = "AE2JourneyActor";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<object> _checks = new();
    private readonly Queue<(string Name, Action Run, int Delay)> _steps = new();
    private readonly Stopwatch _elapsed = new();
    private string _directory, _worldPath, _playerPath, _capture, _nextText;
    private bool _entered, _prepared, _done, _saving, _reload, _moveRight, _instantOrderObserved;
    private int _menuFrames, _worldFrames, _delay, _clickFrames, _checksPassed, _backgroundClicks;
    private Vector2 _pointer = new(12, 680), _movementStart;
    private Point16 _accessPosition, _drivePosition, _craftPosition;
    private MEDiskDriveEntity _drive;
    private MECraftingTerminalEntity _craft;
    private Dictionary<ItemKey, long> _savedStock;
    private int[] _inventoryBeforeOverlap;
    private bool _enabled;
    private bool _copiedWorld;
    private string _expectedWorldName = FixtureWorld;
    private bool _settingsCaptured, _oldAutoPause, _oldMapEnabled, _oldFullscreen;
    private int _oldWidth, _oldHeight;
    private readonly Dictionary<int, Rectangle> _nativeSlotBounds = new();
    private Rectangle _lastClickBounds;
    private string _lastClickType, _failure;
    private int _driveClicks, _clickGuardWaitFrames, _totalClickGuardWaitFrames;
    private int _overlapInventorySlot = -1, _foregroundClicksBeforeOverlap;
    private int _worldRightFrames;
    private Point16 _worldRightTarget;
    private Vector2? _copiedAnchor;
    private UIElement _clickTarget;
    private int _clickGridType;
    private int _resizeFrames;
    private Vector2 _resizeOrigin, _sizeBeforeResize;
    private int _hudDragFrames;
    private Vector2 _hudDragOrigin, _hudBeforeDrag;
    private IDisposable _browserPreferences;
    private bool _favoriteModifier;
    private int _altInputFrames;

    private delegate void InputHook(Action original);
    private delegate void MainInputHook(Action<Main> original, Main self);
    private delegate void PlayerHook(Action<Player> original, Player player);
    private delegate bool LayerHook(Func<GameInterfaceLayer, bool> original, GameInterfaceLayer layer);
    private delegate string TextHook(Func<string, bool, string> original, string text, bool multiline);
    private delegate void PlayerSaveHook(Action<PlayerFileData, bool> original, PlayerFileData data, bool skipMap);
    private delegate void WorldSaveHook(Action<bool, bool> original, bool cloud, bool resetTime);
    private delegate void NativeSlotDrawHook(Action<SpriteBatch, Item[], int, int, Vector2, Color> original,
        SpriteBatch batch, Item[] inventory, int context, int slot, Vector2 position, Color light);

    public override bool IsLoadingEnabled(Mod mod) => !Main.dedServ
        && !Program.LaunchParameters.ContainsKey("-subworld")
        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AE2_PLAYER_JOURNEY"));

    public override void Load()
    {
        string run = Environment.GetEnvironmentVariable("AE2_PLAYER_JOURNEY");
        if (!Regex.IsMatch(run ?? "", "^[a-z0-9-]{1,50}$"))
            throw new InvalidOperationException("AE2 player journey requires a safe, explicit run identifier.");
        _directory = Path.GetFullPath(Path.Combine(Root, run));
        if (!string.Equals(Path.GetDirectoryName(_directory), Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("AE2 player journey path is outside its whitelist.");
        RequireDirectoryIsReal(_directory);
        _browserPreferences = BrowserPreferences.Isolate(Path.Combine(_directory, "browser-preferences"));
        _worldPath = Path.Combine(_directory, "sandbox.wld");
        _playerPath = Path.Combine(_directory, "actor.plr");
        _copiedWorld = Environment.GetEnvironmentVariable("AE2_JOURNEY_COPIED_WORLD") == "1";
        if (!File.Exists(_worldPath) || !File.Exists(Path.ChangeExtension(_worldPath, ".twld")))
            throw new InvalidOperationException("Copy the independently named validation world and its twld into the run directory first.");
        foreach (string path in new[] { _worldPath, Path.ChangeExtension(_worldPath, ".twld"), _playerPath, Path.ChangeExtension(_playerPath, ".tplr") })
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Journey files must not be symlinks: " + path);
        if (_copiedWorld)
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "snapshot.json")));
            if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_worldPath))) != manifest.RootElement.GetProperty("WorldHash").GetString()
                || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.ChangeExtension(_worldPath, ".twld")))) != manifest.RootElement.GetProperty("ModWorldHash").GetString())
                throw new InvalidOperationException("Copied-world snapshot does not match its manifest.");
        }
        _enabled = true;
        On_Main.Draw += Draw;
        MonoModHooks.Add(Method(typeof(PlayerInput), "UpdateInput"), new InputHook(UpdateInput));
        MonoModHooks.Add(Method(typeof(Main), "DoUpdate_HandleInput"), new MainInputHook((original, self) =>
        {
            original(self);
            // Native HandleInput refreshes keyState after PlayerInput.UpdateInput.
            // Inject modifiers at that actual boundary, only in the isolated world.
            if (_enabled && !_done && !_saving && IsSandbox() && !Main.gameMenu)
            {
                Main.keyState = _favoriteModifier ? new KeyboardState(Keys.LeftAlt) : new KeyboardState();
                if (_favoriteModifier && ++_altInputFrames == 1) Mod.Logger.Info("AE2 JOURNEY ALT: injected after native HandleInput keyboard refresh");
            }
        }));
        MonoModHooks.Add(Method(typeof(GameInterfaceLayer), "Draw"), new LayerHook((original, layer) =>
        {
            bool before = Main.LocalPlayer.mouseInterface;
            bool result = original(layer);
            if (IsSandbox() && _worldRightFrames > 0 && !before && Main.LocalPlayer.mouseInterface)
                Mod.Logger.Info("AE2 JOURNEY POINTER OWNER: " + layer.Name);
            return result;
        }));
        MonoModHooks.Add(Method(typeof(Player), "LookForTileInteractions"), new PlayerHook((original, player) =>
        {
            if (IsSandbox() && _worldRightFrames > 0)
                Mod.Logger.Info("AE2 JOURNEY NATIVE INPUT: " + JsonSerializer.Serialize(new
                {
                    _worldRightFrames, Main.mouseRight, Main.mouseRightRelease, player.controlUseTile, player.releaseUseTile,
                    player.mouseInterface, Main.blockInput, Main.drawingPlayerChat, Player.tileTargetX, Player.tileTargetY,
                    target = _worldRightTarget.ToString(), pointer = Main.MouseWorld.ToString(),
                    captured = AEUiSession.CapturesInput, triggerRight = PlayerInput.Triggers.Current.MouseRight,
                    Main.blockMouse, player.tileInteractAttempted, player.tileInteractionHappened,
                    range = player.IsInTileInteractionRange(Player.tileTargetX, Player.tileTargetY, TileReachCheckSettings.Simple)
                }));
            original(player);
        }));
        MonoModHooks.Modify(Method(typeof(Main), "DoUpdate", typeof(GameTime).MakeByRefType()), KeepSandboxUpdating);
        MonoModHooks.Add(Method(typeof(Main), "GetInputText", typeof(string), typeof(bool)), new TextHook(InputText));
        MonoModHooks.Add(Method(typeof(ItemSlot), "Draw", typeof(SpriteBatch), typeof(Item[]), typeof(int), typeof(int), typeof(Vector2), typeof(Color)),
            new NativeSlotDrawHook((original, batch, inventory, context, slot, position, light) =>
            {
                if (IsSandbox() && context == 0 && ReferenceEquals(inventory, Main.LocalPlayer.inventory) && slot >= 0 && slot < 50)
                {
                    int width = (int)(TextureAssets.InventoryBack.Value.Width * Main.inventoryScale);
                    int height = (int)(TextureAssets.InventoryBack.Value.Height * Main.inventoryScale);
                    _nativeSlotBounds[slot] = new Rectangle((int)position.X, (int)position.Y, width, height);
                }
                original(batch, inventory, context, slot, position, light);
            }));
        // No test window geometry is written into the user's preferences.
        MonoModHooks.Add(Method(typeof(WindowLayout), "Save"), new InputHook(_ => { }));
        MonoModHooks.Add(Method(typeof(Player), "SavePlayer", typeof(PlayerFileData), typeof(bool)),
            new PlayerSaveHook((original, data, skipMap) =>
            {
                if (!SamePath(data?.Path, _playerPath) || data.IsCloudSave)
                    throw new InvalidOperationException("Journey refused to save a player outside the sandbox.");
                original(data, true);
            }));
        MonoModHooks.Add(Method(typeof(WorldFile), "SaveWorld", typeof(bool), typeof(bool)),
            new WorldSaveHook((original, cloud, resetTime) =>
            {
                if (!SamePath(Main.ActiveWorldFileData?.Path, _worldPath) || cloud)
                    throw new InvalidOperationException("Journey refused to save a world outside the sandbox.");
                original(false, resetTime);
            }));
        Main.QueueMainThreadAction(() =>
        {
            _oldWidth = Main.screenWidth; _oldHeight = Main.screenHeight; _oldFullscreen = Main.graphics.IsFullScreen;
            _oldAutoPause = Main.autoPause; _oldMapEnabled = Main.mapEnabled; _settingsCaptured = true;
            Main.SetDisplayMode(1280, 720, false); Main.autoPause = false; Main.mapEnabled = false;
        });
        _elapsed.Start();
    }

    public override void Unload() { On_Main.Draw -= Draw; _enabled = false; _browserPreferences?.Dispose(); _browserPreferences = null; }

    private static MethodInfo Method(Type type, string name, params Type[] parameters) =>
        type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance,
            null, parameters, null) ?? throw new MissingMethodException(type.FullName, name);

    private static void RequireDirectoryIsReal(string path)
    {
        for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Journey paths must exist and must not traverse a junction or symlink: " + directory.FullName);
    }

    private static bool SamePath(string first, string second) => first != null && second != null
        && string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);

    private void KeepSandboxUpdating(ILContext context)
    {
        var cursor = new ILCursor(context);
        int matches = 0;
        while (cursor.TryGotoNext(MoveType.After, instruction => instruction.MatchCall<Game>("get_IsActive")))
        {
            // Single-player normally returns before world ticks when another app has focus.
            // Change only these call sites, only inside the exact isolated world/player pair.
            cursor.EmitDelegate<Func<bool, bool>>(active => active || !Main.gameMenu && IsSandbox());
            matches++;
        }
        if (matches == 0) throw new InvalidOperationException("Could not find the native background-pause checks for the isolated journey.");
        Mod.Logger.Info("AE2 JOURNEY BACKGROUND: guarded native focus checks=" + matches);
    }

    private bool IsSandbox() => _entered && Main.netMode == NetmodeID.SinglePlayer
        && Main.worldName == _expectedWorldName && SamePath(Main.ActiveWorldFileData?.Path, _worldPath)
        && SamePath(Main.ActivePlayerFileData?.Path, _playerPath) && Main.LocalPlayer.name == FixturePlayer;

    private void Draw(On_Main.orig_Draw original, Main self, GameTime time)
    {
        original(self, time);
        if (!_enabled) return;
        if (_failure != null)
        {
            try { if (IsSandbox()) Capture("failure"); }
            catch (Exception screenshotError) { Mod.Logger.Error("AE2 JOURNEY failure screenshot unavailable", screenshotError); }
            finally
            {
                WriteResult(false, _failure); _failure = null;
                RestoreSettings(); Main.instance.Exit();
            }
            return;
        }
        if (_done) return;
        try
        {
            if (_elapsed.Elapsed > TimeSpan.FromMinutes(7)) throw new TimeoutException("Real-client journey exceeded its watchdog.");
            if (_capture != null && !Main.gameMenu && IsSandbox()) Capture(_capture);
            if (!_entered && !_saving && Main.gameMenu && Main.menuMode == 0 && StorageSystem.Ready
                && !(bool)(typeof(ModLoader).GetField("isLoading", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) ?? true)
                && ++_menuFrames > 120) EnterWorld();
        }
        catch (Exception error) { Fail(error); }
    }

    private void EnterWorld()
    {
        WorldFileData world = WorldFile.GetAllMetadata(_worldPath, false);
        if (world == null || !_copiedWorld && world.Name != FixtureWorld || !SamePath(world.Path, _worldPath))
            throw new InvalidOperationException("Journey requires the known independent validation world, never a user's world.");
        _expectedWorldName = world.Name;
        PlayerFileData data;
        if (_reload)
        {
            data = Player.GetFileData(_playerPath, false);
            if (data?.Player?.name != FixturePlayer || !SamePath(data.Path, _playerPath))
                throw new InvalidOperationException("Saved sandbox player was not restored.");
        }
        else
        {
            var player = new Player { name = FixturePlayer, statLifeMax = 500, statLife = 500, selectedItem = 0 };
            player.savedPerPlayerFieldsThatArentInThePlayerClass = new Player.SavedPlayerDataWithAnnoyingRules();
            CreativePowerManager.Instance.ResetDataForNewPlayer(player);
            PlayerLoader.SetStartInventory(player, PlayerLoader.GetStartingItems(player, Array.Empty<Item>()));
            for (int i = 0; i < 58; i++) player.inventory[i] = new Item();
            player.inventory[0] = new Item(ItemID.CopperPickaxe);
            player.inventory[20] = new Item(ModContent.ItemType<MEDisk1K>());
            player.inventory[21] = new Item(ItemID.Wood, 100);
            player.inventory[22] = new Item(ItemID.Gel, 12);
            player.inventory[23] = new Item(ItemID.WorkBench);
            player.inventory[44] = new Item(ItemID.StoneBlock, 17);
            data = new PlayerFileData(_playerPath, false)
            { Player = player, Metadata = FileMetadata.FromCurrentSettings(FileType.Player) };
        }
        data.SetAsActive(); world.SetAsActive(); Main.netMode = NetmodeID.SinglePlayer;
        Main.mapEnabled = false; Main.autoPause = false; Main.menuMode = 10;
        _entered = true; _prepared = false; _worldFrames = 0;
        Mod.Logger.Info("AE2 JOURNEY ENTER: " + _worldPath + "; player=" + _playerPath + "; reload=" + _reload);
        WorldGen.playWorld();
    }

    private void UpdateInput(Action original)
    {
        original();
        if (!_enabled || _done || _saving || !IsSandbox() || Main.gameMenu) return;
        PlayerInput.Triggers.Current.Reset();
        PlayerInput.Triggers.Current.Right = _moveRight;
        if (_clickFrames > 0 && _clickTarget != null)
        {
            _pointer = _clickTarget.GetDimensions().Center();
            if (_clickTarget is UIItemGrid grid && _clickGridType > 0)
            {
                int index = grid.KeyMode ? Field<List<ItemKey>>(grid, "_keys").FindIndex(key => key.Type == _clickGridType)
                    : Field<List<int>>(grid, "_items").IndexOf(_clickGridType);
                int row = index / grid.Columns - Field<int>(grid, "_rowOffset");
                var bounds = grid.GetDimensions();
                _pointer = new Vector2(bounds.X + index % grid.Columns * grid.CellSize + grid.CellSize / 2,
                    bounds.Y + row * grid.CellSize + grid.CellSize / 2);
            }
        }
        bool left = _clickFrames == 3 || _clickFrames == 2;
        if (_resizeFrames > 0)
        {
            int phase = 28 - _resizeFrames;
            _pointer = _resizeOrigin + (phase < 2 ? Vector2.Zero : new Vector2(phase % 2 == 0 ? 24 : 12, phase % 2 == 0 ? 18 : 8));
            left = _resizeFrames > 1;
            _resizeFrames--;
        }
        if (_hudDragFrames > 0)
        {
            int phase = 12 - _hudDragFrames;
            _pointer = _hudDragOrigin + (phase < 2 ? Vector2.Zero : new Vector2(-120, 50));
            left = _hudDragFrames > 1; _hudDragFrames--;
        }
        // Native SetState/ResetState reserves 200 ms for opening-click suppression. Wait for
        // that actual timer to expire; never clear it or mistake the suppressed click for a bug.
        if (_clickFrames == 4 && (ClickGuardMilliseconds() > 0 || !Main.hasFocus))
        {
            if (_clickGuardWaitFrames++ == 0)
                Mod.Logger.Info("AE2 JOURNEY WAIT UI TRANSITION: " + JsonSerializer.Serialize(DiagnosticState()));
            _totalClickGuardWaitFrames++;
        }
        else
        {
            if (_clickGuardWaitFrames > 0)
            {
                Mod.Logger.Info("AE2 JOURNEY UI READY: waitedFrames=" + _clickGuardWaitFrames
                    + "; guardMs=" + ClickGuardMilliseconds() + "; hasFocus=" + Main.hasFocus);
                _clickGuardWaitFrames = 0;
            }
            if (_clickFrames > 0 && --_clickFrames == 0) _clickTarget = null;
        }
        PlayerInput.Triggers.Current.MouseLeft = left;
        bool right = _worldRightFrames == 3 || _worldRightFrames == 2;
        if (_worldRightFrames > 0)
        {
            _pointer = Vector2.Transform(new Vector2(_worldRightTarget.X * 16 + 8, _worldRightTarget.Y * 16 + 8)
                - Main.screenPosition, Main.GameViewMatrix.TransformationMatrix) / Main.UIScale;
            _worldRightFrames--;
        }
        PlayerInput.Triggers.Current.MouseRight = right;
        PlayerInput.Triggers.Update();
        PlayerInput.MouseInfoOld = PlayerInput.MouseInfo;
        PlayerInput.MouseInfo = new MouseState((int)(_pointer.X * Main.UIScale), (int)(_pointer.Y * Main.UIScale), 0,
            left ? ButtonState.Pressed : ButtonState.Released, ButtonState.Released, right ? ButtonState.Pressed : ButtonState.Released,
            ButtonState.Released, ButtonState.Released);
        PlayerInput.MouseX = PlayerInput.MouseInfo.X; PlayerInput.MouseY = PlayerInput.MouseInfo.Y;
        PlayerInput.UpdateMainMouse();
        Main.mouseLeft = left; Main.mouseRight = right; Main.mouseMiddle = false;
        PlayerInput.CacheZoomableValues();
    }

    private string InputText(Func<string, bool, string> original, string text, bool multiline)
    {
        if (_nextText == null || !IsSandbox() || !AEUiSession.KeyboardCaptured) return original(text, multiline);
        string value = _nextText; _nextText = null; return value;
    }

    public override void PostUpdateEverything()
    {
        if (!_enabled || _done || _saving || !IsSandbox() || Main.gameMenu || WorldGen.noMapUpdate) return;
        try
        {
            if (_copiedWorld && _copiedAnchor.HasValue)
            { Main.LocalPlayer.Center = _copiedAnchor.Value; Main.LocalPlayer.velocity = Vector2.Zero; }
            Main.LocalPlayer.statLife = Main.LocalPlayer.statLifeMax2;
            Main.LocalPlayer.immune = true; Main.LocalPlayer.immuneTime = 60;
            if (++_worldFrames < 90) return;
            if (!_prepared)
            {
                _prepared = true;
                if (_copiedWorld) BuildCopiedWorldSteps();
                else if (_reload) BuildReloadSteps(); else { PrepareArena(); BuildSteps(); }
            }
            if (--_delay > 0 || _capture != null || _clickFrames > 0 || _resizeFrames > 0 || _hudDragFrames > 0 || _worldRightFrames > 0 || _steps.Count == 0) return;
            var step = _steps.Dequeue();
            long start = Stopwatch.GetTimestamp();
            step.Run();
            double milliseconds = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            _checks.Add(new { step = step.Name, frame = _worldFrames, milliseconds });
            Mod.Logger.Info("AE2 JOURNEY STEP: " + step.Name + " (" + milliseconds.ToString("F3") + " ms)");
            _delay = step.Delay;
        }
        catch (Exception error) { Fail(error); }
    }

    private void PrepareArena()
    {
        Require(IsSandbox() && !Main.gameMenu, "real world and isolated player are active");
        var notices = typeof(InGameNotificationsTracker).GetField("_notifications", BindingFlags.Static | BindingFlags.NonPublic)
            ?.GetValue(null) as System.Collections.IEnumerable;
        if (notices != null)
            foreach (var notice in notices) Mod.Logger.Info("AE2 JOURNEY ENTRY NOTICE: " + notice.GetType().FullName);
        // Newly constructed test actors receive large mod introduction cards in the native
        // notification layer. They cover the world's mouse target; exclude this fixture-only
        // obstruction before testing machines, without changing mods or personal preferences.
        InGameNotificationsTracker.Clear();
        int x = Math.Clamp(Main.spawnTileX + 60, 100, Main.maxTilesX - 140);
        int floor = Math.Clamp(Main.spawnTileY - 5, 100, Main.maxTilesY - 100);
        foreach (var pair in TileEntity.ByPosition.Where(pair => pair.Key.X >= x - 20 && pair.Key.X <= x + 100
            && pair.Key.Y >= floor - 16 && pair.Key.Y <= floor + 1).ToArray())
        { TileEntity.ByPosition.Remove(pair.Key); TileEntity.ByID.Remove(pair.Value.ID); }
        for (int i = x - 20; i <= x + 100; i++)
            for (int j = floor - 16; j <= floor + 1; j++)
            {
                Tile tile = Main.tile[i, j]; tile.ClearEverything();
                if (j >= floor) { tile.HasTile = true; tile.TileType = TileID.Stone; }
            }
        _accessPosition = new Point16(x + 8, floor - 2);
        _drivePosition = new Point16(x + 10, floor - 2);
        _craftPosition = new Point16(x + 12, floor - 2);
        PlaceMachine(_accessPosition, ModContent.TileType<MEAccessTerminalTile2x2>());
        PlaceMachine(_drivePosition, ModContent.TileType<MEDiskDriveTile2x2>());
        PlaceMachine(_craftPosition, ModContent.TileType<MECraftingTerminalTile2x2>());
        _drive = (MEDiskDriveEntity)TileEntity.ByID[ModContent.GetInstance<MEDiskDriveEntity>().Place(_drivePosition.X, _drivePosition.Y)];
        _craft = (MECraftingTerminalEntity)TileEntity.ByID[ModContent.GetInstance<MECraftingTerminalEntity>().Place(_craftPosition.X, _craftPosition.Y)];
        MENetwork.InvalidateCache();
        Main.LocalPlayer.position = new Vector2((x + 3) * 16, floor * 16 - Main.LocalPlayer.height);
        Main.LocalPlayer.velocity = Vector2.Zero;
        Main.LocalPlayer.GetModPlayer<MEPlayer>().MagnetEnabled = false;
        NetworkRegistry.TryClaim(MENetwork.CanonicalAnchor(MENetwork.Collect(_accessPosition.X, _accessPosition.Y)), FixturePlayer);
        Require(MENetwork.CollectStorage(_accessPosition.X, _accessPosition.Y).Contains(_drive), "placed machines form an actual world network");
        _capture = "01-world-arena";
    }

    private static void PlaceMachine(Point16 position, int type)
    {
        for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 2; dy++)
        {
            Tile tile = Main.tile[position.X + dx, position.Y + dy]; tile.ClearEverything();
            tile.HasTile = true; tile.TileType = (ushort)type;
            tile.TileFrameX = (short)(dx * 18); tile.TileFrameY = (short)(dy * 18);
        }
    }

    private void Step(string name, Action action, int delay = 30) => _steps.Enqueue((name, action, delay));

    private void BuildCopiedWorldSteps()
    {
        Require(IsSandbox() && _copiedWorld, "interaction inspection uses only the verified world copy and isolated actor");
        InGameNotificationsTracker.Clear();
        Main.LocalPlayer.GetModPlayer<MEPlayer>().MagnetEnabled = false;
        _drive = TileEntity.ByPosition.Values.OfType<MEDiskDriveEntity>().FirstOrDefault()
            ?? throw new InvalidOperationException("Copied world contains no disk drive entity.");
        _drivePosition = _drive.Position;
        _savedStock = _drive.Ledger.Entries.ToDictionary(pair => pair.Key, pair => pair.Value);
        Mod.Logger.Info("AE2 COPIED WORLD DRIVE: types=" + _drive.Ledger.StoredTypeCount + "/" + _drive.Ledger.MaxTypes
            + "; amount=" + _drive.Ledger.StoredTotalAmount + "; disks=" + _drive.Disks.Count(item => !item.IsAir));
        int access = ModContent.TileType<MEAccessTerminalTile>(), craft = ModContent.TileType<MECraftingTerminalTile>(),
            driveType = ModContent.TileType<MEDiskDriveTile>();
        var machines = MENetwork.Collect(_drivePosition.X, _drivePosition.Y)
            .Select(point => MachineTiles.Origin(point.X, point.Y)).Distinct()
            .Where(point => { int type = MachineTiles.CanonicalType(Main.tile[point.X, point.Y].TileType);
                return type == access || type == craft || type == driveType; }).Take(6).ToArray();
        Require(machines.Length > 0, "existing saved machine anchors are discovered without rebuilding the network");
        foreach (var origin in machines)
        {
            int type = MachineTiles.CanonicalType(Main.tile[origin.X, origin.Y].TileType);
            int size = MachineTiles.IsLarge(Main.tile[origin.X, origin.Y].TileType) ? 2 : 1;
            for (int dx = 0; dx < size; dx++) for (int dy = 0; dy < size; dy++)
            {
                var cell = new Point16(origin.X + dx, origin.Y + dy);
                Step("prepare copied-world native target " + cell, () =>
                {
                    UISystem.CloseAll(); Main.playerInventory = false;
                    _copiedAnchor = new Vector2(cell.X * 16 - 32, cell.Y * 16 + 8);
                    Main.LocalPlayer.Center = _copiedAnchor.Value;
                });
                Step("press copied-world native MouseRight " + cell, () =>
                { _worldRightTarget = cell; _worldRightFrames = 4; });
                Step("verify copied-world machine " + cell, () =>
                {
                    Require(type == driveType ? ReferenceEquals(UISystem.SecondaryInterface.CurrentState, UISystem.DriveState)
                        : ReferenceEquals(UISystem.Interface.CurrentState, UISystem.TerminalState),
                        "saved machine opens through native mouse input at " + cell);
                    if (type != driveType) Require(UISystem.ActiveTerminalViewMode == (type == access ? TerminalViewMode.Access : TerminalViewMode.Craft),
                        "saved terminal selects its correct access/crafting mode");
                });
            }
        }
        Step("finish copied-world inspection without saving", () =>
        {
            Require(_drive.Ledger.Entries.Count == _savedStock.Count && _savedStock.All(pair => _drive.Ledger.GetAmount(pair.Key) == pair.Value),
                "opening saved machines does not alter the copied inventory");
            UISystem.CloseAll(); _done = true; WriteResult(true, null); RestoreSettings(); Main.instance.Exit();
        });
    }

    private void BuildSteps()
    {
        Step("open JEI and begin movement", () =>
        {
            UISystem.OpenSearch(); PositionWindow(UISystem.SearchState);
            Require(!AEUiSession.KeyboardCaptured && !Main.blockInput, "JEI opens without taking the movement keyboard");
            _movementStart = Main.LocalPlayer.position; _moveRight = true;
        }, 65);
        Step("check JEI movement across AE blocks", () =>
        {
            _moveRight = false;
            Require(Main.LocalPlayer.position.X > _movementStart.X + 96, "normal player physics moves through non-solid AE machines with JEI open");
            _capture = "02-jei-moving";
            Click(Field<UIElement>(UISystem.SearchState, "_searchPanel"));
        });
        Step("enter localized search through game text input", () =>
        {
            Require(AEUiSession.KeyboardCaptured, "clicking the search field captures text input");
            _nextText = Lang.GetItemNameValue(ItemID.Wood);
        });
        Step("check search results and click wood", () =>
        {
            Require(Field<string>(UISystem.SearchState, "_query") == Lang.GetItemNameValue(ItemID.Wood), "native text input reaches JEI search");
            var grid = Field<UIItemGrid>(UISystem.SearchState, "_grid");
            Require(Field<List<int>>(grid, "_items").Contains(ItemID.Wood), "localized search finds the real Wood item");
            ClickGrid(grid, ItemID.Wood);
        });
        Step("check actual JEI details", () =>
        {
            Require(Field<UIBrowserDetails>(UISystem.SearchState, "_details").IsOpen, "item click opens JEI details during real gameplay");
            Require(!AEUiSession.KeyboardCaptured, "item click leaves text entry");
            _capture = "03-jei-item-details";
        });
        Step("return from the first JEI detail", () =>
        {
            Click(Field<UIButton>(Field<UIBrowserDetails>(UISystem.SearchState, "_details"), "_back"));
        });
        Step("reopen JEI with persistent history and a clean catalogue", () =>
        {
            Require(!Field<UIBrowserDetails>(UISystem.SearchState, "_details").IsOpen, "first catalogue selection returns directly without a stale item page");
            UISystem.CloseAll(); BrowserHistory.Reset(); UISystem.OpenSearch(); PositionWindow(UISystem.SearchState);
            Require(!Field<UIBrowserDetails>(UISystem.SearchState, "_details").IsOpen
                && BrowserHistory.Entries.Any(visit => visit.Content == BrowserContent.Name(ItemID.Wood)),
                "reopening JEI starts at the catalogue and reloads its previous visit from disk");
            _favoriteModifier = true;
            BrowserFavorites.AlwaysVisible = false; // Exercise both eye modes explicitly; normal new profiles default to always visible.
            ClickGrid(Field<UIItemGrid>(UISystem.SearchState, "_grid"), ItemID.Wood);
        });
        Step("check native Alt favorite and star marker", () =>
        {
            _favoriteModifier = false;
            Require(BrowserFavorites.Contains(ItemID.Wood) && !Field<UIBrowserDetails>(UISystem.SearchState, "_details").IsOpen,
                "native Alt click favorites a JEI item without opening its detail");
            Main.playerInventory = true; _capture = "03b-alt-favorite-mark";
        });
        Step("click the pinned tree eye", () =>
        {
            UISystem.CloseAll();
            Require(FavoriteTreeHud.Visible && !BrowserFavorites.AlwaysVisible, "favorited items appear in the inventory-only tree HUD");
            var eye = (Rectangle)typeof(FavoriteTreeHud).GetField("_eye", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            ClickPoint(eye.Center.ToVector2());
        });
        Step("check always-visible pinned tree outside AE windows", () =>
        {
            Require(BrowserFavorites.AlwaysVisible, "native eye click switches pinned trees to always-visible mode");
            UISystem.CloseAll(); Main.playerInventory = false;
            Require(FavoriteTreeHud.Visible, "pinned tree remains visible after closing JEI and the native inventory");
            _capture = "03c-pinned-tree-always";
        });
        Step("drag the pinned tree through native input", () =>
        {
            _hudBeforeDrag = FavoriteTreeHud.Bounds.Location.ToVector2();
            _hudDragOrigin = _hudBeforeDrag + new Vector2(30, 11); _hudDragFrames = 12;
        });
        Step("check dragged pinned tree and saved position", () =>
        {
            var position = FavoriteTreeHud.Bounds.Location.ToVector2();
            Require(position.X < _hudBeforeDrag.X - 80 && position.Y > _hudBeforeDrag.Y + 20,
                "native header drag moves the pinned tree without an active AE window");
            BrowserFavorites.Reset();
            Require(BrowserFavorites.Position.HasValue && Vector2.Distance(BrowserFavorites.Position.Value, position) < 2,
                "pinned tree drag position survives a preference file reload");
            _capture = "03d-pinned-tree-dragged";
        });
        Step("click eye without any AE window", () =>
        {
            var eye = (Rectangle)typeof(FavoriteTreeHud).GetField("_eye", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            ClickPoint(eye.Center.ToVector2());
        });
        Step("reload pinned visibility and favorite state", () =>
        {
            Require(!BrowserFavorites.AlwaysVisible && !FavoriteTreeHud.Visible, "eye remains interactive without an AE window and hides inventory-only trees");
            BrowserFavorites.Reset(); Main.playerInventory = true;
            Require(BrowserFavorites.Contains(ItemID.Wood) && !BrowserFavorites.AlwaysVisible && FavoriteTreeHud.Visible,
                "favorite and eye settings survive disk reload in the real world session");
        });
        Step("prepare native world right-click", () =>
        {
            UISystem.CloseAll(); Main.playerInventory = false;
            Main.LocalPlayer.Center = new Vector2(_drivePosition.X * 16 - 32, _drivePosition.Y * 16 + 8);
            Main.LocalPlayer.velocity = Vector2.Zero;
        });
        Step("inject native world right-click", () =>
        { _worldRightTarget = _drivePosition; _worldRightFrames = 4; });
        Step("verify native world dispatch", () =>
        {
            Require(ReferenceEquals(UISystem.SecondaryInterface.CurrentState, UISystem.DriveState),
                "injected native MouseRight opens a machine without calling its RightClick callback");
        });
        Step("open actual disk drive", () =>
        {
            UISystem.CloseAll(); RightClick(_drivePosition); PositionWindow(UISystem.DriveState);
            Require(ReferenceEquals(UISystem.SecondaryInterface.CurrentState, UISystem.DriveState), "world drive right-click opens the drive window");
            Require(Main.playerInventory && !Descendants(UISystem.DriveState).OfType<UIPlayerInventoryPanel>().Any(),
                "drive opens native inventory and contains no duplicate backpack");
            var slot = Field<List<UIItemSlotElement>>(UISystem.DriveState, "_slots")[0];
            slot.OnLeftMouseDown += (_, _) => Mod.Logger.Info("AE2 JOURNEY DISK DOWN: " + JsonSerializer.Serialize(DiagnosticState()));
            slot.OnLeftClick += (_, _) =>
            {
                _driveClicks++;
                Mod.Logger.Info("AE2 JOURNEY DISK CLICK: " + JsonSerializer.Serialize(DiagnosticState()));
            };
        });
        Step("pick disk from uncovered native inventory", () =>
        {
            Require(_nativeSlotBounds.TryGetValue(20,out var bounds) && !AEUiSession.CoversPoint(bounds.Center.ToVector2()),
                "the drawn native inventory disk slot is outside the drive window");
            _pointer=bounds.Center.ToVector2();_clickTarget=null;_clickFrames=4;
        });
        Step("put native inventory disk into drive", () =>
        {
            Require(Main.mouseItem.type==ModContent.ItemType<MEDisk1K>() && Main.LocalPlayer.inventory[20].IsAir,
                "native inventory mouse click picks up disk without an AE-owned input override");
            Click(Field<List<UIItemSlotElement>>(UISystem.DriveState, "_slots")[0]);
        });
        Step("verify disk insertion", () =>
        {
            Require(_drive.Disks[0].type == ModContent.ItemType<MEDisk1K>() && Main.mouseItem.IsAir
                && Main.LocalPlayer.inventory[20].IsAir, "native inventory pickup and drive slot click install exactly one disk");
            Require(_drive.Ledger.MaxTypes == 32, "installed first-tier disk exposes thirty-two identities");
            _capture = "04-drive-installed";
        });
        Step("open access terminal and deposit cursor", () =>
        {
            UISystem.CloseAll(); RightClick(_accessPosition); PositionWindow(UISystem.TerminalState);
            Require(UISystem.ActiveTerminalViewMode == TerminalViewMode.Access && Main.playerInventory,
                "access terminal opens stored items alongside native inventory");
            NativePick(21); Click(Field<UIItemGrid>(UISystem.TerminalState, "_grid"));
        });
        Step("verify cursor deposit and prepare native shift deposit", () =>
        {
            Require(_drive.Ledger.GetAmount(ItemKey.Crafted(ItemID.Wood)) == 100 && Main.mouseItem.IsAir,
                "cursor deposit preserves all one hundred Wood items");
            _pointer = new Vector2(20, 400);
        });
        Step("native inventory shift deposit", () =>
        {
            Require(!AEUiSession.CapturesInput, "native inventory outside the terminal remains accessible");
            Require(Main.LocalPlayer.GetModPlayer<MEPlayer>().ShiftClickSlot(Main.LocalPlayer.inventory, 0, 22),
                "native ModPlayer shift-click path accepts uncovered inventory");
            Require(_drive.Ledger.GetAmount(ItemKey.Crafted(ItemID.Gel)) == 12 && Main.LocalPlayer.inventory[22].IsAir,
                "shift deposit transfers the actual Gel stack once");
            Field<UIItemGrid>(UISystem.TerminalState, "_grid").OnActivated(ItemKey.Crafted(ItemID.Wood));
            Click(Field<UIElement>(UISystem.TerminalState, "_quantityButton"));
        });
        Step("withdraw selected quantity", () =>
        {
            Require(Field<long>(UISystem.TerminalState, "_orderQuantity") == 10, "quantity button changes withdrawal quantity to ten");
            Click(Field<UIElement>(UISystem.TerminalState, "_takeButton"));
        });
        Step("check withdrawal and start terminal movement", () =>
        {
            Require(_drive.Ledger.GetAmount(ItemKey.Crafted(ItemID.Wood)) == 90 && InventoryAmount(ItemID.Wood) == 10,
                "terminal withdrawal conserves inventory and storage quantities");
            Require(Field<UIElement>(UISystem.TerminalState, "_playerInventory").Parent == null,
                "access terminal has no duplicate embedded backpack");
            _capture = "05-access-native-inventory";
            _pointer = new Vector2(20, 680); _movementStart = Main.LocalPlayer.position; _moveRight = true;
        }, 35);
        Step("resize access terminal through actual held input", () =>
        {
            _moveRight = false;
            var state = UISystem.TerminalState;
            var grip = (UIResizeGrip)typeof(MovableWindowState).GetField("_grip",PrivateInstance).GetValue(state);
            _resizeOrigin = grip.GetDimensions().Center();
            _sizeBeforeResize = new Vector2(state.Layout.W,state.Layout.H);
            grip.OnLeftMouseDown += (_, _) => Mod.Logger.Info("AE2 JOURNEY RESIZE DOWN: " + JsonSerializer.Serialize(DiagnosticState()));
            grip.OnLeftMouseUp += (_, _) => Mod.Logger.Info("AE2 JOURNEY RESIZE UP: " + JsonSerializer.Serialize(DiagnosticState()));
            Mod.Logger.Info("AE2 JOURNEY RESIZE START: " + JsonSerializer.Serialize(DiagnosticState()));
            _resizeFrames = 28;
        });
        Step("check live resize and preserve its contents", () =>
        {
            Require(UISystem.TerminalState.Layout.W > _sizeBeforeResize.X && UISystem.TerminalState.Layout.H > _sizeBeforeResize.Y,
                "actual corner drag resizes the access terminal while world updates continue");
            Require(_drive.Ledger.GetAmount(ItemKey.Crafted(ItemID.Wood))==90 && InventoryAmount(ItemID.Wood)==10,
                "corner drag preserves inventory and network quantities");
            _capture = "05b-access-corner-resize";
        });
        Step("check terminal movement and open crafting", () =>
        {
            _moveRight = false;
            Require(Main.LocalPlayer.position.X > _movementStart.X + 32, "character continues moving with the access terminal open");
            UISystem.CloseAll();
            var pinnedStock = PinnedInventory.Snapshot();
            Require(pinnedStock != null && pinnedStock.GetValueOrDefault(ItemKey.Crafted(ItemID.Wood)) == 90,
                "closed access terminal continues supplying live network inventory to pinned trees");
            Main.LocalPlayer.position = new Vector2(_craftPosition.X * 16, _craftPosition.Y * 16 - 12);
            Main.LocalPlayer.velocity = Vector2.Zero;
            RightClick(_craftPosition); PositionWindow(UISystem.TerminalState);
        });
        Step("inspect unified catalogue and select stored Wood", () =>
        {
            var grid=Field<UIItemGrid>(UISystem.TerminalState,"_grid");
            Require(Field<UIElement>(UISystem.TerminalState,"_depositButton").Parent!=null
                && grid.CountProvider(ItemKey.Crafted(ItemID.Wood))==90,
                "crafting terminal exposes actual stock and deposit alongside recipes");
            _capture="05c-unified-terminal-catalogue";
            ClickGrid(grid,ItemID.Wood);
        });
        Step("check ordinary stored-item click and return to catalogue", () =>
        {
            var details=Field<UIBrowserDetails>(UISystem.TerminalState,"_info");
            Require(details.IsOpen && details.SelectedKey.Type==ItemID.Wood
                && _drive.Ledger.GetAmount(ItemKey.Crafted(ItemID.Wood))==90 && InventoryAmount(ItemID.Wood)==10,
                "native ordinary left click opens the recipe child page without withdrawing any stock");
            Click(Field<UIElement>(details,"_back"));
        });
        Step("install station through recipe-page slot", () =>
        {
            Require(UISystem.ActiveTerminalViewMode == TerminalViewMode.Craft
                && Field<UIElement>(UISystem.TerminalState, "_stationsButton").Parent == null,
                "crafting opens its recipe page with no separate station tab");
            var stations = Field<UIStationGrid>(UISystem.TerminalState, "_stationGrid");
            var slot = Descendants(stations).OfType<UIItemSlotElement>().First();
            NativePick(23); Click(slot);
        });
        Step("check installed station and select recipe", () =>
        {
            Require(_craft.Stations.Any(item => item.type == ItemID.WorkBench) && Main.mouseItem.IsAir,
                "real station-slot click installs the native Work Bench");
            Field<UIItemGrid>(UISystem.TerminalState, "_grid").OnActivated(ItemKey.Crafted(ItemID.WoodenSword));
        }, 60);
        Step("place crafting order from detail action", () =>
        {
            Require(Field<UIBrowserDetails>(UISystem.TerminalState, "_info").IsOpen,
                "crafting recipe detail opens in the real terminal");
            _capture = "06-crafting-station-and-recipe";
            var orderButton = Field<UIElement>(UISystem.TerminalState, "_orderButton");
            // Observe after the real handler, before another world update can run the queue.
            orderButton.OnLeftClick += (_, _) => _instantOrderObserved =
                _drive.Ledger.GetAmount(ItemKey.Crafted(ItemID.WoodenSword)) > 0
                && UIStatus.Active().Any(line => line.Text.StartsWith("合成完成"));
            Click(orderButton);
        }, 10);
        Step("check actual craft execution and task page", () =>
        {
            Require(_instantOrderObserved,
                "the actual order click completes and reports its output before returning");
            Require(_drive.Ledger.GetAmount(ItemKey.Crafted(ItemID.WoodenSword)) >= 1,
                "completed crafting output remains in storage after the click");
            Require(_drive.Ledger.GetAmount(ItemKey.Crafted(ItemID.Wood)) < 90,
                "craft execution consumes stored materials");
            Require(UIStatus.Active().Any(line=>line.Text.StartsWith("合成完成")),
                "actual crafting click displays its completion toast immediately");
            _capture = "06b-instant-craft-complete";
            Click(Field<UIElement>(UISystem.TerminalState, "_tasksButton"));
        });
        Step("check and capture task page", () =>
        {
            Require(Field<UIElement>(UISystem.TerminalState, "_management").Parent != null,
                "tasks remain reachable after stations merge into recipes");
            _capture = "07-task-management";
        });
        Step("prepare visible background catalogue", () =>
        {
            UISystem.CloseAll(); RightClick(_accessPosition);
            var terminal = UISystem.TerminalState;
            terminal.Layout.W = Math.Min(1100, MovableWindowState.ViewportSize.X - 24);
            terminal.Layout.H = 560; terminal.Layout.X = 0; terminal.Layout.Y = 0;
            terminal.OnActivate(); terminal.Recalculate();
            Require(Field<UIItemGrid>(terminal, "_grid").Parent != null, "background terminal catalogue is attached for the overlap test");
            Field<UIItemGrid>(UISystem.TerminalState, "_grid").OnLeftClick += (_, _) => _backgroundClicks++;
        });
        Step("find a real foreground and backpack overlap", () =>
        {
            RightClick(_drivePosition);
            var drive = UISystem.DriveState;
            drive.Layout.W = drive.Layout.MinW; drive.Layout.H = drive.Layout.MinH;
            drive.OnActivate(); drive.Recalculate();
            if (FindOverlapInventorySlot() >= 0) return;
            // Keep production minimum sizes and the user's UI scale. A taller test viewport
            // is used only when no genuine three-way overlap fits in the compact viewport.
            Main.SetDisplayMode(1280, 960, false);
            _nativeSlotBounds.Clear();
            UISystem.TerminalState.OnActivate(); UISystem.TerminalState.Recalculate();
            drive.OnActivate(); drive.Recalculate();
            Mod.Logger.Info("AE2 JOURNEY OVERLAP VIEWPORT: 1280x960; awaiting fresh native inventory drawing");
        });
        Step("place foreground disk over terminal and native backpack", () =>
        {
            _overlapInventorySlot = FindOverlapInventorySlot();
            Require(_overlapInventorySlot >= 0, "a drawn native backpack slot, visible terminal grid, and movable foreground disk can overlap");
            Require(IsSandbox() && Main.LocalPlayer.inventory[44].type == ItemID.StoneBlock
                && Main.LocalPlayer.inventory[44].stack == 17, "overlap sentinel belongs only to the isolated actor");
            if (_overlapInventorySlot != 44)
            {
                Require(Main.LocalPlayer.inventory[_overlapInventorySlot].IsAir, "overlap sentinel moves into an empty isolated inventory slot");
                Main.LocalPlayer.inventory[_overlapInventorySlot] = Main.LocalPlayer.inventory[44];
                Main.LocalPlayer.inventory[44] = new Item();
            }
            var target = _nativeSlotBounds[_overlapInventorySlot].Center.ToVector2();
            var drive = UISystem.DriveState;
            var slot = Field<List<UIItemSlotElement>>(drive, "_slots")[0];
            Vector2 offset = slot.GetDimensions().Center() - new Vector2(drive.Layout.X, drive.Layout.Y);
            drive.Layout.X = target.X - offset.X; drive.Layout.Y = target.Y - offset.Y;
            drive.OnActivate(); drive.Recalculate();
            Require(slot.GetDimensions().ToRectangle().Contains(target.ToPoint()),
                "foreground disk slot covers the same native backpack and terminal point");
            _pointer = target;
        });
        Step("click foreground disk over other interfaces", () =>
        {
            Require(ReferenceEquals(AEUiSession.FocusedInterface, UISystem.SecondaryInterface), "drive window owns foreground input");
            var grid = Field<UIItemGrid>(UISystem.TerminalState, "_grid");
            var slot = Field<List<UIItemSlotElement>>(UISystem.DriveState, "_slots")[0];
            Point point = _pointer.ToPoint();
            Require(_nativeSlotBounds[_overlapInventorySlot].Contains(point) && grid.Parent != null && grid.GetDimensions().ToRectangle().Contains(point)
                && slot.GetDimensions().ToRectangle().Contains(point), "all three live interaction rectangles overlap the injected click");
            _inventoryBeforeOverlap = Main.LocalPlayer.inventory.Select(item => HashCode.Combine(item.type, item.stack, item.prefix)).ToArray();
            _foregroundClicksBeforeOverlap = _driveClicks;
            _lastClickBounds = slot.GetDimensions().ToRectangle(); _lastClickType = "three-way-overlap";
            _clickTarget = null;
            _clickFrames = 4;
        });
        Step("check no input fallthrough and save", () =>
        {
            Require(_driveClicks == _foregroundClicksBeforeOverlap + 1, "the foreground disk receives exactly one actual click during the overlap test");
            Require(_backgroundClicks == 0, "foreground disk click does not reach the covered terminal grid");
            Require(_inventoryBeforeOverlap.SequenceEqual(Main.LocalPlayer.inventory.Select(item => HashCode.Combine(item.type, item.stack, item.prefix))),
                "foreground disk click does not mutate native backpack behind it");
            Require(_drive.Disks[0].type == ModContent.ItemType<MEDisk1K>() && Main.mouseItem.IsAir,
                "removing the only occupied disk is refused without item loss");
            _capture = "08-foreground-drive";
        });
        Step("fill identity capacity and reject a new item", () =>
        {
            UISystem.CloseAll(); RightClick(_accessPosition); PositionWindow(UISystem.TerminalState);
            for (int type = 1; _drive.Ledger.StoredTypeCount < 32; type++)
                _drive.Ledger.Insert(ItemKey.Crafted(type), 1);
            Main.LocalPlayer.inventory[25] = new Item(ItemID.LunarBar, 7);
            Main.LocalPlayer.inventory[26] = new Item(ModContent.ItemType<MEDisk1K>());
            var preview = StorageOperations.PreviewDeposit(UISystem.ActiveNetwork, Main.LocalPlayer.inventory[25]);
            Require(preview.Accepted == 0 && preview.Reason.Contains("加盘"), "full identity capacity is reported before attempting a deposit");
            NativePick(25); Click(Field<UIItemGrid>(UISystem.TerminalState, "_grid"));
        });
        Step("check full cursor rejection and prepare drive recovery", () =>
        {
            Require(Main.mouseItem.type == ItemID.LunarBar && Main.mouseItem.stack == 7
                && _drive.Ledger.GetAmount(ItemKey.Crafted(ItemID.LunarBar)) == 0,
                "full identity deposit leaves all seven items on the cursor and creates no ledger entry");
            Require(UIStatus.Active().Any(line => line.Text.Contains("类别已满")), "the rejected deposit explicitly tells the player to add a disk");
            bool left = Main.mouseLeft, release = Main.mouseLeftRelease;
            using (AEUiSession.EnterOwnedInput())
            {
                Main.mouseLeft = Main.mouseLeftRelease = true;
                ItemSlot.LeftClick(Main.LocalPlayer.inventory, 0, 25);
            }
            Main.mouseLeft = left; Main.mouseLeftRelease = release;
            Require(Main.mouseItem.IsAir && Main.LocalPlayer.inventory[25].stack == 7, "rejected cursor stack returns through the native inventory slot");
            UISystem.CloseAll(); Main.playerInventory = false;
            Main.LocalPlayer.Center = new Vector2(_drivePosition.X * 16 - 32, _drivePosition.Y * 16 + 8);
            Main.LocalPlayer.velocity = Vector2.Zero;
        });
        Step("right-click full drive with native input", () =>
        { _worldRightTarget = _drivePosition; _worldRightFrames = 4; });
        Step("install expansion disk through the open full drive", () =>
        {
            Require(ReferenceEquals(UISystem.SecondaryInterface.CurrentState, UISystem.DriveState),
                "a drive at its identity limit still opens from native MouseRight");
            NativePick(26); Click(Field<List<UIItemSlotElement>>(UISystem.DriveState, "_slots")[1]);
        });
        Step("verify expansion and retry refused stack", () =>
        {
            Require(_drive.Ledger.MaxTypes == 64 && _drive.Disks[1].type == ModContent.ItemType<MEDisk1K>()
                && Main.mouseItem.IsAir && Main.LocalPlayer.inventory[26].IsAir,
                "adding a disk raises full-drive capacity without moving stored contents");
            UISystem.CloseAll(); RightClick(_accessPosition);
            _pointer = new Vector2(20, 680);
        });
        Step("deposit the preserved stack after expansion", () =>
        {
            Require(Main.LocalPlayer.GetModPlayer<MEPlayer>().ShiftClickSlot(Main.LocalPlayer.inventory, 0, 25)
                && Main.LocalPlayer.inventory[25].IsAir && _drive.Ledger.GetAmount(ItemKey.Crafted(ItemID.LunarBar)) == 7,
                "expansion permits exactly the seven previously refused items to be deposited");
            UIStatus.Clear(); bool damaged = false;
            for (int i = 0; i < 10; i++)
                Require(!TileLoader.GetTile(Main.tile[_craftPosition.X, _craftPosition.Y].TileType)
                    .CanKillTile(_craftPosition.X, _craftPosition.Y, ref damaged), "a terminal containing stations remains protected");
            Require(UIStatus.Active().Count == 1, "repeated station protection checks produce one readable notification");
            _capture = "10-capacity-recovered";
        });
        Step("save sandbox and reload", () =>
        {
            _savedStock = _drive.Ledger.Entries.ToDictionary(pair => pair.Key, pair => pair.Value);
            SaveAndLeave(true);
        });
    }

    private void BuildReloadSteps()
    {
        Step("verify world and player reload", () =>
        {
            Require(TileEntity.ByPosition.TryGetValue(_drivePosition, out var drive) && drive is MEDiskDriveEntity,
                "drive entity reloads from the sandbox world save");
            _drive = (MEDiskDriveEntity)TileEntity.ByPosition[_drivePosition];
            _craft = (MECraftingTerminalEntity)TileEntity.ByPosition[_craftPosition];
            Require(_savedStock.Count == _drive.Ledger.Entries.Count
                && _savedStock.All(pair => _drive.Ledger.GetAmount(pair.Key) == pair.Value),
                "saved storage identities and quantities survive exit and actual world reload");
            Require(_craft.Stations.Any(item => item.type == ItemID.WorkBench), "installed Work Bench survives world reload");
            Require(InventoryAmount(ItemID.Wood) == 10 && Main.LocalPlayer.inventory[20].IsAir,
                "native player inventory reload preserves withdrawals and installed disk ownership");
            RightClick(_accessPosition); PositionWindow(UISystem.TerminalState);
            _capture = "09-reloaded-storage";
        });
        Step("finish real-player journey", () => SaveAndLeave(false));
    }

    private static IEnumerable<UIElement> Descendants(UIElement parent)
    {
        foreach (var child in parent.Children)
        { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }

    private static T Field<T>(object target, string name) => (T)(target.GetType().GetField(name, PrivateInstance)
        ?? throw new MissingFieldException(target.GetType().FullName, name)).GetValue(target);

    private static long InventoryAmount(int type) => Main.LocalPlayer.inventory.Take(58).Where(item => item.type == type).Sum(item => (long)item.stack);

    private int FindOverlapInventorySlot()
    {
        var grid = Field<UIItemGrid>(UISystem.TerminalState, "_grid");
        if (grid.Parent == null) return -1;
        var drive = UISystem.DriveState;
        var slot = Field<List<UIItemSlotElement>>(drive, "_slots")[0];
        Vector2 offset = slot.GetDimensions().Center() - new Vector2(drive.Layout.X, drive.Layout.Y);
        float width = MovableWindowState.ViewportSize.X, height = MovableWindowState.ViewportSize.Y;
        foreach (var pair in _nativeSlotBounds.OrderBy(pair => pair.Key == 44 ? -1 : pair.Key))
        {
            if (pair.Key != 44 && !Main.LocalPlayer.inventory[pair.Key].IsAir) continue;
            Vector2 target = pair.Value.Center.ToVector2(), origin = target - offset;
            if (grid.GetDimensions().ToRectangle().Contains(target.ToPoint()) && origin.X >= 0 && origin.Y >= 0
                && origin.X + drive.Layout.W <= width && origin.Y + drive.Layout.H <= height)
                return pair.Key;
        }
        return -1;
    }

    private static double ClickGuardMilliseconds() => AEUiSession.FocusedInterface?.CurrentState == null ? 0
        : Field<double>(AEUiSession.FocusedInterface, "_clickDisabledTimeRemaining");

    private void Click(UIElement element)
    {
        Require(element != null && element.Parent != null, "click target is attached to its live interface");
        _pointer = element.GetDimensions().Center(); _clickFrames = 4;
        _clickTarget = element; _clickGridType = 0;
        _lastClickBounds = element.GetDimensions().ToRectangle(); _lastClickType = element.GetType().Name;
        Mod.Logger.Info("AE2 JOURNEY CLICK: " + JsonSerializer.Serialize(DiagnosticState()));
    }
    private void ClickPoint(Vector2 point)
    {
        _pointer = point; _clickFrames = 4; _clickTarget = null; _clickGridType = 0;
        _lastClickType = "pinned-tree-control";
    }

    private void ClickGrid(UIItemGrid grid, int itemType)
    {
        int index = grid.KeyMode ? Field<List<ItemKey>>(grid, "_keys").FindIndex(key => key.Type == itemType)
            : Field<List<int>>(grid, "_items").IndexOf(itemType);
        Require(index >= 0, "clicked item exists in the displayed catalogue");
        int row = index / grid.Columns;
        grid.ScrollRows(row - Field<int>(grid, "_rowOffset"));
        int displayedRow = row - Field<int>(grid, "_rowOffset");
        var bounds = grid.GetDimensions();
        _pointer = new Vector2(bounds.X + (index % grid.Columns) * grid.CellSize + grid.CellSize / 2,
            bounds.Y + displayedRow * grid.CellSize + grid.CellSize / 2);
        _clickFrames = 4;
        _clickTarget = grid; _clickGridType = itemType;
    }

    private static void PositionWindow(MovableWindowState window)
    {
        window.Layout.W = window.Layout.DefaultW; window.Layout.H = window.Layout.DefaultH;
        window.Layout.X = Math.Max(0, MovableWindowState.ViewportSize.X - window.Layout.W - 12);
        window.Layout.Y = Math.Max(0, MovableWindowState.ViewportSize.Y - window.Layout.H - 12);
        window.OnActivate(); window.Recalculate();
    }

    private void NativePick(int slot)
    {
        Require(Main.mouseItem.IsAir && !Main.LocalPlayer.inventory[slot].IsAir, "native pickup starts with empty cursor and real inventory item");
        Main.playerInventory = true;
        bool left = Main.mouseLeft, release = Main.mouseLeftRelease;
        try
        {
            Main.mouseLeft = true; Main.mouseLeftRelease = true;
            using var owned = AEUiSession.EnterOwnedInput();
            ItemSlot.LeftClick(Main.LocalPlayer.inventory, 0, slot);
        }
        finally { Main.mouseLeft = left; Main.mouseLeftRelease = release; }
        Require(!Main.mouseItem.IsAir, "vanilla ItemSlot.LeftClick transfers the inventory item to the cursor");
    }

    private static void RightClick(Point16 position)
    {
        // Exercise the game's reach/release/TileLoader dispatch, never call ModTile.RightClick directly.
        var player = Main.LocalPlayer;
        player.Center = new Vector2(position.X * 16 + 8, position.Y * 16 + 8);
        player.velocity = Vector2.Zero;
        Player.tileTargetX = position.X; Player.tileTargetY = position.Y;
        Main.SmartInteractShowingGenuine = false;
        player.controlUseTile = true; player.releaseUseTile = true;
        Method(typeof(Player), "LookForTileInteractions").Invoke(player, null);
        player.controlUseTile = false;
    }

    private void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("AE2 JOURNEY CHECK FAILED: " + message);
        _checksPassed++; Mod.Logger.Info("AE2 JOURNEY PASS: " + message);
    }

    private void Capture(string name)
    {
        var device = Main.instance.GraphicsDevice;
        int width = device.PresentationParameters.BackBufferWidth, height = device.PresentationParameters.BackBufferHeight;
        var pixels = new Color[width * height];
        device.GetBackBufferData(pixels);
        using var image = new Texture2D(device, width, height);
        image.SetData(pixels);
        using var output = File.Create(Path.Combine(_directory, name + ".png"));
        image.SaveAsPng(output, width, height);
        _capture = null;
    }

    private void SaveAndLeave(bool reload)
    {
        Require(IsSandbox(), "save target still equals both isolated file paths");
        _saving = true; _moveRight = false; _clickFrames = 0; UISystem.CloseAll();
        WorldGen.SaveAndQuit(() => Main.QueueMainThreadAction(() =>
        {
            _saving = false; _entered = false; _menuFrames = 0;
            if (reload) { _reload = true; return; }
            WriteResult(true, null); _done = true; RestoreSettings(); Main.instance.Exit();
        }));
    }

    private void Fail(Exception error)
    {
        if (_done) return;
        Mod.Logger.Error("AE2 JOURNEY FAILED", error);
        Mod.Logger.Error("AE2 JOURNEY STATE: " + JsonSerializer.Serialize(DiagnosticState()));
        WriteResult(false, error.ToString());
        _done = true; _moveRight = false; _clickFrames = 0;
        // Capture the next complete normal game draw once, then leave. No retry loop hides a failed check.
        if (IsSandbox()) _failure = error.ToString();
        else Main.QueueMainThreadAction(() => { RestoreSettings(); Main.instance.Exit(); });
    }

    private object DiagnosticState()
    {
        static object ItemState(Item item) => item == null ? null : new { item.type, item.stack, item.prefix };
        Rectangle? slot = UISystem.DriveState == null ? null : Field<List<UIItemSlotElement>>(UISystem.DriveState, "_slots").FirstOrDefault()?.GetDimensions().ToRectangle();
        return new
        {
            pointer = new { _pointer.X, _pointer.Y }, clickType = _lastClickType,
            clickBounds = new { _lastClickBounds.X, _lastClickBounds.Y, _lastClickBounds.Width, _lastClickBounds.Height },
            driveSlot = slot?.ToString(), focused = AEUiSession.FocusedInterface?.CurrentState?.GetType().Name,
            scale = Main.UIScale, Main.mouseX, Main.mouseY, Main.mouseLeft, Main.mouseLeftRelease, Main.hasFocus,
            clickGuardMilliseconds = ClickGuardMilliseconds(), clickGuardWaitFrames = _clickGuardWaitFrames,
            overlapInventorySlot = _overlapInventorySlot,
            inputAllowed = AEUiSession.InputAllowed, lockedInventory = Main.LocalPlayer?.HasLockedInventory(),
            cursorPending = MENet.CursorPending, transactionPending = MENet.InventoryTransactionPending, driveClicks = _driveClicks,
            terminalLayout = UISystem.TerminalState == null ? null : new { UISystem.TerminalState.Layout.X, UISystem.TerminalState.Layout.Y, UISystem.TerminalState.Layout.W, UISystem.TerminalState.Layout.H },
            terminalBounds = UISystem.TerminalState?.InteractionBounds.ToString(),
            resizeOrigin = new { _resizeOrigin.X, _resizeOrigin.Y }, resizeFrames = _resizeFrames,
            nativePointer = new { PlayerInput.MouseInfo.X, PlayerInput.MouseInfo.Y },
            targetAtResizeStart = UISystem.TerminalState?.GetElementAt(_resizeOrigin)?.GetType().Name,
            cursor = ItemState(Main.mouseItem), inventory20 = ItemState(Main.LocalPlayer?.inventory[20]),
            inventory58 = ItemState(Main.LocalPlayer?.inventory[58]), disk0 = ItemState(_drive?.Disks[0])
        };
    }

    private void RestoreSettings()
    {
        if (!_settingsCaptured) return;
        Main.autoPause = _oldAutoPause; Main.mapEnabled = _oldMapEnabled;
        Main.SetDisplayMode(_oldWidth, _oldHeight, _oldFullscreen);
        _settingsCaptured = false;
    }

    private void WriteResult(bool passed, string error)
    {
        File.WriteAllText(Path.Combine(_directory, "journey-result.json"), JsonSerializer.Serialize(new
        {
            passed, assertions = _checksPassed, durationSeconds = _elapsed.Elapsed.TotalSeconds,
            totalClickGuardWaitFrames = _totalClickGuardWaitFrames,
            world = _worldPath, player = _playerPath, worldReloaded = _reload, error,
            diagnostics = DiagnosticState(),
            method = "real client world/update/draw; isolated-world background drive with scoped native focus checks; injected PlayerInput plus native ItemSlot/ModPlayer callbacks; not OS automation",
            steps = _checks,
            screenshots = Directory.GetFiles(_directory, "*.png").Select(Path.GetFileName).OrderBy(value => value).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true }));
        Mod.Logger.Info("AE2 JOURNEY " + (passed ? "COMPLETE" : "FAILED") + ": " + _checksPassed + " checks; " + _directory);
    }
}
