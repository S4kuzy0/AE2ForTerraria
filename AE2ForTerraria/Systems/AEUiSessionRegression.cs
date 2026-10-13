using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.UI;
using Terraria.GameContent.UI.Elements;
using Microsoft.Xna.Framework.Input;
using AE2ForTerraria.UI;

namespace AE2ForTerraria.Systems;

internal static class AEUiSessionRegression
{
    private sealed class CountUpdates : MovableWindowState
    {
        public int Count;
        public override void OnInitialize() => InitWindow(new WindowGeometry(300,200,200,150),"Input fixture");
        protected override void UpdateWindow(GameTime gameTime) => Count++;
        public override void OnDeactivate() { }
    }

    public static void Run(Action<bool, string> check)
    {
        if (!Main.gameMenu || Main.dedServ || UISystem.IsOpen)
            throw new InvalidOperationException("UI session checks require an idle menu.");
        bool inventory = Main.playerInventory, block = Main.blockInput;
        Player previousPlayer = Main.player[Main.myPlayer];
        Item previousMouse = Main.mouseItem;
        var previousInputMouse=PlayerInput.MouseInfo;
        bool previousLeft=Main.mouseLeft, previousRight=Main.mouseRight;
        var names = new[] { nameof(UISystem.Interface), nameof(UISystem.SecondaryInterface), nameof(UISystem.OverlayInterface) };
        var previous = new object[names.Length];
        for (int index = 0; index < names.Length; index++) previous[index] = typeof(UISystem).GetProperty(names[index])!.GetValue(null);
        try
        {
            var main = new UserInterface(); var overlay = new UserInterface(); var secondary = new UserInterface();
            var lowerState = new CountUpdates(); var topState = new CountUpdates(); var externalState = new CountUpdates();
            var external = new UserInterface(); external.SetState(externalState);
            main.SetState(lowerState); overlay.SetState(topState);
            typeof(UISystem).GetProperty(nameof(UISystem.Interface))!.SetValue(null, main);
            typeof(UISystem).GetProperty(nameof(UISystem.SecondaryInterface))!.SetValue(null, secondary);
            typeof(UISystem).GetProperty(nameof(UISystem.OverlayInterface))!.SetValue(null, overlay);
            Main.player[Main.myPlayer] = new Player { whoAmI = Main.myPlayer, active = true };
            Main.playerInventory = true;
            Main.gameMenu = false;
            var bounds=topState.InteractionBounds;
            PlayerInput.MouseInfo=new MouseState((int)(bounds.Center.X*Main.UIScale),(int)(bounds.Center.Y*Main.UIScale),0,
                ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released);
            Main.mouseLeft=Main.mouseRight=false;
            Main.blockInput=false;
            AEUiSession.Synchronize();
            check(AEUiSession.CapturesInput && !AEUiSession.InputAllowed && Main.playerInventory && !Main.blockInput,
                "overlapping AE windows capture the pointer while native inventory and movement stay enabled");
            var held = new Item(ItemID.Wood, 17);
            Main.mouseItem = held;
            typeof(Player).GetMethod("dropItemCheck", System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.Invoke(Main.LocalPlayer, null);
            check(ReferenceEquals(Main.mouseItem, held) && Main.mouseItem.stack == 17
                && Main.LocalPlayer.inventory[58].type == ItemID.Wood && Main.LocalPlayer.inventory[58].stack == 17
                && Main.LocalPlayer.inventory[0].IsAir && Main.playerInventory,
                "AE pointer ownership preserves the cursor without hiding native inventory");
            MENet.CursorPending=true;Main.LocalPlayer.inventory[58]=new Item(ItemID.Wood,6);
            typeof(Player).GetMethod("dropItemCheck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic)!.Invoke(Main.LocalPlayer,null);
            check(Main.mouseItem.stack==17 && Main.LocalPlayer.inventory[58].stack==6,
                "pending cursor transaction preserves a native server update until the cursor acknowledgement arrives");
            MENet.CursorPending=false;
            var time = new GameTime();
            external.Update(time); main.Update(time); overlay.Update(time);
            check(externalState.Count == 0 && lowerState.Count == 0 && topState.Count == 0,
                "outside the AE input scope neither foreign nor background UI receives an update");
            using (AEUiSession.EnterOwnedInput())
            {
                check(AEUiSession.InputAllowed, "AE-owned input scope allows actual AE controls");
                external.Update(time); main.Update(time); overlay.Update(time);
            }
            check(topState.Count == 1 && lowerState.Count == 0 && externalState.Count == 0,
                "only the foremost AE overlay updates even inside the owned input scope");
            PlayerInput.MouseInfo=new MouseState(-1000,-1000,0,ButtonState.Released,ButtonState.Released,
                ButtonState.Released,ButtonState.Released,ButtonState.Released);
            external.Update(time);
            check(externalState.Count==1 && !AEUiSession.CapturesInput && !Main.blockInput,
                "outside AE window bounds background inventory UI and game controls remain interactive");

            var layers = new List<GameInterfaceLayer>();
            foreach (string name in new[] { AEUiSession.LayerName, "Vanilla: Cursor", "ExampleMod: Potion HUD", "Vanilla: Mouse Item / NPC Head", "ExampleMod: Last HUD" })
                layers.Add(new LegacyGameInterfaceLayer(name, () => true, InterfaceScaleType.UI));
            AEUiSession.MoveToFront(layers);
            check(layers[2].Name == AEUiSession.LayerName && layers[3].Name == "Vanilla: Cursor"
                && layers[4].Name == "Vanilla: Mouse Item / NPC Head",
                "AE moves after third-party HUD layers while cursor and held item remain above it");
            int mouseX = Main.mouseX, mouseY = Main.mouseY, wheel = PlayerInput.ScrollWheelDelta;
            bool left = Main.mouseLeft, right = Main.mouseRight;
            using (AEUiSession.SuppressMouse())
            {
                PlayerInput.SetZoom_UI();
                check(Main.mouseX < 0 && Main.mouseY < 0 && !Main.mouseLeft && !Main.mouseRight && PlayerInput.ScrollWheelDelta == 0,
                    "background draw layers retain suppressed pointer coordinates through native UI zoom setup");
            }
            check(Main.mouseX == mouseX && Main.mouseY == mouseY && Main.mouseLeft == left && Main.mouseRight == right
                && PlayerInput.ScrollWheelDelta == wheel, "background input suppression restores all sampled mouse state");
            main.SetState(null); overlay.SetState(null);
            AEUiSession.Reset(true);
            check(Main.playerInventory && !AEUiSession.CapturesInput,
                "ending the AE session restores the inventory state from before opening");
        }
        finally
        {
            Main.gameMenu = true;
            AEUiSession.Reset(false);
            MENet.CursorPending=false;
            Main.player[Main.myPlayer] = previousPlayer;
            Main.mouseItem = previousMouse;
            PlayerInput.MouseInfo=previousInputMouse;Main.mouseLeft=previousLeft;Main.mouseRight=previousRight;
            Main.playerInventory = inventory;
            Main.blockInput = block;
            for (int index = 0; index < names.Length; index++) typeof(UISystem).GetProperty(names[index])!.SetValue(null, previous[index]);
        }
    }
}
