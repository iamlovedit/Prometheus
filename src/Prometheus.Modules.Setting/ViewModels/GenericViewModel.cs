using Prism.Mvvm;
using System.Windows;

namespace Prometheus.Modules.Setting.ViewModels
{
    public class GenericViewModel : BindableBase
    {
        private string _title = Application.Current.FindResource("Setting.Generic")?.ToString();
        public string Title
        {
            get { return _title; }
            set { SetProperty(ref _title, value); }
        }

        public GenericViewModel()
        {

        }
    }
}
