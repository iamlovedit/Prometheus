using Prism.Mvvm;
using System.ComponentModel;

namespace Prometheus.Modules.Setting.Models
{
    public class PreferenceSetting : BindableBase
    {
        [Category("Personalization")]
        public Language Language { get; set; }

        private Theme _theme;
        [Category("Personalization")]
        public Theme Theme
        {
            get { return _theme; }
            set
            {
                SetProperty(ref _theme, value);
            }
        }
    }

    public enum Language
    {
        English,
        简体中文
    }

    public enum Theme
    {
        Light,
        Dark,
        System
    }

}
