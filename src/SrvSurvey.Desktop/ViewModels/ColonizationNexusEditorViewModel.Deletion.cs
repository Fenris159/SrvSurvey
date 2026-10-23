namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationNexusEditorViewModel
{
    private bool isDeleteConfirmationVisible;
    private string deletionStatus = string.Empty;
    public string DeletionStatus
    {
        get => deletionStatus;
        private set
        {
            if (Set(ref deletionStatus, value))
            {
                Changed(nameof(HasDeletionStatus));
            }
        }
    }
    public bool HasDeletionStatus => DeletionStatus.Length > 0;
    internal Func<Task> OnDeleted { get; init; } = () => Task.CompletedTask;
    private bool IsOwner => string.Equals(Commander, Owner, StringComparison.OrdinalIgnoreCase);
    public bool CanDelete => IsEditing && IsOwner && CanEdit;
    public bool CanConfirmDeletion =>
        IsEditing && IsOwner && IsCurrent && !IsBusy && !completed && IsDeleteConfirmationVisible;
    public string DeleteWarning => $"Delete Nexus '{Name}'? This cannot be undone.";
    public bool IsDeleteConfirmationVisible
    {
        get => isDeleteConfirmationVisible;
        private set
        {
            if (Set(ref isDeleteConfirmationVisible, value))
            {
                RefreshAvailability();
            }
        }
    }

    public void RequestDeletion()
    {
        if (CanDelete)
        {
            DeletionStatus = string.Empty;
            Changed(nameof(DeleteWarning));
            IsDeleteConfirmationVisible = true;
        }
    }

    public void CancelDeletion()
    {
        if (!IsBusy)
        {
            IsDeleteConfirmationVisible = false;
        }
    }

    public async Task ConfirmDeletionAsync()
    {
        if (!CanConfirmDeletion || created is null)
        {
            return;
        }
        IsBusy = true;
        try
        {
            Status = "Deleting Nexus on Raven Colonial...";
            DeletionStatus = Status;
            await writer.DeleteNexusAsync(created.Id, Commander, apiKey, lifetime.Token);
            if (!IsCurrent)
            {
                return;
            }
            completed = true;
            await OnDeleted();
            if (IsCurrent)
            {
                CloseRequested?.Invoke();
            }
        }
        catch (Exception exception) when (IsServiceFailure(exception))
        {
            if (IsCurrent)
            {
                Status =
                    "Nexus deletion could not be confirmed. Close this window and refresh the Nexus list before trying again.";
                DeletionStatus = Status;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
