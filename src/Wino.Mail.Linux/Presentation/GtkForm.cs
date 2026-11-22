using System.ComponentModel;
using System.Windows.Input;
using Wino.Core.Domain.Interfaces;
using Wino.Presentation.Bindings;

namespace Wino.Mail.Linux.Presentation;

/// <summary>
/// Binds GTK widgets to a ViewModel through the shared typed bindings (<see cref="PropertyBinding{TSource,TValue}"/>,
/// <see cref="CommandBinding"/>): labels, visibility, two-way text, switches and drop-downs, and command buttons.
/// Every binding and signal handler belongs to <see cref="Scope"/>, which the page disposes on release.
/// </summary>
public sealed class GtkForm<T>(T source, BindingScope scope, IDispatcher dispatcher, Action<Exception> error) where T : INotifyPropertyChanged
{
    public BindingScope Scope => scope;
    public T Source => source;
    public IDispatcher Dispatcher => dispatcher;
    public Action<Exception> Error => error;

    /// <summary>Runs <paramref name="apply"/> on the UI thread now and whenever <paramref name="property"/> changes.</summary>
    public PropertyBinding<T, TValue> Bind<TValue>(string property, Func<T, TValue> read, Action<TValue> apply, Action<T, TValue>? write = null)
        => scope.Own(new PropertyBinding<T, TValue>(source, property, read, apply, dispatcher, error, write));

    public void Visible(Gtk.Widget widget, string property, Func<T, bool> read) => Bind(property, read, visible => widget.Visible = visible);

    public void Sensitive(Gtk.Widget widget, string property, Func<T, bool> read) => Bind(property, read, enabled => widget.Sensitive = enabled);

    public Gtk.Label Label(string property, Func<T, string?> read, params string[] cssClasses)
    {
        var label = GtkUi.Label(string.Empty, cssClasses);
        Bind(property, read, text =>
        {
            label.SetText(text ?? string.Empty);
            label.Visible = !string.IsNullOrEmpty(text);
        });
        return label;
    }

    /// <summary>A text field, two-way with a string property. <paramref name="secret"/> hides the characters.</summary>
    public Gtk.Entry Text(string property, Func<T, string?> read, Action<T, string> write, string? placeholder = null, string? accessibleName = null, bool secret = false)
    {
        var entry = Gtk.Entry.New();
        entry.Hexpand = true;
        if (placeholder is not null) entry.PlaceholderText = placeholder;
        if (secret)
        {
            entry.Visibility = false;
            entry.InputPurpose = Gtk.InputPurpose.Password;
        }
        if (accessibleName is not null) entry.TooltipText = accessibleName;
        var buffer = entry.GetBuffer();
        var binding = Bind(property, read, value =>
        {
            var text = value ?? string.Empty;
            if (buffer.GetText() != text) buffer.SetText(text, -1);
        }, (s, v) => write(s, v ?? string.Empty));
        void Changed(GObject.Object sender, GObject.Object.NotifySignalArgs args)
        {
            if (args.Pspec.GetName() == "text") binding.UpdateSource(buffer.GetText());
        }
        buffer.OnNotify += Changed;
        scope.Own(new ActionDisposable(() => buffer.OnNotify -= Changed));
        return entry;
    }

    /// <summary>An on/off switch, two-way with a bool property.</summary>
    public Gtk.Switch Switch(string property, Func<T, bool> read, Action<T, bool> write)
    {
        var toggle = Gtk.Switch.New();
        toggle.Valign = Gtk.Align.Center;
        var binding = Bind(property, read, value => { if (toggle.Active != value) toggle.Active = value; }, write);
        void Changed(GObject.Object sender, GObject.Object.NotifySignalArgs args)
        {
            if (args.Pspec.GetName() == "active") binding.UpdateSource(toggle.Active);
        }
        toggle.OnNotify += Changed;
        scope.Own(new ActionDisposable(() => toggle.OnNotify -= Changed));
        return toggle;
    }

    /// <summary>A drop-down over fixed labels, two-way with an index property (-1 shows nothing selected).</summary>
    public Gtk.DropDown DropDown(IEnumerable<string> items, string property, Func<T, int> read, Action<T, int> write)
    {
        var labels = items.ToArray();
        var dropDown = Gtk.DropDown.NewFromStrings(labels);
        dropDown.Valign = Gtk.Align.Center;
        var binding = Bind(property, read, index =>
        {
            var selected = index >= 0 && index < labels.Length ? (uint)index : Gtk.Constants.INVALID_LIST_POSITION;
            if (dropDown.Selected != selected) dropDown.Selected = selected;
        }, write);
        void Changed(GObject.Object sender, GObject.Object.NotifySignalArgs args)
        {
            if (args.Pspec.GetName() != "selected") return;
            var selected = dropDown.Selected;
            if (selected != Gtk.Constants.INVALID_LIST_POSITION && selected < labels.Length) binding.UpdateSource((int)selected);
        }
        dropDown.OnNotify += Changed;
        scope.Own(new ActionDisposable(() => dropDown.OnNotify -= Changed));
        return dropDown;
    }

    /// <summary>A button that runs <paramref name="command"/> and follows its CanExecute.</summary>
    public Gtk.Button Command(string label, ICommand command, Func<object?>? parameter = null, string? cssClass = null)
    {
        var button = Gtk.Button.NewWithLabel(label);
        if (cssClass is not null) button.AddCssClass(cssClass);
        Attach(button, command, parameter);
        return button;
    }

    /// <summary>Connects an existing button to <paramref name="command"/>.</summary>
    public void Attach(Gtk.Button button, ICommand command, Func<object?>? parameter = null)
    {
        var binding = scope.Own(new CommandBinding(command, parameter ?? (() => null), enabled => button.Sensitive = enabled, dispatcher, error));
        void Clicked(Gtk.Button sender, EventArgs args) => binding.Execute();
        button.OnClicked += Clicked;
        scope.Own(new ActionDisposable(() => button.OnClicked -= Clicked));
    }
}

/// <summary>Runs an action once on disposal.</summary>
public sealed class ActionDisposable(Action dispose) : IDisposable
{
    private Action? _dispose = dispose;
    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}
