using Lua.CodeAnalysis.Syntax.Nodes;
using SrvSurvey.game;
using SrvSurvey.plotters;
using SrvSurvey.widgets;
using System.ComponentModel;
using System.Diagnostics;

namespace SrvSurvey.forms;

[System.ComponentModel.DesignerCategory("")]
internal abstract class BaseFormZippy : SizableForm, PlotterForm
{
    /// <summary> The set of all visible controls on this form. For perf reasons We manage these manually instead of using actual WinForms controls.handling). </summary>
    protected List<Ctrl> ctrls = [];

    /// <summary> The ctrl that is "current" or active / has focus </summary>
    protected Ctrl? ctrlCurrent = null;

    protected bool mouseDown = false;
    protected bool mouseOverCtrl = false;

    public BaseFormZippy()
    {
        SetStyle(ControlStyles.UserPaint, true);
        SetStyle(ControlStyles.AllPaintingInWmPaint, true);
        SetStyle(ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

        this.Name = this.GetType().Name;
        this.Text = this.Name;
        this.ShowIcon = false;
        this.StartPosition = FormStartPosition.Manual;
        this.MinimizeBox = false;
        this.MaximizeBox = false;
        this.ControlBox = false;
        this.FormBorderStyle = FormBorderStyle.None;
        this.DoubleBuffered = true;
        this.ResizeRedraw = true;
        this.ShowInTaskbar = false;
        this.BackColor = C.black;
        this.ForeColor = C.orange;

        this.Opacity = 0;

        this.Activated += (o, s) => KeyboardHook.redirect = true;
        this.Deactivate += (o, s) => KeyboardHook.redirect = false;

        initCtrls();
        ctrlCurrent = ctrls.FirstOrDefault();
    }

    protected abstract void initCtrls();

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // position ourself over the top/right quadrant of the game
        var r = Elite.getWindowRect();
        if (r.Width > 0)
        {
            this.Width = (int)(r.Width * 0.3f);
            this.Height = (int)(r.Height * 0.5f);
            this.Left = r.Right - this.Width - 20;
            this.Top = r.Top + 10 + (PlotBase2.getPlotter<PlotQuestMini>()?.bottom ?? 20);
            Application.DoEvents();
        }
        this.BackgroundImage = GameGraphics.getBackgroundImage(this.ClientSize);
        this.Invalidate();

        // delay fading in to avoid initial renders that are always Window coloured
        Util.deferAfter(100, () => Util.fadeOpacity(this, 0.95f, Game.settings.fadeInDuration));
    }

