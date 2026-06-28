using System.Windows;
using System.Windows.Input;

namespace GoatShot.App.Windows;

public static class EscapeKeyCloseBehavior
{
    public static void Attach(Window window, bool cancelDialog = false)
    {
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || Keyboard.Modifiers != ModifierKeys.None)
            {
                return;
            }

            e.Handled = true;
            if (!cancelDialog)
            {
                window.Close();
                return;
            }

            try
            {
                window.DialogResult = false;
            }
            catch (InvalidOperationException)
            {
                window.Close();
            }
        };
    }
}
