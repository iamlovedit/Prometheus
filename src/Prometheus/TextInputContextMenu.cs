using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Prometheus
{
    internal static class TextInputContextMenu
    {
        private const string ContextMenuStyleKey = "TextInputContextMenuStyle";
        private const string MenuItemStyleKey = "TextInputMenuItemStyle";
        private const string SeparatorStyleKey = "TextInputMenuSeparatorStyle";

        private static int _isRegistered;

        public static void Register()
        {
            if (Interlocked.Exchange(ref _isRegistered, 1) != 0)
            {
                return;
            }

            EventManager.RegisterClassHandler(
                typeof(TextBoxBase),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(HandleTextInputLoaded));
            EventManager.RegisterClassHandler(
                typeof(PasswordBox),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(HandleTextInputLoaded));
        }

        private static void HandleTextInputLoaded(object sender, RoutedEventArgs args)
        {
            if (sender is not FrameworkElement input || input.ContextMenu is not null)
            {
                return;
            }

            input.ContextMenu = sender is PasswordBox
                ? CreatePasswordBoxMenu((PasswordBox)sender)
                : CreateTextBoxMenu((TextBoxBase)sender);
        }

        private static ContextMenu CreateTextBoxMenu(TextBoxBase target)
        {
            var menu = CreateMenu();
            menu.Items.Add(CreateCommandItem(
                ApplicationCommands.Undo,
                target,
                "TextInput.ContextMenu.Undo",
                "Ctrl+Z",
                "\uE7A7"));
            menu.Items.Add(CreateSeparator());
            menu.Items.Add(CreateCommandItem(
                ApplicationCommands.Cut,
                target,
                "TextInput.ContextMenu.Cut",
                "Ctrl+X",
                "\uE8C6"));
            menu.Items.Add(CreateCommandItem(
                ApplicationCommands.Copy,
                target,
                "TextInput.ContextMenu.Copy",
                "Ctrl+C",
                "\uE8C8"));
            menu.Items.Add(CreateCommandItem(
                ApplicationCommands.Paste,
                target,
                "TextInput.ContextMenu.Paste",
                "Ctrl+V",
                "\uE77F"));
            menu.Items.Add(CreateCommandItem(
                EditingCommands.Delete,
                target,
                "TextInput.ContextMenu.Delete",
                "Delete",
                "\uE74D"));
            menu.Items.Add(CreateSeparator());
            menu.Items.Add(CreateCommandItem(
                ApplicationCommands.SelectAll,
                target,
                "TextInput.ContextMenu.SelectAll",
                "Ctrl+A",
                "\uE8B3"));
            return menu;
        }

        private static ContextMenu CreatePasswordBoxMenu(PasswordBox target)
        {
            var menu = CreateMenu();
            menu.Items.Add(CreateCommandItem(
                ApplicationCommands.Paste,
                target,
                "TextInput.ContextMenu.Paste",
                "Ctrl+V",
                "\uE77F"));
            menu.Items.Add(CreateSeparator());
            menu.Items.Add(CreateCommandItem(
                ApplicationCommands.SelectAll,
                target,
                "TextInput.ContextMenu.SelectAll",
                "Ctrl+A",
                "\uE8B3"));
            return menu;
        }

        private static ContextMenu CreateMenu()
        {
            var menu = new ContextMenu();
            menu.SetResourceReference(FrameworkElement.StyleProperty, ContextMenuStyleKey);
            return menu;
        }

        private static MenuItem CreateCommandItem(
            ICommand command,
            IInputElement target,
            string headerResourceKey,
            string inputGestureText,
            string iconGlyph)
        {
            var icon = new TextBlock
            {
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 13,
                Text = iconGlyph,
                VerticalAlignment = VerticalAlignment.Center
            };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");

            var item = new MenuItem
            {
                Command = command,
                CommandTarget = target,
                Icon = icon,
                InputGestureText = inputGestureText
            };
            item.SetResourceReference(FrameworkElement.StyleProperty, MenuItemStyleKey);
            item.SetResourceReference(HeaderedItemsControl.HeaderProperty, headerResourceKey);
            return item;
        }

        private static Separator CreateSeparator()
        {
            var separator = new Separator();
            separator.SetResourceReference(FrameworkElement.StyleProperty, SeparatorStyleKey);
            return separator;
        }
    }
}
