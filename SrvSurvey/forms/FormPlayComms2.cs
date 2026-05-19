using SrvSurvey.game;
using SrvSurvey.game.RavenColonial;
using SrvSurvey.plotters;
using SrvSurvey.quests;
using SrvSurvey.widgets;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;

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
        /// <summary> Show details for a specific message </summary>
        msgItem,
        /// <summary> Show a catalog of available quests </summary>
        catalog,
        /// <summary> Show details on a specific quest from the catalog </summary>
        defQuest,
        /// <summary> Show a list of quests for current Cmdr </summary>
        cmdrQuests,
    }

    private Mode mode;
    private Action? onBackCustom;
    private BtnFillDrawCtrl btnMsgs;
    private BtnFillDrawCtrl btnCmdr;
    private BtnFillDrawCtrl btnCat;
    private List<Ctrl> extras = [];

    private bool loadingCatalog;
    private static List<DefQuest>? publishedQuests;
    private static List<QuestCmdrStatus> cmdrQuests = [];
    private static string? lastFID;

    public FormPlayComms2() : base()
    {
        this.Font = GameColors.Fonts.arial_12;
        this.ForeColor = C.orange;
        this.Size = new(800, 800);
        this.MinimumSize = new(560, 400);
        setScroll(100, 72, -10, -10);

        Task.Run(() => this.init()).justDoIt();
    }

    private async Task init()
    {
        // init static ctrls
        btnMsgs = new BtnFillDrawCtrl
        {
            offset = new(10, 72),
            sz = new(72, 72),
            iconName = "envelope",
            iconSize = 53,
            iconOffset = new(9, 16),
            disabled = !(PlayState.current?.messagesTotal > 0),
            onClick = btn => setMode(Mode.msgs),
        };

        btnCmdr = new BtnFillDrawCtrl
        {
            follow = Side.Top,
            gap = 10,
            sz = new(72, 72),
            iconName = "logo",
            iconSize = 46,
            iconOffset = new(15, 12),
            onClick = btn => setMode(Mode.cmdrQuests),
        };

        btnCat = new BtnFillDrawCtrl
        {
            follow = Side.Top,
            gap = 10,
            sz = new(72, 72),
            iconName = "page",
            iconSize = 51,
            iconOffset = new(15, 12),
            onClick = btn => setMode(Mode.catalog),
        };

        addCtrl(
            btnMsgs,
            btnCmdr,
            btnCat,
            new BtnFillDrawCtrl { offset = new(10, -10), r = new(0, 0, 72, 32), iconName = "close", iconSize = 18, iconOffset = new(26, 10), onClick = btn => this.Close(), }
        );
#if DEBUG
        addCtrl(
            new BtnFillTextCtrl { follow = Side.Bottom, gap = 10, sz = new(72, 32), text = "(watch)", onClick = btn => BaseForm.show<FormPlayJournal>(), disabled = Game.activeGame == null },
            new BtnFillTextCtrl { follow = Side.Bottom, gap = 10, sz = new(72, 32), text = "(dev)", onClick = btn => BaseForm.show<FormPlayDev>(), }
        );
#endif

        // now do async stuff
        this.Invalidate();

        // do we need to load current play-state
        var fid = CommanderSettings.currentOrLastFid;
        if (PlayState.current == null)
            await PlayState.loadAsync(fid);

        if (PlayState.current == null) throw new Exception($"Cannot get PlayState for: {fid}");

        // fetch cmdr's current mission status, if needed
        if (lastFID != fid || true)
            await fetchCmdrQuests(fid);

        // choose a starting mode
        var ps = PlayState.current;
        if (ps?.messagesTotal > 0)
            setMode(Mode.msgs);
        else if (cmdrQuests.Count > 0)
            setMode(Mode.cmdrQuests);
        else
            setMode(Mode.catalog);
    }

    public static async Task fetchCmdrQuests(string fid)
    {
        cmdrQuests = await Game.rcc.getCmdrQuests(fid);
        lastFID = fid;

        BaseForm.get<FormPlayComms2>()?.Invalidate();
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
        btnCmdr.sideBar = mode == Mode.cmdrQuests;
        btnCat.sideBar = mode == Mode.catalog;
        onBackCustom = null;
        // remove scrollable ctrls
        stack.Clear();
        // and any extra stuff
        ctrls.RemoveAll(c => extras.Contains(c));
        extras.Clear();

        if (mode == Mode.catalog)
            loadPublishedQuests();
        else if (mode == Mode.msgs)
            loadMessages();
        else if (mode == Mode.cmdrQuests)
            loadCmdrQuests();

        this.Invalidate();
    }

    protected override bool onBack()
    {
        // move 'back' in the UI
        if (this.onBackCustom != null)
        {
            this.onBackCustom();
            return true;
        }

        switch (this.mode)
        {
            case Mode.init:
            case Mode.msgs:
            case Mode.catalog:
            case Mode.cmdrQuests:
                // close the window
                this.Close();
                return true;

            case Mode.defQuest:
                setMode(Mode.catalog);
                return true;

            case Mode.msgItem:
                setMode(Mode.msgs);
                return true;
            default:
                throw new Exception($"Unexpected mode: {mode}");
        }
    }

    protected override bool render(Graphics g, TextCursor tt)
    {
        var ps = PlayState.current;
        if (ps == null) return false;
        btnMsgs.disabled = ps.messagesTotal == 0;

        base.render(g, tt);

        //PlotQuestMini.drawLogo(g, 32, N.ten, ps.messagesUnread > 0, 48);

        switch (mode)
        {
            case Mode.init:
                return renderInitializing(g, tt);

            case Mode.msgs:
            case Mode.msgItem:
                return renderMessages(g, tt, ps);

            case Mode.catalog:
            case Mode.defQuest:
                return renderQuestCatalog(g, tt);

            case Mode.cmdrQuests:
                return renderCmdrQuests(g, tt);
        }
        return false;
    }

    private bool renderInitializing(Graphics g, TextCursor tt)
    {
        // TODO: make this more interesting?
        tt.dty = 10;
        tt.dtx = 100;
        tt.draw("Initializing...");
        return false;
    }

    #region catalog

    private bool renderQuestCatalog(Graphics g, TextCursor tt)
    {
        // TODO: make this a ctrl
        tt.dty = N.ten;
        tt.draw(N.hundred, "Quest Catalog", GameColors.Fonts.arial_20);
        tt.newLine(true);
        tt.draw(N.hundred, "Choose your next adventure:", C.orangeDark);
        tt.newLine(N.threeTwo, true);

        if (loadingCatalog)
            tt.draw(N.hundred, "... loading ...");

        return false;
    }

    private async Task fetchPublishedQuests()
    {
        loadingCatalog = true;
        this.Invalidate();

        // only download once per process
        if (publishedQuests == null)
        {
            // fetch whole catalog
            publishedQuests = await Game.rcc.getPublishedQuests(CommanderSettings.currentOrLastFid);
        }

        if (publishedQuests == null || publishedQuests.Count == 0) throw new Exception("Why are there zero quests available?");

        loadingCatalog = false;
    }

    private void loadPublishedQuests()
    {
        // only download once per process
        if (publishedQuests == null)
        {
            // fetch on a thread
            Task.Run(() => this.fetchPublishedQuests().continueOnMain(this, () => this.loadPublishedQuests()));
            return;
        }

        // and include the in-progress devQuest
        if (PlayState.current?.devQuest != null)
        {
            addStack(new QuestCatalogLine
            {
                qd = PlayState.current.devQuest.quest,
                follow = Side.Top,
                gap = 4,
                onClick = btn => showQuestDef(PlayState.current.devQuest.quest),
            });
        }

        // hide items if matches devQuest, or cmdr has activated them before
        var items = publishedQuests.Where(qd => !qd.equals(PlayState.current?.devQuest?.quest) && cmdrQuests.Find(x => x.publisher == qd.publisher && x.id == qd.id) == null);
        addStack(items.Select((qd, i) => new QuestCatalogLine
        {
            qd = qd,
            qs = cmdrQuests?.Find(qs => qs.publisher == qd.publisher && qs.id == qd.id),
            follow = Side.Top,
            gap = 4,
            onClick = btn => showQuestDef(qd),
        }));
        if (stack.Count > 0) ctrlCurrent = stack[0];

        loadingCatalog = false;
        this.Invalidate();
    }

    private void showQuestDef(DefQuest qd)
    {
        var btnBack = new BtnFillDrawCtrl { follow = Side.Top, gap = 4, sz = new(72, 32), iconName = "close", iconSize = 18, iconOffset = new(28, 10) };
        btnBack.onClick = btn =>
        {
            if (onBackCustom != null)
                onBackCustom();
            else
                setMode(Mode.catalog);
        };

        var lblConfirm = new TextCtrl { follow = Side.Top, gap = 4, autoSize = true, color = C.cyan };
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
            ctrlCurrent = btnNo;

            onBackCustom = () =>
            {
                // do the 'No' click action
                btnNo.onClick(btnNo);
            };
        };
        btnNo.onClick = btn =>
        {
            // decline
            btnBack.hidden = false;
            btnAct.hidden = false;
            onBackCustom = null;

            stack.RemoveAll(c => subStack.Contains(c));
            ctrlCurrent = btnBack;
        };

        // adjust messages depending on quest state
        var pq = PlayState.current?.activeQuests.Find(pq => pq.quest.equals(qd));
        var qs = cmdrQuests.Find(x => x.publisher == qd.publisher && x.id == qd.id);
        var isPaused = qs?.state == QuestState.paused;
        if (isPaused)
        {
            lblConfirm.text = "Are you sure?";
            btnAct.text = "Resume quest";
            lblActing.text = "Resuming ...";
            btnYes.onClick = btn =>
            {
                // activate quest
                lblConfirm.color = C.grey;
                btnYes.disabled = true;
                btnNo.disabled = true;
                subStack.AddRange(addStack(lblActing));
                onBackCustom = null;

                PlayState.current?.resumeQuest(qd.publisher, qd.id).continueOnMain(this, () =>
                {
                    btnBack.hidden = false;
                    btnAct.hidden = false;
                    stack.Remove(btnAct);
                    stack.RemoveAll(c => subStack.Contains(c));
                    addStack(new TextCtrl { follow = Side.Left, gap = 10, autoSize = true, text = "Enjoy the quest ...", color = C.oranger });
                    ctrlCurrent = btnBack;
                    btnBack.onClick = b => setMode(PlayState.current?.messagesTotal > 0 ? Mode.msgs : Mode.cmdrQuests);

                    PlotBase2.renderAll(null, true);

                    // update our internal state (rather than re-hit the server)
                    if (lastFID != null)
                        fetchCmdrQuests(lastFID).justDoIt();
                });
            };
        }
        else if (pq == null)
        {
            lblConfirm.text = "Are you sure?";
            btnAct.text = "Activate quest";
            lblActing.text = "Activating ...";
            btnYes.onClick = btn =>
            {
                // activate quest
                lblConfirm.color = C.grey;
                btnYes.disabled = true;
                btnNo.disabled = true;
                subStack.AddRange(addStack(lblActing));
                onBackCustom = null;

                PlayState.current?.activateQuest(qd.publisher, qd.id).continueOnMain(this, () =>
                {
                    btnBack.hidden = false;
                    btnAct.hidden = false;
                    stack.Remove(btnAct);
                    stack.RemoveAll(c => subStack.Contains(c));
                    addStack(new TextCtrl { follow = Side.Left, gap = 10, autoSize = true, text = "Enjoy the quest ...", color = C.oranger });
                    ctrlCurrent = btnBack;
                    btnBack.onClick = b => setMode(PlayState.current?.messagesTotal > 0 ? Mode.msgs : Mode.cmdrQuests);

                    PlotBase2.renderAll(null, true);

                    // update our internal state (rather than re-hit the server)
                    if (lastFID != null)
                        fetchCmdrQuests(lastFID).justDoIt();
                });
            };
        }
        else
        {
            lblConfirm.text = "Are you sure? This will undo any prior progress";
            btnAct.text = "Remove";
            lblActing.text = "Removing ...";
            btnYes.onClick = btn =>
            {
                // remove quest
                lblConfirm.color = C.grey;
                btnYes.disabled = true;
                btnNo.disabled = true;
                subStack.AddRange(addStack(lblActing));
                onBackCustom = null;

                PlayState.current?.removeQuest(pq, QuestState.unknown).continueOnMain(this, () =>
                {
                    btnBack.hidden = false;
                    btnAct.hidden = false;
                    stack.Remove(btnAct);
                    stack.RemoveAll(c => subStack.Contains(c));
                    addStack(new TextCtrl { follow = Side.Left, gap = 10, autoSize = true, text = "Better luck next time ...", color = C.oranger });
                    ctrlCurrent = btnBack;
                    PlotBase2.renderAll(null, true);
                });
            };
        }

        // now, add into the stack
        stack.Clear();
        addStack(
            new QuestCatalogItem { qd = qd, },
            btnBack,
            btnAct
        );
        ctrlCurrent = btnBack;

        mode = Mode.defQuest;
    }

    #endregion

    #region list msgs

    private bool renderMessages(Graphics g, TextCursor tt, PlayState ps)
    {
        tt.dty = N.ten;
        tt.draw(N.hundred, "Messages:", GameColors.Fonts.arial_20);
        tt.draw(ps.messagesTotal.ToString(), C.oranger, GameColors.Fonts.arial_20);
        tt.draw(" Unread: ", GameColors.Fonts.arial_20);
        tt.draw(ps.messagesUnread.ToString(), C.oranger, GameColors.Fonts.arial_20);
        tt.newLine(true);

        return false;
    }

    private void loadMessages()
    {
        var ps = PlayState.current;
        if (ps == null) return;

        var allMsgs = ps.activeQuests
            .SelectMany(q => q.msgs)
            .OrderByDescending(m => m.received)
            .ToList();
        Game.log($"showing {allMsgs.Count} msgs");

        addStack(allMsgs.Select(pm =>
        {
            var qm = pm.parent.quest.msgs.Find(m => m.id == pm.id);
            return new MessageLine
            {
                pm = pm,
                qm = qm,
                follow = Side.Top,
                gap = 4,
                onClick = btn => this.showMessage(pm, qm, ps).justDoIt(),
            };
        }));
        if (stack.Count > 0) ctrlCurrent = stack[0];

        this.Invalidate();
    }

    private async Task showMessage(PlayMsg pm, DefMsg? qm, PlayState ps)
    {
        var raw = pm.body ?? qm?.body ?? "";
        var matchParts = new Regex("`(.+?)`");
        var matches = matchParts.Matches(raw);

        List<Ctrl> subStack = [];
        var btnBack = new BtnFillDrawCtrl { follow = Side.Top, gap = 4, offset = new(0, 0), sz = new(72, 32), iconName = "close", iconSize = 18, iconOffset = new(28, 10), onClick = btn => setMode(Mode.msgs) };

        // body and copy-tags
        string body;
        if (matches.Count == 0)
        {
            body = raw;
        }
        else
        {
            body = raw.Replace("`", "");
            var copyTexts = matches.Select(m => m.Groups[1].Value).ToHashSet();
            if (copyTexts.Count > 0)
            {
                subStack.Add(new TextCtrl { follow = Side.Top, gap = 4, autoSize = true, font = GameColors.Fonts.arial_9, color = C.oranged, text = "Copy:" });
                subStack.AddRange(copyTexts.Select(t => new BtnFillTextCtrl
                {
                    follow = Side.Left,
                    gap = 4,
                    autoSize = true,
                    text = t,
                    onClick = btn => Clipboard.SetText(t),
                }));
                subStack.Add(new HorizLine { follow = Side.Top, gap = 10 });
            }
        }

        // reply buttons
        var actions = pm.actions ?? qm?.actions?.Keys.ToArray();
        Ctrl nextCurrent = btnBack;
        if (actions != null && qm?.actions != null)
        {
            if (pm.replied != null)
            {
                var replied = qm.actions.GetValueOrDefault(pm.replied) ?? pm.replied;
                subStack.AddRange(
                    new TextCtrl { follow = Side.Top, gap = 4, autoSize = true, color = C.oranged, font = GameColors.Fonts.arial_9, text = "Replied: " },
                    new TextCtrl { follow = Side.Left, gap = 4, autoSize = true, color = C.oranged, backColor = C.orangeDarker, text = replied }
                );
                subStack.Add(new HorizLine { follow = Side.Top, gap = 10 });
            }
            else
            {
                subStack.Add(new TextCtrl { follow = Side.Top, gap = 4, autoSize = true, color = C.oranged, font = GameColors.Fonts.arial_9, text = "Reply with:" });
                subStack.AddRange(actions.Select(k => new BtnFillTextCtrl
                {
                    follow = Side.Left,
                    gap = 4,
                    autoSize = true,
                    text = qm.actions.GetValueOrDefault(k) ?? k,
                    onClick = btn =>
                    {
                        pm.parent.invokeMessageAction(pm.id, k).continueOnMain(this, () => setMode(Mode.msgs));
                        this.Invalidate();
                    },
                }));
                subStack.Add(new HorizLine { follow = Side.Top, gap = 10 });
                if (subStack.Count > 2) nextCurrent = subStack[1];
            }
        }

        stack.Clear();
        addStack(new MessageItem { pm = pm, qm = qm, body = body });
        addStack(subStack);
        addStack(btnBack);
        ctrlCurrent = nextCurrent;

        addStack(new BtnFillCtrl
        {
            follow = Side.Left,
            gap = 10,
            sz = new(32, 32),
            onRender = (ctrl, g, tt, isCurrent, isPressed, prior) =>
            {
                PlotQuestMini.drawPage(g, tt.dtx + 7, tt.dty + 6, 20, ColorSet.csFore.get(isCurrent, isPressed, false).toPen(1));
                return false;
            },
            onClick = btn =>
            {
                this.onBackCustom = () =>
                {
                    this.showMessage(pm, qm, ps).justDoIt();
                    this.onBackCustom = null;
                };
                showCmdrQuestSummary(pm.parent);
            },
        });

        // remember this message has now been read
        if (!pm.read)
        {
            await pm.parent.onMessageRead(pm.id);
            pm.read = true;
            await pm.parent.save(true);
        }

        mode = Mode.msgItem;
    }

    private void showCmdrQuestSummary(PlayQuest pq)
    {
        var priorStack = new List<Ctrl>(stack);
        var priorCustomBack = this.onBackCustom;
        List<Ctrl> subStack = [];

        var btnBack = new BtnFillDrawCtrl { follow = Side.Top, gap = 4, offset = new(0, 0), sz = new(72, 32), iconName = "close", iconSize = 18, iconOffset = new(28, 10), onClick = btn => priorCustomBack!() };
        var btnMore = new BtnFillTextCtrl { follow = Side.Left, gap = 10, sz = new(84, 32), autoSize = true, text = "..." };
        var btnPause = new BtnFillTextCtrl { follow = Side.Left, gap = 10, sz = new(84, 32), text = "Pause" };
        var btnRemove = new BtnFillTextCtrl { follow = Side.Left, gap = 10, sz = new(84, 32), text = "Remove" };

        var lblConfirm = new TextCtrl { follow = Side.Top, gap = 4, autoSize = true, color = C.cyan };
        var btnYes = new BtnFillTextCtrl { follow = Side.Top, gap = 10, sz = new(72, 32), text = "Yes", onClick = btn => this.Invalidate() };
        var btnNo = new BtnFillTextCtrl { follow = Side.Left, gap = 10, sz = new(72, 32), text = "No", onClick = btn => this.Invalidate() };
        var lblActing = new TextCtrl { follow = Side.Left, gap = 10, autoSize = true, color = C.oranger };

        btnMore.onClick = b =>
        {
            stack.Remove(btnMore);
            // prevent devQuest from being paused
            if (!pq.dev) addStack(btnPause);
            addStack(btnRemove);
            ctrlCurrent = btnBack;
        };

        btnNo.onClick = btn =>
        {
            // decline
            btnBack.hidden = false;
            btnPause.hidden = false;
            btnRemove.hidden = false;
            onBackCustom = priorCustomBack;

            stack.RemoveAll(c => subStack.Contains(c));
            ctrlCurrent = btnBack;
        };

        var preAction = (QuestState newState) =>
        {
            // show prompt
            btnBack.hidden = true;
            btnPause.hidden = true;
            btnRemove.hidden = true;
            subStack = addStack(lblConfirm, btnYes, btnNo, lblActing);
            ctrlCurrent = btnNo;
            priorCustomBack = this.onBackCustom;

            lblConfirm.text = newState == QuestState.paused
                ? "Are you sure?"
                : "Are you sure? This will remove all progress.";

            btnYes.onClick = btn =>
            {
                lblConfirm.color = C.grey;
                lblActing.text = newState == QuestState.paused ? "Pausing ..." : "Removing ...";
                this.Invalidate();

                pq.parent.removeQuest(pq, newState).continueOnMain(this, () =>
                {
                    btnBack.hidden = false;
                    btnBack.onClick = btn => this.setMode(Mode.cmdrQuests);
                    stack.RemoveAll(c => subStack.Contains(c));
                    addStack(new TextCtrl { follow = Side.Left, gap = 10, autoSize = true, text = "Better luck next time ...", color = C.oranger });
                    ctrlCurrent = btnBack;
                    this.Invalidate();
                    PlotBase2.renderAll(null, true);

                    // update our internal state (rather than re-hit the server)
                    if (lastFID != null)
                        fetchCmdrQuests(lastFID).justDoIt();
                });
            };

            onBackCustom = () =>
            {
                // do the 'No' click action
                btnNo.onClick(btnNo);
            };
        };

        btnPause.onClick = btn => preAction(QuestState.paused);
        btnRemove.onClick = btn => preAction(QuestState.unknown);

        stack.Clear();
        addStack(
            new QuestSummary { pq = pq },
            btnBack,
            btnMore
        );
        ctrlCurrent = btnBack;
    }

    #endregion

    #region Cmdr quests

    private bool renderCmdrQuests(Graphics g, TextCursor tt)
    {
        // TODO: make this a ctrl
        tt.dty = N.ten;
        tt.draw(N.hundred, "Quests:", GameColors.Fonts.arial_16B);
        tt.newLine(true);

        if (loadingCatalog)
        {
            tt.dty += 32;
            tt.draw(N.hundred, "... loading ...");
        }

        return false;
    }

    private void setFilter(Ctrl btn, QuestState newFilter)
    {
        stack.Clear();

        // start loading catalog, if needed
        if (newFilter != QuestState.active && publishedQuests == null)
        {
            // fetch on a thread
            Task.Run(() => this.fetchPublishedQuests().continueOnMain(this, () => this.setFilter(btn, newFilter)));
            return;
        }

        // set bottomBar on the buttons
        extras.ForEach(c => { if (c is BtnFillTextCtrl c2) { c2.bottomBar = c == btn; } });

        // fill stack with filtered lines
        var items = cmdrQuests.Where(qs => qs.state == newFilter).ToList();
        if (PlayState.current?.devQuest != null)
        {
            items.Insert(0, new QuestCmdrStatus
            {
                publisher = PlayState.current.devQuest.publisher,
                id = PlayState.current.devQuest.id,
                ver = PlayState.current.devQuest.ver,
                state = QuestState.active,
                stateChangedOn = PlayState.current.devQuest.startTime.HasValue ? PlayState.current.devQuest.startTime.Value : default,
            });
        }

        if (items.Count == 0)
        {
            addStack(new TextCtrl { offset = new(0, 0), autoSize = true, color = C.grey, text = "None" });
            if (newFilter == QuestState.active)
                addStack(new BtnFillTextCtrl { follow = Side.Top, gap = 10, autoSize = true, text = "Find more in the catalog", onClick = btn => setMode(Mode.catalog) });
        }
        else
        {
            var lines = items.Select(qs =>
            {
                var pq = PlayState.current?.activeQuests.Find(pq => pq.publisher == qs.publisher && pq.id == qs.id);
                var qd = pq?.quest ?? publishedQuests?.Find(x => x.publisher == qs.publisher && x.id == qs.id);

                if (qd == null) return null;
                return new QuestCatalogLine
                {
                    qd = qd,
                    qs = qs,
                    follow = Side.Top,
                    gap = 4,
                    onClick = b =>
                    {
                        this.onBackCustom = () =>
                        {
                            this.setFilter(btn, newFilter);
                            this.onBackCustom = null;
                        };

                        if (newFilter == QuestState.active && pq != null)
                            showCmdrQuestSummary(pq!);
                        else
                            showQuestDef(qd);
                    },
                };
            }).ToList();
            addStack(lines.Where(c => c != null).Cast<Ctrl>());
            ctrlCurrent = stack.FirstOrDefault() ?? ctrlCurrent;
        }
    }

    private void loadCmdrQuests()
    {
        // show 4 filter buttons
        extras = addCtrl(
            new BtnFillTextCtrl { offset = new(190, 14), autoSize = true, text = "Active", onClick = btn => setFilter(btn, QuestState.active) },
            new BtnFillTextCtrl { follow = Side.Left, gap = 10, autoSize = true, text = "Paused", onClick = btn => setFilter(btn, QuestState.paused) },
            new BtnFillTextCtrl { follow = Side.Left, gap = 10, autoSize = true, text = "Completed", onClick = btn => setFilter(btn, QuestState.complete) },
            new BtnFillTextCtrl { follow = Side.Left, gap = 10, autoSize = true, text = "Failed", onClick = btn => setFilter(btn, QuestState.failed) }
        );

        // and default to 'Active'
        setFilter(extras[0], QuestState.active);

        this.Invalidate();
    }

    #endregion
}

