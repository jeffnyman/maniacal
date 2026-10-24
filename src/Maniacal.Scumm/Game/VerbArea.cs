using Daventry.Core.Graphics;
using Maniacal.Scumm.Graphics;

namespace Maniacal.Scumm.Game;

/// <summary>A verb the scripts made: its name and where it shows. Mirrors ScummVM's <c>VerbSlot</c> for text verbs.</summary>
public sealed class VerbSlot
{
    /// <summary>The verb's number, which sentences use; 0 for an empty slot. Mirrors <c>verbid</c>.</summary>
    public int Id { get; internal set; }

    /// <summary>0 hidden, 1 shown, 2 shown dimmed. Mirrors <c>curmode</c>.</summary>
    public int Mode { get; internal set; }

    /// <summary>The name as the script gave it, with its @ fillers. Mirrors the <c>rtVerb</c> resource.</summary>
    public byte[] Name { get; internal set; } = [];

    /// <summary>The top left, in screen pixels. Mirrors <c>curRect.left</c> and <c>curRect.top</c>.</summary>
    public int X { get; internal set; }

    public int Y { get; internal set; }

    /// <summary>Where clicks stop counting: a column a character of the name, fillers too. Mirrors <c>curRect.right</c>.</summary>
    public int Right { get; internal set; }

    public int Bottom { get; internal set; }

    /// <summary>The preposition version 1 puts after the verb's object, or 0xFF to ask the object. Mirrors <c>prep</c>.</summary>
    public int Preposition { get; internal set; }

    /// <summary>The key that clicks it, or 0. Mirrors <c>key</c>.</summary>
    public int Key { get; internal set; }

    public int Color { get; internal set; }

    public int HiColor { get; internal set; }

    public int DimColor { get; internal set; }

    /// <summary>Where the name was last drawn, to clear it from. Mirrors <c>oldRect</c>.</summary>
    internal Rect? Drawn { get; set; }
}

/// <summary>
/// The bottom of the screen: the sentence line, the verbs and the inventory. Mirrors ScummVM's verb
/// virtual screen for versions 1 and 2, drawn into as the original drew into it: <c>drawVerb</c>,
/// <c>restoreVerbBG</c>, <c>redrawVerbs</c>, <c>verbMouseOver</c>, <c>findVerbAtPos</c>,
/// <c>ScummEngine_v2::drawSentence</c>, <c>redrawV2Inventory</c>, <c>checkV2MouseOver</c> and
/// <c>checkV2Inventory</c>.
/// </summary>
/// <remarks>
/// Verbs are placed in screen pixels; the sentence line is the area's first 8 lines and the
/// inventory its lines 32 to 47, two names a row either side of the scroll arrows.
/// </remarks>
public sealed class VerbArea
{
    /// <summary>Mirrors <c>_numVerbs</c> for these versions.</summary>
    public const int SlotCount = 100;

    /// <summary>Where the inventory starts in the area. Mirrors <c>inventoryArea</c>.</summary>
    public const int InventoryTop = 32;

    // the mouse-over boxes of initV2MouseOver: four inventory places, the two arrows, the sentence line
    public const int UpArrow = 4, DownArrow = 5, SentenceLine = 6;

    // the DOS colours of initV2MouseOver, o2_verbOps and drawSentence
    private const int TextColor = 13, ArrowColor = 1, HighlightColor = 14;

    // the C64's (initV2MouseOver, ScummEngine_v0::resetVerbs, flushSentenceLine)
    private const int C64TextColor = 16, C64ArrowColor = 6, C64HighlightColor = 7;
    private const int C64VerbColor = 5, C64VerbHighlight = 7, C64VerbDimColor = 11;
    private const int VerbColor = 2, VerbDimColor = 8;
    private const int SentenceChars = 40, InventoryChars = 18;

    // the keys o2_verbOps gives the first 15 slots
    private const string Keys = "qwertasdfgzxcvb";

