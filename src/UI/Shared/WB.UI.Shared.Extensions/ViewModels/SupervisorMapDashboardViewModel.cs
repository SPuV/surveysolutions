﻿using System.Drawing;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using MvvmCross;
using MvvmCross.Base;
using MvvmCross.Plugin.Messenger;
using MvvmCross.ViewModels;
using WB.Core.GenericSubdomains.Portable.Services;
using WB.Core.SharedKernels.DataCollection.ValueObjects.Interview;
using WB.Core.SharedKernels.Enumerator.Properties;
using WB.Core.SharedKernels.Enumerator.Services;
using WB.Core.SharedKernels.Enumerator.Services.Infrastructure;
using WB.Core.SharedKernels.Enumerator.Services.Infrastructure.Storage;
using WB.Core.SharedKernels.Enumerator.Services.MapService;
using WB.Core.SharedKernels.Enumerator.ViewModels;
using WB.Core.SharedKernels.Enumerator.ViewModels.InterviewLoading;
using WB.Core.SharedKernels.Enumerator.Views;
using WB.UI.Shared.Extensions.Entities;
using WB.UI.Shared.Extensions.Extensions;
using WB.UI.Shared.Extensions.Services;

namespace WB.UI.Shared.Extensions.ViewModels;

public class SupervisorMapDashboardViewModel : MapDashboardViewModel
{
    private readonly IPlainStorage<InterviewerDocument> usersRepository;

    protected override InterviewStatus[] InterviewStatuses { get; } =
    {
        InterviewStatus.Created,
        InterviewStatus.InterviewerAssigned,
        InterviewStatus.Restarted,
        InterviewStatus.RejectedBySupervisor,
        InterviewStatus.Completed,
        InterviewStatus.SupervisorAssigned,
        InterviewStatus.RejectedByHeadquarters,
    };

    public SupervisorMapDashboardViewModel(IPrincipal principal, 
        IViewModelNavigationService viewModelNavigationService, 
        IUserInteractionService userInteractionService, 
        IMapService mapService, 
        IAssignmentDocumentsStorage assignmentsRepository, 
        IPlainStorage<InterviewView> interviewViewRepository, 
        IEnumeratorSettings enumeratorSettings, 
        ILogger logger, 
        IMapUtilityService mapUtilityService, 
        IMvxMainThreadAsyncDispatcher mainThreadAsyncDispatcher, 
        IPlainStorage<InterviewerDocument> usersRepository) 
        : base(principal, viewModelNavigationService, userInteractionService, mapService, assignmentsRepository, interviewViewRepository, enumeratorSettings, logger, mapUtilityService, mainThreadAsyncDispatcher)
    {
        this.usersRepository = usersRepository;
        this.messenger = Mvx.IoCProvider.GetSingleton<IMvxMessenger>();
    }

    public override bool SupportDifferentResponsible => true;
    
    protected override void CollectResponsibles()
    {
        List<ResponsibleItem> result = usersRepository.LoadAll()
            .Where(x => !x.IsLockedByHeadquarters && !x.IsLockedBySupervisor)
            .Select(user => new ResponsibleItem(user.InterviewerId, user.UserName))
            .OrderBy(x => x.Title)
            .ToList();

        var responsibleItems = new List<ResponsibleItem>
        {
            AllResponsibleDefault,
            new ResponsibleItem(Principal.CurrentUserIdentity.UserId, Principal.CurrentUserIdentity.Name),
        };
        responsibleItems.AddRange(result);

        Responsibles = new MvxObservableCollection<ResponsibleItem>(responsibleItems);

        if (SelectedResponsible != AllResponsibleDefault)
            SelectedResponsible = AllResponsibleDefault;
    }
    
    private MvxSubscriptionToken messengerSubscription;
    private readonly IMvxMessenger messenger;
    
    private async Task RefreshCounters()
    {
        ReloadEntities();
        await RefreshMarkers();
    }
    public override void ViewAppeared()
    {
        base.ViewAppeared();
        messengerSubscription = messenger.Subscribe<DashboardChangedMsg>(async msg => await RefreshCounters(), MvxReference.Strong);
    }

    public override void ViewDisappeared()
    {
        base.ViewDisappeared();
        messengerSubscription?.Dispose();
    }

    protected override Symbol GetInterviewMarkerSymbol(MarkerViewModel interview)
    {
        Color markerColor;

        switch (interview.Status)
        {
            case InterviewStatus.Created:
            case InterviewStatus.InterviewerAssigned:
            case InterviewStatus.Restarted:    
            case InterviewStatus.ApprovedBySupervisor:
            case InterviewStatus.RejectedBySupervisor:
                markerColor = Color.FromArgb(0x1f,0x95,0x00);
                break;
            case InterviewStatus.Completed:
                markerColor = Color.FromArgb(0x2a, 0x81, 0xcb);
                break;
            case InterviewStatus.RejectedByHeadquarters:
                markerColor = Color.FromArgb(0xe4,0x51,0x2b);
                break;
            default:
                markerColor = Color.Yellow;
                break;
        }

        return new CompositeSymbol(new[]
        {
            new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, Color.White, 22), //for contrast
            new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, markerColor, 16)
        });
    }
    
    protected override MarkerViewModel GetInterviewMarkerViewModel(InterviewView interview)
    {
        var markerViewModel = base.GetInterviewMarkerViewModel(interview);

        var responsibleName = Responsibles.FirstOrDefault(r => interview.ResponsibleId == r.ResponsibleId)?.Title;

        markerViewModel.Responsible = responsibleName;

        return markerViewModel;
    }

    protected override MarkerViewModel GetAssignmentMarkerViewModel(AssignmentDocument assignment)
    {
        var markerViewModel = base.GetAssignmentMarkerViewModel(assignment);
        markerViewModel.Responsible = assignment.ResponsibleName;
        markerViewModel.CanAssign = true;
        return markerViewModel;
    }
}
