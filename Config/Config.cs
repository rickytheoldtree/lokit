using System;
using System.Collections.Generic;
using LocalizationTranslationWPF.DictConverter;

namespace LocalizationTranslationWPF.Data
{
    [Serializable]
    public class Config
    {
        public WebDriverType webDriverType;
        public DictConverterType dictConverter;
        public int isoPairIndex;
        public enum WebDriverType
        {
            Edge,
            Chrome,
            FireFox,
        }
    }
}
