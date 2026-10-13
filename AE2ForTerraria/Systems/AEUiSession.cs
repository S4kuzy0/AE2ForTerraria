using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq.Expressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI;
using AE2ForTerraria.UI;

namespace AE2ForTerraria.Systems;

/// <summary>One input owner for the complete AE window stack.</summary>
public sealed class AEUiSession : ModSystem
{
    internal const string LayerName = "AE2ForTerraria: Interface";
    private static bool _active, _closing;
    private static ulong _closedAt;
    private static int _ownedDepth;
    private static Action _drawPendingMouseText;
    private static readonly Item EmptyHover = new();
    private sealed class InputCoordinate
    {
        internal readonly Func<int> Read;
        internal readonly Action<int> Write;
        internal InputCoordinate(string name)
        {
            var field=typeof(PlayerInput).GetField(name,BindingFlags.Static|BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(PlayerInput).FullName,name);
            var target=Expression.Field(null,field);var value=Expression.Parameter(typeof(int));
            Read=Expression.Lambda<Func<int>>(target).Compile();
            Write=Expression.Lambda<Action<int>>(Expression.Assign(target,value),value).Compile();
        }
    }
    private static readonly InputCoordinate OriginalX=new("_originalMouseX"), OriginalY=new("_originalMouseY"),
        OriginalLastX=new("_originalLastMouseX"), OriginalLastY=new("_originalLastMouseY");

    internal static bool HasWindows => !Main.dedServ && !Main.gameMenu && UISystem.IsOpen;
    internal static bool KeyboardCaptured => HasWindows &&
        ((FocusedInterface?.CurrentState as MovableWindowState)?.SearchBoxFocused == true || ImeInput.CompositionString.Length > 0);
    internal static Vector2 PointerPosition => new Vector2(PlayerInput.MouseInfo.X, PlayerInput.MouseInfo.Y) / Main.UIScale;
    internal static bool CoversPoint(Vector2 point) => Covers(UISystem.OverlayInterface, point)
        || Covers(UISystem.SecondaryInterface, point) || Covers(UISystem.Interface, point);
    private static bool Covers(UserInterface ui, Vector2 point) => ui?.CurrentState is MovableWindowState window
        && window.InteractionBounds.Contains(point.ToPoint());
    public static bool CapturesInput => !Main.dedServ && !Main.gameMenu && (_active || HasWindows && CoversPoint(PointerPosition));
    public static bool InputAllowed => !HasWindows || _ownedDepth > 0;
    internal static bool BlocksExternalInput => CapturesInput && _ownedDepth == 0;
    internal static UserInterface FocusedInterface => UISystem.OverlayInterface?.CurrentState != null
        ? UISystem.OverlayInterface : UISystem.SecondaryInterface?.CurrentState != null
            ? UISystem.SecondaryInterface : UISystem.Interface;

    private delegate void LayersHook(Action<List<GameInterfaceLayer>> original, List<GameInterfaceLayer> layers);
    private delegate bool LayerDrawHook(Func<GameInterfaceLayer, bool> original, GameInterfaceLayer layer);
    private delegate void UpdateHook(Action<UserInterface, GameTime> original, UserInterface ui, GameTime time);
    private delegate void StateHook(Action<UserInterface, UIState> original, UserInterface ui, UIState state);
    private delegate void TriggersHook(Action<Player, TriggersSet> original, Player player, TriggersSet triggers);
    private delegate void DropHook(Action<Player> original, Player player);

