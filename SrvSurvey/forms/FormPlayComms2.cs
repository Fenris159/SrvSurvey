using SrvSurvey.game;
using SrvSurvey.game.RavenColonial;
using SrvSurvey.plotters;
using SrvSurvey.quests;
using SrvSurvey.widgets;
using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace SrvSurvey.forms.playComms;

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

    enum Mode
    {
        /// <summary> Loading initial PlayState </summary>
        init,
        /// <summary> Show a list of messages </summary>
        msgs,
        /// <summary> Show a catalog of available quests </summary>
        catalog,
        /// <summary> Show details on a specific quest </summary>
        defQuest,
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
        setScroll(100, 72, -10, -10);

        // do we need to start loading?
        if (PlayState.current == null)
            PlayState.loadAsync(CommanderSettings.currentOrLastFid).continueOnMain(this, () => this.init());
        else
            this.init();
    }

    private void init()
    {
        // init ctrls
        btnMsgs = new BtnFillDrawCtrl
        {
            offset = new(10, 72),
            sz = new(72, 72),
            iconName = "envelope",
            iconOffset = new(9, 16),
            disabled = !(PlayState.current?.messagesTotal > 0),
            onClick = btn => setMode(Mode.msgs),
        };

        btnCat = new BtnFillDrawCtrl
        {
            follow = Side.Bottom,
            gap = 10,
            sz = new(72, 72),
            iconName = "page",
            iconOffset = new(15, 12),
            onClick = btn => setMode(Mode.catalog),
        };

        addCtrl(
            btnMsgs,
            new BtnFillDrawCtrl { offset = new(10, -10), r = new(0, 0, 72, 32), iconName = "close", iconOffset = new(26, 6), onClick = btn => this.Close(), },
#if DEBUG
            new BtnFillTextCtrl { follow = Side.Bottom, gap = 10, sz = new(72, 32), text = "(watch)", onClick = btn => BaseForm.show<FormPlayJournal>(), disabled = Game.activeGame == null },
            new BtnFillTextCtrl { follow = Side.Bottom, gap = 10, sz = new(72, 32), text = "(dev)", onClick = btn => BaseForm.show<FormPlayDev>(), },
#endif
            btnCat
        );

        // choose a starting mode
        setMode(Mode.catalog);

        //var ps = PlayState.current;
        //if (ps?.devQuest != null)
        //{
        //    showQuestDef(ps.devQuest.quest);
        //}
        //else 
        //if (ps?.messagesTotal > 0)
        //{
        //    setMode(Mode.msgs);
        //}
        //else if (ps == null)
        //{
        //    setMode(Mode.catalog);
        //}
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
            case Mode.init:
                return renderInitializing(g, tt);

            case Mode.msgs:
                return renderMessages(g, tt);

            case Mode.catalog:
            case Mode.defQuest:
                return renderQuestCatalog(g, tt);
        }
        return false;
    }

    private bool renderInitializing(Graphics g, TextCursor tt)
    {
        tt.dty = 10;
        tt.dtx = 100;
        tt.draw("Initializing...");
        return false;
    }

    #region catalog

    private static List<DefQuest>? publishedQuests;
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
        {
            // fetch whole catalog
            publishedQuests = await Game.rcc.getPublishedQuests(CommanderSettings.currentOrLastFid);

            // and include the in-progress devQuest
            if (PlayState.current?.devQuest != null)
                publishedQuests.Insert(0, PlayState.current.devQuest.quest);
        }

        if (publishedQuests == null || publishedQuests.Count == 0) throw new Exception("Why are there zero quests available?");

        addStack(publishedQuests.Select((qd, i) => new QuestCatalogLine
        {
            qd = qd,
            follow = Side.Top,
            gap = 4,
            onClick = btn => showQuestDef(qd),
        }));
        //addStack(publishedQuests.Select((qd, i) => new QuestCatalogLine(qd)));
        //addStack(publishedQuests.Select((qd, i) => new QuestCatalogLine(qd)));

        loadingCatalog = false;
        this.Invalidate();
    }

    private void showQuestDef(DefQuest qd)
    {
        this.selectedQD = qd;

        var btnBack = new BtnFillDrawCtrl { follow = Side.Top, gap = 4, sz = new(72, 32), iconName = "close", iconOffset = new(28, 10), onClick = btn => setMode(Mode.catalog) };

        var lblConfirm = new TextCtrl { follow = Side.Top, gap = 4, autoSize = true, text = "Are you sure? This will undo any prior progress", color = C.cyan };
        var btnYes = new BtnFillTextCtrl { follow = Side.Top, gap = 10, sz = new(72, 32), text = "Yes", onClick = btn => this.Invalidate() };
        var btnNo = new BtnFillTextCtrl { follow = Side.Left, gap = 10, sz = new(72, 32), text = "No", onClick = btn => this.Invalidate() };
        var btnAct = new BtnFillTextCtrl { follow = Side.Left, gap = 4, sz = new(120, 32), csBack = ColorSet.csCyanBack, csFore = ColorSet.csCyanFore };
        var lblActing = new TextCtrl { follow = Side.Left, gap = 10, autoSize = true, color = C.oranger };

        List<Ctrl> subStack = [];
        btnAct.onClick = btn =>
        {
            // show prompt
            btnBack.hidden = true;
            btnAct.hidden = true;
            subStack = addStack(lblConfirm, btnYes, btnNo);
        };
        btnNo.onClick = btn =>
        {
            // decline
            btnBack.hidden = false;
            btnAct.hidden = false;
            stack.RemoveAll(c => subStack.Contains(c));
        };

        // adjust messages depending on quest state
        var pq = PlayState.current?.activeQuests.Find(pq => pq.quest.equals(qd));
        if (pq == null)
        {
            btnAct.text = "Activate quest";
            lblActing.text = "Activating ...";
            btnYes.onClick = btn =>
            {
                // activate quest
                lblConfirm.color = C.grey;
                btnYes.disabled = true;
                btnNo.disabled = true;
                subStack.AddRange(addStack(lblActing));

                PlayState.current?.activateQuest(qd.publisher, qd.id).continueOnMain(this, () =>
                {
                    btnBack.hidden = false;
                    btnAct.hidden = false;
                    stack.Remove(btnAct);
                    stack.RemoveAll(c => subStack.Contains(c));
                    addStack(new TextCtrl { follow = Side.Left, gap = 10, autoSize = true, text = "Enjoy the quest ...", color = C.oranger });
                });
            };
        }
        else
        {
            btnAct.text = "Remove";
            lblActing.text = "Removing ...";
            btnYes.onClick = btn =>
            {
                // remove quest
                lblConfirm.color = C.grey;
                btnYes.disabled = true;
                btnNo.disabled = true;
                subStack.AddRange(addStack(lblActing));

                PlayState.current?.removeQuest(pq, QuestState.unknown).continueOnMain(this, () =>
                {
                    btnBack.hidden = false;
                    btnAct.hidden = false;
                    stack.Remove(btnAct);
                    stack.RemoveAll(c => subStack.Contains(c));
                    addStack(new TextCtrl { follow = Side.Left, gap = 10, autoSize = true, text = "Better luck next time ...", color = C.oranger });
                });
            };
        }

        // now, add into the stack
        stack.Clear();
        addStack(
            new QuestCatalogItem { qd = qd, },

            //new Ctrl()
            //{
            //    onRender = (Graphics g, TextCursor tt, bool isCurrent, bool isPressed, Ctrl? prior) =>
            //    {
            //        g.DrawLineR(Pens.Lime, this.scrollWidth, this.scrollBox.Top, 44, 44);
            //        return false;
            //    },
            //},

            btnBack,
            btnAct
        );

        this.mode = Mode.defQuest;
        btnMsgs.sideBar = false;
        btnCat.sideBar = true;
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
}