class QuestCatalogLine : BtnFillCtrl
{
    private static Brush brushDevQuest;

    public required DefQuest qd;
    public QuestCmdrStatus? qs;

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
        PlotQuestMini.drawLogo(g, tt.dtx + (isCurrent ? 0 : 6), tt.dty, isCurrent, isCurrent ? 24 : 18);

        tt.dtx += 32;
        var x = tt.dtx;

        // render current state?
        var state = qd.equals(PlayState.current?.devQuest?.quest) ? "DEV" : qs?.state.ToString();
        if (state != null)
            tt.drawRight(form.scrollBox.Right - 6, state, C.cyan, GameColors.Fonts.arial_8);

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
            // TODO: make clickable boxes, like email tag copy
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
        tt.draw($" ({DefQuest.mapQuestDuration.GetValueOrDefault(qd.duration)})", C.oranged);
        tt.newLine(10, true);

        g.DrawLineR(C.Pens.orangeDark2r, r.X, tt.dty, w, 0);
        tt.dty += 10;

        // TODO: make seperate ctrls + publisher is a copy button

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


class MessageLine : BtnFillCtrl
{
    public required PlayMsg pm;
    public required DefMsg? qm;

    override public string ToString() => this.pm.subject ?? pm.body ?? "?";

    public override bool render(Graphics g, TextCursor tt, bool isCurrent, bool isPressed, Ctrl? prior)
    {
        r.Width = form.scrollWidth; // match scrollBox width        
        var redraw = base.render(g, tt, isCurrent, isPressed, prior); // render btn background

        // envelope
        tt.dty = r.Y + 4;
        tt.dtx = r.X + 8;
        PlotQuestMini.drawEnvelope(g, tt.dtx, tt.dty + 4, N.twoEight, pm.read ? C.Pens.orange2r : C.Pens.cyan2r);

        tt.dtx += 40;
        var x = tt.dtx;

        // received time on right side
        var time = pm.received.Subtract(DateTime.Now).TotalDays < 1
            ? pm.received.ToString("HH:mm")
            : pm.received.AddYears(1286).UtcDateTime.ToString("dd MMM yyyy - HH:mm");
        tt.drawRight(form.scrollBox.Right - 6, time, null, GameColors.Fonts.arial_9);

        // message from
        var from = pm.from ?? qm?.from;
        tt.draw(x, string.IsNullOrEmpty(from) ? "?" : from, ColorSet.csFore.get(isCurrent, isPressed, disabled), GameColors.Fonts.arial_12);
        tt.newLine(4, true);

        // subject
        var subject = pm.subject ?? qm?.subject ?? pm.body ?? qm?.body ?? "";
        tt.draw(x, subject, ColorSet.csFore.get(isCurrent, isPressed, disabled), GameColors.Fonts.arial_16);
        tt.newLine(4, true);

        // set our hight to be as larged as we needed
        return adjustHeight(tt.pad().Height - r.Y);
    }
}


internal class MessageItem : Ctrl
{
    public required PlayMsg pm;
    public required DefMsg? qm;
    public required string body;

