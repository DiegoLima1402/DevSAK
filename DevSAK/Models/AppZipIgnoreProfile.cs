using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace DevSAK.Models
{
    public class AppZipIgnoreProfile : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }
        public List<string> IgnoredFiles { get; set; } = new();
        public List<string> IgnoredFolders { get; set; } = new();
        public List<string> IgnoredExtensions { get; set; } = new();
    }
}