class QuestCatalogLine : BtnFillCtrl
{
    private static Brush brushDevQuest;
    public new FormPlayComms2 form => (FormPlayComms2)base.form;

    public required DefQuest qd;

    override public string ToString() => this.qd.title;

    public override bool render(Graphics g, TextCursor tt, bool isCurrent, bool isPressed, Ctrl? prior)
    {
        r.Width = form.scrollWidth; // match scrollBox width

        // render background
        var redraw = base.render(g, tt, isCurrent, isPressed, prior);

        // highlight if this is the devQuest
        if (PlayState.current?.devQuest?.quest == this.qd)
        {
            brushDevQuest ??= new HatchBrush(HatchStyle.WideUpwardDiagonal, Color.FromArgb(199, C.orangeDarker));
            g.FillRectangle(brushDevQuest, r.X, r.Y, 34, r.Height);
        }

        tt.dty = r.Y + 4;
        tt.dtx = r.X + 4;
        PlotQuestMini.drawLogo(g, tt.dtx, tt.dty, isCurrent, 24);

        tt.dtx += 32;
        var x = tt.dtx;

        // render current state?
        var pq = PlayState.current?.activeQuests.Find(pq => pq.quest.equals(qd));
        if (pq != null)
        {
            var msg = qd.equals(PlayState.current?.devQuest?.quest) ? "Dev" : "Active";
            tt.drawRight(form.scrollBox.Right - 6, msg, C.cyan, GameColors.Fonts.arial_8);
        }

        // title
        tt.draw(x, this.qd.title, ColorSet.csFore.get(isCurrent, isPressed, disabled), GameColors.Fonts.arial_16);
        tt.newLine(4, true);
        // draw desc as a single line with ...
        var rr = new Rectangle((int)x, (int)(tt.dty), (int)r.Width - 32, 32);
        TextRenderer.DrawText(g, qd.desc, GameColors.Fonts.arial_12, rr, isCurrent ? C.black : C.oranged, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
        tt.newLine(4, true);

        // set our hight to be as larged as we needed
        return adjustHeight(tt.pad().Height - r.Y);
    }
}


internal class QuestCatalogItem : Ctrl
{
    public required DefQuest qd;

