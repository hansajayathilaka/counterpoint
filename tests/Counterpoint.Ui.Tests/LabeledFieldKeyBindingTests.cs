using System;
using System.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Counterpoint.Ui.Controls;
using FluentAssertions;

namespace Counterpoint.Ui.Tests;

/// <summary>
/// Task P3-T14's own risk mitigation (SRS UI-01, NFR-U4): every side-panel field the sales screen
/// converts from a bare <see cref="TextBox"/> to a P3-T12 <c>LabeledField</c> keeps its
/// Enter-submits-the-panel keyboard behaviour (SRS UI-01 "fully operable from the keyboard"), even
/// though the <c>&lt;TextBox.KeyBindings&gt;</c> that used to sit directly on the box now has to
/// live on the wrapping <see cref="LabeledTextField"/>/<see cref="LabeledNumericField"/>
/// <see cref="UserControl"/> instead, since a field's inner <c>TextBox</c> is no longer the
/// element declared in the view's markup.
/// </summary>
/// <remarks>
/// This is proven here with a real (headless) key press dispatched through
/// <see cref="HeadlessWindowExtensions.KeyPressQwerty"/> - the same routed-event path a real
/// keyboard uses - rather than assumed: Avalonia's <c>KeyBindings</c> collection is consulted by
/// every ancestor an event bubbles through on its way from the focused control to the window, not
/// only by the window itself, so a gesture declared on the wrapper still fires while its nested
/// input has focus.
/// </remarks>
public sealed class LabeledFieldKeyBindingTests
{
    private sealed class RelayCommand(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }

    [AvaloniaFact]
    public void UI_01_AnEnterKeyBindingOnTheWrappingLabeledTextFieldFiresWhileItsInnerTextBoxHasFocus()
    {
        var fired = 0;
        var field = new LabeledTextField { Label = "Probe" };
        field.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.Enter),
            Command = new RelayCommand(() => fired++),
        });

        var window = new Window { Content = field };
        window.Show();

        field.GetVisualDescendants().OfType<TextBox>().Single().Focus();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        fired.Should().Be(
            1,
            "an Enter KeyBinding declared on the LabeledTextField wrapper must still fire while "
                + "its inner TextBox has focus - exactly the case every converted side-panel field "
                + "in SalesWindow now relies on");
    }

    [AvaloniaFact]
    public void UI_01_AnEnterKeyBindingOnTheWrappingLabeledNumericFieldFiresWhileItsInnerTextBoxHasFocus()
    {
        var fired = 0;
        var field = new LabeledNumericField { Label = "Probe" };
        field.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.Enter),
            Command = new RelayCommand(() => fired++),
        });

        var window = new Window { Content = field };
        window.Show();

        field.GetVisualDescendants().OfType<TextBox>().Single().Focus();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        fired.Should().Be(
            1,
            "the numeric variant must behave identically - every tender box and the discount/open "
                + "item/opening-float fields on the sales screen are LabeledNumericField");
    }
}