    private static (Rect Rect, int Color)[] MakeBoxes(int text, int arrow, int offset) =>
    [
        (new Rect(0 + offset, 32 + offset, 144, 8), text),
        (new Rect(176 + offset, 32 + offset, 144, 8), text),
        (new Rect(0 + offset, 40 + offset, 144, 8), text),
        (new Rect(176 + offset, 40 + offset, 144, 8), text),
        (new Rect(144 + offset, 32 + offset, 32, 8), arrow),
        (new Rect(144 + offset, 40 + offset, 32, 8), arrow),
        (new Rect(0 + offset, 0 + offset, 320, 8), text),
    ];

    private readonly (Rect Rect, int Color)[] _boxes;
    private readonly int _textColor, _highlightColor, _offset;

    // the interface bits of the user state (USERSTATE_IFACE_*)
    public const int ShowSentence = 0x20, ShowInventory = 0x40, ShowVerbs = 0x80;

    private readonly Font _font;
    private readonly Func<int> _userState;
    private int _mouseOverVerb;

    /// <param name="font">The font the names are drawn in.</param>
    /// <param name="top">The screen line the area starts at.</param>
    /// <param name="userState">The user state, whose interface bits say what shows (<c>_userState</c>).</param>
    /// <param name="c64">Whether this is the C64's: its colours, and the sentence line and inventory a pixel right and down.</param>
    public VerbArea(Font font, int top, Func<int> userState, bool c64 = false)
    {
        _font = font;
        _userState = userState;
        _offset = c64 ? 1 : 0;
        _textColor = c64 ? C64TextColor : TextColor;
        _highlightColor = c64 ? C64HighlightColor : HighlightColor;
        _boxes = MakeBoxes(_textColor, c64 ? C64ArrowColor : ArrowColor, _offset);
        Top = top;
        Pixels = new Surface8(Width, ScreenHeight - top);
        for (int i = 0; i < SlotCount; i++)
        {
            Slots[i] = new VerbSlot();
        }
    }

    public const int Width = 320;

    private const int ScreenHeight = 200;

    /// <summary>The screen line the area starts at: the sentence line. Mirrors the verb screen's <c>topline</c>.</summary>
    public int Top { get; }

    /// <summary>The area as it is drawn now.</summary>
    public Surface8 Pixels { get; }

    public VerbSlot[] Slots { get; } = new VerbSlot[SlotCount];

    /// <summary>The mouse-over box highlighted, or -1. Mirrors <c>_mouseOverBoxV2</c>.</summary>
    public int MouseOverBox { get; private set; } = -1;

    /// <summary>The first carried object shown, a multiple of 2. Mirrors <c>_inventoryOffset</c>.</summary>
    public int InventoryOffset { get; set; }

    /// <summary>
    /// Makes a verb in a slot, or remakes it, and draws it. Version 1 places verbs a line lower,
    /// under its sentence line. Mirrors the new-verb case of <c>o2_verbOps</c>.
    /// </summary>
    public void SetVerb(int slot, int id, int column, int row, int preposition, ReadOnlySpan<byte> name, int version)
    {
        var verb = Slot(slot);
        verb.Id = id;
        verb.Color = VerbColor;
        verb.HiColor = HighlightColor;
        verb.DimColor = VerbDimColor;
        verb.Mode = 1;
        verb.Key = slot <= Keys.Length ? Keys[slot - 1] : 0;
        verb.Preposition = preposition;
        verb.Name = name.ToArray();
        verb.X = column * 8;
        verb.Y = row * 8 + (version == 1 ? 8 : 0);
        Draw(slot, false);
        MouseOver(0);
    }

    /// <summary>
    /// Makes one of the C64's verbs, which its engine makes itself, at a column and a row under the
    /// sentence line, and draws it. Mirrors a verb of <c>ScummEngine_v0::resetVerbs</c>.
    /// </summary>
    public void SetVerbV0(int slot, int id, int column, int row, int preposition, string name)
    {
        var verb = Slot(slot);
        verb.Id = id;
        verb.Color = C64VerbColor;
        verb.HiColor = C64VerbHighlight;
        verb.DimColor = C64VerbDimColor;
        verb.Mode = 1;
        verb.Key = 0;
        verb.Preposition = preposition;
        verb.Name = System.Text.Encoding.Latin1.GetBytes(name);
        verb.X = column * 8;
        verb.Y = Top + row * 8 + 8;
        Draw(slot, false);
    }

