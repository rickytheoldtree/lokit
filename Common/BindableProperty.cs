using System;

namespace LocalizationTranslationWPF.Common
{
    public class BindableProperty<T>
    {
        private Action<T> onValueChanged = _ => { };
        private T value;

        public T Value
        {
            get => value;
            set
            {
                this.value = value;
                onValueChanged.Invoke(value);
            }
        }

        public BindableProperty(T value = default)
        {
            this.value = value;
        }

        public BindableProperty<T> Register(Action<T> onValueChanged)
        {
            this.onValueChanged += onValueChanged;
            return this;
        }

        public BindableProperty<T> RegisterAndInvoke(Action<T> onValueChanged)
        {
            this.onValueChanged += onValueChanged;
            onValueChanged(value);
            return this;
        }

        public BindableProperty<T> UnRegister(Action<T> onValueChanged)
        {
            this.onValueChanged -= onValueChanged;
            return this;
        }

        public void SetWithoutInvoke(T value)
        {
            this.value = value;
        }
    }
}