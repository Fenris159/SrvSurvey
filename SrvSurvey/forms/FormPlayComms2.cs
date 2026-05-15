using SrvSurvey.game;
using SrvSurvey.plotters;
using SrvSurvey.quests;
using SrvSurvey.widgets;
using System.Diagnostics;

namespace SrvSurvey.forms;

[Draggable]
[System.ComponentModel.DesignerCategory("")]
internal class FormPlayComms2 : BaseFormZippy
{
    public static void toggleForm()
    {
        var form = BaseForm.get<FormPlayComms2>();
        if (form == null)
        {
            BaseForm.show<FormPlayComms2>();
        }
        else
        {
            if (Elite.focusSrvSurvey)
                form.Close();
            else
                form.Activate();
        }
    }

    private Mode mode;
    private BtnFillDrawCtrl btnMsgs;
    private BtnFillDrawCtrl btnCat;

    public FormPlayComms2() : base()
    {
        this.Font = GameColors.Fonts.arial_12;
        this.ForeColor = C.orange;
        this.MinimumSize = new(400, 400);

        // init ctrls
        btnMsgs = new BtnFillDrawCtrl
        {
            pt = new(10, 72),
            r = new(0, 0, 72, 72),
            iconName = "envelope",
            iconOffset = new(9, 16),
            //disabled = true,
            onClick = () => setMode(Mode.msgs),
        };
        addCtrl(btnMsgs);

        btnCat = new BtnFillDrawCtrl
        {
            pt = new(10, -100),
            r = new(0, 0, 72, 72),
            iconName = "page",
            iconOffset = new(15, 12),
            onClick = () => setMode(Mode.catalog),
        };
        addCtrl(btnCat);

        addCtrl(new BtnFillTextCtrl
        {
            pt = new(10, -44),
            r = new(0, 0, 72, 24),
            text = "(refresh)",
            onClick = () => this.Invalidate(),
        });

        addCtrl(new BtnFillDrawCtrl
        {
            pt = new(10, -10),
            r = new(0, 0, 72, 24),
            iconName = "close",
            iconOffset = new(26, 6),
            onClick = () => this.Close(),
        });

        setMode(Mode.msgs);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // position ourself over the top/right quadrant of the game
        var r = Elite.getWindowRect();
        if (r.Width > 0)
        {
            this.Width = (int)(r.Width * 0.4f);
            this.Height = (int)(r.Height * 0.7f);
            this.Left = r.Right - this.Width - 20;
            this.Top = r.Top + 10 + (PlotBase2.getPlotter<PlotQuestMini>()?.bottom ?? 20);
            Application.DoEvents();
        }
        this.BackgroundImage = GameGraphics.getBackgroundImage(this.ClientSize, true);
        this.Invalidate();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);