    public override bool render(Graphics g, TextCursor tt, bool isCurrent, bool isPressed, Ctrl? prior)
    {
        r.Width = form.scrollWidth; // match scrollBox width
        tt.dty = r.Y + 4;
        var x = r.X + 4;
        var w = (int)(r.Width + x - 4);

        // the title
        tt.draw(x, qd.title, C.oranger, GameColors.Fonts.arial_20);
        tt.newLine(10, true);

        // desc with an orange bar
        var y = tt.dty;
        tt.dty += 10;
        var sz = tt.drawWrapped(x + 10, w, qd.desc);
        tt.newLine(10, true);
        g.FillRectangle(C.Brushes.orangeDark, r.X, y, 10, tt.dty - y);
        tt.dty += 10;

        g.DrawLineR(C.Pens.orangeDark2r, r.X, tt.dty, w, 0);
        tt.dty += 10;


        // tags
        tt.draw(x, "Tags: ");
        if (qd.tags?.Length > 0)
        {
            // TODO: many clickable boxes?
            tt.draw(string.Join(", ", qd.tags), C.oranger);
        }
        else
        {
            tt.draw("None", C.grey);
        }
        tt.newLine(6, true);


        // duration
        tt.draw(x, "Duration: ");
        tt.draw(qd.duration.ToString(), C.oranger);
        tt.draw($" ({DefQuest.mapQuestDuration.GetValueOrDefault(qd.duration)})");
        tt.newLine(10, true);

        g.DrawLineR(C.Pens.orangeDark2r, r.X, tt.dty, w, 0);
        tt.dty += 10;


        // publisher and version
        tt.draw(x, "Publisher: ");
        tt.draw(qd.publisher, C.oranger);
        tt.newLine(6, true);
        tt.draw(x, $"Version: ");
        tt.draw(qd.ver.ToString(), C.oranger);
        tt.newLine(10, true);

        g.DrawLineR(C.Pens.orangeDark2r, r.X, tt.dty, w, 0);
        tt.dty += 10;

        return adjustHeight(tt.dty - r.Y);
    }
}