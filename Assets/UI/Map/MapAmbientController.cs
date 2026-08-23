using System.Collections;
using Mikey.UI.SafeArea;
using Mikey.UI.Settings;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>
    /// Единственный драйвер непрерывного («ambient») движения на обоих экранах
    /// карты: дрейф и параллакс облаков, дыхание бумаги, Ken Burns в простое,
    /// дыхание маркеров.
    ///
    /// <para>
    /// Тикает на 30 Гц, а не по кадру: у ambient-движения периоды в десятки
    /// секунд, разница с 60 Гц невидима, а работы вдвое меньше. По той же
    /// причине на экранах карты поднимается
    /// <see cref="OnDemandRendering.renderFrameInterval"/> — карта статичный
    /// экран, ей незачем полная частота.
    /// </para>
    ///
    /// <para>
    /// Пишет ИСКЛЮЧИТЕЛЬНО transform-свойства и прозрачность. Любая запись
    /// геометрических style-свойств (left/top/width/height) здесь означала бы
    /// полный проход лэйаута каждый тик — см. MapAmbientControllerSourceTests,
    /// который это стережёт.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MapAmbientController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;
        private const int TickIntervalMs = 33;
        private const int MapRenderFrameInterval = 2;

        private const string JapanScreenId = "map";
        private const string OkinawaScreenId = "mapOkinawa";

        private VisualElement _root;
        private IScreenNavigator _navigator;
        private IMotionSettings _motion;
        private IVisualElementScheduledItem _tick;

        private Coroutine _bindRoutine;
        private bool _bound;
        private bool _onMapScreen;
        private float _elapsedSeconds;
        private float _kenBurnsWeight;

        private readonly VisualElement[] _clouds = new VisualElement[MapAmbientMath.CloudCount];
        private readonly float[] _cloudRestOpacity = new float[MapAmbientMath.CloudCount];
        private VisualElement _canvas;
        private MapPanZoomController _panZoom;

        private readonly System.Collections.Generic.List<VisualElement> _markerBreaths = new System.Collections.Generic.List<VisualElement>();
        private readonly System.Collections.Generic.List<VisualElement> _markerShadows = new System.Collections.Generic.List<VisualElement>();
        private readonly System.Collections.Generic.List<bool> _markerAlive = new System.Collections.Generic.List<bool>();
        private int _focusMarkerIndex = -1;

        private void OnEnable()
        {
            if (_bound)
                return;
            _bindRoutine = StartCoroutine(BindWhenReady());
        }

        private void OnDisable()
        {
            if (_bindRoutine != null)
            {
                StopCoroutine(_bindRoutine);
                _bindRoutine = null;
            }

            StopTicking();

            if (_motion != null)
                _motion.Changed -= OnMotionSettingsChanged;

            if (_navigator != null)
            {
                _navigator.ScreenChanged -= OnScreenChanged;
                _navigator = null;
            }

            _root = null;
            _motion = null;
            _bound = false;
            _onMapScreen = false;
        }

        private IEnumerator BindWhenReady()
        {
            var document = GetComponent<UIDocument>();

            int frames = 0;
            while (document.rootVisualElement == null)
            {
                if (++frames > MaxRootResolveFrames)
                {
                    Debug.LogError("[MapAmbientController] UIDocument root unavailable; ambient not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            _root = document.rootVisualElement;
            _motion = GetComponent<IMotionSettings>();
            if (_motion != null)
                _motion.Changed += OnMotionSettingsChanged;

            _navigator = GetComponent<IScreenNavigator>();
            if (_navigator != null)
            {
                _navigator.ScreenChanged += OnScreenChanged;
                OnScreenChanged(_navigator.CurrentScreen);
            }

            _bound = true;
            _bindRoutine = null;
        }

        private void OnScreenChanged(string screenId)
        {
            _onMapScreen = screenId == JapanScreenId || screenId == OkinawaScreenId;
            if (_onMapScreen)
            {
                ResolveScreenElements(screenId);
                StartTicking();
            }
            else
                StopTicking();
        }

        /// <summary>
        /// Забирает элементы того экрана, который сейчас показан. Имена
        /// отличаются между экранами ("map-cloud-*" против "okinawa-cloud-*"),
        /// а прозрачность покоя берётся из соответствующего пресета
        /// MapCloudLayout — контроллер её не выдумывает.
        /// </summary>
        private void ResolveScreenElements(string screenId)
        {
            bool japan = screenId == JapanScreenId;
            string prefix = japan ? "map-cloud-" : "okinawa-cloud-";
            _canvas = _root?.Q<VisualElement>(japan ? "map-canvas" : "okinawa-canvas");

            string[] suffixes = { "right-01", "left-01", "left-02", "bottom-01" };
            MapCloudPreset preset = japan ? MapCloudLayout.JapanRest : MapCloudLayout.OkinawaRest;
            float[] restOpacity =
            {
                preset.Right1.Opacity,
                preset.Left1.Opacity,
                preset.Left2.Opacity,
                preset.Bottom1.Opacity,
            };

            for (int i = 0; i < MapAmbientMath.CloudCount; i++)
            {
                _clouds[i] = _root?.Q<VisualElement>(prefix + suffixes[i]);
                _cloudRestOpacity[i] = restOpacity[i];
                if (_clouds[i] != null)
                    _clouds[i].usageHints = UsageHints.DynamicTransform | UsageHints.DynamicColor;
            }

            _panZoom = null;
            foreach (MapPanZoomController candidate in GetComponents<MapPanZoomController>())
            {
                if (candidate.ScreenId == screenId)
                {
                    _panZoom = candidate;
                    break;
                }
            }

            _markerBreaths.Clear();
            _markerShadows.Clear();
            _markerAlive.Clear();
            _focusMarkerIndex = -1;

            string nodeClass = japan ? "chapter-node" : "level-node";
            var nodes = _root?.Query<VisualElement>(className: nodeClass).ToList();
            if (nodes == null)
                return;

            for (int i = 0; i < nodes.Count; i++)
            {
                VisualElement node = nodes[i];
                VisualElement breath = node.Q<VisualElement>(className: nodeClass + "__breath");
                VisualElement shadow = node.Q<VisualElement>(className: nodeClass + "__shadow");

                if (breath != null)
                    breath.usageHints = UsageHints.DynamicTransform;
                if (shadow != null)
                    shadow.usageHints = UsageHints.DynamicTransform | UsageHints.DynamicColor;

                _markerBreaths.Add(breath);
                _markerShadows.Add(shadow);

                // Состояние блокировки читается из класса, а не из отдельного
                // API контроллеров: класс уже есть, он единственный источник
                // правды для внешнего вида, и ambient не заводит вторую копию
                // этого знания. Снимок берётся здесь, при смене экрана, а не
                // на каждом тике — блокировки за время показа экрана не
                // меняются, а тик обязан оставаться дешёвым.
                bool alive = !node.ClassListContains(nodeClass + "--locked");
                _markerAlive.Add(alive);
                if (alive)
                    _focusMarkerIndex = i;

                // Покой выставляется ВСЕМ маркерам на каждом входе на экран,
                // не только заблокированным: цвет тени непрозрачен, и видимой
                // альфой владеет исключительно этот inline opacity — у
                // живого маркера её никто не задаёт до первого тика, значит
                // один кадр после показа экрана его тень рисовалась бы
                // сплошным чёрным. Живым тик (см. TickMarkers) перепишет
                // значение сразу же, лишней работы это не создаёт.
                //
                // Обёртке масштаб снимается через StyleKeyword.Null, а не
                // выставляется явной единицей: тот же элемент читает каскад
                // появления маркеров (MapNodeFeedback), и явный инлайн-
                // масштаб перебил бы его стартовое USS-состояние независимо
                // от того, какой из двух контроллеров экрана отработает
                // раньше. Null лишь снимает прошлый инлайн и отдаёт решение
                // USS — в покое это тот же единичный масштаб.
                if (breath != null)
                    breath.style.scale = StyleKeyword.Null;
                if (shadow != null)
                {
                    shadow.style.scale = new Scale(Vector2.one);
                    shadow.style.opacity = MapAmbientMath.MarkerShadowRestOpacity;
                }
            }
        }

        /// <summary>
        /// Настройка «меньше движения» переключается из модала, который
        /// открывается ПОВЕРХ карты и экран не меняет — значит ScreenChanged не
        /// придёт, и реакция обязана идти от самой настройки. Без этой подписки
        /// выключение движения ловилось бы следующим тиком, а обратное
        /// включение не ловилось бы никогда: тик к тому моменту уже остановлен,
        /// и ambient молчал бы до следующего входа на экран карты.
        /// </summary>
        private void OnMotionSettingsChanged()
        {
            if (!_onMapScreen)
                return;

            if (_motion != null && _motion.ReducedMotion)
                StopTicking();
            else
                StartTicking();
        }

        private void StartTicking()
        {
            if (_root == null || _tick != null)
                return;
            if (_motion != null && _motion.ReducedMotion)
                return;

            _elapsedSeconds = 0f;
            _kenBurnsWeight = 0f;
            OnDemandRendering.renderFrameInterval = MapRenderFrameInterval;
            _tick = _root.schedule.Execute(Tick).Every(TickIntervalMs);
        }

        private void StopTicking()
        {
            _panZoom?.SetAmbientOffset(0f, 0f, 1f);

            if (_tick != null)
            {
                _tick.Pause();
                _tick = null;
            }
            OnDemandRendering.renderFrameInterval = 1;
        }

        /// <summary>
        /// Один шаг ambient-движения. Пока только копит время — конкретные
        /// каналы (облака, камера, маркеры) добавляются следующими задачами
        /// плана и каждый живёт в своём приватном методе.
        /// </summary>
        private void Tick()
        {
            if (!_bound || !_onMapScreen)
                return;
            if (_motion != null && _motion.ReducedMotion)
            {
                StopTicking();
                return;
            }
            if (MapCloudTransitionController.IsTransitioning)
                return;

            _elapsedSeconds += TickIntervalMs / 1000f;
            TickClouds();
            TickCamera();
            TickMarkers();
        }

        /// <summary>
        /// Кладёт дрейф MapAmbientMath поверх раскладки покоя, которую уже
        /// выставил MapCloudLayout.Apply — никогда её не подменяя.
        /// </summary>
        private void TickClouds()
        {
            float width = _canvas?.resolvedStyle.width ?? 0f;
            float height = _canvas?.resolvedStyle.height ?? 0f;
            if (width <= 0f || height <= 0f)
                return;

            float panX = _panZoom?.CurrentPanX ?? 0f;
            float panY = _panZoom?.CurrentPanY ?? 0f;

            for (int i = 0; i < MapAmbientMath.CloudCount; i++)
            {
                VisualElement cloud = _clouds[i];
                if (cloud == null)
                    continue;

                MapAmbientMath.CloudDrift(i, _elapsedSeconds, width, height,
                    out float dx, out float dy, out float dOpacity);

                float factor = MapAmbientMath.CloudParallaxFactors[i];
                dx += MapAmbientMath.ParallaxOffset(panX, factor);
                dy += MapAmbientMath.ParallaxOffset(panY, factor);

                cloud.style.translate = new Translate(dx, dy);
                // Клампим: у правого облака прозрачность покоя ровно 1.00
                // (см. MapCloudLayout), и без ограничения верхняя половина
                // синуса упиралась бы в потолок — облако умело бы только
                // темнеть, но не светлеть, то есть дышало бы вполсилы.
                cloud.style.opacity = Mathf.Clamp01(_cloudRestOpacity[i] + dOpacity);
            }
        }

        /// <summary>
        /// Дыхание бумаги идёт всегда, Ken Burns — только в простое и с
        /// плавным набором/гашением веса. Обе добавки уезжают в
        /// MapPanZoomController одним вызовом: канвас пишет только он.
        /// </summary>
        private void TickCamera()
        {
            if (_panZoom == null)
                return;

            float width = _canvas?.resolvedStyle.width ?? 0f;
            float height = _canvas?.resolvedStyle.height ?? 0f;

            float target = _panZoom.SecondsSinceLastInput >= MapAmbientMath.IdleDelaySeconds ? 1f : 0f;
            _kenBurnsWeight = MapAmbientMath.ApproachWeight(
                _kenBurnsWeight, target, TickIntervalMs / 1000f, MapAmbientMath.KenBurnsFadeSeconds);

            MapAmbientMath.KenBurns(_elapsedSeconds, width, height,
                out float kenPanX, out float kenPanY, out float kenZoom);

            float breath = MapAmbientMath.Breath(
                _elapsedSeconds, MapAmbientMath.PaperBreathPeriodSeconds, MapAmbientMath.PaperBreathAmplitude);

            _panZoom.SetAmbientOffset(
                kenPanX * _kenBurnsWeight,
                kenPanY * _kenBurnsWeight,
                breath + kenZoom * _kenBurnsWeight);
        }

        /// <summary>
        /// Дышат только разблокированные маркеры, и ровно один — текущая цель —
        /// дышит сильнее остальных. Заблокированные стоят абсолютно
        /// неподвижно: контраст сам ведёт взгляд, и никакие стрелки-указатели
        /// поверх карты не нужны.
        /// </summary>
        private void TickMarkers()
        {
            for (int i = 0; i < _markerBreaths.Count; i++)
            {
                // Заблокированные уже приведены в покой один раз в
                // ResolveScreenElements и не меняются, пока экран открыт —
                // писать им каждый тик незачем.
                if (!_markerAlive[i])
                    continue;

                VisualElement breath = _markerBreaths[i];
                if (breath == null)
                    continue;

                float amplitude = MapAmbientMath.MarkerBreathAmplitude
                    * (i == _focusMarkerIndex ? MapAmbientMath.FocusBreathMultiplier : 1f);
                float scale = MapAmbientMath.Breath(_elapsedSeconds, MapAmbientMath.MarkerBreathPeriodSeconds, amplitude);
                breath.style.scale = new Scale(new Vector2(scale, scale));

                VisualElement shadow = _markerShadows[i];
                if (shadow == null)
                    continue;

                // Тень идёт в противофазе: маркер поднимается — тень
                // поджимается и бледнеет. Иначе это читается как рост
                // объекта, а не как отрыв от поверхности.
                float shadowScale = MapAmbientMath.MarkerShadowScale(scale);
                shadow.style.scale = new Scale(new Vector2(shadowScale, shadowScale));
                shadow.style.opacity = MapAmbientMath.MarkerShadowOpacity(scale);
            }
        }
    }
}
