using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class X11OverlayWindowManagementPolicyTests
{
    private const nuint Window = 42;
    private static readonly nint Display = (nint)17;

    /// <summary>Requires an unmapped window and a confirming attribute reply before bypass is reported successful.</summary>
    [Fact]
    public void BypassChangesOnlyOverrideRedirectAndConfirmsItBeforeReturning()
    {
        var native = new RecordingOperations((1, 0, 0), (1, 0, 1));

        Assert.True(X11OverlayWindowManagement.TryEnable(native, Display, Window));

        Assert.Equal(["read", "change", "read"], native.Calls);
        Assert.Equal((nuint)(1u << 9), native.ValueMask);
        Assert.Equal(1, native.ChangedAttributes.OverrideRedirect);
        Assert.Equal(0, native.ChangedAttributes.EventMask);
        Assert.Equal((nuint)0, native.ChangedAttributes.Cursor);
    }

    /// <summary>Leaves missing or already mapped windows unchanged instead of forcing a disruptive remap.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    public void BypassRejectsMissingOrMappedWindows(int status, int mapState)
    {
        var native = new RecordingOperations((status, mapState, 0));

        Assert.False(X11OverlayWindowManagement.TryEnable(native, Display, Window));

        Assert.Equal(["read"], native.Calls);
    }

    /// <summary>Reports failure when the native window disappears or ignores the attribute update.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    public void BypassRequiresConfirmationAfterChangingAttributes(int confirmationStatus, int overrideRedirect)
    {
        var native = new RecordingOperations((1, 0, 0), (confirmationStatus, 0, overrideRedirect));

        Assert.False(X11OverlayWindowManagement.TryEnable(native, Display, Window));

        Assert.Equal(["read", "change", "read"], native.Calls);
    }

    /// <summary>Raises visible unmanaged windows, focusing only explicit editor activation, then flushes requests.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RaisePreservesPassiveFocusAndHonorsEditorActivation(bool activate)
    {
        var native = new RecordingOperations((1, X11Native.IsViewable, 1));

        X11OverlayWindowManagement.Raise(native, Display, Window, activate);

        Assert.Equal(activate ? ["read", "raise", "focus", "flush"] : ["read", "raise", "flush"], native.Calls);
        Assert.Equal(activate ? (2, (nuint)0) : null, native.FocusRequest);
    }

    /// <summary>Keeps missing, managed, hidden, or unviewable windows untouched even when activation is requested.</summary>
    [Theory]
    [InlineData(0, 2, 1)]
    [InlineData(1, 2, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 1)]
    public void RaiseDoesNotActivateOrRemapIneligibleWindows(int status, int mapState, int overrideRedirect)
    {
        var native = new RecordingOperations((status, mapState, overrideRedirect));

        X11OverlayWindowManagement.Raise(native, Display, Window, activate: true);

        Assert.Equal(["read"], native.Calls);
        Assert.Null(native.FocusRequest);
    }

    /// <summary>Records the X11 boundary contract without loading native libraries or requiring a compositor.</summary>
    private sealed class RecordingOperations(params (int Status, int MapState, int OverrideRedirect)[] replies)
        : IX11OverlayWindowOperations
    {
        private readonly Queue<(int Status, int MapState, int OverrideRedirect)> repliesToRead = new(replies);

        /// <summary>Records native operation order to verify synchronization and focus behavior.</summary>
        internal List<string> Calls { get; } = [];

        /// <summary>Captures the X11 attribute-selection mask.</summary>
        internal nuint ValueMask { get; private set; }

        /// <summary>Captures values requested by the placement policy.</summary>
        internal X11Native.XSetWindowAttributes ChangedAttributes { get; private set; }

        /// <summary>Captures focus reversion and time only when explicit activation occurs.</summary>
        internal (int RevertTo, nuint Time)? FocusRequest { get; private set; }

        /// <summary>Returns the next server response and verifies that the original native handles are preserved.</summary>
        public int GetAttributes(nint display, nuint window, out X11Native.XWindowAttributes attributes)
        {
            VerifyHandles(display, window);
            Calls.Add("read");
            (int status, int mapState, int overrideRedirect) = repliesToRead.Dequeue();
            attributes = new X11Native.XWindowAttributes { MapState = mapState, OverrideRedirect = overrideRedirect };
            return status;
        }

        /// <summary>Records the attribute update and its mask without inventing a confirming server response.</summary>
        public void ChangeAttributes(
            nint display,
            nuint window,
            nuint valueMask,
            ref X11Native.XSetWindowAttributes attributes
        )
        {
            VerifyHandles(display, window);
            Calls.Add("change");
            ValueMask = valueMask;
            ChangedAttributes = attributes;
        }

        /// <summary>Records raising without mapping or focusing the window.</summary>
        public void RaiseWindow(nint display, nuint window)
        {
            VerifyHandles(display, window);
            Calls.Add("raise");
        }

        /// <summary>Records the explicit editor focus request.</summary>
        public void SetInputFocus(nint display, nuint window, int revertTo, nuint time)
        {
            VerifyHandles(display, window);
            Calls.Add("focus");
            FocusRequest = (revertTo, time);
        }

        /// <summary>Records flushing of the original display connection.</summary>
        public void Flush(nint display)
        {
            Assert.Equal(Display, display);
            Calls.Add("flush");
        }

        /// <summary>Checks that every window operation uses the requested display and window.</summary>
        private static void VerifyHandles(nint display, nuint window)
        {
            Assert.Equal(Display, display);
            Assert.Equal(Window, window);
        }
    }
}
