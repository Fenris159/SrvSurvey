namespace SrvSurvey.Core.Storage;

/// <summary>Serializes cooperating writers across store instances and application processes.</summary>
internal static class FileMutationLease
{
    private const int UnixWouldBlock = 11;
    private const int SharingViolation = 32;
    private const int LockViolation = 33;

    public static async Task<FileStream> AcquireAsync(string resourcePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string leasePath = resourcePath + ".lease";
        string directory =
            Path.GetDirectoryName(leasePath)
            ?? throw new ArgumentException("The mutation resource must have a parent directory.", nameof(resourcePath));
        Directory.CreateDirectory(directory);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // The persistent lease name must not be replaced or deleted while writers are running.
                return new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception)
                when ((exception.HResult & 0xFFFF) is UnixWouldBlock or SharingViolation or LockViolation)
            {
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
