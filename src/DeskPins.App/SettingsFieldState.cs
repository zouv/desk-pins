using System;
using System.Windows;

namespace DeskPins.App
{
    // A field belongs to the draft, never to the currently displayed page/control.
    internal sealed class SettingsFieldState
    {
        internal string Key, Label, Raw, Error;
        internal int Category, StyleIndex = -1, ShortcutTab = -1;
        internal Func<string, string> Validate;
        internal Action<string> Apply;
        internal FrameworkElement Control;
        internal Action RefreshFeedback;

        internal bool Check()
        {
            Error = Validate == null ? null : Validate(Raw);
            if (string.IsNullOrEmpty(Error) && Apply != null) Apply(Raw);
            if (RefreshFeedback != null) RefreshFeedback();
            return string.IsNullOrEmpty(Error);
        }
    }
}
