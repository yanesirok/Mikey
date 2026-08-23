using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>
    /// Владелец семи плывущих облаков ОДНОГО экрана. Не MonoBehaviour: своего
    /// жизненного цикла у слоя нет, его целиком ведёт
    /// <see cref="MapAmbientController"/> — единственный планировщик 30 Гц во
    /// всей карте.
    ///
    /// <para>
    /// <b>Разделение писателей.</b> Геометрию (<c>width</c>/<c>height</c>/
    /// <c>top</c>/<c>left</c>) пишет только <see cref="MapWindLayout.Apply"/>
    /// и только через <see cref="EnsureLayout"/>, то есть лишь когда размер
    /// канваса реально изменился. Трансформ и прозрачность пишут только
    /// <see cref="Tick"/> и <see cref="Reset"/>. Пересечения нет ни в одном
    /// свойстве.
    /// </para>
    /// </summary>
    public sealed class MapWindLayer
    {
        /// <summary>За сколько секунд слой проявляется после показа экрана. Множитель идёт ТОЛЬКО на прозрачность: на положение он слепил бы все семь облаков в одну точку их полос.</summary>
        public const float SettleSeconds = 1.5f;

        private readonly VisualElement[] _clouds = new VisualElement[MapWindLayout.Clouds.Length];
        private VisualElement _layer;
        private float _laidOutWidth;
        private float _laidOutHeight;
        private bool _visible = true;

        /// <summary>Забирает элементы одного экрана. <paramref name="prefix"/> — "map-wind-" или "okinawa-wind-".</summary>
        public void Bind(VisualElement root, string prefix)
        {
            _layer = root?.Q<VisualElement>(prefix + "layer");

            // Инлайновый display живёт на элементе разметки и переживает уход с
            // экрана, а поле ниже заявляет, что слой показан. Не свести их здесь
            // значит получить залипание: вход при включённом «меньше движения»
            // оставляет на элементе display:none, и после повторной привязки
            // SetVisible(true) упирается в свой ранний выход visible == _visible
            // и молча возвращается — слой невидим до конца сессии.
            if (_layer != null)
                _layer.style.display = DisplayStyle.Flex;

            _laidOutWidth = 0f;
            _laidOutHeight = 0f;
            _visible = true;

            for (int i = 0; i < _clouds.Length; i++)
            {
                _clouds[i] = root?.Q<VisualElement>(prefix + i.ToString());
                if (_clouds[i] != null)
                    _clouds[i].usageHints = UsageHints.DynamicTransform | UsageHints.DynamicColor;
            }
        }

        /// <summary>Один шаг движения слоя.</summary>
        public void Tick(float timeSeconds, float canvasWidth, float canvasHeight, float panX, float panY)
        {
            if (canvasWidth <= 0f || canvasHeight <= 0f)
                return;

            EnsureLayout(canvasWidth, canvasHeight);

            float settle = MapPanZoomMath.EaseOutCubic(timeSeconds / SettleSeconds);

            for (int i = 0; i < _clouds.Length; i++)
            {
                VisualElement cloud = _clouds[i];
                if (cloud == null)
                    continue;

                WindCloud lane = MapWindLayout.Clouds[i];
                float progress = MapWindMath.LaneProgress(timeSeconds, lane.CrossSeconds, lane.Phase);
                float cloudWidth = canvasWidth * lane.WidthFraction;

                float x = MapWindMath.LaneOffsetX(progress, canvasWidth, cloudWidth)
                    + MapAmbientMath.ParallaxOffset(panX, lane.ParallaxFactor);
                float y = MapWindMath.Bob(timeSeconds, lane.CrossSeconds, lane.Phase, canvasHeight)
                    + MapAmbientMath.ParallaxOffset(panY, lane.ParallaxFactor);

                float swell = MapWindMath.Swell(timeSeconds, lane.CrossSeconds, lane.Phase);
                float roll = MapWindMath.RollDegrees(timeSeconds, lane.CrossSeconds, lane.Phase);

                cloud.style.translate = new Translate(x, y);
                cloud.style.scale = new Scale(new Vector2(swell, swell));
                cloud.style.rotate = new Rotate(new Angle(roll, AngleUnit.Degree));
                cloud.style.opacity = MapWindMath.Opacity(
                    lane.RestOpacity, progress, timeSeconds, lane.CrossSeconds, lane.Phase, settle);
            }
        }

        /// <summary>
        /// Перекладывает слой, только если размер канваса действительно
        /// изменился. Без этой проверки раскладка шла бы каждый кадр — ровно
        /// та цена, которой весь дизайн избегает.
        /// </summary>
        private void EnsureLayout(float canvasWidth, float canvasHeight)
        {
            if (canvasWidth == _laidOutWidth && canvasHeight == _laidOutHeight)
                return;

            for (int i = 0; i < _clouds.Length; i++)
                MapWindLayout.Apply(_clouds[i], MapWindLayout.Clouds[i], canvasWidth, canvasHeight);

            _laidOutWidth = canvasWidth;
            _laidOutHeight = canvasHeight;
        }

        /// <summary>
        /// Снимает всё, что написал тик. Прозрачность уходит в Null, а не в
        /// литеральный ноль: покой задан правилом ".map-wind" в USS, и Null
        /// возвращает элемент именно к нему.
        /// </summary>
        public void Reset()
        {
            for (int i = 0; i < _clouds.Length; i++)
            {
                VisualElement cloud = _clouds[i];
                if (cloud == null)
                    continue;

                cloud.style.translate = StyleKeyword.Null;
                cloud.style.scale = StyleKeyword.Null;
                cloud.style.rotate = StyleKeyword.Null;
                cloud.style.opacity = StyleKeyword.Null;
            }

            _laidOutWidth = 0f;
            _laidOutHeight = 0f;
        }

        /// <summary>
        /// Показывает или прячет слой целиком. Через display, а не через
        /// прозрачность: прозрачный элемент всё равно отдаёт геометрию в
        /// цепочку отрисовки, и «меньше движения» тогда не экономило бы ничего.
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (_layer == null || visible == _visible)
                return;

            _visible = visible;
            _layer.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