    #region for PlotterForm

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool didFirstPaint { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool showing { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool forceHide { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool fading { get; set; }

    public void reposition(Rectangle gameRect) { /* no op */ }

    public void setOpacity(double newOpacity)
    {
        this.Opacity = newOpacity;
    }

    public void resetOpacity()
    {
        this.Opacity = 0.9f;
    }

    #endregion


    #region sibling navigation

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        //Debug.WriteLine($"ProcessCmdKey: {keyData}");
        if (keyData == Keys.Enter)
        {
            // "click" the current ctrl
            ctrlCurrent?.onClick();
            return true;
        }

        // find a sibling ctrl
        Ctrl? next = null;
        if (keyData == Keys.Escape)
            next = null;
        else if (keyData == Keys.Left)
            next = findSibling(ctrls, ctrlCurrent, (f, s) => s.r.Left < f?.r.Left, Side.Left, Side.Left);
        else if (keyData == Keys.Right)
            next = findSibling(ctrls, ctrlCurrent, (f, s) => s.r.Right > f?.r.Right, Side.Right, Side.Left);
        else if (keyData == Keys.Up)
            next = findSibling(ctrls, ctrlCurrent, (f, s) => s.r.Top < f?.r.Top, Side.Top, Side.Bottom);
        else if (keyData == Keys.Down)
            next = findSibling(ctrls, ctrlCurrent, (f, s) => s.r.Top > f?.r.Top, Side.Bottom, Side.Top);
        else if (keyData == Keys.Tab)
        {
            if (ctrlCurrent == null)
                next = ctrls.FirstOrDefault();
            else
            {
                var idx = ctrls.IndexOf(ctrlCurrent);
                if (idx == ctrls.Count - 1)
                    next = ctrls.FirstOrDefault();
                else
                    next = ctrls[idx + 1];
            }
        }

        //Debug.WriteLine($"{keyData} => was: {ctrlCurrent}, next: {next}");
        this.ctrlCurrent = next;
        this.Invalidate();
        return true;
    }

    private static Ctrl? findSibling(List<Ctrl> list, Ctrl? from, Func<Ctrl, Ctrl, bool> match, Side fs, Side ss)
    {
        if (from == null) return list.FirstOrDefault();

        // find next ctrl to the left
        var sibs = list.Where(sib => match(from, sib)).ToList();

        if (sibs.Count == 0) return null; // TODO: add another func to select by wrapping around
        if (sibs.Count == 1) return sibs[0];

        // choose best choice from possibles
        var next = sibs.MinBy(sib =>
        {
            var szF = getSide(from, fs);
            var szS = getSide(sib, ss);
            var szD = szF - szS;
            var dist = Math.Sqrt((szD.Width * szD.Width) + (szD.Height * szD.Height));
            return dist;
        });

        return next;
    }

    private static SizeF getSide(Ctrl ctrl, Side side)
    {
        switch (side)
        {
            case Side.Top: return new(ctrl.r.Left + (ctrl.r.Width / 2), ctrl.r.Top);
            case Side.Left: return new(ctrl.r.Left, ctrl.r.Top + (ctrl.r.Height / 2));
            case Side.Bottom: return new(ctrl.r.Left + (ctrl.r.Width / 2), ctrl.r.Bottom);
            case Side.Right: return new(ctrl.r.Right, ctrl.r.Top + (ctrl.r.Height / 2));
            default: throw new Exception($"Unexpected side: {side}");
        }
    }

    enum Side
    {
        Top,
        Left,
        Bottom,
        Right
    }

    #endregion


    #region ctrl rendering and mouse states

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        // tell any ctrls
        var dirty = false;
        mouseOverCtrl = false;
        foreach (var ctrl in this.ctrls)
        {
            var match = ctrl.r.Contains(e.Location);
            dirty |= ctrl.setHovered(match);
            if (match)
            {
                mouseOverCtrl = true;
                ctrlCurrent = ctrl;
                dirty = true;
            }
        }

        if (!mouseOverCtrl)
            ctrlCurrent = null;

        if (dirty)
            this.Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        mouseDown = true;
        this.Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        mouseDown = false;
        this.Invalidate();

        if (mouseOverCtrl && ctrlCurrent != null)
            ctrlCurrent.onClick();
    }

    #endregion
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var tt = new TextCursor(g, this);

        drawCommon(g, tt);
    }

    void drawCommon(Graphics g, TextCursor tt)
    {
        foreach (var ctrl in this.ctrls)
        {
            tt.dtx = ctrl.r.X;
            tt.dty = ctrl.r.Y;
            ctrl.render(g, tt, ctrl == ctrlCurrent, mouseOverCtrl && ctrl == ctrlCurrent && mouseDown);
        }
    }
}

/// <summary>
/// A proxy for control, but windowless.
/// </summary>
abstract class Ctrl
{
    public RectangleF r;
    protected bool hovered;
    public bool isCurrent;

    public abstract void render(Graphics g, TextCursor tt, bool isCurrent, bool isPressed);

    public virtual bool setHovered(bool hovered)
    {
        var changed = this.hovered != hovered;
        this.hovered = hovered;

        return changed;
    }

    public virtual void onClick()
    {

    }
}

