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
        this.Size = new(800, 800);
        this.MinimumSize = new(400, 400);

        // init ctrls
        btnMsgs = new BtnFillDrawCtrl
        {
            offset = new(10, 72),
            sz = new(72, 72),
            iconName = "envelope",
            iconOffset = new(9, 16),
            //disabled = true,
            onClick = () => setMode(Mode.msgs),
        };

        btnCat = new BtnFillDrawCtrl
        {
            offset = new(10, -100),
            sz = new(72, 72),
            iconName = "page",
            iconOffset = new(15, 12),
            onClick = () => setMode(Mode.catalog),
        };
        addCtrl(btnMsgs, btnCat,
            new BtnFillTextCtrl
            {
                offset = new(10, -44),
                sz = new(72, 72),
                text = "(refresh)",
                onClick = () => this.Invalidate(),
            },
            new BtnFillDrawCtrl
            {
                offset = new(10, -10),
                r = new(0, 0, 72, 24),
                iconName = "close",
                iconOffset = new(26, 6),
                onClick = () => this.Close(),
            }
        );

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
        stack.Clear();

        if (mode == Mode.catalog)
            loadPublishedQuests().justDoIt();

        this.Invalidate();
    }

    protected override bool render(Graphics g, TextCursor tt)
    {
        base.render(g, tt);

        PlotQuestMini.drawLogo(g, 32, N.ten, false, 48);

        switch (mode)
        {
            case Mode.msgs:
                return renderMessages(g, tt);

            case Mode.catalog:
                return renderQuestCatalog(g, tt);
        }
        return false;
    }

    #region catalog

    private static DefQuest[]? publishedQuests;
    private bool loadingCatalog;
    private DefQuest? selectedQD;

    private bool renderQuestCatalog(Graphics g, TextCursor tt)
    {
        tt.dty = N.ten;
        tt.draw(N.hundred, "Quest Catalog", GameColors.Fonts.arial_20);
        tt.newLine(true);
        tt.draw(N.hundred, "Choose your next adventure:", C.orangeDark);
        tt.newLine(N.threeTwo, true);

        if (loadingCatalog)
            tt.draw(N.hundred, "... loading ...");

        return false;
    }

    private async Task loadPublishedQuests()
    {
        // TODO: background thread!
        loadingCatalog = true;
        this.Invalidate();

        // only download once per process
        if (publishedQuests == null)
            publishedQuests = await Game.rcc.getPublishedQuests(CommanderSettings.currentOrLastFid);

        if (publishedQuests == null || publishedQuests.Length == 0) throw new Exception("Why are there zero quests available?");

        setScroll(100, 72, -10, -10);
        addStack(publishedQuests.Select((qd, i) => new QuestCatalogLine(qd)
        {
            onClick = () => showQuestDef(qd),
        }).ToArray());
        //addStack(publishedQuests.Select((qd, i) => new QuestCatalogLine(qd)).ToArray());
        //addStack(publishedQuests.Select((qd, i) => new QuestCatalogLine(qd)).ToArray());

        loadingCatalog = false;
        this.Invalidate();
    }

    private void showQuestDef(DefQuest qd)
    {
        this.selectedQD = qd;
        // remove list items
        stack.Clear();

        // add new items
        addStack(
            new QuestCatalogItem(qd) { offset = scrollZone.Location },
            new BtnFillDrawCtrl
            {
                offset = new(200, 200),
                r = new(0, 0, 72, 24),
                iconName = "close",
                iconOffset = new(26, 6),
                onClick = () => setMode(Mode.catalog),
            }
        );
    }

    #endregion

    #region list msgs

    private bool renderMessages(Graphics g, TextCursor tt)
    {
        tt.dty = 10;
        tt.dtx = 100;
        tt.draw("Hello!");
        return false;
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
    public new FormPlayComms2 form => (FormPlayComms2)base.form;

    public DefQuest qd;

    public QuestCatalogLine(DefQuest qd) : base()
    {
        this.qd = qd;
    }

    override public string ToString() => this.qd.title;

    public override bool render(Graphics g, TextCursor tt, bool isCurrent, bool isPressed, Ctrl? prior)
    {
        // match scrollBox width
        var sb = form.scrollBox;
        r.Width = sb.Width;

        // be 4px below the prior
        if (prior != null)
            r.Y = prior.r.Bottom + 4;

        // render background
        var redraw = base.render(g, tt, isCurrent, isPressed, prior);

        tt.dty = r.Y + 4;
        tt.dtx = r.X + 4;
        PlotQuestMini.drawLogo(g, tt.dtx, tt.dty, isCurrent, 24);

        tt.dtx += 32;
        var x = tt.dtx;
        tt.draw(x, this.qd.title, ColorSet.csFore.get(isCurrent, isPressed, disabled), GameColors.Fonts.arial_16);
        tt.newLine(4, true);
        // draw desc as a single line with ...
        var rr = new Rectangle((int)x, (int)(tt.dty), (int)r.Width - 32, 32);
        TextRenderer.DrawText(g, qd.desc, GameColors.Fonts.arial_12, rr, isCurrent ? C.black : C.orangeDark, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
        tt.newLine(4, true);

        // set our hight to be as larged as we needed
        var newHeight = tt.pad().Height - r.Y;
        if (newHeight != r.Height)
        {
            r.Height = newHeight;
            redraw = true;
        }
        return redraw;
    }
}


internal class QuestCatalogItem : Ctrl
{
    private DefQuest qd;

    public QuestCatalogItem(DefQuest qd) : base()
    {
        this.qd = qd;
        r.Width = 200;
        r.Height = 400;
    }

    public override bool render(Graphics g, TextCursor tt, bool isCurrent, bool isPressed, Ctrl? prior)
    {
        tt.draw("**" + qd.title);
        return false;
    }
}