using HandyControl.Controls;
using Prism.Services.Dialogs;

namespace Prometheus.Shared.Views
{
    public class DialogWindow : Window, IDialogWindow
    {
        public IDialogResult Result { get; set; }
    }
}
