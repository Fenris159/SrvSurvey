using SrvSurvey.game;
using SrvSurvey.plotters;
using SrvSurvey.widgets;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace SrvSurvey.forms;

[System.ComponentModel.DesignerCategory("")]
internal abstract class BaseFormZippy : SizableForm, PlotterForm
{
    /// <summary> The set of all visible controls on this form. For perf reasons We manage these manually instead of using actual WinForms controls.handling). </summary>
    public List<Ctrl> ctrls = [];
    public Ctrl? scrollFrom = null;
    public Rectangle scrollBox;
    public Size scrollPad;
    public float scrollUp;
    public float scrollMax;

    /// <summary> The ctrl that is "current" or active / has focus </summary>
    protected Ctrl? ctrlCurrent = null;
    /// <summary> The last ctrl that was "current" but isn't any more </summary>
    private Ctrl? ctrlLast = null;

    protected bool mouseDown = false;
    protected bool mouseOverCtrl = false;

    public BaseFormZippy()
    {
        ctrls.Clear();

        SetStyle(ControlStyles.UserPaint, true);
        SetStyle(ControlStyles.AllPaintingInWmPaint, true);
        SetStyle(ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

        this.Name = this.GetType().Name;
        this.ShowIcon = false;
        this.StartPosition = FormStartPosition.Manual;
        this.MinimizeBox = false;
        this.MaximizeBox = false;
        this.ControlBox = false;
        this.FormBorderStyle = FormBorderStyle.SizableToolWindow; // .None; // <-- TMP!
        this.DoubleBuffered = true;
        this.ResizeRedraw = true;
        //this.ShowInTaskbar = false; // <-- TMP!
        this.BackColor = C.black;
        this.ForeColor = C.orange;

        this.Opacity = 0;

        this.Activated += (o, s) => KeyboardHook.redirect = true;
        this.Deactivate += (o, s) => KeyboardHook.redirect = false;

    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        ctrlCurrent = ctrls.FirstOrDefault(sib => !sib.disabled);
        if (ctrlCurrent != null) ctrlLast = ctrlCurrent;

        // delay fading in to avoid initial renders that are always Window coloured
        Util.deferAfter(100, () => Util.fadeOpacity(this, 0.95f, Game.settings.fadeInDuration));
    }

    protected void addCtrl(params Ctrl[] newCtrls)
    {
        foreach (var ctrl in newCtrls)
        {
            ctrl.form = this;
            ctrls.Add(ctrl);
        }
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
        var last = ctrlCurrent ?? ctrlLast;
        //Debug.WriteLine($"ProcessCmdKey: {keyData}");
        if (keyData == Keys.Enter)
        {
            // "click" the current ctrl
            if (last?.onClick != null && !last.disabled)
                last.onClick();
            return true;
        }

        // find a sibling ctrl
        Ctrl? next = null;
        if (keyData == Keys.Escape)
            next = null;
        else if (keyData == Keys.Left)
            next = findSibling(ctrls, last, (f, s) => s.r.Left < f?.r.Left, Side.Left, Side.Left);
        else if (keyData == Keys.Right)
            next = findSibling(ctrls, last, (f, s) => s.r.Left > f?.r.Left, Side.Right, Side.Left);
        else if (keyData == Keys.Up)
            next = findSibling(ctrls, last, (f, s) => s.r.Top < f?.r.Top, Side.Top, Side.Bottom);
        else if (keyData == Keys.Down)
            next = findSibling(ctrls, last, (f, s) => s.r.Top > f?.r.Top, Side.Bottom, Side.Top);
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

        // only change if we found something
        if (next != null)
        {
            //Debug.WriteLine($"{keyData} => was: {last}, next: {next}");
            this.ctrlCurrent = next;
            this.ctrlLast = next;
            this.Invalidate();
        }
        return true;
    }

    private static Ctrl? findSibling(List<Ctrl> list, Ctrl? from, Func<Ctrl, Ctrl, bool> match, Side fs, Side ss)
    {
        if (from == null) return list.FirstOrDefault(sib => !sib.disabled);

        // find next ctrl to the left
        var sibs = list.Where(sib => sib != from && !sib.disabled && match(from, sib)).ToList();

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
        var doInvalidate = false;
        var newMouseOverCtrl = false;
        foreach (var ctrl in ctrls)
        {
            var x = e.Location.X;
            var y = e.Location.Y;
            if (scrollFrom != null && scrollBox.Contains(e.Location))
                y += (int)scrollUp;

            var match = ctrl.r.Contains(x, y);
            doInvalidate |= ctrl.setHovered(match);
            if (match)
            {
                newMouseOverCtrl = true;
                ctrlCurrent = ctrl;
                ctrlLast = ctrl;
                doInvalidate = true;
            }
        }

        // only clear this if mouse has just left a ctrl
        if (!newMouseOverCtrl && newMouseOverCtrl != mouseOverCtrl)
            ctrlCurrent = null;

        mouseOverCtrl = newMouseOverCtrl;

        if (doInvalidate)
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

        if (mouseOverCtrl && ctrlCurrent?.onClick != null && !ctrlCurrent.disabled)
            ctrlCurrent.onClick();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (scrollFrom != null)
        {
            scrollUp -= e.Delta * 0.5f;
            if (scrollUp < 0) scrollUp = 0;
            if (scrollUp > scrollMax) scrollUp = scrollMax;
            this.Invalidate();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (scrollFrom != null)
        {
            var w = scrollPad.Width;
            var h = scrollPad.Height;
            scrollBox = new(
                scrollBox.X, scrollBox.Y,
                w > 0 ? w : ClientSize.Width - scrollBox.X + w,
                h > 0 ? h : ClientSize.Height - scrollBox.Y + h
            );

            this.Invalidate();
        }
    }

    public void stopScroll()
    {
        scrollFrom = null;
        scrollUp = 0;
        scrollBox = default;
        scrollPad = default;
    }

    public void setScroll(int x, int y, int w, int h)
    {
        scrollUp = 0;
        scrollPad = new(w, h);
        scrollBox = new(
            x, y,
            w > 0 ? w : ClientSize.Width - x + w,
            h > 0 ? h : ClientSize.Height - y + h
        );
    }

    #endregion

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var tt = new TextCursor(g, this);
        tt.flags |= TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform;

        drawCommon(g, tt);

        render(g, tt);
    }

    protected virtual void render(Graphics g, TextCursor tt)
    {
        // no op
    }

    void drawCommon(Graphics g, TextCursor tt)
    {
        for (var n = 0; n < ctrls.Count; n++)
        {
            var ctrl = ctrls[n];

            // apply scrolling for everything that follows
            if (ctrl == scrollFrom)
            {
                g.Clip = new Region(scrollBox);
                g.TranslateTransform(0, -scrollUp);
            }

            ctrl.setOrigin(this.ClientSize);
            tt.dtx = ctrl.r.X;
            tt.dty = ctrl.r.Y;
            ctrl.render(g, tt, ctrl == ctrlCurrent, mouseOverCtrl && ctrl == ctrlCurrent && mouseDown, n == 0 ? null : ctrls[n - 1]);
        }

        if (scrollFrom != null)
        {
            g.ResetTransform();
            g.ResetClip();

            var lastBottom = ctrls.LastOrDefault()?.r.Bottom;
            if (lastBottom > scrollBox.Bottom)
            {
                scrollMax = lastBottom - scrollBox.Height - scrollBox.Top ?? 0;
                if (scrollUp > scrollMax) scrollUp = scrollMax;

                var ratio = scrollBox.Height / (lastBottom - scrollBox.Top);
                var x = ClientSize.Width - 10;
                var y = scrollUp * ratio;
                var h = scrollBox.Height * ratio;
                g.FillRectangle(C.Brushes.orangeDark, x, scrollBox.Top + (int)y, 6, (int)h);
            }
        }
    }
}

/// <summary>
/// A proxy for control, but windowless.
/// </summary>
abstract class Ctrl
{
    public BaseFormZippy form;
    public PointF pt;
    public RectangleF r;
    public bool disabled;
    protected bool hovered;
    public bool isCurrent;

