using System.Collections.Generic;

namespace Prometheus.Services.Interfaces.Models
{
    public class Equipment
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public bool Active { get; set; }

        public bool InStore { get; set; }

        public List<int> From { get; set; }

        public List<int> To { get; set; }

        public List<string> Categories { get; set; }

        public int MaxStacks { get; set; }

        public string RequiredChampion { get; set; }

        public string RequiredAlly { get; set; }

        public string RequiredBuffCurrencyName { get; set; }

        public int RequiredBuffCurrencyCost { get; set; }

        public int SpecialRecipe { get; set; }

        public bool IsEnchantment { get; set; }

        public int Price { get; set; }

        public int PriceTotal { get; set; }

        public string IconPath { get; set; }
    }
}
