using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using AE2ForTerraria.Content.Items;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Creative;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems;

/// <summary>
/// Explicit, isolated live TCP integration fixture. Disabled without AE2_LIVE_REGRESSION=server/client.
/// Uses the real mod pack, a separately named world, and fresh in-memory characters with sandboxed paths.
/// No world is loaded or process started by this class. The coordinator controls lifecycle and collects logs.
/// </summary>
public sealed class LiveIntegrationRegression : ModSystem
{
    private static string Root => Path.Combine(RegressionPaths.Root, "stage1", "live");
    private const string WorldName = "AE2ValidationCore20261003";
    private const string ActorName = "AE2LiveActor";
    private const string ObserverName = "AE2LiveObserver";
    private const int Port = 17779;
    private static bool _clientGuardEnabled;
    private string _role, _kind, _directory, _worldPath;
    private bool _enabled, _serverArmed, _joined, _clientReady, _requested, _done, _serverStarted;
    private bool _requireObserver, _extended, _deniedRequested, _deniedChecked, _observerSawUpgrade;
    private bool _deposits, _depositDeniedRequested;
    private bool _craft, _craftRequested, _reconnectLedgerRequested;
    private int _craftRecipeIndex = -1;
    private int _craftRequestFrame, _blockedPickups;
    private int _depositPhase;
    private int _upgradePhase;
    private int _menuFrames, _frames;
    private Point16 _unitPosition;

    internal static bool IsFixtureEnabledForProcess(bool clientOnly=false)
    {
        if (Program.LaunchParameters.ContainsKey("-subworld") || (clientOnly && Main.dedServ)) return false;
        string role=Environment.GetEnvironmentVariable("AE2_LIVE_REGRESSION");
        return Main.dedServ ? role=="server" : role=="client";
    }

    public override bool IsLoadingEnabled(Mod mod) => IsFixtureEnabledForProcess();

