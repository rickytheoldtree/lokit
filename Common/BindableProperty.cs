using System;

namespace LocalizationTranslationWPF.Common
{
    public class BindableProperty<T>
    {
        private event Action<T> OnValueChanged = _ => { };
        private T value;

        public T Value
        {
            get => value;
            set
            {
                this.value = value;
                OnValueChanged.Invoke(value);
            }
        }

        public BindableProperty(T value = default(T))
        {
            this.value = value;
        }

        public BindableProperty<T> Register(Action<T> onValueChanged)
        {
            OnValueChanged += onValueChanged;
            return this;
        }

        public BindableProperty<T> RegisterAndInvoke(Action<T> onValueChanged)
        {
            OnValueChanged += onValueChanged;
            onValueChanged(value);
            return this;
        }

        public BindableProperty<T> UnRegister(Action<T> onValueChanged)
        {
            OnValueChanged -= onValueChanged;
            return this;
        }

        public void SetWithoutInvoke(T value)
        {
            this.value = value;
        }
    }
}