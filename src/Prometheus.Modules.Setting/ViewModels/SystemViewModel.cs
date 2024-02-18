using Prism.Mvvm;
using System.Windows;

namespace Prometheus.Modules.Setting.ViewModels
{
    public class SystemViewModel : BindableBase
    {
        private string _title = Application.Current.FindResource("Setting.System")?.ToString();
        public string Title
        {
            get { return _title; }
            set { SetProperty(ref _title, value); }
        }

        public SystemViewModel()
        {

        }
    }
}
