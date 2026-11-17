using GeoSemantic.Services;
using Microsoft.AspNetCore.Components;
namespace GeoSemantic.Components.Shared;
public abstract class WorkspaceComponent : ComponentBase, IDisposable
{
    [Inject] protected WorkspaceState State { get; set; } = default!;
    private bool disposed;
    protected override void OnInitialized() => State.Changed += HandleChange;
    private void HandleChange() { if (!disposed) _ = InvokeAsync(StateHasChanged); }
    public virtual void Dispose() { disposed = true; State.Changed -= HandleChange; GC.SuppressFinalize(this); }
}