    public override void Load()
    {
        // SubworldLibrary inherits the parent environment when starting its auxiliary server.
        // It must never arm the main-world fixture or erase that server's coordination markers.
        if (Program.LaunchParameters.ContainsKey("-subworld")) return;
        _role = Environment.GetEnvironmentVariable("AE2_LIVE_REGRESSION");
        if (_role != "server" && _role != "client") return;
        if ((_role == "server") != Main.dedServ) return;
        string run = Environment.GetEnvironmentVariable("AE2_LIVE_RUN") ?? "core-20261003";
        if (!Regex.IsMatch(run, "^[a-z0-9-]{1,40}$")) throw new InvalidOperationException("Invalid AE2 live run identifier");
        _kind = Environment.GetEnvironmentVariable("AE2_LIVE_CLIENT_KIND") ?? "actor";
        if (_kind != "actor" && _kind != "observer" && _kind != "reconnect")
            throw new InvalidOperationException("Invalid AE2 live client kind");
        _requireObserver = Environment.GetEnvironmentVariable("AE2_LIVE_REQUIRE_OBSERVER") == "1";
        _extended = Environment.GetEnvironmentVariable("AE2_LIVE_EXTENDED") == "1";
        _deposits = Environment.GetEnvironmentVariable("AE2_LIVE_DEPOSITS") == "1";
        _craft = Environment.GetEnvironmentVariable("AE2_LIVE_CRAFT") == "1";
        if (_deposits && !_extended) throw new InvalidOperationException("Live deposits require extended upgrade coordination");
        if (_craft && !_deposits) throw new InvalidOperationException("Live crafting requires deposit coordination");
        _directory = Path.GetFullPath(Path.Combine(Root,run));
        if (!string.Equals(Path.GetDirectoryName(_directory),Path.GetFullPath(Root),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("AE2 live path escaped whitelist");
        Directory.CreateDirectory(_directory);
        if ((File.GetAttributes(_directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("AE2 live directory must not be a junction");
        _worldPath = Path.Combine(_directory,WorldName + ".wld");
        _enabled = true;
        if (!Main.dedServ)
        {
            _clientGuardEnabled = true;
            On_Main.Draw += MenuJoin;
            Main.QueueMainThreadAction(() => Main.SetDisplayMode(1280, 720, false));
        }
    }
    public override void Unload()
    {
        On_Main.Draw -= MenuJoin;
        _clientGuardEnabled = false;
    }

    internal static bool IsIsolatedClientPlayer(Player player)
    {
        if (!_clientGuardEnabled || Main.netMode!=NetmodeID.MultiplayerClient || Main.worldName!=WorldName
            || player==null || !ReferenceEquals(player,Main.LocalPlayer)
            || (player.name!=ActorName && player.name!=ObserverName)) return false;
        var fixture=ModContent.GetInstance<LiveIntegrationRegression>();
        return fixture._enabled && fixture._role=="client" && fixture._joined
            && Main.ActivePlayerFileData!=null
            && string.Equals(Main.ActivePlayerFileData.Path,fixture.Signal(fixture._kind+".plr"),StringComparison.OrdinalIgnoreCase);
    }

    internal static void RecordBlockedPickup(Item item)
    {
        var fixture=ModContent.GetInstance<LiveIntegrationRegression>();
        if (++fixture._blockedPickups<=8)
            fixture.Mod.Logger.Info("AE2 LIVE PICKUP BLOCKED ["+fixture._kind+"]: world item type="+item.type
                +", stack="+item.stack+", clientReady="+fixture._clientReady+", depositPhase="+fixture._depositPhase);
    }

    private string Signal(string file)
    {
        // Callers use only fixed filenames; even accidental future callers cannot escape the fixture.
        if (!Regex.IsMatch(file,"^[a-z0-9-]+\\.(json|flag|plr)$"))
            throw new InvalidOperationException("Invalid AE2 live fixture filename");
        return Path.Combine(_directory,file);
    }
    private bool Exists(string file) => File.Exists(Signal(file));
    private void Write(string file, object value)
    {
        string path = Signal(file), temporary = path + ".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(value,new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary,path,true);
    }
    private void Pass(string detail) => Mod.Logger.Info("AE2 LIVE PASS [" + _role + "/" + _kind + "]: " + detail);
    private void Fail(Exception ex)
    {
        _done = true;
        Mod.Logger.Error("AE2 LIVE FAILED [" + _role + "/" + _kind + "]",ex);
        Write(_role == "server" ? "server-failed.json" : _kind + "-failed.json",new { error=ex.ToString() });
    }
    private static void Require(bool condition,string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private bool ExactServerWorld() => Main.netMode == NetmodeID.Server
        && Main.worldName == WorldName && Main.ActiveWorldFileData != null
        && string.Equals(Path.GetFullPath(Main.ActiveWorldFileData.Path),_worldPath,StringComparison.OrdinalIgnoreCase);

    public override void PostWorldLoad()
    {
        // All saved component policies have been restored. A dedicated server does
        // not tick without clients, so first-update initialization would deadlock joining.
        if (_enabled && _role == "server") ArmWorld();
    }
    private void ArmWorld()
    {
        if (!_enabled || _role != "server") return;
        // Triple guard: role environment, exact world name, exact independently named filesystem path.
        if (!ExactServerWorld())
        {
            Mod.Logger.Error("AE2 LIVE REFUSED: world name/path is outside the isolated fixture; no changes made.");
            return;
        }
        try
        {
            foreach (string file in new[] {"server-ready.json","server-before.json","server-pass.json","server-failed.json",
                "start-upgrade.flag","actor-ready.json","actor-pass.json","actor-failed.json",
                "observer-ready.json","observer-pass.json","observer-failed.json","reconnect-pass.json","reconnect-failed.json",
                "observer-denied-pass.json","server-denied-pass.json","actor-max-ready.json","server-max-ready.json",
                "actor-max-refusal-pass.json","observer-final-pass.json","server-complete.json","server-upgrade-complete.json",
                "start-deposits.flag","actor-deposit-single-pass.json","server-deposit-single-pass.json",
                "actor-deposit-type-pass.json","server-deposit-type-pass.json",
                "actor-deposit-bulk-pass.json","server-deposit-bulk-pass.json",
                "actor-deposit-extras-pass.json","server-deposit-extras-pass.json",
                "observer-deposit-denied-pass.json","server-deposit-denied-pass.json",
                "server-final-complete.json","server-craft-before.json","start-craft.flag",
                "actor-craft-request.json","actor-craft-pass.json","server-craft-pass.json"})
                if (Exists(file)) File.Delete(Signal(file));
            int x = Main.spawnTileX + 4, y = Main.spawnTileY - 2;
            Require(WorldGen.InWorld(x-2,y-5,10) && WorldGen.InWorld(x+12,y+2,10),"Fixture position outside world bounds");
            for (int tx=x-2;tx<=x+10;tx++)
            {
                for (int ty=y-4;ty<=y;ty++) Main.tile[tx,ty].ClearEverything();
                Tile floor = Main.tile[tx,y+1]; floor.ClearEverything(); floor.HasTile=true; floor.TileType=TileID.Stone;
            }
            _unitPosition = new Point16(x,y);
            PlaceMachine<MEStorageUnitEntity>(x,y,ModContent.TileType<MEStorageUnitTile>());
            PlaceMachine<MEMolecularAssemblerEntity>(x+1,y,ModContent.TileType<MEMolecularAssemblerTile>());
            var unit=(MEStorageUnitEntity)TileEntity.ByPosition[_unitPosition];
            unit.Ledger.Restore(ItemKey.Crafted(ItemID.Wood),16);
            var assembler=(MEMolecularAssemblerEntity)TileEntity.ByPosition[new Point16(x+1,y)];
            assembler.Stations[0]=new Item(ItemID.WorkBench);
            if (_craft)
            {
                unit.Ledger.Restore(ItemKey.Crafted(ItemID.Torch),5);
                // Inspect the recipes actually registered by the loaded mod pack; never register or replace test recipes.
                _craftRecipeIndex = FindLoadedTorchRecipe(assembler);
                Require(_craftRecipeIndex >= 0,"Loaded mod pack has no usable vanilla Torch x3 recipe from Wood x1 and Gel x1");
                Mod.Logger.Info("AE2 LIVE CRAFT SETUP: loaded vanilla recipe index="+_craftRecipeIndex
                    +"; Torch x3 from Wood x1 + Gel x1; existing Torch x5; no recipe registration was changed");
            }
            int chestX=x+7,chestY=y-1;
            int existingChest=Chest.FindChest(chestX,chestY);
            if (existingChest >= 0) Main.chest[existingChest]=null;
            for(int dx=0;dx<2;dx++) for(int dy=0;dy<2;dy++)
            {
                Tile tile=Main.tile[chestX+dx,chestY+dy]; tile.ClearEverything();
                tile.HasTile=true; tile.TileType=TileID.Containers;
                tile.TileFrameX=(short)(dx*18); tile.TileFrameY=(short)(dy*18);
            }
            int chest=Chest.CreateChest(chestX,chestY);
            Require(chest>=0,"Could not allocate fixture vanilla chest");
            Main.chest[chest].item[0]=new Item(ItemID.Gel,8);
            MENetwork.InvalidateCache();
            Point16 anchor=MENetwork.CanonicalAnchor(MENetwork.Collect(x,y));
            if(NetworkRegistry.GetOwner(anchor)==null)
                Require(NetworkRegistry.TryClaim(anchor,ActorName),"Could not claim isolated network for fixture actor");
            Require(NetworkRegistry.GetOwner(anchor)==ActorName,"Fixture network unexpectedly has another owner");
            NetworkRegistry.SetLevel(anchor,ObserverName,NetworkRegistry.LevelReadOnly);
            Require(NetworkRegistry.GetLevel(anchor,ObserverName)==NetworkRegistry.LevelReadOnly,
                "Fixture observer must have read-only snapshot permission");
            _serverArmed=true;
            Write("server-ready.json",new { world=WorldName,x,y,initialLevel=unit.Level,capacity=unit.CurrentCapacity,port=Port,
                owner=ActorName,observerLevel=NetworkRegistry.LevelReadOnly,extended=_extended,requireObserver=_requireObserver,
                deposits=_deposits,craft=_craft,craftRecipeIndex=_craftRecipeIndex,initialTorch=Stock(unit,ItemID.Torch) });
            Pass("isolated world armed at " + x + "," + y + "; user worlds and players were not opened");
        }
        catch(Exception ex) { Fail(ex); }
    }
    private static void PlaceMachine<T>(int x,int y,int tileType) where T : ModTileEntity
    {
        var point=new Point16(x,y);
        if(TileEntity.ByPosition.TryGetValue(point,out var previous))
        { TileEntity.ByPosition.Remove(point); TileEntity.ByID.Remove(previous.ID); }
        Tile tile=Main.tile[x,y]; tile.ClearEverything(); tile.HasTile=true; tile.TileType=(ushort)tileType;
        int id=ModContent.GetInstance<T>().Place(x,y);
        Require(id>=0 && TileEntity.ByPosition.ContainsKey(point),"Fixture tile entity placement failed");
    }
    private void MenuJoin(On_Main.orig_Draw orig,Main self,GameTime time)
    {
        orig(self,time);
        if (!_enabled || _role != "client" || _done || _joined || !Main.gameMenu || Main.menuMode != 0 || !StorageSystem.Ready) return;
        if (++_menuFrames<120 || !Exists("server-ready.json")) return;
        try
        {
            // Both opt-in systems run synchronously on Draw. Wait until the menu-only UI pass
            // has returned before leaving the menu; _done records an attempted pass, not success.
            // Its COMPLETE/FAILED log remains the authority for whether those assertions passed.
            if(Environment.GetEnvironmentVariable("AE2_UI_REGRESSION")=="1")
            {
                var finished=typeof(UiRegressionSystem).GetField("_done",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                Require(finished!=null,"UI regression completion field could not be located");
                if (!(bool)finished.GetValue(ModContent.GetInstance<UiRegressionSystem>())) return;
            }
            using var ready=JsonDocument.Parse(File.ReadAllText(Signal("server-ready.json")));
            Require(ready.RootElement.GetProperty("world").GetString()==WorldName,"Unexpected server fixture marker");
            Require(ready.RootElement.GetProperty("extended").GetBoolean()==_extended,"Client/server extended-test settings differ");
            Require(ready.RootElement.GetProperty("deposits").GetBoolean()==_deposits,"Client/server deposit-test settings differ");
            Require(ready.RootElement.GetProperty("craft").GetBoolean()==_craft,"Client/server craft-test settings differ");
            _craftRecipeIndex=ready.RootElement.GetProperty("craftRecipeIndex").GetInt32();
            _requireObserver=ready.RootElement.GetProperty("requireObserver").GetBoolean();
            _unitPosition=new Point16(ready.RootElement.GetProperty("x").GetInt32(),ready.RootElement.GetProperty("y").GetInt32());
            var player=new Player { name=_kind=="observer" ? ObserverName : ActorName,statLifeMax=400,statLife=400,selectedItem=0 };
            // Mirror UICharacterCreation.SetupPlayerStatsAndInventoryBasedOnDifficulty's
            // new-character initialization without calling its save/load-player-list methods.
            player.savedPerPlayerFieldsThatArentInThePlayerClass=new Player.SavedPlayerDataWithAnnoyingRules();
            CreativePowerManager.Instance.ResetDataForNewPlayer(player);
            PlayerLoader.SetStartInventory(player,PlayerLoader.GetStartingItems(player,Array.Empty<Item>()));
            // These are fresh isolated characters. Preserve every other mod's starter items,
            // marking them favorite so DepositAll exercises only the fixed fixture slots.
            if (_deposits)
                foreach (Item item in player.inventory) if (item != null && !item.IsAir) item.favorited=true;
            if (_kind=="actor") player.inventory[0]=new Item(ModContent.ItemType<MEStorageUpgrade>(),3);
            if (_kind=="actor") player.inventory[1]=new Item(ItemID.Wood,7);
            if (_kind=="observer" && _extended) player.inventory[0]=new Item(ModContent.ItemType<MEStorageUpgrade>(),1);
            if (_deposits && _kind=="actor")
            {
                player.inventory[2]=new Item(ItemID.Gel,5);
                player.inventory[3]=new Item(ItemID.Gel,9);
                player.inventory[4]=new Item(ItemID.StoneBlock,11);
                player.inventory[5]=new Item(ItemID.StoneBlock,13) { favorited=true };
                player.inventory[6]=new Item(ItemID.CopperOre,17);
                player.inventory[50]=new Item(ItemID.SilverCoin,23);
                player.inventory[54]=new Item(ItemID.WoodenArrow,29);
                player.inventory[55]=new Item(ItemID.WoodenArrow,31) { favorited=true };
            }
            if (_deposits && _kind=="observer") player.inventory[2]=new Item(ItemID.Ruby,7);
            // Never enumerate existing characters or call CreateAndSave. Any later game autosave remains isolated.
            var data=new PlayerFileData(Signal(_kind+".plr"),false)
            { Player=player, Metadata=FileMetadata.FromCurrentSettings(FileType.Player) };
            data.SetAsActive();
            Require(string.Equals(Main.ActivePlayerFileData.Path,Signal(_kind+".plr"),StringComparison.OrdinalIgnoreCase),"Temporary player path mismatch");
            Require(Netplay.SetRemoteIP("127.0.0.1"),"Loopback address rejected");
            Netplay.ListenPort=Port;
            Main.menuMode=10;
            _joined=true;
            Netplay.StartTcpClient();
            Mod.Logger.Info("AE2 LIVE CONNECT ["+_kind+"]: fresh in-memory character, loopback port "+Port);
        }
        catch(Exception ex) { Fail(ex); }
    }
    public override void PostUpdateEverything()
    {
        if (!_enabled || _done || (!Main.dedServ && Main.gameMenu)) return;
        if (++_frames % 30 != 0) return;
        try
        {
            if (_role=="server") { if(_serverArmed) ServerCheck(); }
            else if (_joined && Main.netMode==NetmodeID.MultiplayerClient) ClientCheck();
        }
        catch(Exception ex) { Fail(ex); }
    }
    private MEStorageUnitEntity Unit()
    {
        if (TileEntity.ByPosition.TryGetValue(_unitPosition,out var entity) && entity is MEStorageUnitEntity unit) return unit;
        return null;
    }
    private static Player NamedPlayer(string name)
    {
        foreach(Player player in Main.player) if(player!=null && player.active && player.name==name) return player;
        return null;
    }
    private void ServerCheck()
    {
        Require(ExactServerWorld(),"Server left isolated world while fixture active");
        var actor=NamedPlayer(ActorName);
        var unit=Unit(); Require(unit!=null,"Server fixture unit disappeared");
        if (!_serverStarted && actor!=null && Exists("actor-ready.json")
            && (!_requireObserver || Exists("observer-ready.json"))
            && (!_requireObserver || !_extended || Exists("observer-denied-pass.json")))
        {
            Require(unit.Level==1 && actor.inventory[0].type==ModContent.ItemType<MEStorageUpgrade>() && actor.inventory[0].stack==3,
                "Initial server inventory/level does not match fresh client fixture");
            if(_requireObserver && _extended)
            {
                Player observer=NamedPlayer(ObserverName);
                Require(observer!=null && observer.inventory[0].type==ModContent.ItemType<MEStorageUpgrade>() && observer.inventory[0].stack==1,
                    "Denied observer request changed server inventory");
                Point16 anchor=MENetwork.CanonicalAnchor(MENetwork.Collect(_unitPosition.X,_unitPosition.Y));
                Require(NetworkRegistry.GetLevel(anchor,ObserverName)==NetworkRegistry.LevelReadOnly,"Observer gained write permission");
                Write("server-denied-pass.json",new {level=unit.Level,stack=observer.inventory[0].stack});
                Pass("read-only observer's live upgrade request was refused without consuming its item");
            }
            Write("server-before.json",new { level=unit.Level,slotType=actor.inventory[0].type,stack=actor.inventory[0].stack,
                player=actor.whoAmI,inventoryAndLedger=DepositSnapshot(unit,actor) });
            Write("start-upgrade.flag",new { start=true }); _serverStarted=true;
        }
        if (_serverStarted && actor!=null && Exists("actor-pass.json") && !Exists("server-pass.json"))
        {
            Require(unit.Level==2 && unit.CurrentCapacity==MEStorageUnitEntity.LevelCapacity[1]
                && actor.inventory[0].type==ModContent.ItemType<MEStorageUpgrade>() && actor.inventory[0].stack==2
                && actor.inventory[1].type==ItemID.Wood && actor.inventory[1].stack==7,
                "Server level/capacity or requester inventory differs from acknowledged client result");
            Write("server-pass.json",new { level=unit.Level,capacity=unit.CurrentCapacity,stack=actor.inventory[0].stack });
            Pass("one real upgrade request increased level once and consumed exactly one upgrade");
            if(!_extended) Write("server-complete.json",new {level=unit.Level,capacity=unit.CurrentCapacity});
        }
        if(_extended && actor!=null && Exists("actor-max-ready.json") && !Exists("server-max-ready.json"))
        {
            Require(unit.Level==MEStorageUnitEntity.MaxLevel && actor.inventory[0].stack==1,"Second upgrade failed server accounting");
            Write("server-max-ready.json",new {level=unit.Level,stack=actor.inventory[0].stack});
        }
        if(_extended && actor!=null && Exists("actor-max-refusal-pass.json") && !Exists("server-upgrade-complete.json"))
        {
            Require(unit.Level==MEStorageUnitEntity.MaxLevel && actor.inventory[0].type==ModContent.ItemType<MEStorageUpgrade>()
                && actor.inventory[0].stack==1 && actor.inventory[1].type==ItemID.Wood && actor.inventory[1].stack==7,
                "Full-level refusal changed server machine or inventory");
            Write("server-upgrade-complete.json",new {level=unit.Level,capacity=unit.CurrentCapacity,stack=actor.inventory[0].stack});
            if(!_deposits) Write("server-complete.json",new {level=unit.Level,capacity=unit.CurrentCapacity,stack=actor.inventory[0].stack});
            Pass("full-level live request was acknowledged without consuming an upgrade");
        }
        if(_deposits && actor!=null && Exists("server-upgrade-complete.json")
            && (!_requireObserver || Exists("observer-final-pass.json"))) ServerDepositCheck(actor,unit);
        if(_craft && actor!=null && Exists("server-complete.json")) ServerCraftCheck(actor,unit);
        else if(!_craft && Exists("server-complete.json") && !Exists("server-final-complete.json"))
            Write("server-final-complete.json",new {level=unit.Level,capacity=unit.CurrentCapacity,craft=false,deposits=_deposits});
    }
    private void ClientCheck()
    {
        Require(Main.worldName==WorldName,"Client connected to unexpected world");
        Require(Main.ActivePlayerFileData!=null && string.Equals(Main.ActivePlayerFileData.Path,Signal(_kind+".plr"),StringComparison.OrdinalIgnoreCase),
            "Client temporary player isolation lost");
        if (_frames>60*600) throw new TimeoutException("Live integration client timed out while awaiting coordinated clients");
        var unit=Unit(); if(unit==null || !Main.LocalPlayer.active) return;
        // The unit is near the normal spawn. No teleport or client-authoritative item mutation is needed.
        if (_kind=="reconnect")
        {
            if(!Exists("server-final-complete.json")) return;
            using var completed=JsonDocument.Parse(File.ReadAllText(Signal("server-final-complete.json")));
            int expected=completed.RootElement.GetProperty("level").GetInt32();
            Require(expected>=2 && expected<=MEStorageUnitEntity.MaxLevel,"Invalid expected fixture level");
            Require(unit.Level==expected && unit.CurrentCapacity==MEStorageUnitEntity.LevelCapacity[expected-1],"Reconnect did not receive authoritative machine level");
            if (_craft)
            {
                if (!_reconnectLedgerRequested) { MENet.RequestLedger(_unitPosition); _reconnectLedgerRequested=true; return; }
                if (Stock(unit,ItemID.Torch)==0) return; // Wait for the normal requested ledger snapshot.
                ExpectCraftStock(unit);
            }
            Write("reconnect-pass.json",new { level=unit.Level,capacity=unit.CurrentCapacity,craft=_craft,
                torch=Stock(unit,ItemID.Torch),wood=Stock(unit,ItemID.Wood),gel=Stock(unit,ItemID.Gel) });
            Pass("fresh second connection received upgraded tile entity through normal section sync"); _done=true; return;
        }
        if(!_clientReady)
        {
            Require(unit.Level==1,"Initial client tile entity must be level one");
            // Record the joined character before any ME business request. This distinguishes startup
            // item additions from later quantity changes; it never rewrites fixture or user inventory.
            object initialInventory=DepositSnapshot(unit,Main.LocalPlayer);
            Write(_kind+"-ready.json",new { player=Main.myPlayer,level=unit.Level,blockedWorldPickups=_blockedPickups,
                inventoryAndLedger=initialInventory });
            Mod.Logger.Info("AE2 LIVE INITIAL SLOTS ["+_kind+"]: "+JsonSerializer.Serialize(initialInventory));
            _clientReady=true;
            if (_deposits) MENet.RequestLedger(_unitPosition);
        }
        if(_kind=="observer")
        {
            if(_extended && !_deniedChecked)
            {
                Require(_requireObserver,"Extended observer requires coordinated observer gating on server");
                if(!_deniedRequested)
                {
                    Main.LocalPlayer.selectedItem=0; MENet.UpgradeUnit(_unitPosition);
                    Require(MENet.StorageUpgradePending,"Observer did not initiate public upgrade request");
                    _deniedRequested=true; return;
                }
                if(MENet.StorageUpgradePending) return;
                Require(unit.Level==1 && Main.LocalPlayer.inventory[0].type==ModContent.ItemType<MEStorageUpgrade>()
                    && Main.LocalPlayer.inventory[0].stack==1,"Read-only request was not refused without mutation");
                Write("observer-denied-pass.json",new {level=unit.Level,stack=Main.LocalPlayer.inventory[0].stack});
                Pass("read-only upgrade request completed without item consumption or machine change");
                _deniedChecked=true; return;
            }
            if(!_observerSawUpgrade && unit.Level==2)
            {
                Require(unit.CurrentCapacity==MEStorageUnitEntity.LevelCapacity[1],"Observer capacity not synchronized");
                Write("observer-pass.json",new { level=unit.Level,capacity=unit.CurrentCapacity });
                Pass("concurrent observer received authorized level and capacity broadcast");
                _observerSawUpgrade=true; if(!_extended) _done=true;
            }
            if(_extended && _observerSawUpgrade && unit.Level==MEStorageUnitEntity.MaxLevel && !Exists("observer-final-pass.json"))
            {
                Require(unit.CurrentCapacity==MEStorageUnitEntity.LevelCapacity[^1],"Observer final capacity not synchronized");
                Write("observer-final-pass.json",new {level=unit.Level,capacity=unit.CurrentCapacity});
                Pass("read-only observer also received final upgraded capacity"); if(!_deposits) _done=true;
            }
            if(_deposits && Exists("observer-final-pass.json")) ObserverDepositCheck(unit);
            return;
        }
        if(!_requested && Exists("start-upgrade.flag"))
        {
            Require(Main.LocalPlayer.inventory[0].type==ModContent.ItemType<MEStorageUpgrade>() && Main.LocalPlayer.inventory[0].stack==3,
                "Client initial upgrade stack changed before request");
            Main.LocalPlayer.selectedItem=0;
            MENet.UpgradeUnit(_unitPosition);
            Require(MENet.StorageUpgradePending,"Actor did not initiate public upgrade request");
            // Exercise a normal hotbar selection change while the original requested slot is pending.
            Main.LocalPlayer.selectedItem=1;
            NetMessage.SendData(MessageID.PlayerControls,-1,-1,null,Main.myPlayer);
            _requested=true;
            Mod.Logger.Info("AE2 LIVE REQUEST: public MENet.UpgradeUnit called from real client");
            return;
        }
        if(_requested && _upgradePhase==0 && unit.Level==2 && !MENet.StorageUpgradePending)
        {
            Require(unit.CurrentCapacity==MEStorageUnitEntity.LevelCapacity[1]
                && Main.LocalPlayer.inventory[0].type==ModContent.ItemType<MEStorageUpgrade>() && Main.LocalPlayer.inventory[0].stack==2
                && Main.LocalPlayer.inventory[1].type==ItemID.Wood && Main.LocalPlayer.inventory[1].stack==7,
                "Client request acknowledgement did not converge to authoritative slot and level");
            Write("actor-pass.json",new { level=unit.Level,capacity=unit.CurrentCapacity,stack=Main.LocalPlayer.inventory[0].stack });
            Pass("requester consumed original-slot item after hotbar change; other slot intact and level synchronized");
            _upgradePhase=1; if(!_extended) _done=true;
            return;
        }
        if(_extended && _upgradePhase==1 && Exists("server-pass.json") && (!_requireObserver || Exists("observer-pass.json")))
        {
            Main.LocalPlayer.selectedItem=0; MENet.UpgradeUnit(_unitPosition);
            Require(MENet.StorageUpgradePending,"Second public upgrade request did not start");
            _upgradePhase=2; return;
        }
        if(_extended && _upgradePhase==2 && !MENet.StorageUpgradePending)
        {
            Require(unit.Level==MEStorageUnitEntity.MaxLevel && Main.LocalPlayer.inventory[0].stack==1,"Second upgrade did not converge");
            Write("actor-max-ready.json",new {level=unit.Level,stack=Main.LocalPlayer.inventory[0].stack});
            _upgradePhase=3; return;
        }
        if(_extended && _upgradePhase==3 && Exists("server-max-ready.json"))
        {
            Main.LocalPlayer.selectedItem=0; MENet.UpgradeUnit(_unitPosition);
            Require(MENet.StorageUpgradePending,"Full-level public request did not start");
            _upgradePhase=4; return;
        }
        if(_extended && _upgradePhase==4 && !MENet.StorageUpgradePending)
        {
            Require(unit.Level==MEStorageUnitEntity.MaxLevel && Main.LocalPlayer.inventory[0].type==ModContent.ItemType<MEStorageUpgrade>()
                && Main.LocalPlayer.inventory[0].stack==1,"Full-level refusal consumed an item or changed machine");
            Write("actor-max-refusal-pass.json",new {level=unit.Level,stack=Main.LocalPlayer.inventory[0].stack});
            Pass("full-level request returned completion with unchanged remaining upgrade item");
            _upgradePhase=5; if(!_deposits) _done=true;
        }
        if(_deposits && _upgradePhase==5) ActorDepositCheck(unit);
        if(_craft && _depositPhase==8 && Exists("server-complete.json")) ActorCraftCheck(unit);
    }

    private static long Stock(MEStorageUnitEntity unit,int type)
    {
        long count=0;
        foreach(var entry in unit.Ledger.Entries) if(entry.Key.Type==type) count+=entry.Value;
        return count;
    }
    private static void ExpectSlot(Player player,int slot,int type,int amount)
    {
        Item item=player.inventory[slot];
        Require(amount==0 ? item==null || item.IsAir : item!=null && item.type==type && item.stack==amount,
            "Deposit inventory mismatch at slot "+slot+": expected "+type+" x"+amount
            +", got "+(item?.type ?? 0)+" x"+(item?.stack ?? 0));
    }
    private object DepositSnapshot(MEStorageUnitEntity unit,Player player) => new
    {
        level=unit.Level,capacity=unit.CurrentCapacity,
        ledger=new[] {ItemID.Wood,ItemID.Gel,ItemID.StoneBlock,ItemID.CopperOre,ItemID.SilverCoin,
            ItemID.WoodenArrow,ItemID.Ruby,ModContent.ItemType<MEStorageUpgrade>()}
            .Select(type=>new {type,amount=Stock(unit,type)}).ToArray(),
        slots=new[] {0,1,2,3,4,5,6,50,54,55}.Select(slot=>new
            {slot,type=player.inventory[slot].type,stack=player.inventory[slot].stack,favorited=player.inventory[slot].favorited}).ToArray()
    };
    private static void ExpectDepositState(MEStorageUnitEntity unit,Player player,int stage)
    {
        ExpectSlot(player,1,ItemID.Wood,stage>=1 ? 0 : 7);
        ExpectSlot(player,2,ItemID.Gel,stage>=2 ? 0 : 5);
        ExpectSlot(player,3,ItemID.Gel,stage>=2 ? 0 : 9);
        ExpectSlot(player,4,ItemID.StoneBlock,stage>=3 ? 0 : 11);
        ExpectSlot(player,5,ItemID.StoneBlock,13);
        ExpectSlot(player,6,ItemID.CopperOre,stage>=3 ? 0 : 17);
        ExpectSlot(player,0,ModContent.ItemType<MEStorageUpgrade>(),stage>=3 ? 0 : 1);
        ExpectSlot(player,50,ItemID.SilverCoin,stage>=4 ? 0 : 23);
        ExpectSlot(player,54,ItemID.WoodenArrow,stage>=4 ? 0 : 29);
        ExpectSlot(player,55,ItemID.WoodenArrow,31);
        Require(Stock(unit,ItemID.Wood)==(stage>=1 ? 23 : 16)
            && Stock(unit,ItemID.Gel)==(stage>=2 ? 14 : 0)
            && Stock(unit,ItemID.StoneBlock)==(stage>=3 ? 11 : 0)
            && Stock(unit,ItemID.CopperOre)==(stage>=3 ? 17 : 0)
            && Stock(unit,ModContent.ItemType<MEStorageUpgrade>())==(stage>=3 ? 1 : 0)
            && Stock(unit,ItemID.SilverCoin)==(stage>=4 ? 23 : 0)
            && Stock(unit,ItemID.WoodenArrow)==(stage>=4 ? 29 : 0)
            && Stock(unit,ItemID.Ruby)==0,
            "Deposit ledger mismatch at completed stage "+stage);
    }

    private void ServerDepositCheck(Player actor,MEStorageUnitEntity unit)
    {
        if(!Exists("start-deposits.flag"))
        {
            ExpectDepositState(unit,actor,0);
            if(_requireObserver) ExpectSlot(NamedPlayer(ObserverName) ?? throw new InvalidOperationException("Observer disconnected"),2,ItemID.Ruby,7);
            Write("start-deposits.flag",DepositSnapshot(unit,actor));
            Pass("upgrade validation complete; isolated deposit fixture inventory verified on server");
        }
        foreach(var phase in new[] { ("single",1),("type",2),("bulk",3),("extras",4) })
        {
            string clientFile="actor-deposit-"+phase.Item1+"-pass.json", serverFile="server-deposit-"+phase.Item1+"-pass.json";
            if(!Exists(clientFile) || Exists(serverFile)) continue;
            ExpectDepositState(unit,actor,phase.Item2);
            Write(serverFile,DepositSnapshot(unit,actor));
            Pass("live "+phase.Item1+" deposit converged on server; favorite and inventory scope counts match");
        }
        if(_requireObserver && Exists("observer-deposit-denied-pass.json") && !Exists("server-deposit-denied-pass.json"))
        {
            Player observer=NamedPlayer(ObserverName) ?? throw new InvalidOperationException("Observer disconnected");
            ExpectSlot(observer,2,ItemID.Ruby,7);
            Require(Stock(unit,ItemID.Ruby)==0
                && NetworkRegistry.GetLevel(MENetwork.CanonicalAnchor(MENetwork.Collect(_unitPosition.X,_unitPosition.Y)),ObserverName)==NetworkRegistry.LevelReadOnly,
                "Read-only deposit changed server ledger or permission");
            Write("server-deposit-denied-pass.json",DepositSnapshot(unit,observer));
            Pass("read-only observer's live deposit was rejected without item consumption");
        }
        if(Exists("server-deposit-extras-pass.json") && (!_requireObserver || Exists("server-deposit-denied-pass.json"))
            && !Exists("server-complete.json"))
        {
            ExpectDepositState(unit,actor,4);
            Write("server-complete.json",new {level=unit.Level,capacity=unit.CurrentCapacity,deposits=true,
                inventoryAndLedger=DepositSnapshot(unit,actor),observerDenied=_requireObserver});
            Pass("all enabled live upgrade and deposit stages completed with authoritative inventory accounting");
        }
    }

    private void ActorDepositCheck(MEStorageUnitEntity unit)
    {
        Player player=Main.LocalPlayer;
        if(!Exists("start-deposits.flag")) return;
        if(_depositPhase==0)
        {
            ExpectDepositState(unit,player,0);
            MENet.DepositScope(_unitPosition,1,ItemKey.FromItem(player.inventory[1]),sameType:false,extras:false);
            Require(MENet.InventoryDepositPending,"Public single-slot deposit did not begin");
            _depositPhase=1; return;
        }
        if(MENet.InventoryDepositPending) return;
        if(_depositPhase==1)
        {
            ExpectDepositState(unit,player,1);
            Write("actor-deposit-single-pass.json",DepositSnapshot(unit,player));
            Pass("single-slot deposit acknowledged: selected stack empty, other stacks unchanged, live ledger updated");
            _depositPhase=2; return;
        }
        if(_depositPhase==2 && Exists("server-deposit-single-pass.json"))
        {
            MENet.DepositScope(_unitPosition,2,ItemKey.FromItem(player.inventory[2]),sameType:true,extras:false);
            Require(MENet.InventoryDepositPending,"Public same-type deposit did not begin");
            _depositPhase=3; return;
        }
        if(_depositPhase==3)
        {
            ExpectDepositState(unit,player,2);
            Write("actor-deposit-type-pass.json",DepositSnapshot(unit,player));
            Pass("same-type deposit acknowledged for both stacks without consuming unrelated materials");
            _depositPhase=4; return;
        }
        if(_depositPhase==4 && Exists("server-deposit-type-pass.json"))
        {
            MENet.DepositAll(_unitPosition,includeAmmoAndCoins:false);
            Require(MENet.InventoryDepositPending,"Public main-inventory bulk deposit did not begin");
            _depositPhase=5; return;
        }
        if(_depositPhase==5)
        {
            ExpectDepositState(unit,player,3);
            Require(player.inventory[5].favorited && player.inventory[55].favorited,"Favorite flags were unexpectedly cleared");
            Write("actor-deposit-bulk-pass.json",DepositSnapshot(unit,player));
            Pass("main-inventory bulk deposit acknowledged; favorite stack, coins and ammunition retained");
            _depositPhase=6; return;
        }
        if(_depositPhase==6 && Exists("server-deposit-bulk-pass.json"))
        {
            MENet.DepositAll(_unitPosition,includeAmmoAndCoins:true);
            Require(MENet.InventoryDepositPending,"Public extended-scope bulk deposit did not begin");
            _depositPhase=7; return;
        }
        if(_depositPhase==7)
        {
            ExpectDepositState(unit,player,4);
            Require(player.inventory[5].favorited && player.inventory[55].favorited,"Extended bulk deposit lost favorite flags");
            Write("actor-deposit-extras-pass.json",DepositSnapshot(unit,player));
            Pass("extended bulk deposit acknowledged for coins and ammunition; favorite ammunition retained");
            _depositPhase=8; return;
        }
        if(!_craft && _depositPhase==8 && Exists("server-final-complete.json")) _done=true;
    }

    private void ObserverDepositCheck(MEStorageUnitEntity unit)
    {
        if(!Exists("start-deposits.flag")) return;
        Player observer=Main.LocalPlayer;
        if(!_depositDeniedRequested)
        {
            ExpectSlot(observer,2,ItemID.Ruby,7);
            MENet.DepositScope(_unitPosition,2,ItemKey.FromItem(observer.inventory[2]),sameType:false,extras:false);
            Require(MENet.InventoryDepositPending,"Observer public single-slot deposit did not begin");
            _depositDeniedRequested=true; return;
        }
        if(MENet.InventoryDepositPending) return;
        if(!Exists("observer-deposit-denied-pass.json"))
        {
            ExpectSlot(observer,2,ItemID.Ruby,7);
            Require(Stock(unit,ItemID.Ruby)==0,"Denied observer deposit appeared in replicated ledger");
            Write("observer-deposit-denied-pass.json",DepositSnapshot(unit,observer));
            Pass("read-only live deposit completed without changing requester stack or network stock");
        }
        if(Exists("server-final-complete.json")) _done=true;
    }

    private static int FindLoadedTorchRecipe(MEMolecularAssemblerEntity assembler)
    {
        for (int index=0;index<Recipe.numRecipes;index++)
        {
            Recipe recipe=Main.recipe[index];
            if (recipe==null || recipe.Mod!=null || recipe.Disabled || recipe.createItem.type!=ItemID.Torch
                || recipe.createItem.stack!=3 || !ItemKey.FromItem(recipe.createItem).Equals(ItemKey.Crafted(ItemID.Torch))) continue;
            Item[] ingredients=recipe.requiredItem.Where(item=>item!=null && !item.IsAir).ToArray();
            if (ingredients.Length==2 && ingredients.Any(item=>item.type==ItemID.Wood && item.stack==1)
                && ingredients.Any(item=>item.type==ItemID.Gel && item.stack==1)
                && RecipeExecutionPolicy.BlockReason(recipe)==null && assembler.CanUseRecipe(recipe)) return index;
        }
        return -1;
    }

    private MEMolecularAssemblerEntity CraftAssembler()
    {
        var assembler=MENetwork.FindAssembler(_unitPosition.X,_unitPosition.Y);
        Require(assembler!=null && assembler.Position.Equals(new Point16(_unitPosition.X+1,_unitPosition.Y)),
            "Expected adjacent isolated assembler was not found");
        Require(MENetwork.FindCraftingCpu(_unitPosition.X,_unitPosition.Y)==null,"Craft fixture unexpectedly contains a CPU");
        return assembler;
    }

    private static void ExpectCraftStock(MEStorageUnitEntity unit)
    {
        Require(Stock(unit,ItemID.Torch)==8 && Stock(unit,ItemID.Wood)==22 && Stock(unit,ItemID.Gel)==13,
            "No-CPU craft did not preserve five existing torches, add one three-torch batch and consume one Wood/Gel each");
    }

    private object CraftSnapshot(MEStorageUnitEntity unit) => new
    {
        level=unit.Level,capacity=unit.CurrentCapacity,recipeIndex=_craftRecipeIndex,
        torch=Stock(unit,ItemID.Torch),wood=Stock(unit,ItemID.Wood),gel=Stock(unit,ItemID.Gel),
        ledgerTotal=unit.Ledger.StoredTotalAmount,craftTicksPerItem=MEMolecularAssemblerEntity.TicksPerItem
    };

    private void ServerCraftCheck(Player actor,MEStorageUnitEntity unit)
    {
        if (Exists("server-final-complete.json")) return;
        MEMolecularAssemblerEntity assembler=CraftAssembler();
        if (!Exists("start-craft.flag"))
        {
            ExpectDepositState(unit,actor,4);
            Require(Stock(unit,ItemID.Torch)==5 && assembler.Target.IsAir && assembler.ActiveKey.IsAir,
                "Craft fixture no longer has five existing torches and an idle no-CPU assembler");
            Require(FindLoadedTorchRecipe(assembler)==_craftRecipeIndex,"Loaded Torch recipe changed after fixture setup");
            // Public Order selects a route through the real planner. Validate its actual preflight plan,
            // including any mod-pack alternatives, rather than forcing a chosen recipe or modifying registrations.
            CraftPlan plan=StorageSystem.Planner.Plan(unit.Ledger,ItemKey.Crafted(ItemID.Torch),1,
                assembler.CanUseRecipe,useExistingTarget:false);
            Require(plan.FullySatisfied && plan.Recipes.Contains(_craftRecipeIndex)
                && plan.Consumed.Count==2 && plan.Consumed.GetValueOrDefault(ItemKey.Crafted(ItemID.Wood))==1
                && plan.Consumed.GetValueOrDefault(ItemKey.Crafted(ItemID.Gel))==1
                && plan.Outputs.Count==1 && plan.Outputs.GetValueOrDefault(ItemKey.Crafted(ItemID.Torch))==3,
                "Real loaded recipes selected a different Torch route; the fixture must not force or fabricate its expected recipe");
            Write("server-craft-before.json",CraftSnapshot(unit));
            Write("start-craft.flag",CraftSnapshot(unit));
            Pass("real loaded Torch plan verified against final deposited stock; awaiting public client order");
            return;
        }
        if (!Exists("actor-craft-pass.json")) return;
        ExpectCraftStock(unit);
        Require(assembler.Target.IsAir && assembler.ActiveKey.IsAir,"Server assembler still has unfinished craft work");
        Write("server-craft-pass.json",CraftSnapshot(unit));
        Write("server-final-complete.json",new {level=unit.Level,capacity=unit.CurrentCapacity,
            deposits=true,craft=true,craftResult=CraftSnapshot(unit),observerDenied=_requireObserver});
        Pass("real TCP no-CPU order added three torches without consuming existing target stock; client and server agree");
    }

    private void ActorCraftCheck(MEStorageUnitEntity unit)
    {
        if (!Exists("start-craft.flag")) return;
        MEMolecularAssemblerEntity assembler=CraftAssembler();
        if (!_craftRequested)
        {
            ExpectDepositState(unit,Main.LocalPlayer,4);
            Require(Stock(unit,ItemID.Torch)==5,"Actor did not receive the existing target stock before placing craft order");
            Write("actor-craft-request.json",CraftSnapshot(unit));
            MENet.Order(_unitPosition,ItemKey.Crafted(ItemID.Torch),1);
            _craftRequested=true;
            _craftRequestFrame=_frames;
            Mod.Logger.Info("AE2 LIVE CRAFT REQUEST: public MENet.Order from real client; target Torch; amount one; no CPU");
            return;
        }
        if (!Exists("actor-craft-pass.json"))
        {
            if (_frames-_craftRequestFrame>60*60)
                throw new TimeoutException("Live craft did not finish within the isolated fixture budget; assembler status="
                    +assembler.LastStatus+", target="+assembler.Target+", active="+assembler.ActiveKey);
            if (Stock(unit,ItemID.Torch)==5) return;
            ExpectCraftStock(unit);
            if (!assembler.Target.IsAir || !assembler.ActiveKey.IsAir) return; // Ledger and assembler are distinct normal packets.
            Write("actor-craft-pass.json",CraftSnapshot(unit));
            Pass("requester received real ledger and assembler completion: Torch eight, Wood twenty-two, Gel thirteen");
        }
        if (Exists("server-final-complete.json")) _done=true;
    }
}

// Global hooks remain inert outside the explicitly guarded client and its isolated temporary character.
public sealed class LiveIntegrationPickupGuard : GlobalItem
{
    public override bool IsLoadingEnabled(Mod mod) => LiveIntegrationRegression.IsFixtureEnabledForProcess(clientOnly:true);

    public override bool CanPickup(Item item,Player player)
    {
        if (!LiveIntegrationRegression.IsIsolatedClientPlayer(player)) return true;
        LiveIntegrationRegression.RecordBlockedPickup(item);
        return false;
    }
}

public sealed class LiveIntegrationPlayerGuard : ModPlayer
{
    public override bool IsLoadingEnabled(Mod mod) => LiveIntegrationRegression.IsFixtureEnabledForProcess(clientOnly:true);

    public override void SetControls()
    {
        if (!LiveIntegrationRegression.IsIsolatedClientPlayer(Player)) return;
        Player.controlUseItem=false;
        Player.controlUseTile=false;
        Player.controlThrow=false;
    }
    public override bool FreeDodge(Player.HurtInfo info) => LiveIntegrationRegression.IsIsolatedClientPlayer(Player);
}
