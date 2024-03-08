using System;
using System.Reflection;
using System.Windows;

namespace Prometheus.Core.Models
{
    [AttributeUsage(AttributeTargets.Field)]
    public class DisplayKeyAttribute(string key) : Attribute
    {
        public string Key { get; } = key;
        public string GetDisplayValue()
        {
            return Application.Current.FindResource(Key)?.ToString();
        }

        public static DisplayKeyAttribute GetDisplayKey(object @object)
        {
            return @object.GetType().GetField(@object.ToString()).GetCustomAttribute<DisplayKeyAttribute>();
        }
    }
    public enum MenuName
    {
        [DisplayKey("Menu.Home")]
        Home,
        [DisplayKey("Menu.Career")]
        Career,
        [DisplayKey("Menu.Inventory")]
        Inventory,
        [DisplayKey("Menu.Search")]
        Search,
        [DisplayKey("Menu.Match")]
        Match,
        [DisplayKey("Menu.Utility")]
        Utility,
        [DisplayKey("Menu.Setting")]
        Setting
    }
}