    public override bool render(Graphics g, TextCursor tt, bool isCurrent, bool isPressed, Ctrl? prior)
    {
        r.Width = form.scrollWidth; // match scrollBox width
        var w = (int)(r.Width + r.X - 0);
        tt.dty += 1;

        g.DrawLineR(C.Pens.orangeDark2r, r.X, tt.dty, w, 0);
        tt.dty += 12;

        PlotQuestMini.drawEnvelope(g, r.X, tt.dty, 56, pm.read ? C.Pens.orange3r : C.Pens.cyan3r);

        var x2 = r.X + 72;
        var x3 = r.X + 122;

        // from
        var from = pm.from ?? qm?.from;
        tt.draw(x2, "From: ", GameColors.Fonts.arial_9);
        tt.draw(x3, string.IsNullOrEmpty(from) ? "?" : from, C.oranger, GameColors.Fonts.arial_12);
        tt.newLine(2, true);

        // sent
        var time = pm.received.AddYears(1286).UtcDateTime.ToString("dd MMM yyyy HH:mm");
        tt.draw(x2, "Sent: ", GameColors.Fonts.arial_9);
        tt.draw(x3, time, C.oranger, GameColors.Fonts.arial_12);
        tt.newLine(2, true);

        // subject
        var subject = pm.subject ?? qm?.subject ?? pm.body ?? qm?.body ?? "";
        tt.draw(x2, "Subject: ", GameColors.Fonts.arial_9);
        tt.draw(x3, subject, C.oranger, GameColors.Fonts.arial_16);
        tt.newLine(8, true);

        g.DrawLineR(C.Pens.orangeDark2r, r.X, tt.dty, w, 0);
        tt.dty += 12;

        // body
        var sz = tt.drawWrapped(r.X + 10, w - 10, body, C.oranger, GameColors.Fonts.arial_12);
        tt.newLine(true);

        g.DrawLineR(C.Pens.orangeDark2r, r.X, tt.dty, w, 0);
        tt.dty += 12;

        // set our hight to be as larged as we needed
        return adjustHeight(tt.pad(0, 10).Height - r.Y);
    }
}

internal class QuestSummary : Ctrl
{
    public required PlayQuest pq;

