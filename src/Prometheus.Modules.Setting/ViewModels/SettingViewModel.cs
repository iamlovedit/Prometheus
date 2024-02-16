using Prism.Commands;
using Prism.Mvvm;
using Prometheus.Modules.Setting.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace Prometheus.Modules.Setting.ViewModels
{
    public class SettingViewModel : BindableBase
    {
        public SettingViewModel()
        {
            DemoModel = new PropertyGridDemoModel
            {
                String = "TestString",
                Enum = Gender.Female,
                Boolean = true,
                Integer = 98,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            PreferenceSetting = new PreferenceSetting()
            {
                Language = Language.English,
                Theme = Theme.Light
            };
        }

        public PropertyGridDemoModel DemoModel { get; set; }

        public PreferenceSetting PreferenceSetting { get; set; }

        public GenericSetting GenericSetting { get; set; }

        public SystemSetting SystemSetting { get; set; }
    }
    public class PropertyGridDemoModel
    {
        [Category("Category1")]
        public string String { get; set; }

        [Category("Category2")]
        public int Integer { get; set; }

        [Category("Category2")]
        public bool Boolean { get; set; }

        [Category("Category1")]
        public Gender Enum { get; set; }

        public HorizontalAlignment HorizontalAlignment { get; set; }

        public VerticalAlignment VerticalAlignment { get; set; }

        public ImageSource ImageSource { get; set; }
    }

    public enum Gender
    {
        Male,
        Female
    }
}