    public abstract void render(Graphics g, TextCursor tt, bool isCurrent, bool isPressed, Ctrl? prior);

    public virtual bool setHovered(bool hovered)
    {
        var changed = this.hovered != hovered;
        this.hovered = hovered;

        return changed;
    }

    public Action onClick;

    public void setOrigin(SizeF sz)
    {
        // negative values mean re-position in from the right or bottom edge
        r.X = pt.X >= 0 ? pt.X : sz.Width - r.Width + pt.X;
        r.Y = pt.Y >= 0 ? pt.Y : sz.Height - r.Height + pt.Y;
    }
}

struct ColorSet
{
    public Color normal;
    public Color current;
    public Color pressed;
    public Color disabled;

    public Color get(bool isCurrent, bool isPressed, bool isDisabled)
    {
        if (isDisabled)
            return disabled;
        else if (isPressed)
            return pressed;
        else if (isCurrent)
            return current;
        else
            return normal;
    }

    public static ColorSet csFore = new()
    {
        normal = C.orange,
        current = C.menuGold,
        pressed = C.black,
        disabled = C.black,
    };

    public static ColorSet csForeIcon = new()
    {
        normal = C.orangeDark,
        current = C.menuGold,
        pressed = C.black,
        disabled = C.black,
    };

    public static ColorSet csBack = new()
    {
        normal = C.orangeDarker,
        current = C.orangeDark,
        pressed = C.orange,
        disabled = C.grey,
    };