    public override void Load()
    {
        if (Main.dedServ) return;
        MonoModHooks.Add(Required(typeof(SystemLoader), nameof(SystemLoader.ModifyInterfaceLayers),
            typeof(List<GameInterfaceLayer>)), new LayersHook((original, layers) =>
        {
            original(layers);
            if (HasWindows) MoveToFront(layers);
        }));
        MonoModHooks.Add(Required(typeof(GameInterfaceLayer), nameof(GameInterfaceLayer.Draw)),
            new LayerDrawHook((original, layer) =>
        {
            if (!CapturesInput || layer.Name == LayerName || IsCursorLayer(layer.Name)) return original(layer);
            using var input = SuppressMouse();
            return original(layer);
        }));
        MonoModHooks.Add(Required(typeof(UserInterface), nameof(UserInterface.Update), typeof(GameTime)),
            new UpdateHook((original, ui, time) =>
        {
            if (IsOwned(ui))
            {
                if (!HasWindows || ReferenceEquals(ui, FocusedInterface) && InputAllowed) original(ui, time);
            }
            else if (!CapturesInput) original(ui, time);
        }));
        MonoModHooks.Add(Required(typeof(UserInterface), nameof(UserInterface.SetState), typeof(UIState)),
            new StateHook((original, ui, state) =>
        {
            original(ui, state);
            if (IsOwned(ui)) Synchronize();
        }));
        MonoModHooks.Add(Required(typeof(PlayerLoader), nameof(PlayerLoader.ProcessTriggers),
            typeof(Player), typeof(TriggersSet)), new TriggersHook((original, player, triggers) =>
        {
            if (player.whoAmI != Main.myPlayer || !KeyboardCaptured) original(player, triggers);
        }));
        MonoModHooks.Add(Required(typeof(Player), nameof(Player.DropSelectedItem)),
            new DropHook((original, player) =>
        {
            if (player.whoAmI != Main.myPlayer || !BlocksExternalInput && !MENet.CursorPending && !KeyboardCaptured) original(player);
        }));
        // Closing vanilla inventory normally returns/drops Main.mouseItem every update.
        // AE owns that cursor while its embedded backpack is open.
        MonoModHooks.Add(Required(typeof(Player), "dropItemCheck"), new DropHook((original, player) =>
        {
            if (player.whoAmI != Main.myPlayer || !CapturesInput && !MENet.CursorPending) { original(player); return; }
            player.noThrow = 2;
            if(!MENet.CursorPending) player.inventory[58] = Main.mouseItem.Clone();
        }));
        _drawPendingMouseText = Required(typeof(Main), "DrawPendingMouseText").CreateDelegate<Action>();
    }

