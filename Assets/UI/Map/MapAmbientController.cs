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
    /// каскадное появление маркеров при входе на экран и их дыхание после.
    ///
    /// <para>
    /// Каскад появления — численный, не USS-переход: живёт в TickMarkers
    /// наравне с дыханием (см. MapAmbientMath.MarkerEntranceProgress), пишется
    /// тем же присваиванием, что и дыхание, и потому не может гоняться с ним
    /// за scale. Под «меньше движения» тикает только он (см. StartTicking/
    /// Tick) — короткий и сам себя останавливает, а не непрерывный ambient.
    /// </para>
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

        /// <summary>
        /// Секунды с начала входа на текущий экран, отдельно от
        /// <see cref="_elapsedSeconds"/>: та сбрасывается на каждый
        /// <see cref="StartTicking"/> (в т.ч. при выключении «меньше
        /// движения» посреди визита на экран), а каскад появления маркеров
        /// должен отсчитываться только от настоящего входа на экран — см.
        /// <see cref="ResolveScreenElements"/>.
        /// </summary>
        private float _markerEntranceElapsedSeconds;

        /// <summary>
        /// «Меньше движения», защёлкнутое на момент старта входа (см.
        /// ResolveScreenElements), а не перечитанное на каждом тике: вход
        /// шага/длительности реагирует на дискретный тик, и живое
        /// переключение настройки посреди входа пересчитало бы прогресс
        /// против другой длительности — маркер прыгнул бы назад. Живой Tick
        /// (непрерывный ambient) по-прежнему реагирует на настройку сразу,
        /// это касается только внутренней математики каскада.
        /// </summary>
        private bool _markerEntranceReducedMotion;

        private readonly VisualElement[] _clouds = new VisualElement[MapAmbientMath.CloudCount];
        private readonly float[] _cloudRestOpacity = new float[MapAmbientMath.CloudCount];

        /// <summary>Плывущий слой текущего экрана. Своего планировщика у него нет — его тикает Tick ниже.</summary>
        private readonly MapWindLayer _wind = new MapWindLayer();
        private VisualElement _canvas;
        private MapPanZoomController _panZoom;

        private readonly System.Collections.Generic.List<VisualElement> _markerBreaths = new System.Collections.Generic.List<VisualElement>();
        private readonly System.Collections.Generic.List<VisualElement> _markerShadows = new System.Collections.Generic.List<VisualElement>();
        private readonly System.Collections.Generic.List<VisualElement> _markerNodes = new System.Collections.Generic.List<VisualElement>();
        private readonly System.Collections.Generic.List<bool> _markerAlive = new System.Collections.Generic.List<bool>();

        /// <summary>
        /// Правда только для заблокированных: живой продолжает тикать
        /// всегда (дыхание меняется каждый тик), а заблокированный, вход
        /// которого уже был доведён до точного покоя один раз, дальше не
        /// трогается — писать те же 1/0/1 каждый тик впустую незачем.
        /// </summary>
        private readonly System.Collections.Generic.List<bool> _markerEntranceSettled = new System.Collections.Generic.List<bool>();
        private int _focusMarkerIndex = -1;

        /// <summary>
        /// nodeClass + "--selected" для текущего экрана. В отличие от
        /// _markerAlive (снимок на смену экрана — блокировка не меняется,
        /// пока экран открыт) состояние выбора меняется тапом ПРЯМО ВО
        /// ВРЕМЯ показа экрана, поэтому снимок здесь неприменим: его читают
        /// каждый тик через ClassListContains в TickMarkers, а не один раз
        /// в ResolveScreenElements. На ~12 маркерах это порядка четырёхсот
        /// сравнений строк в секунду — на фоне остального тика ничтожно.
        /// Правило "не читать классы в тике" тут не нарушается, а
        /// уточняется: снимок — для того, что не меняется без смены
        /// экрана; то, что меняется по действию игрока, читается на месте.
        /// Не заводи для этого снимок обратно — это сломает реакцию тени на
        /// выбор.
        /// </summary>
        private string _markerSelectedClass;

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

            LeaveMapScreen();

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
                LeaveMapScreen();
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

            // Сброс идёт вплотную ПЕРЕД привязкой, пока слой ещё держит
            // элементы прошлого экрана: на прямом переходе Япония<->Окинава
            // StopTicking не зовётся вовсе (тик прошлого экрана жив, и
            // StartTicking возвращается сразу — см. её комментарий про
            // _kenBurnsWeight), и без этой строки инлайн прошлого экрана
            // остался бы висеть на ЕГО облаках: при возврате они мигнули бы
            // позой с прошлого визита раньше, чем их перепишет первый тик.
            _wind.Reset();
            _wind.Bind(_root, japan ? "map-wind-" : "okinawa-wind-");

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
            _markerNodes.Clear();
            _markerAlive.Clear();
            _markerEntranceSettled.Clear();
            _focusMarkerIndex = -1;

            string nodeClass = japan ? "chapter-node" : "level-node";
            _markerSelectedClass = nodeClass + "--selected";
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
                _markerNodes.Add(node);

                // Состояние блокировки читается из класса, а не из отдельного
                // API контроллеров: класс уже есть, он единственный источник
                // правды для внешнего вида, и ambient не заводит вторую копию
                // этого знания. Снимок берётся здесь, при смене экрана, а не
                // на каждом тике — блокировки за время показа экрана не
                // меняются, а тик обязан оставаться дешёвым.
                bool alive = !node.ClassListContains(nodeClass + "--locked");
                _markerAlive.Add(alive);
                _markerEntranceSettled.Add(false);
                if (alive)
                    _focusMarkerIndex = i;
            }

            // Каскад появления начинается заново на каждом входе на экран —
            // и красится синхронно, одним вызовом TickMarkers на t=0, а не
            // отдельным "состоянием покоя": иначе ровно тот же класс багов,
            // что чинил предыдущий ре-ревью (тень без заданной opacity до
            // первого тика), возник бы заново для translate/scale каскада.
            // TickMarkers сам решает, что писать заблокированным и живым —
            // здесь не дублируем эту логику. reducedMotion защёлкивается
            // здесь же, один раз на весь вход — см. поле _markerEntranceReducedMotion.
            //
            // _elapsedSeconds тоже сбрасывается здесь, а не только в
            // StartTicking (которая вызывается ПОСЛЕ этого синхронного
            // TickMarkers): иначе при повторном входе на экран дыхание,
            // которое теперь сомножитель в MarkerScale, стартовало бы со
            // старого значения с прошлого визита, а не с гарантированной по
            // контракту Breath единицы — первый нарисованный кадр был бы
            // чуть мимо 0.92 у каждого маркера (см. ре-ре-ре-ревью задачи 9).
            _elapsedSeconds = 0f;
            _markerEntranceElapsedSeconds = 0f;
            // Вес Ken Burns тоже принадлежит ВХОДУ НА ЭКРАН, а не тику:
            // StartTicking (единственное другое место, где он сбрасывался)
            // на переходе Япония<->Окинава возвращается сразу — тик с
            // прошлого экрана ещё жив, — и вес доезжал бы на новый экран
            // тем, чем закончил на старом. Экран, кадр которого только что
            // поставил переход, обязан начинать из полного покоя.
            _kenBurnsWeight = 0f;
            _markerEntranceReducedMotion = _motion != null && _motion.ReducedMotion;
            TickMarkers();
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
            {
                // Включение настройки посреди каскада не должно замораживать
                // маркеры на полпути (полупрозрачные, смещённые) до
                // следующего входа на экран — этот дефект появился именно с
                // каскадом (раньше замирать было нечему), значит чинится
                // здесь же, а не оставляется как "было и раньше".
                SettleMarkerEntranceImmediately();
                StopTicking();
            }
            else
                StartTicking();
        }

        /// <summary>
        /// Форсирует вход у всех маркеров в завершённое состояние и красит
        /// его — один вызов TickMarkers с "бесконечным" временем входа даёт
        /// ровно те же точные значения (см. MarkerEntranceTransform), что и
        /// естественное завершение, переиспользуя тот же путь вместо второго
        /// набора присваиваний. Нужно перед остановкой тика посреди каскада
        /// (см. OnMotionSettingsChanged) — единственный путь, где тик может
        /// остановиться, пока чей-то вход ещё не завершён: уход с экрана
        /// карты (OnScreenChanged) и отключение компонента (OnDisable) сюда
        /// не относятся — оба случая либо перерисовывают всё заново при
        /// следующем ResolveScreenElements, либо скрывают дерево целиком, а
        /// самоостановка Tick по завершении входа уже settled по построению.
        /// </summary>
        private void SettleMarkerEntranceImmediately()
        {
            _markerEntranceElapsedSeconds = float.PositiveInfinity;
            TickMarkers();
        }

        /// <summary>
        /// Не отказывается стартовать под «меньше движения» — короткий вход
        /// маркеров (см. TickMarkers/MarkerEntranceProgress) тикает и тогда,
        /// это не непрерывный ambient, а одноразовый эффект ограниченной
        /// длительности. Единственный вызов этого метода, который вообще
        /// может случиться при включённом «меньше движения», приходит из
        /// OnScreenChanged на настоящем входе на экран (см.
        /// OnMotionSettingsChanged — он зовёт StartTicking только когда
        /// настройка уже выключена); Tick сам остановит себя, как только вход
        /// у всех маркеров закончится.
        /// </summary>
        private void StartTicking()
        {
            if (_root == null || _tick != null)
                return;

            _elapsedSeconds = 0f;
            _kenBurnsWeight = 0f;
            OnDemandRendering.renderFrameInterval = MapRenderFrameInterval;
            _tick = _root.schedule.Execute(Tick).Every(TickIntervalMs);
        }

        /// <summary>
        /// Останавливает тик и возвращает В ПОКОЙ всё, что тик успел сдвинуть:
        /// добавку камеры, дрейф облаков. Частоту кадров здесь НЕ возвращает —
        /// см. <see cref="LeaveMapScreen"/>.
        /// </summary>
        private void StopTicking()
        {
            _panZoom?.SetAmbientOffset(0f, 0f, 1f);
            ResetCloudDrift();
            _wind.Reset();
            // И прячем: все три точки остановки — это «меньше движения»
            // включили посреди визита (OnMotionSettingsChanged минует
            // SetVisible из Tick), уход с карты и самоостановка под той же
            // настройкой. Ни в одной слой не должен оставаться в цепочке
            // отрисовки: покой .map-wind это opacity 0, то есть семь
            // невидимых квадов, за которые незачем платить. Обратно его
            // вернёт SetVisible(true) из Tick — Bind при новом входе на
            // экран тоже показывает слой безусловно.
            _wind.SetVisible(false);

            if (_tick != null)
            {
                _tick.Pause();
                _tick = null;
            }
        }

        /// <summary>
        /// Снимает дрейф облаков. Без этого «меньше движения», включённое
        /// посреди визита, оставляло бы каждое облако на его последнем
        /// смещении и не-покойной прозрачности до конца сессии — инлайн живёт
        /// на самих элементах разметки и переживает даже повторный вход на
        /// экран.
        ///
        /// <para>
        /// Смещение снимается в Null (у ".map-cloud" своего translate в USS
        /// нет, очистка и есть покой), а прозрачность ВОЗВРАЩАЕТСЯ ЗНАЧЕНИЕМ:
        /// её покой — тоже инлайн, его пишет MapCloudLayout.Apply из пресета,
        /// и Null стёр бы вместе с дрейфом ещё и раскладку.
        /// </para>
        /// </summary>
        private void ResetCloudDrift()
        {
            for (int i = 0; i < _clouds.Length; i++)
            {
                VisualElement cloud = _clouds[i];
                if (cloud == null)
                    continue;
                cloud.style.translate = StyleKeyword.Null;
                cloud.style.opacity = _cloudRestOpacity[i];
            }
        }

        /// <summary>
        /// Настоящий уход с экранов карты — единственный случай, когда частота
        /// кадров обязана вернуться к полной. Тик останавливается и по другим
        /// поводам (включили «меньше движения»; вход маркеров под ним
        /// доигрался и тик снял сам себя), и на них карта всё ещё показана и
        /// всё ещё статична: вернуть там единицу означало бы, что читатель
        /// настройки «меньше движения» весь визит платит за карту ВДВОЕ против
        /// всех остальных.
        /// </summary>
        private void LeaveMapScreen()
        {
            StopTicking();
            OnDemandRendering.renderFrameInterval = 1;
        }

        /// <summary>
        /// Один шаг ambient-движения. Облака/камера/дыхание маркеров стоят
        /// под «меньше движения» — тикает только вход маркеров, короткий и
        /// ограниченный по времени (см. TickMarkers), а как только он у всех
        /// маркеров закончился, тик сам себя останавливает: непрерывный
        /// ambient под «меньше движения» не включается никогда.
        /// </summary>
        private void Tick()
        {
            if (!_bound || !_onMapScreen)
                return;
            if (MapCloudTransitionController.IsTransitioning || MapCeremonyController.IsPlaying)
                return;

            bool liveReducedMotion = _motion != null && _motion.ReducedMotion;
            // Строго ДО раннего выхода ниже: вход на экран при включённой
            // настройке иначе оставил бы слой показанным (Bind показывает его
            // безусловно, снимая залипший display:none), и обещанная
            // настройкой экономия семи квадов не работала бы.
            _wind.SetVisible(!liveReducedMotion);
            _elapsedSeconds += TickIntervalMs / 1000f;
            _markerEntranceElapsedSeconds += TickIntervalMs / 1000f;

            TickMarkers();

            if (liveReducedMotion)
            {
                // Готовность читаем из _markerEntranceSettled (посчитан
                // TickMarkers по ЗАЩЁЛКНУТОМУ _markerEntranceReducedMotion,
                // не по liveReducedMotion) — последний индекс это худший
                // случай: под полным входом он приходит последним из-за
                // ступеньки, под reducedMotion все приходят одновременно.
                bool allSettled = _markerEntranceSettled.Count == 0
                    || _markerEntranceSettled[_markerEntranceSettled.Count - 1];
                if (allSettled)
                    StopTicking();
                return;
            }

            TickClouds();
            TickCamera();
            TickWind();
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
        /// Двигает плывущий слой. Размер канваса и пан читаются ровно так же,
        /// как в <see cref="TickClouds"/> — слой живёт в том же
        /// трансформированном канвасе, что и рамка.
        /// </summary>
        private void TickWind()
        {
            float width = _canvas?.resolvedStyle.width ?? 0f;
            float height = _canvas?.resolvedStyle.height ?? 0f;
            if (width <= 0f || height <= 0f)
                return;

            _wind.Tick(_elapsedSeconds, width, height,
                _panZoom?.CurrentPanX ?? 0f, _panZoom?.CurrentPanY ?? 0f);
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
        /// Масштаб маркера — ПРОИЗВЕДЕНИЕ дыхания (или 1 для заблокированного
        /// — он не дышит) на множитель входа (см.
        /// MapAmbientMath.MarkerEntranceTransform), а не переключение между
        /// "каскад владеет scale" / "дыхание владеет scale": множитель входа
        /// сам стремится к 1, поэтому границы передачи владения не
        /// существует, и разрыву неоткуда взяться (было — до ре-ревью:
        /// скачок ~1.4% на последнем маркере Окинавы, потому что фаза
        /// дыхания в момент завершения входа была произвольной).
        ///
        /// <para>
        /// Прозрачность/смещение/множитель ДОВОДЯТСЯ ДО ТОЧНОГО ПОКОЯ явно —
        /// см. MarkerEntranceTransform — а не остаются тем, что случайно
        /// получилось на предпоследнем тике: 33-миллисекундный тик почти
        /// никогда не делит длительность входа нацело, поэтому "прогресс
        /// ровно 1" на каком-то тике не гарантирован. Раньше (баг из
        /// ре-ревью) прозрачность писалась ТОЛЬКО пока прогресс < 1, и
        /// потому никогда не доходила до 1 вовсе.
        /// </para>
        ///
        /// <para>
        /// Заблокированный, чей вход уже был доведён до точного покоя один
        /// раз (_markerEntranceSettled), дальше не трогается — писать те же
        /// 1/0/1 каждый тик впустую незачем. Живой продолжает тикать всегда:
        /// дыхание меняется каждый тик независимо от входа.
        /// </para>
        ///
        /// <para>
        /// Вызывается и синхронно, один раз, из ResolveScreenElements (красит
        /// t=0 сразу, до первого реального тика — иначе тень маркера
        /// рисовалась бы без заданной прозрачности первый кадр после показа
        /// экрана), и затем каждый тик. reducedMotion для АРИФМЕТИКИ ВХОДА
        /// читается из _markerEntranceReducedMotion — защёлкнутого на момент
        /// старта входа, см. это поле; дыхание, наоборот, смотрит на живую
        /// настройку, чтобы замереть в тот же момент, когда её переключили.
        /// </para>
        /// </summary>
        private void TickMarkers()
        {
            // Живая настройка, не защёлкнутая _markerEntranceReducedMotion:
            // та существует ради арифметики каскада (см. это поле), а дыхание
            // обязано замереть в тот же момент, когда игрок сдвинул тумблер,
            // иначе живые маркеры остаются застывшими посреди вдоха до конца
            // сессии — SettleMarkerEntranceImmediately доводит вход, но не
            // дыхание.
            bool reducedMotion = _motion != null && _motion.ReducedMotion;

            for (int i = 0; i < _markerBreaths.Count; i++)
            {
                bool alive = _markerAlive[i];
                float entranceProgress = MapAmbientMath.MarkerEntranceProgress(i, _markerEntranceElapsedSeconds, _markerEntranceReducedMotion);
                bool settled = entranceProgress >= 1f;
                bool wasSettled = _markerEntranceSettled[i];

                if (settled && !alive && wasSettled)
                    continue;
                _markerEntranceSettled[i] = settled;

                VisualElement breath = _markerBreaths[i];
                VisualElement shadow = _markerShadows[i];

                float breathScale = alive && !reducedMotion
                    ? MapAmbientMath.Breath(_elapsedSeconds, MapAmbientMath.MarkerBreathPeriodSeconds,
                        MapAmbientMath.MarkerBreathAmplitude * (i == _focusMarkerIndex ? MapAmbientMath.FocusBreathMultiplier : 1f))
                    : 1f;

                MapAmbientMath.MarkerEntranceTransform(entranceProgress, _markerEntranceReducedMotion,
                    out float opacity, out float offsetY, out float entranceMultiplier);

                float scale = MapAmbientMath.MarkerScale(breathScale, entranceMultiplier);

                // Прозрачность входа пишется САМОМУ УЗЛУ, а не обёртке дыхания.
                // Обёртка — родитель одной лишь иконки; тень и подпись ей
                // СЁСТРЫ, и пока прозрачность жила на ней, входил один значок:
                // подписи ("OKINAWA", "LVL 0"...) выскакивали непрозрачными на
                // первом же кадре, а тень рисовалась ТЕМНЕЕ и ШИРЕ своего покоя
                // (её прозрачность выводится из масштаба, а тот в начале входа
                // 0.92) — девять чёрных эллипсов без пинов почти на секунду при
                // входе на Окинаву. На узле прозрачность бесплатна и
                // перемножается с детьми, поэтому входит МАРКЕР целиком, как и
                // описано в дизайне, а тень с подписью проявляются вместе со
                // своим пином. Собственный "translate: -50% -100%" узла —
                // привязка кончика пина — при этом не трогается: смещение и
                // масштаб входа остаются на обёртке.
                //
                // Пишется только пока вход идёт, плюс ровно один раз в момент
                // его завершения: у живого маркера тик крутится вечно, и
                // писать ему opacity 1 каждые 33 мс значило бы гонять
                // перекраску всего поддерева узла впустую.
                VisualElement node = _markerNodes[i];
                if (node != null && (!settled || !wasSettled))
                    node.style.opacity = opacity;

                if (breath != null)
                {
                    breath.style.translate = new Translate(0, offsetY);
                    breath.style.scale = new Scale(new Vector2(scale, scale));
                }

                if (shadow == null)
                    continue;

                // Состояние выбора читается ЗДЕСЬ, в тике, а не снимком в
                // ResolveScreenElements — см. _markerSelectedClass: оно
                // меняется тапом по ходу показа экрана, а не только на
                // входе на него.
                bool selected = node != null && node.ClassListContains(_markerSelectedClass);

                // Тень всегда в противофазе к ТЕКУЩЕМУ scale, откуда бы он ни
                // взялся — из каскада появления, из дыхания или из их
                // произведения: маркер поднимается — тень поджимается и
                // бледнеет. Выбранный маркер поверх этого получает свою
                // добавку — шире и бледнее, чем дало бы одно дыхание,
                // читается вместе с приподнятой иконкой (см. Map.uss).
                float shadowScale = MapAmbientMath.MarkerShadowScale(scale, selected);
                shadow.style.scale = new Scale(new Vector2(shadowScale, shadowScale));
                shadow.style.opacity = MapAmbientMath.MarkerShadowOpacity(scale, selected);
            }
        }
    }
}
