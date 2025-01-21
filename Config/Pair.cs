using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LocalizationTranslationWPF.Data
{
    public struct Pair
    {
        public Pair(string name, string isoCode)
        {
            Name = name;
            this.isoCode = isoCode;
        }
        public string Name { get; }
        public string isoCode;
    }
}
