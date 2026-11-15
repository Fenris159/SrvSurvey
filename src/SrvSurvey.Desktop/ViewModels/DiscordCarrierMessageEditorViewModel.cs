using System.ComponentModel;
using SrvSurvey.Core.Discord;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Routes;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Keeps all three message drafts isolated until the editor applies them to settings.</summary>
public sealed class DiscordCarrierMessageEditorViewModel : INotifyPropertyChanged
{
    private readonly FrontierAccountSnapshot? account;
    private readonly DateTimeOffset previewTime;
    private DiscordCarrierMessageSettings settings;
    private int selectedEvent;
    private int selectedCarrier;

    public DiscordCarrierMessageEditorViewModel(
        DiscordCarrierMessageSettings settings,
        FrontierAccountSnapshot? account = null,
        DateTimeOffset? previewTime = null
    )
    {
        this.settings = settings;
        this.account = account;
        this.previewTime = previewTime ?? DateTimeOffset.Now;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public DiscordCarrierMessageSettings Settings => settings;
    internal int ImageSelectionVersion { get; private set; }
    public int SelectedEvent
    {
        get => selectedEvent;
        set
        {
            selectedEvent = value;
            Changed();
        }
    }
    public int SelectedCarrier
    {
        get => selectedCarrier;
        set
        {
            selectedCarrier = value;
            Changed();
        }
    }
    private CarrierAnnouncementKind Kind => (CarrierAnnouncementKind)SelectedEvent;
    private DiscordCarrierMessageTemplate Template => settings.ForEvent(Kind);
    public string Title
    {
        get => Template.Title;
        set => SetTemplate(Template with { Title = value ?? "" });
    }
    public string Message
    {
        get => Template.Message;
        set => SetTemplate(Template with { Message = value ?? "" });
    }
    public bool IncludeDetails
    {
        get => Template.IncludeDetails;
        set => SetTemplate(Template with { IncludeDetails = value });
    }
    public string ImagePath
    {
        get => (SelectedCarrier == 1 ? settings.SquadronImagePath : settings.PersonalImagePath) ?? "";
        set
        {
            settings =
                SelectedCarrier == 1
                    ? settings with
                    {
                        SquadronImagePath = value,
                        SquadronImageCrop = null,
                    }
                    : settings with
                    {
                        PersonalImagePath = value,
                        PersonalImageCrop = null,
                    };
            Changed();
        }
    }
    public DiscordCarrierImageCrop? ImageCrop
    {
        get => SelectedCarrier == 1 ? settings.SquadronImageCrop : settings.PersonalImageCrop;
        set
        {
            if (ImageCrop == value)
            {
                return;
            }
            settings =
                SelectedCarrier == 1
                    ? settings with
                    {
                        SquadronImageCrop = value,
                    }
                    : settings with
                    {
                        PersonalImageCrop = value,
                    };
            Changed();
        }
    }
    public double ImageZoom
    {
        get => ImageCrop?.EffectiveZoom ?? 1;
        set => ImageCrop = (ImageCrop ?? new()) with { Zoom = value };
    }
    public string ZoomLabel => $"{ImageZoom:0.0}×";

    public void SelectImage(string path)
    {
        ImageSelectionVersion++;
        settings =
            SelectedCarrier == 1
                ? settings with
                {
                    SquadronImagePath = path,
                    SquadronImageCrop = new(),
                }
                : settings with
                {
                    PersonalImagePath = path,
                    PersonalImageCrop = new(),
                };
        Changed();
    }

    public void ResetImageCrop() => ImageCrop = new();

    public string ImageName =>
        string.IsNullOrWhiteSpace(ImagePath) ? "No carrier image selected" : Path.GetFileName(ImagePath);
    public string? ValidationMessage =>
        Enum.GetValues<CarrierAnnouncementKind>()
            .Select(kind => DiscordCarrierMessageFormatter.Validate(settings.ForEvent(kind)))
            .FirstOrDefault(message => message is not null);
    public bool CanApply => ValidationMessage is null;
    public string CharacterCount => $"{Message.Length} / 4096 characters";
    public DiscordCarrierMessage Preview => DiscordCarrierMessageFormatter.Render(Sample, Template);
    public CarrierJumpAnnouncement Sample
    {
        get
        {
            FrontierCarrierSnapshot? carrier = SelectedCarrier == 1 ? account?.SquadronCarrier : account?.Carrier;
            return new(
                Kind,
                SelectedCarrier == 1 ? FollowRouteCarrierKind.Squadron : FollowRouteCarrierKind.Personal,
                42,
                carrier?.Name ?? "Your carrier",
                carrier?.Callsign ?? "ABC-123",
                carrier?.System ?? "Sol",
                "Lave",
                "Lave 1",
                previewTime.AddMinutes(15),
                previewTime,
                "Exploration route: Sol → Lave → Colonia"
            );
        }
    }
    public DateTimeOffset PreviewTime => previewTime;

    public void RestoreDefault() => SetTemplate(DiscordCarrierMessageSettings.DefaultTemplate(Kind));

    private void SetTemplate(DiscordCarrierMessageTemplate template)
    {
        settings = Kind switch
        {
            CarrierAnnouncementKind.Scheduled => settings with { Scheduled = template },
            CarrierAnnouncementKind.Cancelled => settings with { Cancelled = template },
            _ => settings with { Arrived = template },
        };
        Changed();
    }

    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
