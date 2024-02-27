using Prism.Mvvm;
using Prism.Services.Dialogs;
using System;

namespace Prometheus.Modules.Summoner.ViewModels
{
    public class SelectBackgroundDialogViewModel : BindableBase, IDialogAware
    {
        public SelectBackgroundDialogViewModel()
        {

        }

        public string Title { get; }

        public event Action<IDialogResult> RequestClose;

        public bool CanCloseDialog()
        {
            return true;
        }

        public void OnDialogClosed()
        {

        }

        public void OnDialogOpened(IDialogParameters parameters)
        {

        }
    }
}