        if (Elite.isGameRunning && !Elite.eliteMinimized)
            Elite.setFocusED();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);

        // close ourself anytime we lose focus (but not if debugging)
        if (!Debugger.IsAttached)
            Program.defer(() => this.Close());
    }

    private void setMode(Mode mode)
    {
        this.mode = mode;
        btnMsgs.sideBar = mode == Mode.msgs;
        btnCat.sideBar = mode == Mode.catalog;
        stopScroll();

        if (mode == Mode.catalog)
        {
            loadPublishedQuests().justDoIt();
        }
        else
        {
            ctrls.RemoveAll(ctrl => ctrl is QuestCatalogLine);
        }
        this.Invalidate();
    }

    protected override void render(Graphics g, TextCursor tt)
    {
        base.render(g, tt);

        PlotQuestMini.drawLogo(g, 32, N.ten, false, 48);

        switch (mode)
        {
            case Mode.msgs:
                renderMessages(g, tt);
                break;
            case Mode.catalog:
                renderQuestCatalog(g, tt);
                break;
        }
    }

    #region catalog

    private bool loadingCatalog;

    private async Task loadPublishedQuests()
    {
        // TODO: background thread!
        ctrls.RemoveAll(ctrl => ctrl is QuestCatalogLine);
        loadingCatalog = true;
        this.Invalidate();

        var quests = await Game.rcc.getPublishedQuests(CommanderSettings.currentOrLastFid);

        setScroll(100, 72, -10, -10);
        var catalog = quests.Select((qd, i) => new QuestCatalogLine() { qd = qd, first = i == 0 }).ToArray();
        addCtrl(catalog);
        addCtrl(quests.Select((qd, i) => new QuestCatalogLine() { qd = qd }).ToArray());
        addCtrl(quests.Select((qd, i) => new QuestCatalogLine() { qd = qd }).ToArray());
        addCtrl(quests.Select((qd, i) => new QuestCatalogLine() { qd = qd }).ToArray());

        scrollFrom = catalog[0];
        loadingCatalog = false;
        this.Invalidate();
    }

    private void renderQuestCatalog(Graphics g, TextCursor tt)
    {
        tt.dty = N.ten;
        tt.draw(N.hundred, "Quest Catalog", GameColors.Fonts.arial_20);
        tt.newLine(true);
        tt.draw(N.hundred, "Choose your next adventure:", C.orangeDark);
        tt.newLine(N.threeTwo, true);

        if (loadingCatalog)
            tt.draw(N.hundred, "... loading ...");
    }

    #endregion

    #region list msgs

    private void renderMessages(Graphics g, TextCursor tt)
    {
        tt.dty = 10;
        tt.dtx = 100;
        tt.draw("Hello!");
    }

    #endregion

    enum Mode
    {
        msgs,
        catalog,
    }
}

class QuestCatalogLine : BtnFillCtrl
{
    private static QuestCatalogLine? expandedLine;
    private static BtnFillTextCtrl? btnActivate;

    public DefQuest qd;
    public bool first;

    public QuestCatalogLine() : base()
    {
        onClick = () => this.toggle();
    }

    public void toggle()
    {
        if (btnActivate != null)
            form.ctrls.Remove(btnActivate);

        if (expandedLine == this)
        {
            expandedLine = null;
        }
        else
        {
            var idx = form.ctrls.IndexOf(this);
            btnActivate = new BtnFillTextCtrl()
            {
                text = "Activate Quest",
                pt = new(-20, r.Top),
                csBack = ColorSet.csCyanBack,
                csFore = ColorSet.csCyanFore,
            };
            form.ctrls.Insert(idx + 1, btnActivate);
            expandedLine = this;
        }
    }

    override public string ToString() => this.qd.title;

    public override void render(Graphics g, TextCursor tt, bool isCurrent, bool isPressed, Ctrl? prior)
    {
        // are we the first scrolling item?
        var sb = form.scrollBox;
        r.Y = first || prior == null
            ? sb.Y
            : prior.r.Bottom + 4;

        r.X = sb.X;
        r.Width = tt.containerWidth - r.X - 13;


        // render background
        base.render(g, tt, isCurrent, isPressed, prior);

        tt.dty = r.Y + 4;
        tt.dtx = r.X + 4;
        PlotQuestMini.drawLogo(g, tt.dtx, tt.dty, isCurrent, 24);

        tt.dtx += 32;
        var x = tt.dtx;
        tt.draw(x, this.qd.title, ColorSet.csFore.get(isCurrent, isPressed, disabled), GameColors.Fonts.arial_16);
        tt.newLine(4, true);
        if (expandedLine != this)
        {
            // draw desc as a single line with ...
            var rr = new Rectangle((int)x, (int)(tt.dty), (int)r.Width - 32, 32);
            TextRenderer.DrawText(g, qd.desc, GameColors.Fonts.arial_12, rr, isCurrent ? C.black : C.orangeDark, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
        }
        else
        {
            // draw whole desc + buttons
            tt.drawWrapped(x, qd.desc, isCurrent ? C.menuGold : C.orangeDark, GameColors.Fonts.arial_12);

        }
        tt.newLine(4, true);

        // set our hight to be as larged as we needed
        r.Height = tt.pad().Height - r.Y;

        if (expandedLine == this && btnActivate != null)
            btnActivate.pt.Y = r.Bottom + 6;
    }
}