    public static ColorSet csCyanBack = new()
    {
        normal = C.cyanDarker,
        current = C.cyanDark,
        pressed = C.cyan,
        disabled = C.grey,
    };

    public static ColorSet csCyanFore = new()
    {
        normal = C.cyanDark,
        current = Color.White,
        pressed = C.black,
        disabled = C.black,
    };

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

class BtnFillCtrl : Ctrl
{
    public ColorSet csBack = ColorSet.csBack;
    private SolidBrush? backBrush;

    //public Action<Graphics, TextCursor, bool, bool>? onRender;

    public override void render(Graphics g, TextCursor tt, bool isCurrent, bool isPressed, Ctrl? prior)
    {
        // choose colours based on state
        var backColor = csBack.get(isCurrent, isPressed, disabled);
        if (backBrush == null || backBrush.Color != backColor)
        {
            backBrush?.Dispose();
            backBrush = null;
            if (backColor != Color.Transparent)
                backBrush = backColor.toBrush();
        }

        // draw background
        if (backBrush != null)
            g.FillRectangle(backBrush, r);

        // TODO: do we really want a notion of current (has-focus) as well as hovered? For now ... I think not

        //if (onRender != null)
        //    onRender(g, tt, isCurrent, isPressed);
    }
}

class BtnFillTextCtrl : BtnFillCtrl
{
    public ColorSet csFore = ColorSet.csFore;
    public float pad = N.four;
    public string text;

    override public string ToString()
    {
        return this.text;
    }

    public override void render(Graphics g, TextCursor tt, bool isCurrent, bool isPressed, Ctrl? prior)
    {
        // set our size based on text + padding
        tt.dtx += pad;
        tt.dty += pad;

        var sz = TextRenderer.MeasureText(g, this.text, tt.font);

        r.Width = sz.Width + pad + pad;
        r.Height = sz.Height + pad + pad;

        base.render(g, tt, isCurrent, isPressed, prior);

        // finally, draw the text
        tt.draw(" " + this.text, csFore.get(isCurrent, isPressed, disabled));
    }
}

class BtnFillDrawCtrl : BtnFillCtrl
{
    public bool sideBar;
    public string? iconName;
    public PointF iconOffset;
    private Color iconColor;
    private Pen? iconPen;
    override public string ToString()
    {
        return this.iconName ?? "?";
    }

    public override void render(Graphics g, TextCursor tt, bool isCurrent, bool isPressed, Ctrl? prior)
    {
        base.render(g, tt, isCurrent, isPressed, prior);

        iconColor = ColorSet.csForeIcon.get(isCurrent, isPressed, disabled);
        if (iconPen == null || iconPen.Color != iconColor)
        {
            iconPen?.Dispose();
            iconPen = null;
            if (iconColor != Color.Transparent)
                iconPen = iconColor.toPen(3, LineCap.Round);
        }

        // draw the icon
        g.SmoothingMode = SmoothingMode.AntiAlias;
        switch (iconName)
        {
            case "close":
                PlotQuestMini.drawBackArrow(g, r.X + iconOffset.X, r.Y + iconOffset.Y, 18, iconPen!);
                break;
            case "envelope":
                PlotQuestMini.drawEnvelope(g, r.X + iconOffset.X, r.Y + iconOffset.Y, 53, iconPen!);
                break;
            case "page":
                PlotQuestMini.drawPage(g, r.X + iconOffset.X, r.Y + iconOffset.Y, 51, iconPen!);
                break;

            default: throw new Exception($"Unexpected iconName: {iconName}");
        }

        if (sideBar && iconPen != null)
        {
            g.SmoothingMode = SmoothingMode.Default;
            g.DrawLineR(iconPen, r.Right - 2, r.Top, 0, r.Height);
        }
    }
}