/*
class BtnBorderTextCtrl : Ctrl
{
    public string text;
    private ColorSet csBorder = new()
    {
        normal = C.orangeDark,
        current = C.orange,
        pressed = C.menuGold,
        disabled = C.grey,
    };
    private ColorSet csBack = new()
    {
        normal = Color.Transparent,
        current = C.orangeDarker,
        pressed = C.orangeDark,
        disabled = C.grey,
    };
    private ColorSet csText = new()
    {
        normal = C.orange,
        current = C.menuGold,
        pressed = C.black,
        disabled = C.black,
    };
    private Pen? borderPen;
    private SolidBrush? backBrush;

    public override string ToString()
    {
        return this.text;
    }

    public override void onClick()
    {
        Debug.WriteLine($"CLICK: {text}");
    }

    public override void render(Graphics g, TextCursor tt, bool isCurrent, bool isPressed)
    {
        var pad = N.four;
        tt.dtx += pad;
        tt.dty += pad;

        //if (isPressed) Debugger.Break();
        var sz = TextRenderer.MeasureText(g, this.text, tt.font);
        r.Width = sz.Width + pad + pad;
        r.Height = sz.Height + pad + pad;

        // choose colours based on state
        var borderColor = csBorder.get(isCurrent, isPressed);
        if (borderPen == null || borderPen.Color != borderColor)
        {
            borderPen?.Dispose();
            borderPen = null;

            if (borderColor != Color.Transparent)
                borderPen = borderColor.toPen(1);
        }
        var backColor = csBack.get(isCurrent, isPressed);
        if (backBrush == null || backBrush.Color != backColor)
        {
            backBrush?.Dispose();
            backBrush = null;
            if (backColor != Color.Transparent)
                backBrush = backColor.toBrush();
        }

        // draw background and/or border?
        if (backBrush != null)
            g.FillRectangle(backBrush, r);

        if (borderPen != null)
            g.DrawRectangle(borderPen, this.r);

        // TODO: do we really want a notion of current (has-focus) as well as hovered? For now ... I think not

        // finally, draw the text
        tt.draw(this.text, csText.get(isCurrent, isPressed));
    }
}
*/

class BtnFillTextCtrl : Ctrl
{
    public string text;
    private ColorSet csBack = new()
    {
        normal = C.orangeDarker,
        current = C.orangeDark,
        pressed = C.orange,
        disabled = C.grey,
    };
    private ColorSet csText = new()
    {
        normal = C.orange,
        current = C.menuGold,
        pressed = C.black,
        disabled = C.black,
    };
    private Pen? borderPen;
    private SolidBrush? backBrush;

    public override string ToString()
    {
        return this.text;
    }

    public override void onClick()
    {
        Debug.WriteLine($"CLICK: {text}");
    }

    public override void render(Graphics g, TextCursor tt, bool isCurrent, bool isPressed)
    {
        var pad = N.four;
        tt.dtx += pad;
        tt.dty += pad;

        //if (isPressed) Debugger.Break();
        var sz = TextRenderer.MeasureText(g, this.text, tt.font);
        r.Width = sz.Width + pad + pad;
        r.Height = sz.Height + pad + pad;

        // choose colours based on state
        var backColor = csBack.get(isCurrent, isPressed);
        if (backBrush == null || backBrush.Color != backColor)
        {
            backBrush?.Dispose();
            backBrush = null;
            if (backColor != Color.Transparent)
                backBrush = backColor.toBrush();
        }

        // draw background and/or border?
        if (backBrush != null)
            g.FillRectangle(backBrush, r);

        if (borderPen != null)
            g.DrawRectangle(borderPen, this.r);

        // TODO: do we really want a notion of current (has-focus) as well as hovered? For now ... I think not

        // finally, draw the text
        tt.draw(this.text, csText.get(isCurrent, isPressed));
    }
}

struct ColorSet
{
    public Color normal;
    public Color current;
    public Color pressed;
    public Color disabled;

    public Color get(bool isCurrent, bool isPressed)
    {
        if (isPressed)
            return pressed;
        else if (isCurrent)
            return current;
        else
            return normal;
    }
}