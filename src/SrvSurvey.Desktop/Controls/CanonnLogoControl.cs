using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace SrvSurvey.Desktop.Controls;

/// <summary>
/// Renders the original 16x16 Canonn Research mark used by legacy
/// PlotBioSystem beside a body's biological reward PIPs.
/// </summary>
public sealed class CanonnLogoControl : Control
{
    public const double NativeSize = 16;

    private const string OriginalCanonnLogo =
        "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAABhGlDQ1BJQ0MgcHJvZmlsZQAAKJF9kT1Iw0AcxV9TpaIVBzuIOgSsThZERRylikWwUNoKrTqYXPoFTRqSFBdHwbXg4Mdi1cHFWVcHV0EQ/ABxdnBSdJES/5cUWsR4cNyPd/ced+8AoV5mqtkxAaiaZSRjUTGTXRUDr+hBEAKGMCIxU4+nFtPwHF/38PH1LsKzvM/9OXqVnMkAn0g8x3TDIt4gntm0dM77xCFWlBTic+Jxgy5I/Mh12eU3zgWHBZ4ZMtLJeeIQsVhoY7mNWdFQiaeJw4qqUb6QcVnhvMVZLVdZ8578hcGctpLiOs1hxLCEOBIQIaOKEsqwEKFVI8VEkvajHv5Bx58gl0yuEhg5FlCBCsnxg//B727N/NSkmxSMAp0vtv0xCgR2gUbNtr+PbbtxAvifgSut5a/UgdlP0mstLXwE9G0DF9ctTd4DLneAgSddMiRH8tMU8nng/Yy+KQv03wLda25vzX2cPgBp6mr5Bjg4BMYKlL3u8e6u9t7+PdPs7wdtpXKloCOwegAAAAZiS0dEAP8A/wD/oL2nkwAAAAlwSFlzAAAuIwAALiMBeKU/dgAAAAd0SU1FB+gLDAIhIlG+PY8AAAAZdEVYdENvbW1lbnQAQ3JlYXRlZCB3aXRoIEdJTVBXgQ4XAAACgUlEQVQ4y42TQUhUURSG//PuceY5jIPTpE6UTaMZhVZQRkWIUUEmuagginZtohDGUMRatGhRmJjVokWLlm1LiKB2UUiBphRJNqKUmZNJKqMzr5l732mhDoEK/qvDPT/fvdz/HMIq+tnKtRZhnRH0bryjx1bzWSsdJtq4MK1lLNyunzpaiup3gFYDrNj4cEWV7XloRgCg+wJxRcgqL/HTGQDRWQeP8xR86SzebLunMwwA31v4SJ6Ck8pKki2Y6RTyAeBkJehJv5gbR2ETUEeEgzZLx0ya+gt9Ug2ghxcv5Q3tuue/F1R8alRlIR81aJHZ2TT6EnOoK/AiWNphxodiKpzVNJr7A+3KyGCM/UsAj0LS0fjtaAwaLf1KYTKtpbC0Q48DgADFkU49kQOU3TXDIRstf67zs69NltfLFHQBv98j036bjMeC+JiiADDQqCJexuiyFCwLFURo8LJ1YtaB2hSgcDKDLdNpBDyKwgKZHr6qajwW3GinSS4DEOTi3F/sn8tIQkQUWzibyiDtVRTJuiCbsWtqHnE7D8VjrexdBtAuZD4r6coH5p3NKDQib/1eGvR55JTNiIhADjwyibJO06dd2dd9bmEEcoCMod3b75vPAECEuAWqcrJi5jPUbAHr5zIYWvK6Br17S1VNDvAlpvxsyVKkePXDTMw48jqYT8dtxuGplLwP2MilVN5lHFZUAgAMAMF8iihaqONNajMrKrIZ55WFl0W39QsAGG1WtUNNqspmCtiMIgFUbpRvHgNdqubTWRcpRTJAII+HMSiCydAtHY3H8ogsd+fWLvMRa9XUNa5JtPHlxc08FG/igjUv05J+takoQPUi8i3cbp6v5PkH3Wr+Ai4/WW8AAAAASUVORK5CYII=";

    private static readonly Lazy<Bitmap> Logo = new(CreateLogo);

    internal static byte[] GetOriginalPngBytes() =>
        Convert.FromBase64String(OriginalCanonnLogo);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Draw(context, new Rect(Bounds.Size));
    }

    internal static void Draw(DrawingContext context, Rect destination)
    {
        if (destination.Width <= 0 || destination.Height <= 0)
        {
            return;
        }

        var bitmap = Logo.Value;
        var scale = Math.Min(
            destination.Width / bitmap.Size.Width,
            destination.Height / bitmap.Size.Height);
        var size = new Size(
            bitmap.Size.Width * scale,
            bitmap.Size.Height * scale);
        var target = new Rect(
            destination.Center.X - size.Width / 2,
            destination.Center.Y - size.Height / 2,
            size.Width,
            size.Height);
        context.DrawImage(bitmap, new Rect(bitmap.Size), target);
    }

    private static Bitmap CreateLogo()
    {
        var bytes = GetOriginalPngBytes();
        return new Bitmap(new MemoryStream(bytes, writable: false));
    }
}
