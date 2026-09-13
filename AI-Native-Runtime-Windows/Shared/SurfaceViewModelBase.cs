using AI_Native_Runtime_Windows.Services.Transport;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AI_Native_Runtime_Windows.Shared
{
    /// <summary>The loading/empty/error/access-denied state pattern every feature
    /// surface renders (`desktop-shell-conventions.md` §6), built once here rather
    /// than reinvented per surface.</summary>
    public enum SurfaceState
    {
        Loading,
        Empty,
        Data,
        Error,
    }

    /// <summary>
    /// Base view model for a feature surface backed by one or more RPC
    /// calls. Subclasses implement <see cref="LoadCoreAsync"/> and set
    /// <see cref="State"/> to <see cref="SurfaceState.Empty"/> or
    /// <see cref="SurfaceState.Data"/> once their own data is populated;
    /// this base class handles the loading/error transitions and the
    /// typed error mapping (§5) uniformly.
    /// </summary>
    public abstract partial class SurfaceViewModelBase : ObservableObject
    {
        [ObservableProperty]
        private SurfaceState state = SurfaceState.Loading;

        [ObservableProperty]
        private string errorMessage = "";

        [ObservableProperty]
        private bool isRetryable;

        [ObservableProperty]
        private bool isAccessDenied;

        public bool IsLoading => State == SurfaceState.Loading;
        public bool IsEmpty => State == SurfaceState.Empty;
        public bool IsData => State == SurfaceState.Data;
        public bool IsError => State == SurfaceState.Error;

        partial void OnStateChanged(SurfaceState value)
        {
            OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsData));
            OnPropertyChanged(nameof(IsError));
        }

        protected abstract Task LoadCoreAsync(CancellationToken ct);

        [RelayCommand]
        public async Task LoadAsync()
        {
            State = SurfaceState.Loading;
            try
            {
                await LoadCoreAsync(CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                var appError = RuntimeAppError.FromException(ex);
                ErrorMessage = appError.Message;
                IsRetryable = appError.PresentationCategory == ErrorPresentation.RetryableFailure;
                IsAccessDenied = appError.PresentationCategory == ErrorPresentation.AccessDenied;
                State = SurfaceState.Error;
            }
        }

        [RelayCommand]
        public Task RetryAsync() => LoadAsync();
    }
}
