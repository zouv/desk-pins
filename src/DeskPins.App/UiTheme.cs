using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace DeskPins.App
{
    public static class UiTheme
    {
        public static Brush Accent => SystemParameters.HighContrast ? SystemColors.HighlightBrush : Ui.Brush("#1268B3");
        public static Brush Surface => SystemParameters.HighContrast ? SystemColors.WindowBrush : Brushes.White;
        public static Brush Background => SystemParameters.HighContrast ? SystemColors.WindowBrush : Ui.Brush("#F5F7FA");
        public static Brush Text => SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Ui.Brush("#243244");
        public static Brush Muted => SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Ui.Brush("#596779");
        public static Brush Border => SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Ui.Brush("#CBD3DD");
        public static Brush Danger => SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Ui.Brush("#B42318");
        public static Brush Selected => SystemParameters.HighContrast ? SystemColors.HighlightBrush : Ui.Brush("#E6F1FB");

        public static void Apply(Window window)
        {
            window.Background = Surface; window.Foreground = Text;
            window.FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); window.FontSize = 13;
            window.UseLayoutRounding = true; window.SnapsToDevicePixels = true;
            var input = new Style(typeof(TextBox));
            input.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 5, 7, 5)));
            input.Setters.Add(new Setter(Control.BorderBrushProperty, Border));
            input.Setters.Add(new Setter(Control.BackgroundProperty, Surface));
            input.Setters.Add(new Setter(Control.ForegroundProperty, Text));
            input.Setters.Add(new Setter(Control.MinHeightProperty, 30d));
            window.Resources[typeof(TextBox)] = input;
            var combo = new Style(typeof(ComboBox));
            combo.Setters.Add(new Setter(Control.MinHeightProperty, 30d));
            combo.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 3, 6, 3)));
            combo.Setters.Add(new Setter(Control.ForegroundProperty, Text));
            window.Resources[typeof(ComboBox)] = combo;
        }

        public static Style ThinScrollBarStyle()
        {
            var style = new Style(typeof(ScrollBar));
            var vertical = new Trigger { Property = ScrollBar.OrientationProperty, Value = Orientation.Vertical };
            vertical.Setters.Add(new Setter(FrameworkElement.WidthProperty, 6d));
            vertical.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 6d));
            var horizontal = new Trigger { Property = ScrollBar.OrientationProperty, Value = Orientation.Horizontal };
            horizontal.Setters.Add(new Setter(FrameworkElement.HeightProperty, 6d));
            horizontal.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 6d));
            style.Triggers.Add(vertical); style.Triggers.Add(horizontal);
            return style;
        }

        public static Style ComboBoxStyle()
        {
            var style = new Style(typeof(ComboBox));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Surface));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Border));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 0, 26, 0)));
            style.Setters.Add(new Setter(Control.MinHeightProperty, 30d));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));

            var itemStyle = new Style(typeof(ComboBoxItem));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, Text));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 4, 8, 4)));
            var itemSelected = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
            itemSelected.Setters.Add(new Setter(Control.BackgroundProperty, Selected)); itemStyle.Triggers.Add(itemSelected);
            var itemHighlighted = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            itemHighlighted.Setters.Add(new Setter(Control.BackgroundProperty, Selected)); itemStyle.Triggers.Add(itemHighlighted);
            style.Setters.Add(new Setter(ItemsControl.ItemContainerStyleProperty, itemStyle));

            var template = new ControlTemplate(typeof(ComboBox));
            var root = new FrameworkElementFactory(typeof(Grid));
            var chrome = new FrameworkElementFactory(typeof(System.Windows.Controls.Border)); chrome.Name = "Chrome";
            chrome.SetValue(System.Windows.Controls.Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            chrome.SetValue(System.Windows.Controls.Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            chrome.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            chrome.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(4));
            root.AppendChild(chrome);

            var selection = new FrameworkElementFactory(typeof(ContentPresenter));
            selection.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            selection.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            selection.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            selection.SetValue(ContentPresenter.IsHitTestVisibleProperty, false);
            selection.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
            selection.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemTemplateProperty));
            selection.SetValue(ContentPresenter.ContentStringFormatProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemStringFormatProperty));
            chrome.AppendChild(selection);

            var toggleTemplate = new ControlTemplate(typeof(System.Windows.Controls.Primitives.ToggleButton));
            var toggleRoot = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            toggleRoot.SetValue(System.Windows.Controls.Border.BackgroundProperty, Brushes.Transparent);
            var arrow = new FrameworkElementFactory(typeof(TextBlock));
            arrow.SetValue(TextBlock.TextProperty, "");
            arrow.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets"));
            arrow.SetValue(TextBlock.FontSizeProperty, 10d);
            arrow.SetValue(TextBlock.ForegroundProperty, Muted);
            arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            arrow.SetValue(UIElement.IsHitTestVisibleProperty, false);
            toggleRoot.AppendChild(arrow); toggleTemplate.VisualTree = toggleRoot;
            var toggle = new FrameworkElementFactory(typeof(System.Windows.Controls.Primitives.ToggleButton)); toggle.Name = "DropDownToggle";
            toggle.SetValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding("IsDropDownOpen") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Mode = BindingMode.TwoWay });
            toggle.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            toggle.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            toggle.SetValue(UIElement.FocusableProperty, false);
            toggle.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            toggle.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
            toggle.SetValue(Control.TemplateProperty, toggleTemplate);
            root.AppendChild(toggle);

            var popup = new FrameworkElementFactory(typeof(Popup)); popup.Name = "PART_Popup";
            popup.SetValue(Popup.IsOpenProperty, new TemplateBindingExtension(ComboBox.IsDropDownOpenProperty));
            popup.SetValue(Popup.PlacementProperty, System.Windows.Controls.Primitives.PlacementMode.Bottom);
            popup.SetValue(Popup.PlacementTargetProperty, new Binding(".") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(FrameworkElement.WidthProperty, new Binding("ActualWidth") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            var popupBorder = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            popupBorder.SetValue(System.Windows.Controls.Border.BackgroundProperty, Surface);
            popupBorder.SetValue(System.Windows.Controls.Border.BorderBrushProperty, Border);
            popupBorder.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(1));
            popupBorder.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(4));
            var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            scroll.SetValue(FrameworkElement.MaxHeightProperty, 240d);
            scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            popupBorder.AppendChild(scroll); popup.AppendChild(popupBorder); root.AppendChild(popup);
            template.VisualTree = root;

            var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focus.Setters.Add(new Setter(Control.BorderBrushProperty, Accent)); template.Triggers.Add(focus);
            var dropped = new Trigger { Property = ComboBox.IsDropDownOpenProperty, Value = true };
            dropped.Setters.Add(new Setter(Control.BorderBrushProperty, Accent)); template.Triggers.Add(dropped);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .45)); template.Triggers.Add(disabled);
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        public static Style ButtonStyle(bool primary = false, bool danger = false)
        {            var foreground = primary ? (SystemParameters.HighContrast ? SystemColors.HighlightTextBrush : Brushes.White) : danger ? Danger : Text;
            var background = primary ? Accent : Surface;
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.BackgroundProperty, background));
            style.Setters.Add(new Setter(Control.ForegroundProperty, foreground));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, primary ? Accent : Border));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border)); border.Name = "Chrome";
            border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(System.Windows.Controls.Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(System.Windows.Controls.Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true); border.AppendChild(content); template.VisualTree = border;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BorderBrushProperty, Accent)); template.Triggers.Add(hover);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2))); focus.Setters.Add(new Setter(Control.BorderBrushProperty, Accent)); template.Triggers.Add(focus);
            var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, .8)); template.Triggers.Add(pressed);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .45)); template.Triggers.Add(disabled);
            style.Setters.Add(new Setter(Control.TemplateProperty, template)); return style;
        }
    }
}
