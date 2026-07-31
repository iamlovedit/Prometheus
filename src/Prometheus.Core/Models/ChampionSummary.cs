using Prism.Mvvm;

namespace Prometheus.Core.Models
{
    public class ChampionSummary : BindableBase
    {
        private string _iconUri;

        public int Id { get; set; }

        public string Name { get; set; }

        public string Alias { get; set; }

        public string SquarePortraitPath { get; set; }

        public List<string> Roles { get; set; }

        public string IconUri
        {
            get => _iconUri;
            set => SetProperty(ref _iconUri, value);
        }
    }
}
