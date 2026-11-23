namespace Wino.Mail.Linux.Infrastructure;

/// <summary>The Mail page's reading pane, which shows rendering-frame pages (reader, idle page) one at a time.</summary>
public interface ILinuxRenderingFrameHost
{
    /// <summary>Shows and activates <paramref name="page"/>, or hands <paramref name="parameter"/> to the reader already shown.</summary>
    Task ShowAsync(ILinuxPage page, object? parameter);

    /// <summary>Releases the page in the pane.</summary>
    Task ClearAsync();
}