    /// <summary>Removes a verb, clearing where it showed. Mirrors <c>killVerb</c>.</summary>
    public void Kill(int slot)
    {
        var verb = Slot(slot);
        verb.Id = 0;
        verb.Mode = 0;
        verb.Name = [];
        Draw(slot, false);
        MouseOver(0);
    }

    /// <summary>Shows, hides or dims the verb with a number. Mirrors the on/off case of <c>o2_verbOps</c>.</summary>
    public void SetMode(int id, int mode)
    {
        int slot = SlotOf(id);
        Slots[slot].Mode = mode;
        Draw(slot, false);
        MouseOver(0);
    }

    /// <summary>The slot holding a verb, or 0. Mirrors <c>getVerbSlot</c> with no save id.</summary>
    public int SlotOf(int id)
    {
        for (int i = 1; i < SlotCount; i++)
        {
            if (Slots[i].Id == id)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// Draws a verb, highlighted or not, after clearing where it last showed; a hidden or empty verb
    /// is only cleared. Its clickable area runs a character for each byte of its name. Mirrors
    /// <c>drawVerb</c> for versions 0 to 2, with the C64's pixel offsets.
    /// </summary>
    public void Draw(int slot, bool highlighted)
    {
        if (slot == 0)
        {
            return;
        }

        var verb = Slots[slot];
        ClearDrawn(verb);
        if (verb.Mode == 0 || verb.Id == 0)
        {
            return;
        }

        // the C64 draws a verb a pixel right and down, and its area reaches a pixel further
        int color = verb.Mode == 2 ? verb.DimColor : highlighted && verb.HiColor != 0 ? verb.HiColor : verb.Color;
        var drawn = DrawString(verb.Name, verb.X + _offset, verb.Y + _offset, Width - 1 + _offset, color);
        verb.Right = _offset + Math.Min(verb.X + verb.Name.Length * 8, Width);
        verb.Bottom = drawn.Bottom + _offset;
        verb.Drawn = drawn;
    }

    /// <summary>The slot of the shown verb at a screen point, or 0; later slots come first. Mirrors <c>findVerbAtPos</c>.</summary>
    public int FindAt(int x, int y)
    {
        for (int i = SlotCount - 1; i > 0; i--)
        {
            var verb = Slots[i];
            if (verb.Mode == 1 && verb.Id != 0 && y >= verb.Y && y < verb.Bottom && x >= verb.X && x < verb.Right)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>Clears the whole area. Mirrors the <c>restoreBackground</c> of <c>ScummEngine_v2::setUserState</c>.</summary>
    public void Clear()
    {
        Pixels.Fill(0);
        foreach (var verb in Slots)
        {
            verb.Drawn = null;
        }
    }

    /// <summary>
    /// Draws every verb, the one under the mouse (when there is one) highlighted. Nothing shows while
    /// the verbs are off. Mirrors <c>redrawVerbs</c>.
    /// </summary>
    public void Redraw(int mouseVerb)
    {
        if ((_userState() & ShowVerbs) == 0)
        {
            return;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            Draw(i, i == mouseVerb && Slots[i].HiColor != 0);
        }

        _mouseOverVerb = mouseVerb;
    }

    /// <summary>Moves the highlight to the verb under the mouse. Mirrors <c>verbMouseOver</c>.</summary>
    public void MouseOver(int verb)
    {
        if ((_userState() & ShowVerbs) == 0 || _mouseOverVerb == verb)
        {
            return;
        }

        Draw(_mouseOverVerb, false);
        _mouseOverVerb = verb;
        if (Slots[verb].HiColor != 0)
        {
            Draw(verb, true);
        }
    }

    /// <summary>
    /// Shows a sentence on the sentence line, up to 40 characters not counting fillers, while the
    /// sentence line is on. Mirrors the drawing half of <c>ScummEngine_v2::drawSentence</c>.
    /// </summary>
    public void DrawSentence(ReadOnlySpan<byte> sentence)
    {
        if ((_userState() & ShowSentence) == 0)
        {
            return;
        }

        int length = 0, end = 0;
        while (end < sentence.Length && sentence[end] != 0)
        {
            if (sentence[end] != '@' && ++length > SentenceChars)
            {
                break;
            }

            end++;
        }

        Pixels.FillRect(_boxes[SentenceLine].Rect, 0);
        DrawString(sentence[..end], _offset, Top + _offset, Width - 1 + _offset, _textColor);
    }

    /// <summary>
    /// Shows the carried objects from <see cref="InventoryOffset"/>, four at most, with arrows when
    /// there are more either way. Mirrors <c>redrawV2Inventory</c> for DOS.
    /// </summary>
    public void DrawInventory(IReadOnlyList<string> names)
    {
        MouseOverBox = -1;
        if ((_userState() & ShowInventory) == 0)
        {
            return;
        }

        Pixels.FillRect(new Rect(_offset, InventoryTop + _offset, Width - _offset, Pixels.Height - InventoryTop - _offset), 0);
        int shown = Math.Min(names.Count - InventoryOffset, 4);
        for (int i = 0; i < shown; i++)
        {
            var box = _boxes[i];
            var name = System.Text.Encoding.Latin1.GetBytes(names[InventoryOffset + i]);
            DrawString(name.AsSpan(0, Math.Min(name.Length, InventoryChars)), box.Rect.Left, Top + box.Rect.Top, box.Rect.Right - 1, box.Color);
        }

        if (InventoryOffset > 0)
        {
            var box = _boxes[UpArrow];
            DrawString(" \x01\x02"u8, box.Rect.Left, Top + box.Rect.Top, box.Rect.Right - 1, box.Color);
        }

        if (InventoryOffset + 4 < names.Count)
        {
            var box = _boxes[DownArrow];
            DrawString(" \x03\x04"u8, box.Rect.Left, Top + box.Rect.Top, box.Rect.Right - 1, box.Color);
        }
    }

    /// <summary>
    /// Highlights the box under the mouse while the inventory shows: its text colour becomes the
    /// highlight, and the last box's goes back. Mirrors <c>checkV2MouseOver</c>.
    /// </summary>
    public void CheckMouseOver(int x, int y, bool cursorShown)
    {
        if ((_userState() & ShowInventory) == 0)
        {
            MouseOverBox = -1;
            return;
        }

        int box = -1;
        if (cursorShown)
        {
            box = Array.FindIndex(_boxes, b => b.Rect.Contains(x, y - Top));
        }

        if (box == MouseOverBox)
        {
            return;
        }

        if (MouseOverBox != -1)
        {
            Recolor(_boxes[MouseOverBox].Rect, _highlightColor, _boxes[MouseOverBox].Color);
        }

        if (box != -1)
        {
            Recolor(_boxes[box].Rect, _boxes[box].Color, _highlightColor);
        }

        MouseOverBox = box;
    }

    /// <summary>
    /// What a click in the inventory does: scrolls with the arrows, or names the place clicked, 0 to
    /// 3, or -1 for none. The caller picks the object. Mirrors <c>checkV2Inventory</c>.
    /// </summary>
    public int ClickInventory(int x, int y, int carried)
    {
        y -= Top;
        if (y < InventoryTop)
        {
            return -1;
        }

        if (_boxes[UpArrow].Rect.Contains(x, y))
        {
            if (InventoryOffset >= 2)
            {
                InventoryOffset -= 2;
                return -2;
            }
        }
        else if (_boxes[DownArrow].Rect.Contains(x, y))
        {
            if (InventoryOffset + 4 < carried)
            {
                InventoryOffset += 2;
                return -2;
            }
        }

        for (int place = 0; place < 4; place++)
        {
            if (_boxes[place].Rect.Contains(x, y))
            {
                return place;
            }
        }

        return -1;
    }

    /// <summary>
    /// Copies the verbs into a save as ScummVM keeps them: each slot's rectangles, colours, mode,
    /// key and preposition, and its name as a verb resource ending in 0 (<c>rtVerb</c>). An empty
    /// slot is as <c>resetScumm</c> leaves it.
    /// </summary>
    internal void SaveTo(Saves.ScummState s)
    {
        s.InventoryOffset = InventoryOffset;
        for (int i = 0; i < SlotCount; i++)
        {
            var verb = Slots[i];
            var state = s.Verbs[i] = new Saves.ScummState.VerbState();
            if (verb.Id == 0 && verb.Name.Length == 0)
            {
                continue;
            }

            state.Id = verb.Id;
            state.Left = verb.X;
            state.Top = verb.Y;
            state.Right = verb.Right;
            state.Bottom = verb.Bottom;
            (state.OldLeft, state.OldTop, state.OldRight, state.OldBottom) = verb.Drawn is { } drawn
                ? (drawn.Left, drawn.Top, drawn.Right, drawn.Bottom)
                : (-1, 0, 0, 0);
            state.Color = verb.Color;
            state.HiColor = verb.HiColor;
            state.DimColor = verb.DimColor;
            state.Charset = 0;
            state.Mode = verb.Mode;
            state.Key = verb.Key;
            state.Prep = verb.Preposition;
            if (verb.Name.Length > 0)
            {
                s.Resources[(Saves.ScummState.TypeVerb, i)] = new Saves.ScummState.Resource([.. verb.Name, 0]);
            }
        }
    }

    /// <summary>Takes the verbs from a save; the area is drawn again by the caller.</summary>
    internal void LoadFrom(Saves.ScummState s)
    {
        InventoryOffset = s.InventoryOffset;
        MouseOverBox = -1;
        _mouseOverVerb = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            var state = s.Verbs[i];
            var verb = Slots[i] = new VerbSlot
            {
                Id = state.Id,
                X = state.Left,
                Y = state.Top,
                Right = state.Right,
                Bottom = state.Bottom,
                Color = state.Color,
                HiColor = state.HiColor,
                DimColor = state.DimColor,
                Mode = state.Mode,
                Key = state.Key,
                Preposition = state.Prep,
            };

            if (s.Resources.TryGetValue((Saves.ScummState.TypeVerb, i), out var name))
            {
                int end = Array.IndexOf(name.Data, (byte)0);
                verb.Name = name.Data[..(end < 0 ? name.Data.Length : end)];
            }
        }

        Pixels.Fill(0);
    }

    private VerbSlot Slot(int slot)
        => slot is > 0 and < SlotCount ? Slots[slot] : throw new InvalidOperationException($"Verb slot {slot} is not one of 1 to {SlotCount - 1}.");

    private void ClearDrawn(VerbSlot verb)
    {
        if (verb.Drawn is { } drawn)
        {
            Pixels.FillRect(drawn with { Y = drawn.Y - Top }, 0);
            verb.Drawn = null;
        }
    }

    private void Recolor(Rect rect, int from, int to)
    {
        for (int y = rect.Top; y < rect.Bottom && y < Pixels.Height; y++)
        {
            var row = Pixels.Row(y).Slice(rect.Left, Math.Min(rect.Width, Width - rect.Left));
            foreach (ref byte pixel in row)
            {
                if (pixel == from)
                {
                    pixel = (byte)to;
                }
            }
        }
    }

    /// <summary>
    /// Draws text at a screen point and returns the rectangle its characters cover, in screen
    /// pixels. Fillers (@) take no room, and a character that would pass <paramref name="right"/>
    /// is left out. Mirrors <c>drawString</c> with <c>CharsetRendererV3::printChar</c>.
    /// </summary>
    private Rect DrawString(ReadOnlySpan<byte> text, int x, int y, int right, int color)
    {
        int left = x;
        foreach (byte c in text)
        {
            if (c == 0)
            {
                break;
            }

            if (c == '@' || x + Font.CharWidth > right + 1)
            {
                continue;
            }

            if (c < Font.GlyphCount)
            {
                _font.DrawGlyph(Pixels, c, x, y - Top, (byte)color);
            }

            x += Font.CharWidth;
        }

        return Rect.FromBounds(left, y, x, y + Font.Height);
    }
}