    private static MethodInfo Required(Type type, string name, params Type[] parameters)
        => type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
            BindingFlags.Instance, null, parameters, null) ?? throw new MissingMethodException(type.FullName, name);

    private static bool IsOwned(UserInterface ui) => ReferenceEquals(ui, UISystem.Interface)
        || ReferenceEquals(ui, UISystem.SecondaryInterface) || ReferenceEquals(ui, UISystem.OverlayInterface);

    internal static bool IsCursorLayer(string name) => name == "Vanilla: Cursor" || name == "Vanilla: Mouse Item / NPC Head";

    internal static void MoveToFront(List<GameInterfaceLayer> layers)
    {
        int index = layers.FindIndex(layer => layer.Name == LayerName);
        if (index < 0) return;
        var ae = layers[index];
        layers.RemoveAt(index);
        GameInterfaceLayer cursor = null, held = null;
        for (int i = layers.Count - 1; i >= 0; i--)
        {
            if (layers[i].Name == "Vanilla: Cursor") { cursor = layers[i]; layers.RemoveAt(i); }
            else if (layers[i].Name == "Vanilla: Mouse Item / NPC Head") { held = layers[i]; layers.RemoveAt(i); }
        }
        layers.Add(ae);
        if (cursor != null) layers.Add(cursor);
        if (held != null) layers.Add(held);
    }

    internal static void Synchronize()
    {
        if (Main.dedServ || Main.gameMenu) return;
        if (HasWindows && CoversPoint(PointerPosition))
        {
            Main.LocalPlayer.mouseInterface = true;
            PlayerInput.LockVanillaMouseScroll("AE2ForTerraria");
        }
        // A press that starts on AE remains owned until release, even if a window
        // moves or closes underneath it. It cannot become a background click.
        if (HasWindows && CoversPoint(PointerPosition) && (Main.mouseLeft || Main.mouseRight))
        { _active = true; _closing = false; }
        if (_active && !Main.mouseLeft && !Main.mouseRight && !_closing)
        {
            _closing = true;
            _closedAt = Main.GameUpdateCount;
        }
    }

    internal static void Advance()
    {
        if (Main.gameMenu) { Reset(false); return; }
        if (UISystem.IsOpen && Main.LocalPlayer.dead) UISystem.CloseAll();
        Synchronize();
        if (!_active) return;
        Main.LocalPlayer.mouseInterface = true;
        PlayerInput.LockVanillaMouseScroll("AE2ForTerraria");
        if (_closing && Main.GameUpdateCount > _closedAt + 1 && !Main.mouseLeft && !Main.mouseRight
            && !Main.keyState.IsKeyDown(Keys.Escape)) Reset(true);
    }

    internal static void Reset(bool restoreInventory)
    {
        _active = _closing = false;
        _ownedDepth = 0;
    }

    public override void ClearWorld() => Reset(false);
    public override void Unload() { Reset(false); _drawPendingMouseText = null; }

    internal static OwnedInput EnterOwnedInput() => new();
    internal readonly struct OwnedInput : IDisposable
    {
        public OwnedInput() { _ownedDepth++; }
        public void Dispose() { _ownedDepth = Math.Max(0, _ownedDepth - 1); }
    }

    internal static MouseSuppression SuppressMouse() => new();
    internal readonly struct MouseSuppression : IDisposable
    {
        private readonly int _x, _y, _inputX, _inputY, _wheel, _uiWheel;
        private readonly bool _left, _right, _leftRelease, _rightRelease;
        private readonly MouseState _mouse, _oldMouse;
        private readonly int _originalX,_originalY,_originalLastX,_originalLastY;
        public MouseSuppression()
        {
            _x = Main.mouseX; _y = Main.mouseY; _inputX = PlayerInput.MouseX; _inputY = PlayerInput.MouseY;
            _wheel = PlayerInput.ScrollWheelDelta; _uiWheel = PlayerInput.ScrollWheelDeltaForUI;
            _left = Main.mouseLeft; _right = Main.mouseRight;
            _leftRelease = Main.mouseLeftRelease; _rightRelease = Main.mouseRightRelease;
            _mouse = PlayerInput.MouseInfo; _oldMouse = PlayerInput.MouseInfoOld;
            _originalX=OriginalX.Read();_originalY=OriginalY.Read();_originalLastX=OriginalLastX.Read();_originalLastY=OriginalLastY.Read();
            OriginalX.Write(-10000);OriginalY.Write(-10000);OriginalLastX.Write(-10000);OriginalLastY.Write(-10000);
            Main.mouseX = Main.mouseY = PlayerInput.MouseX = PlayerInput.MouseY = -10000;
            Main.mouseLeft = Main.mouseRight = Main.mouseLeftRelease = Main.mouseRightRelease = false;
            PlayerInput.ScrollWheelDelta = PlayerInput.ScrollWheelDeltaForUI = 0;
            PlayerInput.MouseInfo = PlayerInput.MouseInfoOld = new MouseState(-10000, -10000,
                _mouse.ScrollWheelValue, ButtonState.Released, ButtonState.Released, ButtonState.Released,
                ButtonState.Released, ButtonState.Released);
        }
        public void Dispose()
        {
            Main.mouseX = _x; Main.mouseY = _y; PlayerInput.MouseX = _inputX; PlayerInput.MouseY = _inputY;
            Main.mouseLeft = _left; Main.mouseRight = _right;
            Main.mouseLeftRelease = _leftRelease; Main.mouseRightRelease = _rightRelease;
            PlayerInput.ScrollWheelDelta = _wheel; PlayerInput.ScrollWheelDeltaForUI = _uiWheel;
            PlayerInput.MouseInfo = _mouse; PlayerInput.MouseInfoOld = _oldMouse;
            OriginalX.Write(_originalX);OriginalY.Write(_originalY);OriginalLastX.Write(_originalLastX);OriginalLastY.Write(_originalLastY);
        }
    }

    internal static void DrawWindow(UserInterface ui, GameTime time)
    {
        if (ui?.CurrentState == null) return;
        if (ReferenceEquals(ui, FocusedInterface))
        {
            using var owned = EnterOwnedInput();
            ui.Draw(Main.spriteBatch, time);
        }
        else
        {
            using var ignored = SuppressMouse();
            ui.Draw(Main.spriteBatch, time);
        }
    }

    internal static void BeginHover()
    {
        Main.HoverItem = EmptyHover;
        Main.hoverItemName = string.Empty;
        Main.LocalPlayer.cursorItemIconEnabled = false;
    }

    internal static void DrawHover()
    {
        if (Main.mouseItem.type > 0 || string.IsNullOrEmpty(Main.hoverItemName)) return;
        using var owned = EnterOwnedInput();
        Main.instance.MouseText(Main.hoverItemName, Main.HoverItem?.rare ?? 0);
        _drawPendingMouseText?.Invoke();
    }
}