    public override bool render(Graphics g, TextCursor tt, bool isCurrent, bool isPressed, Ctrl? prior)
    {
        r.Width = form.scrollWidth; // match scrollBox width
        var w = (int)(r.Width + r.X - 0);
        tt.dty += 1;

        g.DrawLineR(C.Pens.orangeDark2r, r.X, tt.dty, w, 0);
        tt.dty += 12;
        var x = tt.dtx;
        var x2 = tt.dtx + 32;

        // title
        tt.draw(x, pq.quest.title, C.oranger, GameColors.Fonts.arial_16B);
        tt.newLine(6, true);

        // list objectives
        tt.draw(x, "Objectives:", C.oranged);
        tt.newLine(10, true);
        tt.dtx = x2;
        foreach (var (key, obj) in pq.objectives)
            if (obj.state == PlayObjective.State.visible)
                PanelQuest.drawObjective(g, tt, C.orange, key, obj, pq, false, null, r.X);

        // time played
        tt.dty += 10;
        tt.draw(x, "Started: ", C.oranged);
        var duration = DateTimeOffset.Now.Subtract(pq.startTime!.Value);
        tt.draw(Util.timeSpanToString(duration), C.oranger);
        tt.newLine(6, true);

        tt.dty += 6;
        g.DrawLineR(C.Pens.orangeDark2r, r.X, tt.dty, w, 0);

        // set our hight to be as larged as we needed
        return adjustHeight(tt.pad(0, 23).Height - r.Y);
    }
}
