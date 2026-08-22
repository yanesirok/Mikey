using System;

namespace Mikey.UI.Settings
{
    /// <summary>
    /// Единственная настройка движения: «меньше движения» выключает
    /// декоративный ambient-слой карты целиком и сокращает каскады до
    /// простого проявления. Прямой отклик на палец (инерция пана, резинка
    /// на границах) она НЕ трогает — это управление, а не декор.
    /// Форма зеркалит <see cref="Mikey.UI.Audio.IAudioSettings"/>, чтобы
    /// общий Settings-модал читал обе настройки одинаково.
    /// </summary>
    public interface IMotionSettings
    {
        /// <summary>Поднимается только при настоящей смене значения, включая загрузку из хранилища.</summary>
        event Action Changed;

        /// <summary>true — декоративное движение выключено. По умолчанию false.</summary>
        bool ReducedMotion { get; set; }
    }
}
